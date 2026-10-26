// Program.cs: the oriel command line.
//   oriel mcp [--port N]         MCP server on stdio. In the desktop session the engine runs in this process; elsewhere
//                                (an SSH login or a service, session 0, which has no desktop) it talks to the daemon in
//                                the user's desktop session over loopback, starting it when needed. So from another
//                                computer an agent's MCP command can simply be: ssh mypc oriel mcp
//   oriel serve [--port N]       the daemon: run it in the desktop session (install starts it; --autostart at every logon)
//   oriel call <action> [--Option value ...]   one action, printed (e.g. oriel call tree --Window Notepad)
//   oriel install [--autostart]  per user, no admin: copies to %LOCALAPPDATA%\Programs\oriel and starts the daemon;
//                                --autostart also starts it at every logon (HKCU Run)
//   oriel uninstall              undoes install and deletes %LOCALAPPDATA%\oriel (token, port, config.json)
//   oriel status | port | version
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Oriel
{
public static class Program
{
    public const string Version = BuildInfo.Version;  // from build\build.ps1 -Version

    [STAThread]
    public static int Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0] : "help";
        int port = Engine.UserPort();
        for (int i = 1; i + 1 < args.Length; i++) if (args[i] == "--port") port = int.Parse(args[i + 1]);
        try
        {
            switch (cmd)
            {
                case "mcp": return RunMcp(port);
                case "serve": Engine.Serve(port); return 0;
                case "spawn": Daemon.Start(null, port); return 0;  // run by Daemon.Start's scheduled task
                case "call": return RunCall(args);
                case "install": return Install(port, Array.IndexOf(args, "--autostart") > 0);
                case "uninstall": return Uninstall();
                case "status": Console.WriteLine(new Daemon(port).Request(new Dictionary<string, object> { { "action", "status" } })); return 0;
                case "port": Console.WriteLine(port); return 0;
                case "version": Console.WriteLine("oriel " + Version); return 0;
                default:
                    Console.WriteLine("oriel " + Version + ": Windows UI Automation for AI agents, as text, over MCP.\n" +
                        "  oriel mcp [--port N]         MCP server on stdio (from another computer: ssh mypc oriel mcp)\n" +
                        "  oriel serve [--port N]       the daemon, in the desktop session\n" +
                        "  oriel call <action> [--Option value ...]   one action, e.g. oriel call tree --Window Notepad\n" +
                        "  oriel install [--autostart]  per user, no admin: copy to %LOCALAPPDATA%\\Programs\\oriel and start the daemon;\n" +
                        "                               --autostart also starts it at every logon (HKCU Run)\n" +
                        "  oriel uninstall              stop the daemon; remove the logon entry, the installed exe and %LOCALAPPDATA%\\oriel\n" +
                        "  oriel status | port | version");
                    return cmd == "help" ? 0 : 2;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("oriel: " + Engine.Message(e));
            return 1;
        }
    }

    // Session 0 holds services and SSH logins: no desktop to automate there.
    static bool Interactive() { return Process.GetCurrentProcess().SessionId != 0; }

    static int RunMcp(int port)
    {
        if (Interactive())
        {
            Engine.Init();
            Mcp.Serve(options => { object a; options.TryGetValue("action", out a); return Engine.Run((string)a, options); }, Version);
        }
        else
        {
            var daemon = new Daemon(port);
            Mcp.Serve(daemon.Request, Version);
        }
        return 0;
    }

    // oriel call tree --Window "Character Map" --Max 50 ; --Steps takes JSON.
    static int RunCall(string[] args)
    {
        if (args.Length < 2) throw new OrielError("usage: oriel call <action> [--Option value ...]");
        Console.OutputEncoding = new UTF8Encoding(false);  // names like "Ω" must survive a pipe or SSH
        var options = new Dictionary<string, object>();
        for (int i = 2; i + 1 < args.Length; i += 2)
        {
            var key = args[i].TrimStart('-');
            options[key] = key.Equals("Steps", StringComparison.OrdinalIgnoreCase) ? new JavaScriptSerializer().DeserializeObject(args[i + 1]) : args[i + 1];
        }
        if (Interactive())
        {
            Console.WriteLine(Engine.Run(args[1], options));
        }
        else
        {
            options["action"] = args[1];
            Console.WriteLine(new Daemon(Engine.UserPort()).Request(options));
        }
        return 0;
    }

    static string ExePath() { return Assembly.GetExecutingAssembly().Location; }

    static string InstallDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "oriel");
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Per user and without admin: the exe goes to %LOCALAPPDATA%\Programs\oriel and the daemon starts now, headless.
    // A plain install leaves an entry from an earlier --autostart in place.
    static int Install(int port, bool autostart)
    {
        var dir = InstallDir();
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "oriel.exe");
        if (!string.Equals(Path.GetFullPath(ExePath()), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            // An upgrade: the running daemon holds the old exe open, so stop it first (it restarts below).
            foreach (var p in Process.GetProcessesByName("oriel"))
            {
                try { if (string.Equals(p.MainModule.FileName, target, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); } }
                catch (Exception) { }  // another user's process: not ours to touch
            }
            File.Copy(ExePath(), target, true);
        }
        string logon;
        using (var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (autostart) run.SetValue("oriel", "conhost.exe --headless \"" + target + "\" serve --port " + port);
            logon = run.GetValue("oriel") != null ? "it starts at every logon (HKCU Run \"oriel\")" :
                "it doesn't start at logon (install --autostart adds that), but oriel mcp over SSH starts it when needed";
        }
        Engine.LoadToken();
        Daemon.Start(target, port);
        Console.WriteLine("installed " + target + "\nthe daemon is starting now on 127.0.0.1:" + port + "; " + logon +
            "\nMCP command for agents on this PC:   " + target + " mcp" +
            "\nfrom another computer (OpenSSH):     ssh <this-pc> \"" + target + "\" mcp");
        return 0;
    }

    // Undoes install: the logon entry, the daemon (and any other oriel process), the installed exe and the per-user state
    // in %LOCALAPPDATA%\oriel (token, port, config.json). Prints what it removed.
    static int Uninstall()
    {
        var report = new List<string>();
        using (var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
            if (run.GetValue("oriel") != null) { run.DeleteValue("oriel"); report.Add("removed the logon entry (HKCU Run \"oriel\")"); }
        int stopped = 0;
        foreach (var p in Process.GetProcessesByName("oriel"))
        {
            if (p.Id == Process.GetCurrentProcess().Id) continue;
            try { p.Kill(); p.WaitForExit(3000); stopped++; }
            catch (Exception) { }  // another user's process: not ours to touch
        }
        if (stopped > 0) report.Add("stopped " + stopped + " running oriel process" + (stopped == 1 ? "" : "es"));
        bool clean = true;
        var dir = InstallDir();
        if (Directory.Exists(dir))
        {
            if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(ExePath())), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            {
                // A running exe can't delete itself: a hidden cmd removes the folder once this process has exited.
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() });
                report.Add("removing " + dir + " in 2 s, once this exe has exited");
            }
            else clean &= Delete(dir, report);
        }
        if (Directory.Exists(Engine.Home)) clean &= Delete(Engine.Home, report);
        Console.WriteLine(report.Count > 0 ? string.Join("\n", report) : "nothing to remove: oriel isn't installed for this user");
        return clean ? 0 : 1;
    }

    static bool Delete(string dir, List<string> report)
    {
        try { Directory.Delete(dir, true); report.Add("removed " + dir); return true; }
        catch (Exception e)
        {
            // For example a terminal that still has the folder as its current directory.
            if (!(e is IOException || e is UnauthorizedAccessException)) throw;
            report.Add("could not remove " + dir + ": " + e.Message);
            return false;
        }
    }
}

// A client for the daemon on 127.0.0.1: one persistent connection, opened with the handshake described at
// Engine.token (each side proves it holds the user's key; the key itself is never sent).
public class Daemon
{
    readonly int port;
    readonly string key = Engine.LoadToken();
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    TcpClient client;
    NetworkStream stream;
    string auth;
    const string Eot = "<EOT>\n";

    public Daemon(int port) { this.port = port; }

    public string Request(Dictionary<string, object> options)
    {
        Connect();
        var payload = new Dictionary<string, object>(options);
        payload["Auth"] = auth;
        var line = Encoding.UTF8.GetBytes(json.Serialize(payload) + "\n");
        string reply;
        try
        {
            stream.Write(line, 0, line.Length);
            reply = ReadReply();
        }
        catch (Exception e)
        {
            if (!(e is IOException || e is SocketException || e is ObjectDisposedException)) throw;
            Close();
            // The request may already have run: sending it again could repeat an action (toggle a box back).
            throw new OrielError("lost the connection to the daemon after sending the request; it may or may not have run (check with tree)");
        }
        // The daemon ends each reply with "(action took N ms)"; errors start with "ERROR: ".
        int took = reply.LastIndexOf("\n(", StringComparison.Ordinal);
        if (took >= 0 && reply.EndsWith(" ms)", StringComparison.Ordinal)) reply = reply.Substring(0, took);
        if (reply.StartsWith("ERROR: ", StringComparison.Ordinal)) throw new OrielError(reply.Substring(7));
        return reply;
    }

    // Reuse the connection while it is alive; otherwise connect, starting the daemon if nothing answers.
    void Connect()
    {
        if (client != null && !(client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0)) return;
        Close();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                client = new TcpClient();
                client.Connect("127.0.0.1", port);
                client.NoDelay = true;
                stream = client.GetStream();
                Handshake();
                return;
            }
            catch (SocketException)
            {
                Close();
                if (attempt == 0) Start(null, port);  // nothing listens: start the daemon in the desktop session
                else if (attempt >= 40) throw new OrielError("the oriel daemon doesn't answer on 127.0.0.1:" + port + " (is someone signed in to the desktop?)");
                Thread.Sleep(250);
            }
        }
    }

    void Handshake()
    {
        var nonce = Engine.Nonce();
        var hello = Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string, object> { { "hello", nonce } }) + "\n");
        stream.Write(hello, 0, hello.Length);
        var answer = json.DeserializeObject(ReadLine()) as Dictionary<string, object>;
        object proof, theirs;
        if (answer == null || !answer.TryGetValue("proof", out proof) || !answer.TryGetValue("nonce", out theirs) ||
            (string)proof != Engine.Hmac(key, "oriel-daemon\n" + nonce))
        {
            Close();
            throw new OrielError("the process on 127.0.0.1:" + port + " is not this user's oriel daemon");
        }
        auth = Engine.Hmac(key, "oriel-client\n" + theirs);
    }

    string ReadLine()
    {
        var line = new MemoryStream();
        int b;
        while ((b = stream.ReadByte()) != -1 && b != '\n') line.WriteByte((byte)b);
        if (b == -1) throw new IOException("the daemon closed the connection");
        return Encoding.UTF8.GetString(line.ToArray());
    }

    void Close()
    {
        if (client != null) client.Close();
        client = null;
    }

    string ReadReply()
    {
        var buf = new MemoryStream();
        var chunk = new byte[65536];
        while (true)
        {
            int n = stream.Read(chunk, 0, chunk.Length);
            if (n <= 0) throw new IOException("the daemon closed the connection");
            buf.Write(chunk, 0, n);
            var text = Encoding.UTF8.GetString(buf.ToArray());
            if (text.EndsWith(Eot, StringComparison.Ordinal)) return text.Substring(0, text.Length - Eot.Length).TrimEnd();
        }
    }

    // Start "oriel serve" in the signed-in user's desktop session, headless. From the desktop session that is a plain
    // child process. From session 0 it takes a one-shot scheduled task with an interactive logon (what Task Scheduler
    // offers instead of "run in another session"). The task runs "oriel spawn", which starts the daemon and exits:
    // deleting a task kills what it is still running, so the task is deleted only once spawn has finished.
    public static void Start(string exe, int port)
    {
        exe = exe ?? Assembly.GetExecutingAssembly().Location;
        if (Process.GetCurrentProcess().SessionId != 0)
        {
            // Through ShellExecute, so the daemon inherits none of this process's handles: with CreateProcess it kept the
            // caller's stdout pipe open, and an agent's shell reading install's output waited for the daemon to exit.
            Process.Start(new ProcessStartInfo("conhost.exe", "--headless \"" + exe + "\" serve --port " + port) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            return;
        }
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic task = service.NewTask(0);
        task.Principal.UserId = Environment.UserDomainName + "\\" + Environment.UserName;
        task.Principal.LogonType = 3;                // TASK_LOGON_INTERACTIVE_TOKEN: the user's desktop session
        task.Settings.ExecutionTimeLimit = "PT0S";   // the daemon runs until logoff
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        dynamic action = task.Actions.Create(0);     // TASK_ACTION_EXEC
        action.Path = "conhost.exe";
        action.Arguments = "--headless \"" + exe + "\" spawn --port " + port;
        dynamic registered = folder.RegisterTaskDefinition("oriel-start", task, 6, null, null, 3);  // create or update
        registered.Run(null);
        // Wait until the task has started and finished (State 4 = running; it can still read "queued" or "ready" right
        // after Run), at most 15 s.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        bool started = false;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(200);
            int state = registered.State;
            if (state == 4) started = true;
            else if (started || registered.LastTaskResult != 267011) break;  // 267011 = SCHED_S_TASK_HAS_NOT_RUN
        }
        folder.DeleteTask("oriel-start", 0);
    }
}
}
