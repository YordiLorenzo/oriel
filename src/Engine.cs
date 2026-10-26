// Engine.cs: Windows UI Automation (the accessibility tree) as text, for AI agents. It drives Windows' native UI
// Automation (the COM API that Narrator uses) through interop types embedded at build time from UIAutomationCore.dll's
// type library (build/build.ps1), and builds with the .NET Framework's own C# compiler, so it is C# 5 (no $"", ?.,
// => members or exception filters). Program.cs runs it as an MCP server, a daemon (Engine.Serve) or a one-shot CLI.
// Engine.Run(action, options) returns text and throws OrielError for problems the caller should read. Actions and options:
//   ping                                     "pong": the floor cost of a call
//   status                                   locked or not, seconds since the user's last input, foreground window
//   windows                                  visible top-level windows, front first: [handle] process "title"
//   tree   Window [Depth 12] [Max 400]       named or actionable on-screen elements, each with an id: a ref like
//                                            "e17" from the daemon (stable while the element lives), else a
//                                            position path like "1.1.0" (uia.ps1, one-shot)
//   find   Window [Name] [Type]              elements whose name contains Name
//   invoke Window (Id | Name [Type])         press / toggle / select / expand
//   set    Window (Id | Name [Type]) Value   fill a field (never a password field)
//   text   Window [Id | Name [Type]]         text of a document or field (TextPattern, else value, else name)
//                                            (invoke, set and text need no Window when Id is a ref)
//   wait   Window Name [Type] [Timeout 10]   poll on this PC until the element appears
//   scroll Window|Id|Name [Direction down|up|left|right] [Amount page|line]  scroll a container, or an element into view
//   select (Id | Window Name) Option         pick an item by its text in a combo box, list, tab strip or menu
//   window Window [State minimize|maximize|restore] [X Y] [Width Height]
//   screenshot Window [MaxWidth 1280]        a PNG of the window (for canvases and games), as IMAGE:<mime>;<base64>
//   focus  Window                            restore and bring to the front
//   close  Window                            close through WindowPattern
//   launch Path [Args] [WaitTitle] [Timeout 10] [Tree false] [Max 150]  start a program, file or URI, optionally
//                                            wait for its new window, and with Tree return that window's tree
//   keys   Keys Window [Id | Name [Type]]    SendKeys syntax ({ENTER}, ^a = Ctrl+A, %{F4}, +{TAB}), sent only once the
//                                            window is verified in front and the user has been idle for 0.6 s
//   batch  Steps [Window]                    run several of the above in one call; each step is an options object
//                                            with its own action and inherits Window; stops at the first error
// invoke, set, keys and batch (with a Window) report what they changed: elements that appeared (+), changed (~) or
// went away (-), and windows that opened, after the window settles. Changes=false skips that. Trace=true adds the
// time each stage took.
// Window matches a handle, a process name or part of the title (case-insensitive), front-most window first.
// invoke, set and select don't move the mouse or take focus, and work while the PC is locked; keys and focus don't.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using U = Interop.UIAutomationClient;

namespace Oriel
{
public class OrielError : Exception { public OrielError(string message) : base(message) { } }

public static class Engine
{
    class Node { public U.IUIAutomationElement El; public string Path; public int Depth; }
    class Win { public IntPtr Handle; public string Process; public string Title; }
    // The window's UIA element is fetched on first use: keys only need the handle, and a hung provider (LibreOffice
    // behind a modal dialog) times out on ElementFromHandle, which must not stop the keys that would close the dialog.
    class WinRef
    {
        public IntPtr H;
        U.IUIAutomationElement el;
        public U.IUIAutomationElement El { get { return el ?? (el = ElementOf(H)); } set { el = value; } }
    }
    // A look at a window: its nodes, and (worked out only when a change report needs them) the shown elements by
    // key and their text.
    class Snap
    {
        public List<Node> Nodes;
        public DateTime At;
        List<KeyValuePair<string, Node>> shown;
        Dictionary<string, string> body;
        public List<KeyValuePair<string, Node>> Shown { get { Fill(); return shown; } }
        public Dictionary<string, string> Body { get { Fill(); return body; } }
        void Fill()
        {
            if (body != null) return;
            shown = new List<KeyValuePair<string, Node>>();
            body = new Dictionary<string, string>();
            foreach (var n in Nodes)
            {
                if (!Engine.Shown(n.El)) continue;
                var key = Key(n.El) ?? "#" + n.Path;
                if (body.ContainsKey(key)) continue;
                body[key] = Engine.Body(n.El);
                shown.Add(new KeyValuePair<string, Node>(key, n));
            }
        }
    }

    // Property, pattern and control type ids from UIAutomationClient.h.
    const int RuntimeIdP = 30000, BoundsP = 30001, ProcessIdP = 30002, ControlTypeP = 30003, NameP = 30005,
        IsEnabledP = 30010, AutomationIdP = 30011, IsPasswordP = 30019, HwndP = 30020, IsOffscreenP = 30022,
        CanExpandP = 30028, CanInvokeP = 30031, CanSelectP = 30036, CanToggleP = 30041,
        CanSetP = 30043, ValueP = 30045, ReadOnlyP = 30046, ExpandStateP = 30070, IsSelectedP = 30079, ToggleStateP = 30086,
        CanRangeP = 30033, CanScrollP = 30034, CanScrollItemP = 30035, RangeValueP = 30047, RangeReadOnlyP = 30048,
        RangeMinP = 30049, RangeMaxP = 30050, VScrollPercentP = 30053, VViewSizeP = 30054;
    const int InvokePat = 10000, ValuePat = 10002, RangePat = 10003, ScrollPat = 10004, ExpandPat = 10005, WindowPat = 10009,
        SelectPat = 10010, TextPat = 10014, TogglePat = 10015, TransformPat = 10016, ScrollItemPat = 10017, LegacyPat = 10018;
    const int ListItemType = 50007, SliderType = 50015, MenuItemType = 50011;
    const int EditType = 50004, DocumentType = 50030;
    static readonly string[] Types = { "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image",
        "ListItem", "List", "Menu", "MenuBar", "MenuItem", "ProgressBar", "RadioButton", "ScrollBar", "Slider", "Spinner",
        "StatusBar", "Tab", "TabItem", "Text", "ToolBar", "ToolTip", "Tree", "TreeItem", "Custom", "Group", "Thumb",
        "DataGrid", "DataItem", "Document", "SplitButton", "Window", "Pane", "Header", "HeaderItem", "Table", "TitleBar",
        "Separator", "SemanticZoom", "AppBar" };
    static readonly int[] Props = { RuntimeIdP, BoundsP, ProcessIdP, ControlTypeP, NameP, IsEnabledP, AutomationIdP,
        IsPasswordP, HwndP, IsOffscreenP, CanExpandP, CanInvokeP, CanSelectP, CanToggleP, CanSetP, ValueP, ReadOnlyP,
        ExpandStateP, IsSelectedP, ToggleStateP, CanRangeP, CanScrollP, CanScrollItemP, RangeValueP, RangeReadOnlyP, RangeMinP, RangeMaxP, VScrollPercentP, VViewSizeP };

    static readonly U.IUIAutomation Auto = NewAutomation(10000);
    // For the focus check before keys: a provider that hangs there (LibreOffice Calc while busy) shouldn't hold the
    // keys for 10 s; after 2 s the check falls back to Win32 (SendKeysTo).
    static readonly U.IUIAutomation AutoQuick = NewAutomation(2000);

    // UIA's action methods (Invoke, SetValue, Select, ...) first move keyboard focus to the element by default, and
    // when Windows refuses (the PC is locked, or another app such as a game holds the foreground) they wait 2 s:
    // measured 2.0 s per call in Chrome and in Win32 dialogs, against 0-8 ms with AutoSetFocus off. Off, they also
    // stop stealing focus from the user. A provider that hangs costs at most 10 s (default 20 s) instead of the daemon.
    static U.IUIAutomation NewAutomation(uint timeoutMs)
    {
        // CUIAutomation8 (Windows 8+), created by class id: interop classes can't be embedded in the exe, interfaces can.
        var auto = (U.IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("e22ad333-b25f-460c-83d0-0581107395c9")));
        var two = (U.IUIAutomation2)auto;
        two.AutoSetFocus = 0;
        two.TransactionTimeout = timeoutMs;
        return auto;
    }
    // What the tree shows: control elements that are on screen (not scrolled away, minimised or in another tab).
    static readonly U.IUIAutomationCondition Visible =
        Auto.CreateAndCondition(Auto.ControlViewCondition, Auto.CreatePropertyCondition(IsOffscreenP, false));
    // Every property we print comes with the elements. Subtree brings a whole window across in one cross-process
    // call instead of two per element.
    static readonly U.IUIAutomationCacheRequest One = NewCache(U.TreeScope.TreeScope_Element, Auto.CreateTrueCondition());
    static readonly U.IUIAutomationCacheRequest Subtree = NewCache(U.TreeScope.TreeScope_Subtree, Visible);
    static readonly U.IUIAutomationCacheRequest FocusCheck = FocusCache();

    static U.IUIAutomationCacheRequest FocusCache()
    {
        var c = AutoQuick.CreateCacheRequest();
        c.AddProperty(ProcessIdP);
        c.AddProperty(IsPasswordP);
        c.AddProperty(ControlTypeP);
        c.AddProperty(NameP);
        return c;
    }

    static U.IUIAutomationCacheRequest NewCache(U.TreeScope scope, U.IUIAutomationCondition filter)
    {
        var c = Auto.CreateCacheRequest();
        foreach (var p in Props) c.AddProperty(p);
        c.TreeScope = scope;
        c.TreeFilter = filter;
        return c;
    }

    // ---------------------------------------------------------------- options and tracing

    class Opts
    {
        readonly Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public Opts(IDictionary src) { foreach (DictionaryEntry e in src) if (e.Value != null) d[(string)e.Key] = e.Value; }
        public object Raw(string k) { object v; return d.TryGetValue(k, out v) ? v : null; }
        // Empty strings count as missing: uia.ps1 passes every parameter, set or not.
        public string S(string k) { var v = Raw(k); var s = v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); return s == "" ? null : s; }
        public double D(string k, double def) { var s = S(k); return s == null ? def : double.Parse(s, CultureInfo.InvariantCulture); }
        public int I(string k, int def) { return (int)D(k, def); }
        public bool B(string k, bool def) { var s = S(k); return s == null ? def : !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) && s != "0"; }
        public Opts With(IDictionary step)
        {
            var merged = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in d)
                if (!string.Equals(kv.Key, "Steps", StringComparison.OrdinalIgnoreCase) && !string.Equals(kv.Key, "action", StringComparison.OrdinalIgnoreCase)) merged[kv.Key] = kv.Value;
            foreach (DictionaryEntry e in step) merged[e.Key] = e.Value;
            merged["Changes"] = false;  // the batch reports once, at the end
            return new Opts(merged);
        }
    }

    static StringBuilder trace;
    static readonly Stopwatch traceClock = new Stopwatch();
    static void Mark(string stage)
    {
        if (trace == null) return;
        trace.Append(' ').Append(stage).Append('=').Append(traceClock.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
        traceClock.Restart();
    }

    // ---------------------------------------------------------------- native windows and input

    delegate bool EnumProc(IntPtr h, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

    // Process names by pid, forgotten every minute so a reused pid can't keep a dead process's name for long.
    static readonly Dictionary<uint, string> nameByPid = new Dictionary<uint, string>();
    static DateTime namesSince = DateTime.UtcNow;

    static string ProcessName(uint pid)
    {
        if ((DateTime.UtcNow - namesSince).TotalSeconds > 60) { nameByPid.Clear(); namesSince = DateTime.UtcNow; }
        string name;
        if (nameByPid.TryGetValue(pid, out name)) return name;
        var h = OpenProcess(0x1000, false, pid);  // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero)
        {
            // Protected processes refuse even that; the process snapshot (~5 ms) still has their name.
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { name = "?"; }
        }
        else
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            name = QueryFullProcessImageName(h, 0, sb, ref size) ? Path.GetFileNameWithoutExtension(sb.ToString()) : "?";
            CloseHandle(h);
        }
        return nameByPid[pid] = name;
    }

    static string ProcessOf(IntPtr h)
    {
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        return ProcessName(pid);
    }

    static string Title(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // Visible, titled, uncloaked top-level windows in z-order (front first), dialogs included. One EnumWindows call,
    // where the UI Automation desktop walk takes about 1.3 s because some windows (Chrome, NVIDIA overlay) answer slowly.
    static List<Win> Windows()
    {
        var list = new List<Win>();
        EnumWindows(delegate (IntPtr h, IntPtr _) {
            if (!IsWindowVisible(h) || (GetWindowLong(h, -20) & 0x80) != 0) return true;  // WS_EX_TOOLWINDOW
            int cloaked;
            if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;  // DWMWA_CLOAKED
            var title = Title(h);
            if (title == "" || title == "Program Manager") return true;
            list.Add(new Win { Handle = h, Process = ProcessOf(h), Title = title });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static string Describe(Win w) { return "[" + w.Handle + "] " + w.Process + " \"" + w.Title + "\"" + (Allowed(w.Process) ? "" : " (off limits)"); }

    static uint IdleMs()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);
        GetLastInputInfo(ref info);
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    // The lock screen (LockApp) holds the foreground while the PC is locked: keystrokes and focus go nowhere then.
    static bool Locked() { return ProcessOf(GetForegroundWindow()) == "LockApp"; }

    // UIA SetFocus can't foreground top-level windows (File Explorer refuses), so attach to the foreground thread's
    // input queue and use SetForegroundWindow; when the foreground lock still refuses, a restore from minimised is
    // always allowed to activate.
    static bool Activate(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9);
        // AttachThreadInput fails on a thread that has no message queue yet; peeking creates one.
        MSG queue;
        PeekMessage(out queue, IntPtr.Zero, 0, 0, 0);
        uint ignored, fg = GetWindowThreadProcessId(GetForegroundWindow(), out ignored), me = GetCurrentThreadId();
        bool attached = fg != me && AttachThreadInput(me, fg, true);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        if (attached) AttachThreadInput(me, fg, false);
        if (InFront(h, 300)) return true;
        ShowWindow(h, 6);
        ShowWindow(h, 9);
        SetForegroundWindow(h);
        if (InFront(h, 300)) return true;
        // A busy app (LibreOffice Calc on a benchmark VM) handles the activation late; SwitchToThisWindow is what
        // Alt+Tab uses and is let through more often.
        SwitchToThisWindow(h, true);
        if (InFront(h, 600)) return true;
        // A notification toast (ShellExperienceHost's "New notification") can keep the foreground after it has shown,
        // until the user clicks elsewhere, and Windows refuses all of the above meanwhile: on WindowsAgentArena VMs
        // that blocked keys for whole runs. Alt+Tab has the system itself switch windows, which ends the hold.
        var holder = GetForegroundWindow();
        if (ProcessOf(holder) != "ShellExperienceHost") return false;
        AssertUserIdle();
        Press(0x12, false);
        try { Tap(0x09, 1); }
        finally { Press(0x12, true); }  // a stuck Alt would turn later typing into shortcuts
        var until = Environment.TickCount + 500;
        while (GetForegroundWindow() == holder && Environment.TickCount < until) Thread.Sleep(20);
        SetForegroundWindow(h);
        if (InFront(h, 300)) return true;
        SwitchToThisWindow(h, true);
        return InFront(h, 600);
    }

    static bool InFront(IntPtr h, int ms)
    {
        var deadline = Environment.TickCount + ms;
        while (GetForegroundWindow() != h)
        {
            if (Environment.TickCount > deadline) return false;
            Thread.Sleep(20);
        }
        return true;
    }
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool altTab);

    // GetLastInputInfo counts our own SendKeys too, so input newer than our last send is the user. Something on
    // some PCs report input every few seconds, so wait up to 3 s for a quiet 0.6 s instead of refusing outright.
    static int lastSend = int.MinValue;
    static void AssertUserIdle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            uint idle = IdleMs();
            uint sinceSend = unchecked((uint)(Environment.TickCount - lastSend));
            if (idle >= 600 || (lastSend != int.MinValue && idle + 300 >= sinceSend)) return;
            if (DateTime.UtcNow > deadline) throw new OrielError("the user is typing or moving the mouse; try again in a few seconds");
            Thread.Sleep(100);
        }
    }

    static void Foreground(WinRef w)
    {
        if (Locked()) throw new OrielError("the PC is locked: keys and focus need the user to unlock it (invoke and set still work in desktop apps)");
        if (Activate(w.H)) return;
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && GetAncestor(fg, 3) == w.H)  // GA_ROOTOWNER: a dialog this window owns is in front
            throw new OrielError("\"" + Title(fg) + "\", a dialog of \"" + Title(w.H) + "\", is in front; send the keys to that window (or close it first)");
        throw new OrielError("could not bring \"" + Title(w.H) + "\" to the front: [" + fg + "] " + ProcessOf(fg) + " \"" + Title(fg) +
            "\" holds it (close that window, or wait if the app is busy)");
    }

    // ---------------------------------------------------------------- windows

    // The same query nearly always names the same window as last time, so that window is checked (still there,
    // visible, still matching) before listing every window again. launch and windows start over.
    static readonly Dictionary<string, IntPtr> windowByQuery = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<IntPtr, U.IUIAutomationElement> elementByWindow = new Dictionary<IntPtr, U.IUIAutomationElement>();

    static bool Matches(IntPtr h, string q)
    {
        return h.ToString() == q || string.Equals(ProcessOf(h), q, StringComparison.OrdinalIgnoreCase) ||
            Title(h).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Windows an agent must never drive, whatever it is asked: the lock and sign-in screens, UAC prompts, Windows'
    // credential dialogs and password managers. config.json in %LOCALAPPDATA%\oriel can add to this list ("deny") or
    // limit control to named processes ("allow"); both match process names, case-insensitive.
    static readonly List<string> Deny = new List<string> { "LockApp", "LogonUI", "consent", "CredentialUIBroker",
        "1Password", "KeePass", "KeePassXC", "Bitwarden", "Dashlane", "LastPass", "RoboForm", "Enpass", "NordPass", "Keeper" };
    static List<string> Allow = new List<string>();
    // "shell": true in config.json lifts the interpreter and terminal rules below (the deny and allow lists still apply).
    static bool Shell;

    static bool policyLoaded;

    static void LoadPolicy()
    {
        if (policyLoaded) return;
        policyLoaded = true;
        var file = Path.Combine(Home, "config.json");
        if (!File.Exists(file)) return;
        var cfg = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
        if (cfg == null) throw new OrielError(file + " must hold a JSON object");
        object v;
        if (cfg.TryGetValue("deny", out v) && v is object[]) foreach (var x in (object[])v) Deny.Add(Convert.ToString(x));
        if (cfg.TryGetValue("allow", out v) && v is object[]) foreach (var x in (object[])v) Allow.Add(Convert.ToString(x));
        if (cfg.TryGetValue("shell", out v) && v is bool) Shell = (bool)v;
    }

    // oriel operates apps through their UI; starting command interpreters and script hosts is not its job, so launch
    // refuses them unless config.json allows them by name or turns "shell" on.
    static readonly string[] Interpreters = { "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32",
        "regsvr32", "wsl", "bash", "conhost", "wt" };

    static void CheckLaunch(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.Trim('"'));
        bool listed = Allow.Exists(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (Array.Exists(Interpreters, i => string.Equals(i, name, StringComparison.OrdinalIgnoreCase)) && !listed && !Shell)
            throw new OrielError(name + " is a command interpreter; launch starts apps, documents and URIs (\"shell\": true in config.json allows it)");
        if (Allow.Count > 0 && !listed)
            throw new OrielError(name + " is not in the allow-list in config.json");
    }

    // A terminal that is already open is a shell too: keys typed into it run as commands. So terminal windows are
    // off limits like interpreters are for launch, unless config.json allows them by name or turns "shell" on. (An agent
    // on a benchmark VM drove an open PowerShell this way after launch refused one.)
    static readonly string[] Terminals = { "WindowsTerminal", "OpenConsole", "powershell_ise", "mintty", "alacritty", "wezterm-gui" };

    static bool Allowed(string process)
    {
        if (Deny.Exists(d => string.Equals(d, process, StringComparison.OrdinalIgnoreCase))) return false;
        bool listed = Allow.Exists(a => string.Equals(a, process, StringComparison.OrdinalIgnoreCase));
        if (!listed && !Shell && (Array.Exists(Interpreters, i => string.Equals(i, process, StringComparison.OrdinalIgnoreCase)) ||
                        Array.Exists(Terminals, t => string.Equals(t, process, StringComparison.OrdinalIgnoreCase)))) return false;
        return Allow.Count == 0 || listed;
    }

    static WinRef ResolveWindow(string query)
    {
        if (query == null) throw new OrielError("Window is required");
        IntPtr h;
        if (!windowByQuery.TryGetValue(query, out h) || !IsWindow(h) || !IsWindowVisible(h) || !Matches(h, query))
        {
            var wins = Windows();
            Win hit = wins.Find(w => w.Handle.ToString() == query)
                ?? wins.Find(w => string.Equals(w.Process, query, StringComparison.OrdinalIgnoreCase))
                ?? wins.Find(w => w.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            if (hit == null) throw new OrielError("no window matches '" + query + "'" + (wins.Count == 0 ? ", and no windows are open" :
                "; the open windows are:\n" + string.Join("\n", wins.ConvertAll(w => Describe(w)))));
            h = windowByQuery[query] = hit.Handle;
        }
        var process = ProcessOf(h);
        if (!Allowed(process)) throw new OrielError("\"" + Title(h) + "\" (" + process + ") is off limits by policy");
        Mark("window");
        return new WinRef { H = h };
    }

    static U.IUIAutomationElement ElementOf(IntPtr h)
    {
        U.IUIAutomationElement el;
        if (!elementByWindow.TryGetValue(h, out el))
        {
            if (elementByWindow.Count > 500) elementByWindow.Clear();
            Evict(h);  // a handle seen before may have been recycled for another window
            el = elementByWindow[h] = Auto.ElementFromHandle(h);
        }
        return el;
    }

    // A window that went away may come back under the same handle: forget what was cached about it.
    static void Evict(IntPtr h)
    {
        elementByWindow.Remove(h);
        lastLook.Remove(h);
        namedByWindow.Remove(h);
    }

    // After an element or window went away somewhere, look every window up afresh (it costs ~1 ms each).
    static void Stale(Exception e)
    {
        var com = e as COMException;
        if (com == null || com.ErrorCode != NotAvailable) return;
        foreach (var h in new List<IntPtr>(elementByWindow.Keys)) Evict(h);
        windowByQuery.Clear();
    }

    static string Header(WinRef w)
    {
        var win = w.El.BuildUpdatedCache(One);
        var r = win.CachedBoundingRectangle;
        var where = IsIconic(w.H) ? "minimised (its controls are hidden: focus it first)"
            : r.right <= r.left ? "without bounds"
            : "at " + r.left + "," + r.top + " " + (r.right - r.left) + "x" + (r.bottom - r.top);
        return "window [" + w.H + "] " + ProcessName((uint)win.CachedProcessId) + " \"" + win.CachedName + "\" " + where;
    }

    // ---------------------------------------------------------------- elements as text

    static string Kind(U.IUIAutomationElement el)
    {
        int t = el.CachedControlType - 50000;
        return t >= 0 && t < Types.Length ? Types[t] : "Custom";
    }

    static int TypeId(string type)
    {
        if (type == null) return 0;
        for (int i = 0; i < Types.Length; i++) if (string.Equals(Types[i], type, StringComparison.OrdinalIgnoreCase)) return 50000 + i;
        return 0;
    }

    static bool Flag(U.IUIAutomationElement el, int prop)
    {
        var v = el.GetCachedPropertyValue(prop);
        return v is bool && (bool)v;
    }

    // Many providers expose a read-only ValuePattern (Explorer's status bar, Chrome's links): not something to set.
    static bool Settable(U.IUIAutomationElement el) { return Flag(el, CanSetP) && !Flag(el, ReadOnlyP) && el.CachedIsPassword == 0; }

    // Chrome gives buttons, check boxes, radio buttons and links a ValuePattern too; typing a value into them means
    // nothing, so "set" is left off for those types.
    static readonly int[] NoTypedValue = { 50000, 50002, 50005, 50011, 50013, 50019, 50024 };

    static List<string> Actions(U.IUIAutomationElement el)
    {
        var a = new List<string>();
        if (Flag(el, CanInvokeP)) a.Add("invoke");
        if (Flag(el, CanToggleP)) a.Add("toggle");
        if (Flag(el, CanSelectP)) a.Add("select");
        if (Flag(el, CanExpandP)) a.Add("expand");
        if ((Settable(el) && Array.IndexOf(NoTypedValue, el.CachedControlType) < 0) || (Flag(el, CanRangeP) && !Flag(el, RangeReadOnlyP))) a.Add("set");
        if (Flag(el, CanScrollP)) a.Add("scroll");
        return a;
    }

    // What a control of this type normally does; lines only spell out actions when they differ from it.
    static string Implied(int controlType)
    {
        switch (controlType)
        {
            case 50000: case 50005: case 50011: case 50031: return "invoke";  // Button, Hyperlink, MenuItem, SplitButton
            case 50002: return "toggle";  // CheckBox
            case 50007: case 50013: case 50019: case 50024: case 50029: return "select";  // ListItem, RadioButton, TabItem, TreeItem, DataItem
            case 50003: return "expand";  // ComboBox
            case 50004: case 50015: return "set";  // Edit, Slider
            default: return "";
        }
    }

    static bool Shown(U.IUIAutomationElement el) { return !string.IsNullOrEmpty(el.CachedName) || Actions(el).Count > 0; }

    static readonly string[] ToggleStates = { "off", "on", "indeterminate" };
    static readonly System.Text.RegularExpressions.Regex Spaces = new System.Text.RegularExpressions.Regex(@"\s+");
    // Automation ids that are only a counter (Win32 control ids, Chrome's view_1012) carry nothing for a reader.
    static readonly System.Text.RegularExpressions.Regex Generated = new System.Text.RegularExpressions.Regex(@"^(view_)?\d+$");

    // One element without its ref: type "name" id=.. value=".." state {actions}.
    static string Body(U.IUIAutomationElement el)
    {
        var line = new StringBuilder(Kind(el));
        var name = el.CachedName ?? "";
        if (name != "") line.Append(" \"").Append(Spaces.Replace(name, " ")).Append('"');
        var aid = el.CachedAutomationId ?? "";
        if (aid != "" && !Generated.IsMatch(aid)) line.Append(" id=").Append(aid);
        if (el.CachedIsPassword != 0) line.Append(" (password)");
        else if (Flag(el, CanSetP) && !Flag(el, CanRangeP))
        {
            var v = el.GetCachedPropertyValue(ValueP) as string;
            if (!string.IsNullOrEmpty(v) && v != name) line.Append(" value=\"").Append(Spaces.Replace(v.Length > 80 ? v.Substring(0, 80) + "..." : v, " ")).Append('"');
        }
        if (Flag(el, CanRangeP))
        {
            var rv = el.GetCachedPropertyValue(RangeValueP);
            if (rv is double) line.Append(" value=").Append(((double)rv).ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" (").Append(Convert.ToDouble(el.GetCachedPropertyValue(RangeMinP)).ToString("0.##", CultureInfo.InvariantCulture))
                .Append("..").Append(Convert.ToDouble(el.GetCachedPropertyValue(RangeMaxP)).ToString("0.##", CultureInfo.InvariantCulture)).Append(')');
        }
        if (Flag(el, CanToggleP))
        {
            var t = el.GetCachedPropertyValue(ToggleStateP);
            if (t is int && (int)t >= 0 && (int)t < 3) line.Append(' ').Append(ToggleStates[(int)t]);
        }
        if (Flag(el, CanSelectP) && Flag(el, IsSelectedP)) line.Append(" selected");
        if (Flag(el, CanExpandP))
        {
            var s = el.GetCachedPropertyValue(ExpandStateP);
            if (s is int && (int)s == 0) line.Append(" collapsed");
            else if (s is int && (int)s == 1) line.Append(" expanded");
        }
        if (el.CachedIsEnabled == 0) line.Append(" (disabled)");
        if (Flag(el, IsOffscreenP)) line.Append(" (off screen)");
        if (Flag(el, CanScrollP))
        {
            // Where a scrollable area stands, so the caller knows there is more than the tree shows.
            var at = el.GetCachedPropertyValue(VScrollPercentP);
            var view = el.GetCachedPropertyValue(VViewSizeP);
            if (at is double && view is double && (double)view < 100 && (double)at >= 0)
                line.Append(" (scrolled ").Append(((double)at).ToString("0", CultureInfo.InvariantCulture)).Append('%')
                    .Append((double)at < 99.5 ? ", more below" : "").Append((double)at > 0.5 ? ", more above" : "").Append(')');
        }
        var acts = string.Join(",", Actions(el));
        var usual = Implied(el.CachedControlType);
        // A list or tree item without actions is just a container (web pages are full of them): no "{none}" there.
        if (acts != usual && !(acts == "" && usual == "select")) line.Append(" {").Append(acts == "" ? "none" : acts).Append('}');
        return line.ToString();
    }

    static string Format(Node n, int indent) { return new string(' ', indent * 2) + "[" + Label(n) + "] " + Body(n.El); }

    // ---------------------------------------------------------------- refs

    // Refs: the daemon names each element it shows (e.g. "kq17") and keeps the element and its window, so a later
    // call can act on it directly (no walk). The same element keeps its ref, found by UIA's runtime id plus type and
    // automation id. Runtime ids can be reused once an element is gone, so a full walk of a window retires the refs
    // of its elements that are no longer there: a later element with the recycled id gets a new ref. Numbers never
    // restart, and the three letters come from a generation counter kept in %LOCALAPPDATA%\oriel, which goes up at
    // every daemon start (10,648 starts before a prefix repeats), so a ref from before a restart fails instead of
    // naming whatever got that number this time. One-shot runs have no memory, so they print position paths.
    static bool refs;
    static int nextRef;
    static string RefPrefix = "";  // set by Serve: one-shot runs print paths and need none
    static readonly Dictionary<IntPtr, List<string>> keysByWindow = new Dictionary<IntPtr, List<string>>();

    static string NewPrefix()
    {
        const string letters = "abcdfghjkmnpqrstuvwxyz";
        Directory.CreateDirectory(Home);
        var file = Path.Combine(Home, "generation");
        int generation;
        if (!File.Exists(file) || !int.TryParse(File.ReadAllText(file).Trim(), out generation)) generation = new Random().Next(10648);
        int n = letters.Length;
        generation = (generation + 1) % (n * n * n);  // 10,648 daemon starts before a prefix comes back
        File.WriteAllText(file, generation.ToString());
        return "" + letters[generation / (n * n)] + letters[generation / n % n] + letters[generation % n];
    }
    static IntPtr walking;  // the window whose elements are being labelled
    static readonly Dictionary<string, string> refByKey = new Dictionary<string, string>();
    static readonly Dictionary<string, U.IUIAutomationElement> elementByRef = new Dictionary<string, U.IUIAutomationElement>();
    static readonly Dictionary<string, IntPtr> windowByRef = new Dictionary<string, IntPtr>();
    static readonly Dictionary<string, string> keyByRef = new Dictionary<string, string>();
    // Per window, the named elements of its last walk (tree, change report, find miss). A lookup by a name that
    // exactly one element had there (of the right type) skips the search (~15 ms) once
    // the element checks out as still there, enabled, on screen and unchanged in name. Batches of "invoke Seven,
    // Plus, Eight" hit this all the time.
    static readonly Dictionary<IntPtr, List<Node>> namedByWindow = new Dictionary<IntPtr, List<Node>>();

    static void IndexNames(IntPtr h, List<Node> nodes)
    {
        if (namedByWindow.Count > 200) namedByWindow.Clear();
        namedByWindow[h] = nodes.FindAll(n => !string.IsNullOrEmpty(n.El.CachedName));
    }

    static U.IUIAutomationElement Indexed(WinRef w, string name, string type)
    {
        List<Node> named;
        if (name == null || !namedByWindow.TryGetValue(w.H, out named)) return null;
        // Exact names only: a partial match that was unique then can be beaten by an exact match that appeared since.
        var exact = Match(named, name, type).FindAll(n => string.Equals(n.El.CachedName, name, StringComparison.OrdinalIgnoreCase));
        if (exact.Count != 1) return null;
        var pick = exact[0];
        U.IUIAutomationElement el;
        try { el = pick.El.BuildUpdatedCache(One); }
        catch (COMException) { return null; }
        if (!string.Equals(el.CachedName, pick.El.CachedName, StringComparison.Ordinal) || el.CachedIsEnabled == 0 || Flag(el, IsOffscreenP)) return null;
        Mark("indexed");
        return el;
    }

    static string Key(U.IUIAutomationElement el)
    {
        var rid = el.GetCachedPropertyValue(RuntimeIdP) as int[];
        if (rid == null) return null;
        return string.Join(".", Array.ConvertAll(rid, i => i.ToString())) + "|" + el.CachedControlType + "|" + el.CachedAutomationId;
    }

    static string Label(Node n)
    {
        if (!refs) return n.Path;
        var key = Key(n.El);
        string r;
        if (key == null || !refByKey.TryGetValue(key, out r))
        {
            if (elementByRef.Count >= 50000) { refByKey.Clear(); elementByRef.Clear(); windowByRef.Clear(); keysByWindow.Clear(); keyByRef.Clear(); }
            r = RefPrefix + (++nextRef);
            if (key != null)
            {
                refByKey[key] = r;
                keyByRef[r] = key;
                List<string> keys;
                if (!keysByWindow.TryGetValue(walking, out keys)) keysByWindow[walking] = keys = new List<string>();
                keys.Add(key);
            }
        }
        // A ref keeps the element it was first given: an element that replaced it under a recycled runtime id must
        // not inherit it. A dead element shows when the ref is used (RefElement), which then retires the ref.
        if (!elementByRef.ContainsKey(r)) elementByRef[r] = n.El;
        windowByRef[r] = walking;
        return r;
    }

    static bool IsRef(string id) { return id != null && id.Length > 0 && char.IsLetter(id[0]); }

    // After a full walk of window h: forget the refs of its elements that the walk no longer found.
    static void Retire(IntPtr h, List<Node> nodes)
    {
        List<string> keys;
        if (!refs || !keysByWindow.TryGetValue(h, out keys)) return;
        var seen = new Dictionary<string, bool>();
        foreach (var n in nodes) { var k = Key(n.El); if (k != null) seen[k] = true; }
        var kept = new List<string>();
        foreach (var k in keys)
        {
            string r;
            if (seen.ContainsKey(k)) kept.Add(k);
            else if (refByKey.TryGetValue(k, out r)) Forget(r);
        }
        keysByWindow[h] = kept;
    }

    static void Forget(string r)
    {
        string key;
        if (keyByRef.TryGetValue(r, out key)) refByKey.Remove(key);
        keyByRef.Remove(r);
        elementByRef.Remove(r);
        windowByRef.Remove(r);
    }

    static U.IUIAutomationElement RefElement(string id)
    {
        if (!id.StartsWith(RefPrefix, StringComparison.Ordinal)) throw new OrielError(id + " is from before the daemon restarted; run tree or find again");
        U.IUIAutomationElement known;
        if (!elementByRef.TryGetValue(id, out known)) throw new OrielError("unknown ref " + id + "; run tree or find again");
        try { return known.BuildUpdatedCache(One); }
        catch (COMException e)
        {
            if (e.ErrorCode != NotAvailable) throw;
            Forget(id);
            throw new OrielError(id + " is gone (the window changed); run tree or find again");
        }
    }

    // ---------------------------------------------------------------- finding

    // Depth-first over the cached subtree, children numbered by position: "1.0.2" is the third child of the first
    // child of the second child of the window. Id paths walk the same numbering.
    static List<Node> Walk(WinRef w, int maxDepth)
    {
        var root = w.El.BuildUpdatedCache(Subtree);
        walking = w.H;
        var output = new List<Node>();
        var stack = new Stack<Node>();
        stack.Push(new Node { El = root, Path = "", Depth = 0 });
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Depth > 0) output.Add(n);
            if (n.Depth >= maxDepth) continue;
            var kids = n.El.GetCachedChildren();
            if (kids == null) continue;
            for (int i = kids.Length - 1; i >= 0; i--)
                stack.Push(new Node { El = kids.GetElement(i), Path = n.Depth == 0 ? i.ToString() : n.Path + "." + i, Depth = n.Depth + 1 });
        }
        IndexNames(w.H, output);
        if (maxDepth >= FullDepth) Retire(w.H, output);
        Mark("walk");
        return output;
    }

    // A type that names a control type matches it exactly, as the provider-side search does (Button is not RadioButton);
    // anything else matches the end of the type's name (Item: ListItem, TabItem, TreeItem, ...).
    static List<Node> Match(List<Node> nodes, string name, string type)
    {
        int typeId = TypeId(type);
        return nodes.FindAll(n => (name == null || (n.El.CachedName ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (type == null || (typeId != 0 ? n.El.CachedControlType == typeId : Kind(n.El).EndsWith(type, StringComparison.OrdinalIgnoreCase))));
    }

    // Elements whose name contains `name`. The daemon lets UIA match inside the app (IgnoreCase | MatchSubstring,
    // Windows 10 1809+), so only the matches cross over instead of the whole window; one-shot runs walk, for paths.
    static List<Node> Candidates(WinRef w, string name, string type, int depth)
    {
        if (!refs) return Match(Walk(w, depth), name, type);
        int typeId = TypeId(type);
        // Off-screen elements count too (a link below the fold, an item further down a list): actions on them work
        // without scrolling, and their lines say "(off screen)".
        var cond = Auto.ControlViewCondition;
        if (name != null) cond = Auto.CreateAndCondition(cond, Auto.CreatePropertyConditionEx(NameP, name, (U.PropertyConditionFlags)3));
        if (typeId != 0) cond = Auto.CreateAndCondition(cond, Auto.CreatePropertyCondition(ControlTypeP, typeId));
        var found = w.El.FindAllBuildCache(U.TreeScope.TreeScope_Descendants, cond, One);
        walking = w.H;
        var list = new List<Node>();
        if (found != null) for (int i = 0; i < found.Length; i++) list.Add(new Node { El = found.GetElement(i) });
        Mark("find");
        return typeId != 0 || type == null ? list : Match(list, null, type);
    }

    static U.IUIAutomationElement Best(List<Node> hits, string name)
    {
        // An exact name beats a partial one, and enabled beats disabled: "Clear" hits the Clear button, not a disabled
        // "Clear all memory" before it; "Copy" hits the Copy button (and says it is disabled), not the field label
        // "Characters to copy".
        Predicate<Node> exact = n => string.Equals(n.El.CachedName, name, StringComparison.OrdinalIgnoreCase);
        Predicate<Node> usable = n => n.El.CachedIsEnabled != 0 && !Flag(n.El, IsOffscreenP);
        var best = hits.Find(n => exact(n) && usable(n)) ?? hits.Find(n => exact(n) && n.El.CachedIsEnabled != 0) ?? hits.Find(exact)
            ?? hits.Find(usable) ?? hits.Find(n => n.El.CachedIsEnabled != 0) ?? (hits.Count > 0 ? hits[0] : null);
        return best == null ? null : best.El;
    }

    static U.IUIAutomationElement PathElement(WinRef w, string id)
    {
        var el = w.El.BuildUpdatedCache(Subtree);
        foreach (var part in id.Split('.'))
        {
            int i;
            var kids = el.GetCachedChildren();
            if (!int.TryParse(part, out i) || kids == null || i >= kids.Length) return null;
            el = kids.GetElement(i);
        }
        return el;
    }

    // The element an action is for: a ref needs no window; a name or path is looked up in Window.
    static U.IUIAutomationElement Target(Opts o)
    {
        if (IsRef(o.S("Id"))) return RefElement(o.S("Id"));
        return FindTarget(ResolveWindow(o.S("Window")), o);
    }

    // Controls can appear a moment after the window or the action that brings them (Calculator right after launch,
    // the next page after an invoke), so a miss retries for up to 3 s when a launch, invoke or keys call ran in the
    // last 2 s (later steps of a batch), else for up to 0.8 s. A miss lists the closest names, so the caller can
    // correct itself without a tree call.
    static DateTime lastChange = DateTime.MinValue;
    // reported: a change report on w follows, so a walk made here serves as its look before the action.
    static U.IUIAutomationElement FindTarget(WinRef w, Opts o, bool reported = false)
    {
        var id = o.S("Id");
        var name = o.S("Name");
        if (IsRef(id)) return RefElement(id);
        if (id == null && name == null) throw new OrielError("give Id or Name");
        Func<U.IUIAutomationElement> attempt = () =>
            id != null ? PathElement(w, id) : Indexed(w, name, o.S("Type")) ?? (reported ? FromNewLook(w, name, o.S("Type")) : null) ??
            Best(Candidates(w, name, o.S("Type"), o.I("Depth", 12)), name);
        var found = attempt();
        if (found != null) return found;
        bool settling = (DateTime.UtcNow - lastChange).TotalSeconds < 2;
        var start = DateTime.UtcNow;
        var deadline = start.AddMilliseconds(settling ? 3000 : 800);
        // Retry on the window's own events: a window that has been silent for 150 ms (and no action of ours is still
        // landing) is not about to grow the control, so the miss is answered then rather than after the full wait.
        found = Listening(w.H, () =>
        {
            while (true)
            {
                var el = attempt();
                if (el != null) return el;
                if (DateTime.UtcNow > deadline || (!settling && eventCount == 0 && (DateTime.UtcNow - start).TotalMilliseconds >= 150)) return null;
                int seen = eventCount;
                Pump(50, () => eventCount != seen);
            }
        });
        if (found != null) return found;
        if (id != null) throw new OrielError("id " + id + " no longer exists; run tree again");
        var type = o.S("Type");
        throw new OrielError("no element named like '" + name + "'" + (type == null ? "" : " of type " + type) +
            " in this window." + Closest(name, Walk(w, o.I("Depth", 12))));
    }

    // A window not looked at in the last 120 s gets walked by the change report before the action anyway; walking it
    // here instead finds an exactly named target in that same walk, rather than in a search of its own first (a search
    // costs about as much as a walk: 2 s in Explorer on a slow VM). The walk sees on-screen controls only, so anything
    // short of an enabled match there (none, or only disabled ones while an enabled one may be off screen) is searched
    // for as before, and so is everything when the walk times out on a busy app.
    static U.IUIAutomationElement FromNewLook(WinRef w, string name, string type)
    {
        Snap last;
        if (!refs || w.H == unreadable || (lastLook.TryGetValue(w.H, out last) && (DateTime.UtcNow - last.At).TotalSeconds <= 120)) return null;
        try
        {
            var exact = Match(Capture(w).Nodes, name, type).FindAll(n => string.Equals(n.El.CachedName, name, StringComparison.OrdinalIgnoreCase));
            var best = Best(exact, name);
            if (best == null) return null;
            var el = best.BuildUpdatedCache(One);  // every property, as a search result has
            return el.CachedIsEnabled != 0 && !Flag(el, IsOffscreenP) ? el : null;
        }
        catch (Exception e)
        {
            var com = e as COMException;
            if (IsTimeout(e)) unreadable = w.H;  // the change report then acts without walking it again
            if (IsTimeout(e) || (com != null && com.ErrorCode == NotAvailable)) return null;
            throw;
        }
    }

    // A window whose walk timed out during this request (its app is busy or hung).
    static IntPtr unreadable;

    static string Closest(string query, List<Node> nodes)
    {
        var scored = new List<KeyValuePair<int, Node>>();
        var q = query.ToLowerInvariant();
        var seen = new Dictionary<string, bool>();
        foreach (var n in nodes)
        {
            var name = n.El.CachedName ?? "";
            if (name == "" || Actions(n.El).Count == 0 || seen.ContainsKey(name)) continue;
            seen[name] = true;
            scored.Add(new KeyValuePair<int, Node>(Distance(q, name.ToLowerInvariant()) * 100000 + scored.Count, n));
        }
        if (scored.Count == 0) return " It has no named controls (yet); run tree.";
        scored.Sort((a, b) => a.Key.CompareTo(b.Key));
        var sb = new StringBuilder(" Closest:");
        for (int i = 0; i < Math.Min(8, scored.Count); i++)
            sb.Append("\n  ").Append(Format(scored[i].Value, 0));
        return sb.ToString();
    }

    // Edits needed to turn a into some part of b: "rick astlee" is 1 away from "Rick Astley - Wikipedia".
    static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];  // all zeros: a match may start anywhere in b
        var cur = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            var t = prev; prev = cur; cur = t;
        }
        int best = a.Length;
        foreach (var d in prev) best = Math.Min(best, d);
        return best;
    }

    // ---------------------------------------------------------------- what an action changed

    // Change reports and ref retirement look at every level: the Subtree request brings the whole window across
    // anyway, so the depth only bounds local work. (Web pages nest deeper than the tree's default 12.)
    const int FullDepth = 64;

    // The last full look at each window (a tree or a change report). A change report diffs against it instead of
    // walking first, which saves a walk per action and reports what changed since the caller last looked.
    static readonly Dictionary<IntPtr, Snap> lastLook = new Dictionary<IntPtr, Snap>();

    static Snap Remember(IntPtr h, List<Node> nodes)
    {
        if (lastLook.Count > 200) lastLook.Clear();
        return lastLook[h] = new Snap { Nodes = nodes, At = DateTime.UtcNow };
    }

    static Snap Capture(WinRef w) { return Remember(w.H, Walk(w, FullDepth)); }

    // While an action runs, an out-of-context WinEvent hook on all processes (a Store app's content runs in another
    // process than its frame) notes every accessibility event in the target window, plus any new top-level window.
    // The report then walks once, when the events have been quiet for 20 ms, instead of walking repeatedly to see
    // whether anything moved; an action that changes nothing costs 120 ms of listening. Events only decide when to
    // look: the walk decides what changed, so a control that raises no events still shows up in the diff.
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint ms, uint wakeMask);

    static IntPtr watched;
    static int eventCount;
    static long lastEvent;
    static readonly WinEventProc OnEvent = Noted;  // a field, so the delegate outlives every hook that uses it

    static void Noted(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == -8 || idObject == -9) return;  // OBJID_CARET, OBJID_CURSOR: they blink and move all the time
        var root = GetAncestor(hwnd, 2);  // GA_ROOT
        bool newWindow = ev == 0x8002 && idObject == 0 && root == hwnd;  // EVENT_OBJECT_SHOW of a top-level window
        if (root != watched && !newWindow) return;
        eventCount++;
        lastEvent = Stopwatch.GetTimestamp();
    }

    // Listen to window h while body runs, then restore the listener around it: a launch or wait inside a batch that
    // reports its changes must not take over the batch's own listening.
    static T Listening<T>(IntPtr h, Func<T> body)
    {
        IntPtr outer = watched;
        int outerCount = eventCount;
        long outerLast = lastEvent;
        var hook = Listen(h);
        try { return body(); }
        finally { UnhookWinEvent(hook); watched = outer; eventCount = outerCount; lastEvent = outerLast; }
    }

    static IntPtr Listen(IntPtr h)
    {
        watched = h;
        eventCount = 0;
        return SetWinEventHook(0x8000, 0x80FF, IntPtr.Zero, OnEvent, 0, 0, 0x0002);  // object events; OUTOFCONTEXT | SKIPOWNPROCESS
    }

    static double Ms(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }

    // Deliver hook events (they arrive through this thread's message queue) until done() or maxMs.
    static void Pump(int maxMs, Func<bool> done)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            MSG m;
            while (PeekMessage(out m, IntPtr.Zero, 0, 0, 1)) DispatchMessage(ref m);  // PM_REMOVE
            if (done() || Ms(Stopwatch.GetTimestamp() - start) >= maxMs) return;
            MsgWaitForMultipleObjects(0, null, false, 2, 0x04FF);  // QS_ALLINPUT
        }
    }

    // Wait until the watched window has been quiet for quietMs after an event, or firstMs passed without any (counting
    // from now); at most maxMs. False: still busy at the end.
    static bool AwaitQuiet(int firstMs, int quietMs, int maxMs)
    {
        long start = Stopwatch.GetTimestamp();
        int seen = eventCount;
        bool quiet = false;
        Pump(maxMs, () =>
        {
            long now = Stopwatch.GetTimestamp();
            return quiet = eventCount == seen ? Ms(now - start) >= firstMs : Ms(now - lastEvent) >= quietMs;
        });
        Mark("quiet:" + eventCount);
        return quiet;
    }

    static List<string> Changes(Snap a, Snap b)
    {
        var lines = new List<string>();
        foreach (var kv in b.Shown)
        {
            string old;
            if (!a.Body.TryGetValue(kv.Key, out old)) lines.Add("+ " + Format(kv.Value, 0));
            else if (old != b.Body[kv.Key]) lines.Add("~ " + Format(kv.Value, 0));
        }
        foreach (var kv in a.Shown) if (!b.Body.ContainsKey(kv.Key)) lines.Add("- " + a.Body[kv.Key]);
        return lines;
    }

    // Run an action on window w and report what it changed, so the caller rarely needs a tree or find afterwards.
    static string Report(WinRef w, bool wanted, Func<string> act)
    {
        if (!wanted || w == null) return act();
        Snap before;
        if (w.H == unreadable) return act() + "\n(no change report: the app's accessibility interface didn't answer)";
        try { if (!lastLook.TryGetValue(w.H, out before) || (DateTime.UtcNow - before.At).TotalSeconds > 120) before = Capture(w); }
        catch (Exception e)
        {
            // The window can't be read (its app is busy or hung); act anyway, without a report.
            if (!IsTimeout(e)) throw;
            return act() + "\n(no change report: the app's accessibility interface didn't answer)";
        }
        double age = (DateTime.UtcNow - before.At).TotalSeconds;
        var windowsBefore = new Dictionary<IntPtr, bool>();
        foreach (var x in Windows()) windowsBefore[x.Handle] = true;
        Mark("before");
        var hook = Listen(w.H);
        StringBuilder result;
        Snap after = null;
        bool stable;
        try
        {
            result = new StringBuilder(act());
            Mark("act");
            stable = AwaitQuiet(120, 20, 700);
            try
            {
                if (IsWindow(w.H)) after = Capture(w);
                // A browser starts loading a page some 100-200 ms after a click, with nothing to see before that
                // (Chrome has no busy state), and the old page goes before the new one arrives. So in a browser, when
                // nothing changed yet or the page is gone without a new one, listen on (up to 1.5 s) and look again.
                if (after != null && Browsers.Contains(ProcessOf(w.H)) && (Changes(before, after).Count == 0 || Pages(after) < Pages(before)))
                {
                    stable = AwaitQuiet(1500, 60, 3000);
                    after = IsWindow(w.H) ? Capture(w) : null;
                }
            }
            catch (Exception e)
            {
                if (IsTimeout(e)) { UnhookWinEvent(hook); hook = IntPtr.Zero; return result + "\n(no change report: the app's accessibility interface didn't answer)"; }
                var com = e as COMException;
                if (com == null || com.ErrorCode != NotAvailable) throw;
                after = null;  // it closed meanwhile
            }
        }
        finally { if (hook != IntPtr.Zero) UnhookWinEvent(hook); }
        if (after == null) result.Append("\nwindow closed");
        else
        {
            var changes = Changes(before, after);
            if (age >= 5 && changes.Count > 0) result.Append("\n(changes since the last look, " + (int)age + " s ago)");
            if (changes.Count == 0) result.Append("\nno visible change in the window");
            // A changed page or view: its new tree serves better than a long list of changes (and saves a tree call).
            else if (changes.Count > 15) result.Append("\nthe window changed a lot (" + changes.Count + " changes); now:\n").Append(TreeOf(w, after.Nodes, 12, 150));
            else result.Append('\n').Append(string.Join("\n", changes));
            if (!stable) result.Append("\n(still changing)");
        }
        bool first = true;
        foreach (var x in Windows())
        {
            if (windowsBefore.ContainsKey(x.Handle)) continue;
            result.Append("\n+ window ").Append(Describe(x));
            // The window an action opened (a dialog, usually) is what the caller works with next: include its tree.
            if (first) { first = false; try { result.Append('\n').Append(Tree(ResolveWindow(x.Handle.ToString()), 12, 60)); } catch (COMException) { } }
        }
        Mark("diff");
        return result.ToString();
    }

    static readonly List<string> Browsers = new List<string> { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };
    static int Pages(Snap s) { return s.Nodes.FindAll(n => n.El.CachedControlType == DocumentType).Count; }

    static WinRef WindowOf(Opts o)
    {
        var id = o.S("Id");
        IntPtr h;
        if (IsRef(id) && windowByRef.TryGetValue(id, out h) && IsWindow(h)) return ResolveWindow(h.ToString());
        return o.S("Window") == null ? null : ResolveWindow(o.S("Window"));
    }

    static T Pattern<T>(U.IUIAutomationElement el, int id) where T : class { return el.GetCurrentPattern(id) as T; }

    // UIA patterns first (with AutoSetFocus off they take 0-8 ms and leave focus alone), MSAA (the LegacyIAccessible
    // pattern) for elements that offer no pattern but have a default action, which many custom and older controls do.
    static U.IUIAutomationLegacyIAccessiblePattern Legacy(U.IUIAutomationElement el)
    {
        return Pattern<U.IUIAutomationLegacyIAccessiblePattern>(el, LegacyPat);
    }

    static string Invoke(U.IUIAutomationElement el)
    {
        lastChange = DateTime.UtcNow;
        var name = "\"" + el.CachedName + "\"";
        U.IUIAutomationInvokePattern inv; U.IUIAutomationTogglePattern tog; U.IUIAutomationSelectionItemPattern sel; U.IUIAutomationExpandCollapsePattern exp;
        if ((inv = Pattern<U.IUIAutomationInvokePattern>(el, InvokePat)) != null) { inv.Invoke(); return "invoked " + name; }
        if ((tog = Pattern<U.IUIAutomationTogglePattern>(el, TogglePat)) != null)
        {
            tog.Toggle();
            return "toggled " + name;  // the new state may not be there yet (Chrome): the change report shows it
        }
        if ((sel = Pattern<U.IUIAutomationSelectionItemPattern>(el, SelectPat)) != null) { sel.Select(); return "selected " + name; }
        if ((exp = Pattern<U.IUIAutomationExpandCollapsePattern>(el, ExpandPat)) != null)
        {
            if (exp.CurrentExpandCollapseState == U.ExpandCollapseState.ExpandCollapseState_Collapsed) { exp.Expand(); return "expanded " + name; }
            exp.Collapse(); return "collapsed " + name;
        }
        var legacy = Legacy(el);
        var verb = legacy == null ? null : legacy.CurrentDefaultAction;
        if (!string.IsNullOrEmpty(verb)) { legacy.DoDefaultAction(); return verb.ToLowerInvariant() + " " + name; }
        throw new OrielError(name + " has no invoke/toggle/select/expand pattern or default action; use click on it (the mouse, at its center)");
    }

    static string SetValue(U.IUIAutomationElement el, string value)
    {
        if (el.CachedIsPassword != 0) throw new OrielError("refusing to fill a password field");
        if (!Settable(el) && Flag(el, CanRangeP) && !Flag(el, RangeReadOnlyP))
        {
            double number;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                throw new OrielError("\"" + el.CachedName + "\" is a slider: give a number");
            Pattern<U.IUIAutomationRangeValuePattern>(el, RangePat).SetValue(number);
            return "set \"" + el.CachedName + "\" to " + value;
        }
        if (!Settable(el)) throw new OrielError("\"" + el.CachedName + "\" takes no value; use keys");
        Pattern<U.IUIAutomationValuePattern>(el, ValuePat).SetValue(value);
        return "set \"" + el.CachedName + "\"";
    }

    // SendKeys units: modifiers (+ ^ %) with the key, group or character they apply to; {..} keys; ~; and runs of
    // plain characters. "{+}"-style escapes, plain runs and a bare ( ) [ ] } type text; everything else may move focus.
    static List<string> KeyUnits(string keys)
    {
        var units = new List<string>();
        int i = 0;
        while (i < keys.Length)
        {
            int start = i;
            while (i < keys.Length && "+^%".IndexOf(keys[i]) >= 0) i++;
            bool modified = i > start;
            if (i < keys.Length)
            {
                char c = keys[i];
                if (c == '{') { int close = keys.IndexOf('}', i + 2); i = close < 0 ? keys.Length : close + 1; }  // "{}}" is a key too
                else if (c == '(' && modified) { int close = keys.IndexOf(')', i); i = close < 0 ? keys.Length : close + 1; }
                else if (!modified && "~()[]}".IndexOf(c) < 0) while (i < keys.Length && "+^%~{}()[]".IndexOf(keys[i]) < 0) i++;
                else i++;
            }
            units.Add(keys.Substring(start, i - start));
        }
        return units;
    }

    static bool TypesText(string unit)
    {
        if (unit.Length == 3 && unit[0] == '{' && unit[2] == '}') return true;  // an escaped character such as {+}
        return "+^%~{".IndexOf(unit[0]) < 0;  // a bare ( ) [ ] } types itself: only a modifier's ( opens a group
    }

    // Units that stay in the focused field: text, escaped characters, a character repeated ({a 5}), editing and caret
    // keys (repeated too: +{DOWN 4}), and Ctrl+A/C/X/V/Z/Y. Arrows, page keys and Ctrl+Home/End move the caret or the
    // selection, or the item within the same list or group, never to another control. Everything else (TAB, ENTER,
    // ESC, function keys, Alt and other Ctrl combinations such as Ctrl+W or Ctrl+L) may move the focus or change what
    // holds it. (Agents editing LibreOffice Writer documents were refused "+{END} after +{DOWN 4}" and "+{RIGHT} after
    // ^{HOME}" under the earlier, shorter list.)
    static readonly string[] FieldKeys = { "{BACKSPACE}", "{BS}", "{BKSP}", "{DEL}", "{DELETE}", "{INS}", "{INSERT}",
        "{LEFT}", "{RIGHT}", "{UP}", "{DOWN}", "{HOME}", "{END}", "{PGUP}", "{PGDN}",
        "+{LEFT}", "+{RIGHT}", "+{UP}", "+{DOWN}", "+{HOME}", "+{END}", "+{PGUP}", "+{PGDN}",
        "^{LEFT}", "^{RIGHT}", "^{UP}", "^{DOWN}", "^{HOME}", "^{END}",
        "^+{LEFT}", "^+{RIGHT}", "^+{UP}", "^+{DOWN}", "^+{HOME}", "^+{END}", "^a", "^c", "^x", "^v", "^z", "^y" };
    static readonly System.Text.RegularExpressions.Regex TabKey = new System.Text.RegularExpressions.Regex(@"^\+?\{TAB( \d+)?\}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex Enter = new System.Text.RegularExpressions.Regex(@"^(~|\{ENTER( \d+)?\})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex RepeatCount = new System.Text.RegularExpressions.Regex(@" \d+\}$");
    static readonly System.Text.RegularExpressions.Regex Repeat = new System.Text.RegularExpressions.Regex(@"^\{[^{}\s]\s+\d+\}$");

    static bool InField(string unit)
    {
        if (TypesText(unit) || Repeat.IsMatch(unit)) return true;
        if (unit.StartsWith("+(") && unit.Length > 3 && unit.Substring(2, unit.Length - 3).IndexOfAny("{}~+^%()".ToCharArray()) < 0)
            return true;  // +(abc) types "ABC"; anything else inside a group is a key or modifier
        var bare = RepeatCount.Replace(unit, "}");  // +{DOWN 4} counts as +{DOWN}
        foreach (var k in FieldKeys) if (string.Equals(bare, k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // After the focus may have moved, only these may follow: keys that move around or confirm and can't type or paste
    // anything (a whitelist, because Shift+Insert pastes, keypad {ADD} types "+", and ^v, +a or +(abc) type too).
    static readonly System.Text.RegularExpressions.Regex Navigation = new System.Text.RegularExpressions.Regex(
        @"^(\+?\{TAB( \d+)?\}|~|\{(ENTER|ESC|UP|DOWN|LEFT|RIGHT|HOME|END|PGUP|PGDN|F\d{1,2})( \d+)?\})$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int caretLeft, caretTop, caretRight, caretBottom;
    }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

    // The window's thread moves focus to its control when it handles the activation, after SetForegroundWindow has
    // returned: wait (up to 300 ms) until its focused HWND is inside this window.
    static bool FocusInside(IntPtr h, out IntPtr focus)
    {
        uint pid, thread = GetWindowThreadProcessId(h, out pid);
        var deadline = DateTime.UtcNow.AddMilliseconds(300);
        while (true)
        {
            var info = new GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(info);
            focus = info.hwndFocus;
            if (GetGUIThreadInfo(thread, ref info) && (focus = info.hwndFocus) != IntPtr.Zero && GetAncestor(focus, 2) == h) return true;
            if (DateTime.UtcNow > deadline) return false;
            Thread.Sleep(5);
        }
    }

    // Keys go to whatever has focus, and SendKeys doesn't wait for the app to handle them, so no check made between
    // keys can be trusted. Instead the focus is checked once, before anything is sent (the window in front, the focus
    // inside it, not a password field), and a call may hold text only before its first key that can move the focus:
    // "hello{ENTER}" and "^ahello" are fine, "{TAB}secret" and "^wsecret" must be split into two calls.
    static string SendKeysTo(WinRef w, Opts o, string keys)
    {
        var units = KeyUnits(keys);
        foreach (var c in keys)
            if (char.IsControl(c)) throw new OrielError("a control character (tab, newline) types a key; write {TAB} or {ENTER} instead; nothing was sent");
        // A group holds its modifier across every key in it: ^(wv) is Ctrl+W then Ctrl+V. Only +(abc) (capitals) stays.
        if (units.Exists(u => u.IndexOf('(') > 0 && !InField(u)))
            throw new OrielError("only +(...) with plain characters may group keys; write each shortcut on its own, e.g. ^w^v; nothing was sent");
        int moves = units.FindIndex(u => !InField(u));
        int after = moves < 0 ? -1 : units.FindIndex(moves + 1, u => !Navigation.IsMatch(u));
        // Enter is a new line in a document but can press a dialog's default button: text after it is checked
        // against the focused element below, once that is known.
        bool enterOnly = after >= 0 && units.TrueForAll(u => InField(u) || Enter.IsMatch(u) || TabKey.IsMatch(u));
        string refused = after < 0 ? null : units[after] + " after " + units[moves] + " could type into wherever the focus went; only {TAB} {ENTER} {ESC} arrows {PGUP} {PGDN} and F-keys may follow it; send the rest in its own keys call (or use set); nothing was sent";
        if (refused != null && !enterOnly) throw new OrielError(refused);
        Foreground(w);
        if (o.S("Id") != null || o.S("Name") != null)
        {
            var el = FindTarget(w, o);
            IntPtr owner;
            if (IsRef(o.S("Id")) && (!windowByRef.TryGetValue(o.S("Id"), out owner) || owner != w.H))
                throw new OrielError(o.S("Id") + " is not in that window; nothing was sent");
            if (el.CachedIsPassword != 0) throw new OrielError("refusing to type into a password field");
            el.SetFocus();
        }
        // Last checks, right before sending (finding the element and focusing it took time): the window is still in
        // front, its thread's focus is inside it, and the element UIA calls focused belongs to that focus's process
        // and is not a password field.
        IntPtr focusHwnd;
        if (GetForegroundWindow() != w.H || !FocusInside(w.H, out focusHwnd)) throw new OrielError("the window lost the foreground or the focus; nothing was sent");
        uint focusPid;
        GetWindowThreadProcessId(focusHwnd, out focusPid);
        U.IUIAutomationElement focused = null;
        try
        {
            // One cross-process call for the element and the properties checked here and after sending.
            focused = AutoQuick.GetFocusedElementBuildCache(FocusCheck);
            if (focused == null || focused.CachedProcessId != (int)focusPid) throw new OrielError("the focus is not where the window says; nothing was sent");
            if (focused.CachedIsPassword != 0) throw new OrielError("the focused field is a password field; nothing was sent");
        }
        catch (Exception e)
        {
            // The app's UIA provider is busy or hung, but its window has the foreground and its own focus (checked
            // above). Win32 still tells a classic password box; anything else gets the keys.
            if (!IsTimeout(e)) throw;
            focused = null;
            var cls = new StringBuilder(64);
            GetClassName(focusHwnd, cls, cls.Capacity);
            if (cls.ToString().IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0 && (GetWindowLong(focusHwnd, -16) & 0x20) != 0)  // ES_PASSWORD
                throw new OrielError("the focused field is a password field; nothing was sent");
        }
        // In a document Enter starts a line and Tab inserts a tab; in a spreadsheet's grid both move to the next cell.
        // Anywhere else (a dialog, a form) they may move the focus to another field, so text may not follow them.
        int focusType = focused == null ? 0 : focused.CachedControlType;
        bool cells = focusType == 50036 || focusType == 50029 || focusType == 50028;  // Table, DataItem, DataGrid
        if (refused != null && focusType != DocumentType && !cells)
            throw new OrielError(refused + " (text may follow {ENTER} or {TAB} only in a document or a spreadsheet's cells)");
        TypeUnits(units);
        lastSend = Environment.TickCount;
        lastChange = DateTime.UtcNow;
        // Text sent while the focus sits on a tab, button or bare window usually goes nowhere, and the next key (^s)
        // then acts on the empty field: a benchmark agent saved an empty file after typing into freshly launched
        // Notepad, whose tab still had the focus. Say so instead of leaving only "no visible change".
        if (focused == null) return "sent " + keys + " (the app's accessibility interface didn't answer, so the focus was checked through Win32 only)";
        // From the check before sending: asking the app now would wait until it has handled every key.
        if (units.Exists(TypesText) && Array.IndexOf(NoText, focusType) >= 0)
            return "sent " + keys + "\nnote: the focus was on " + Types[focusType - 50000] + " \"" + focused.CachedName +
                "\", which doesn't take text; check the text arrived, or pass Id or Name to focus the field first";
        return "sent " + keys;
    }

    // Button, CheckBox, Hyperlink, Image, MenuItem, RadioButton, Tab, TabItem, Text, ToolBar, SplitButton, Window, TitleBar.
    static readonly int[] NoText = { 50000, 50002, 50005, 50006, 50011, 50013, 50018, 50019, 50020, 50021, 50031, 50032, 50037 };

    // Scroll an element into view (ScrollItemPattern), or scroll a container (its own ScrollPattern, else the window's
    // first scrollable one) by a page or a line.
    static string Scroll(WinRef w, U.IUIAutomationElement el, string direction, string amount)
    {
        lastChange = DateTime.UtcNow;
        if (el != null && Flag(el, CanScrollItemP) && !Flag(el, CanScrollP))
        {
            Pattern<U.IUIAutomationScrollItemPattern>(el, ScrollItemPat).ScrollIntoView();
            return "scrolled \"" + el.CachedName + "\" into view";
        }
        if (el == null)
        {
            if (w == null) throw new OrielError("give Window, Id or Name");
            var box = Walk(w, FullDepth).Find(n => Flag(n.El, CanScrollP));
            if (box == null) throw new OrielError("nothing in this window scrolls");
            el = box.El;
        }
        var sp = Pattern<U.IUIAutomationScrollPattern>(el, ScrollPat);
        if (sp == null) throw new OrielError("\"" + el.CachedName + "\" doesn't scroll");
        var step = amount == "line" ? U.ScrollAmount.ScrollAmount_SmallIncrement : U.ScrollAmount.ScrollAmount_LargeIncrement;
        var back = amount == "line" ? U.ScrollAmount.ScrollAmount_SmallDecrement : U.ScrollAmount.ScrollAmount_LargeDecrement;
        var none = U.ScrollAmount.ScrollAmount_NoAmount;
        switch (direction)
        {
            case "down": sp.Scroll(none, step); break;
            case "up": sp.Scroll(none, back); break;
            case "right": sp.Scroll(step, none); break;
            case "left": sp.Scroll(back, none); break;
            default: throw new OrielError("Direction is up, down, left or right");
        }
        return "scrolled " + direction + " a " + (amount == "line" ? "line" : "page") + " in \"" + el.CachedName + "\"";
    }

    // Pick an item by its text in a combo box, list, tab strip or menu: open it if it opens, select (or invoke) the
    // item (an exact name first, else one that contains the text), then close it again.
    static string SelectOption(U.IUIAutomationElement box, string option)
    {
        lastChange = DateTime.UtcNow;
        var exp = Pattern<U.IUIAutomationExpandCollapsePattern>(box, ExpandPat);
        if (exp != null && exp.CurrentExpandCollapseState == U.ExpandCollapseState.ExpandCollapseState_Collapsed) exp.Expand();
        var cond = Auto.CreatePropertyConditionEx(NameP, option, (U.PropertyConditionFlags)3);
        U.IUIAutomationElement pick = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(1500);  // a drop-down fills in after it opens
        while (pick == null && DateTime.UtcNow < deadline)
        {
            var found = box.FindAllBuildCache(U.TreeScope.TreeScope_Descendants, cond, One);
            for (int i = 0; found != null && i < found.Length; i++)
            {
                var el = found.GetElement(i);
                if (Actions(el).Count == 0) continue;
                if (string.Equals(el.CachedName, option, StringComparison.OrdinalIgnoreCase)) { pick = el; break; }
                if (pick == null) pick = el;
            }
            if (pick == null) Thread.Sleep(50);
        }
        if (pick == null)
        {
            if (exp != null) try { exp.Collapse(); } catch (COMException) { }
            throw new OrielError("no item like '" + option + "' in \"" + box.CachedName + "\"");
        }
        var sel = Pattern<U.IUIAutomationSelectionItemPattern>(pick, SelectPat);
        if (sel != null) sel.Select(); else Invoke(pick);
        if (exp != null) try { if (exp.CurrentExpandCollapseState == U.ExpandCollapseState.ExpandCollapseState_Expanded) exp.Collapse(); } catch (COMException) { }
        return "selected \"" + pick.CachedName + "\" in \"" + box.CachedName + "\"";
    }

    static string WindowState(WinRef w, Opts o)
    {
        var title = Title(w.H);
        var state = o.S("State");
        if (state != null)
        {
            var wp = Pattern<U.IUIAutomationWindowPattern>(w.El, WindowPat);
            if (wp == null) throw new OrielError("\"" + title + "\" has no WindowPattern");
            if (state == "minimize") wp.SetWindowVisualState(U.WindowVisualState.WindowVisualState_Minimized);
            else if (state == "maximize") wp.SetWindowVisualState(U.WindowVisualState.WindowVisualState_Maximized);
            else if (state == "restore") wp.SetWindowVisualState(U.WindowVisualState.WindowVisualState_Normal);
            else throw new OrielError("State is minimize, maximize or restore");
        }
        if (o.S("X") != null || o.S("Width") != null)
        {
            var tp = Pattern<U.IUIAutomationTransformPattern>(w.El, TransformPat);
            if (tp == null) throw new OrielError("\"" + title + "\" can't be moved or resized");
            if (o.S("X") != null) tp.Move(o.D("X", 0), o.D("Y", 0));
            if (o.S("Width") != null) tp.Resize(o.D("Width", 800), o.D("Height", 600));
        }
        return Header(w);
    }

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    // ---------------------------------------------------------------- mouse

    // click and drag send real mouse input, as keys sends real keystrokes (for what has no UIA pattern: a sheet's
    // cells in LibreOffice, a canvas, a drawing), so they get the same checks: the PC unlocked, the user idle, the
    // window verified in front, and the point inside it and not covered by another window.
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    // Keys go out through SendInput, paced like a keyboard: a modifier goes down, then the key, then the modifier comes
    // up, each in its own call a few ms apart. SendKeys (and even one SendInput batch per shortcut) let WinUI Notepad on
    // a slow benchmark VM read keys without their modifiers: ^n arrived as "n", "~" as "`", ")" as "0", and the
    // text after it was lost. Characters go as their own keys (with Shift when the layout needs it); KEYEVENTF_UNICODE
    // packets only for characters the layout lacks, one at a time (several at once reached Notepad as the last one
    // repeated).
    static readonly Dictionary<string, ushort> NamedKeys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase) {
        { "BACKSPACE", 0x08 }, { "BS", 0x08 }, { "BKSP", 0x08 }, { "TAB", 0x09 }, { "ENTER", 0x0D }, { "BREAK", 0x03 },
        { "CAPSLOCK", 0x14 }, { "ESC", 0x1B }, { "SPACE", 0x20 }, { "PGUP", 0x21 }, { "PGDN", 0x22 }, { "END", 0x23 },
        { "HOME", 0x24 }, { "LEFT", 0x25 }, { "UP", 0x26 }, { "RIGHT", 0x27 }, { "DOWN", 0x28 }, { "PRTSC", 0x2C },
        { "INSERT", 0x2D }, { "INS", 0x2D }, { "DELETE", 0x2E }, { "DEL", 0x2E }, { "HELP", 0x2F }, { "MULTIPLY", 0x6A },
        { "ADD", 0x6B }, { "SUBTRACT", 0x6D }, { "DIVIDE", 0x6F }, { "NUMLOCK", 0x90 }, { "SCROLLLOCK", 0x91 } };
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScan(char c);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);

    static void TypeUnits(List<string> units)
    {
        foreach (var unit in units)
        {
            var mods = new List<ushort>();
            int i = 0;
            while (i < unit.Length - 1 && "+^%".IndexOf(unit[i]) >= 0) { mods.Add(unit[i] == '+' ? (ushort)0x10 : unit[i] == '^' ? (ushort)0x11 : (ushort)0x12); i++; }
            var rest = unit.Substring(i);
            bool chord = mods.Count > 0;
            foreach (var m in mods) Press(m, false);
            try
            {
                if (rest == "~") Tap(0x0D, 1);
                else if (rest.Length >= 3 && rest[0] == '{' && rest[rest.Length - 1] == '}')
                {
                    var body = rest.Substring(1, rest.Length - 2);  // ENTER, ENTER 3, +, a 5
                    int count = 1, space = body.LastIndexOf(' ');
                    if (space > 0 && int.TryParse(body.Substring(space + 1), out count)) body = body.Substring(0, space);
                    else count = 1;
                    ushort vk;
                    int f;
                    if (body.Length == 1) for (int n = 0; n < count; n++) Char(body[0], chord);
                    else if (NamedKeys.TryGetValue(body, out vk)) Tap(vk, count);
                    else if ((body[0] == 'F' || body[0] == 'f') && int.TryParse(body.Substring(1), out f) && f >= 1 && f <= 24) Tap((ushort)(0x6F + f), count);
                    else throw new OrielError("unknown key {" + body + "}; nothing more was sent");
                }
                else if (chord && rest.Length > 2 && rest[0] == '(' && rest[rest.Length - 1] == ')')
                    foreach (var c in rest.Substring(1, rest.Length - 2)) Char(c, true);
                else foreach (var c in rest) Char(c, chord);
            }
            finally
            {
                for (int k = mods.Count - 1; k >= 0; k--) Press(mods[k], true);
            }
            Thread.Sleep(15);  // let the app take each unit before the next, as SendKeys.SendWait did
        }
    }

    static void Press(ushort vk, bool up)
    {
        // Arrows, Home/End, Page Up/Down, Insert/Delete and Divide are extended keys: without the flag some apps read
        // them as the numeric keypad's.
        bool extended = (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x6F || vk == 0x2C || vk == 0x90;
        Send(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? 2u : 0u) | (extended ? 1u : 0u) } } });
        // Modifiers get time to register before the key and after its release; ordinary keys arrived intact at
        // 2 ms (only the modified ones went wrong), so text types at about 4 ms a character.
        bool modifier = vk == 0x10 || vk == 0x11 || vk == 0x12;
        Thread.Sleep(modifier ? (up ? 5 : 8) : 2);
    }

    static void Tap(ushort vk, int count)
    {
        for (int n = 0; n < count; n++) { Press(vk, false); Press(vk, true); }
    }

    static void Char(char c, bool chord)
    {
        short scan = VkKeyScan(c);
        if (scan == -1)
        {
            if (chord) throw new OrielError("'" + c + "' has no key on this keyboard layout to combine with a modifier; nothing more was sent");
            Send(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = 4 } } });       // KEYEVENTF_UNICODE
            Send(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = 4 | 2 } } });   // ... | KEYUP
            Thread.Sleep(10);
            return;
        }
        var vk = (ushort)(scan & 0xFF);
        if (chord) { Tap(vk, 1); return; }  // ^a: the A key under the modifiers already held
        bool shift = (scan & 0x100) != 0, ctrl = (scan & 0x200) != 0, alt = (scan & 0x400) != 0;
        if (char.IsLetter(c) && (GetKeyState(0x14) & 1) != 0) shift = !shift;  // Caps Lock flips the case of letters
        if (ctrl) Press(0x11, false);
        if (alt) Press(0x12, false);
        if (shift) Press(0x10, false);
        Tap(vk, 1);
        if (shift) Press(0x10, true);
        if (alt) Press(0x12, true);
        if (ctrl) Press(0x11, true);
    }

    static void Send(INPUT input)
    {
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1)
            throw new OrielError("Windows blocked the keystrokes (an elevated window or a secure desktop has the input); part of them may have been sent");
    }

    static string ClickAction(Opts o)
    {
        AssertUserIdle();
        var w = WindowOf(o);
        int x, y;
        if (o.S("Id") != null || o.S("Name") != null)
        {
            var el = w == null ? Target(o) : FindTarget(w, o, o.B("Changes", true));
            if (w == null) w = ResolveWindow(o.S("Window"));
            var b = el.CurrentBoundingRectangle;
            if (b.right <= b.left || b.bottom <= b.top) throw new OrielError("\"" + el.CurrentName + "\" has no bounds on screen (scroll it into view first)");
            x = (b.left + b.right) / 2;
            y = (b.top + b.bottom) / 2;
        }
        else
        {
            if (w == null) throw new OrielError("give Id, Name, or Window with x and y");
            if (o.S("X") == null || o.S("Y") == null) throw new OrielError("x and y are required without Id or Name");
            RECT r;
            GetWindowRect(w.H, out r);
            x = r.Left + o.I("X", 0);
            y = r.Top + o.I("Y", 0);
        }
        var button = (o.S("Button") ?? "left").ToLowerInvariant();
        if (button != "left" && button != "right") throw new OrielError("button is left or right");
        int count = o.B("Double", false) ? 2 : 1;
        return Report(w, o.B("Changes", true), delegate
        {
            Foreground(w);
            PointInside(w, x, y);
            MoveTo(x, y);
            uint down = button == "right" ? 0x8u : 0x2u, up = button == "right" ? 0x10u : 0x4u;
            for (int i = 0; i < count; i++) { Mouse(down); Mouse(up); }
            Sent();
            RECT r;
            GetWindowRect(w.H, out r);
            return (count == 2 ? "double-clicked" : button == "right" ? "right-clicked" : "clicked") + " at " + (x - r.Left) + "," + (y - r.Top) + " in the window";
        });
    }

    static string DragAction(Opts o)
    {
        AssertUserIdle();
        var w = ResolveWindow(o.S("Window"));
        foreach (var k in new[] { "X1", "Y1", "X2", "Y2" })
            if (o.S(k) == null) throw new OrielError(k.ToLowerInvariant() + " is required");
        return Report(w, o.B("Changes", true), delegate
        {
            Foreground(w);
            RECT r;
            GetWindowRect(w.H, out r);
            int x1 = r.Left + o.I("X1", 0), y1 = r.Top + o.I("Y1", 0), x2 = r.Left + o.I("X2", 0), y2 = r.Top + o.I("Y2", 0);
            PointInside(w, x1, y1);
            PointInside(w, x2, y2);
            MoveTo(x1, y1);
            Mouse(0x2);
            // In steps, so apps that draw or track the drag see the path, not a jump.
            for (int i = 1; i <= 20; i++)
            {
                MoveTo(x1 + (x2 - x1) * i / 20, y1 + (y2 - y1) * i / 20);
                Thread.Sleep(10);
            }
            Mouse(0x4);
            Sent();
            return "dragged from " + o.I("X1", 0) + "," + o.I("Y1", 0) + " to " + o.I("X2", 0) + "," + o.I("Y2", 0) + " in the window";
        });
    }

    static void PointInside(WinRef w, int x, int y)
    {
        RECT r;
        GetWindowRect(w.H, out r);
        if (x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom)
            throw new OrielError("(" + (x - r.Left) + "," + (y - r.Top) + ") is outside \"" + Title(w.H) + "\" (" + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + "); nothing was sent");
        var hit = WindowFromPoint(new POINT { X = x, Y = y });
        if (hit != IntPtr.Zero && GetAncestor(hit, 2) != w.H && GetAncestor(hit, 3) != w.H)  // GA_ROOT, GA_ROOTOWNER (its own menus)
            throw new OrielError("\"" + Title(GetAncestor(hit, 2)) + "\" covers that point; nothing was sent");
    }

    static void Mouse(uint flags)
    {
        var input = new[] { new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } } };
        SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
    }

    // An absolute move over the virtual desktop (MOUSEEVENTF_MOVE | ABSOLUTE | VIRTUALDESK), so the app gets
    // WM_MOUSEMOVE there, which a bare SetCursorPos doesn't guarantee.
    static void MoveTo(int x, int y)
    {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        var input = new[] { new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT {
            dx = (int)Math.Round((x - left) * 65535.0 / (width - 1)), dy = (int)Math.Round((y - top) * 65535.0 / (height - 1)), dwFlags = 0x1 | 0x8000 | 0x4000 } } } };
        SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
    }

    static void Sent()
    {
        lastSend = Environment.TickCount;
        lastChange = DateTime.UtcNow;
    }

    // A picture of one window (PrintWindow renders it even when covered), for what text can't show: canvases,
    // games, charts. The MCP server turns "IMAGE:<mime>;<base64>" into an image result.
    static string Screenshot(WinRef w, int maxWidth)
    {
        // PrintWindow waits for the app to paint; an app that is hung would hold up every request behind it, so the
        // capture runs on its own thread and gives up after 5 s.
        string result = null;
        Exception failure = null;
        var worker = new Thread(() => { try { result = PrintWindowPng(w, maxWidth); } catch (Exception e) { failure = e; } }) { IsBackground = true };
        worker.Start();
        if (!worker.Join(5000)) throw new OrielError("the window did not paint within 5 s (the app may be busy or hung)");
        if (failure != null) throw failure is OrielError ? failure : new OrielError("screenshot failed: " + failure.Message);
        return result;
    }

    static string PrintWindowPng(WinRef w, int maxWidth)
    {
        RECT r;
        if (!GetWindowRect(w.H, out r) || r.Right <= r.Left) throw new OrielError("the window has no size (minimised?)");
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        using (var bmp = new System.Drawing.Bitmap(width, height))
        {
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                bool ok = PrintWindow(w.H, hdc, 2);  // PW_RENDERFULLCONTENT: DirectX and Chromium content too
                g.ReleaseHdc(hdc);
                if (!ok) throw new OrielError("Windows could not render this window");
            }
            var scale = Math.Min(1.0, (double)maxWidth / width);
            using (var shot = scale < 1 ? new System.Drawing.Bitmap(bmp, (int)(width * scale), (int)(height * scale)) : new System.Drawing.Bitmap(bmp))
            using (var png = new MemoryStream())
            {
                shot.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                // click and drag take window pixels; say how this image's pixels map to them.
                var note = "window " + width + "x" + height + (scale < 1
                    ? ", shown at " + Math.Round(scale * 100) + "%: for click/drag x,y multiply image x,y by " + Math.Round(1 / scale, 3).ToString(CultureInfo.InvariantCulture)
                    : ": image x,y are click/drag x,y");
                return "IMAGE:image/png;" + Convert.ToBase64String(png.ToArray()) + "\n" + note;
            }
        }
    }

    static string Tree(WinRef w, int depth, int max)
    {
        var nodes = Walk(w, refs ? FullDepth : depth);  // the fetch is the same; the full look serves change reports
        if (refs) Remember(w.H, nodes);
        return TreeOf(w, nodes, depth, max);
    }

    static string TreeOf(WinRef w, List<Node> nodes, int depth, int max)
    {
        var sb = new StringBuilder(Header(w));
        // Indent by printed ancestors only: unnamed containers would otherwise push lines far to the right.
        var shown = new int[depth + 2];
        int printed = 0;
        foreach (var n in nodes)
        {
            if (n.Depth > depth) continue;
            int indent = shown[n.Depth - 1];
            bool show = Shown(n.El);
            shown[n.Depth] = indent + (show ? 1 : 0);
            if (!show) continue;
            if (++printed > max) { sb.Append("\n(stopped at ").Append(max).Append(" elements; raise Max or narrow with find)"); break; }
            sb.Append('\n').Append(Format(n, indent));
        }
        Mark("format");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- actions

    public static string Run(string action, IDictionary options) { LoadPolicy(); unreadable = IntPtr.Zero; return Run(action, new Opts(options)); }

    // invoke, set, keys, click, drag and batch can bring a picture of the window along (screenshot=true), so checking
    // an action by eye costs no extra call: agents on WindowsAgentArena followed an action with a screenshot call
    // 2.7 times a task, a model turn each.
    static readonly string[] WithPicture = { "invoke", "set", "keys", "click", "drag", "batch" };

    static string Run(string action, Opts o)
    {
        var text = Act(action, o);
        if (!o.B("Screenshot", false) || Array.IndexOf(WithPicture, action) < 0) return text;
        WinRef w = null;
        try { w = WindowOf(o); } catch (OrielError) { }
        try
        {
            // The action's window, or the one in front when it closed (a dialog the action dismissed); either way
            // through ResolveWindow, so the off-limits policy holds for the picture too.
            var h = w != null && IsWindow(w.H) ? w.H : GetForegroundWindow();
            return text + "\n" + Screenshot(ResolveWindow(h.ToString()), o.I("MaxWidth", 1024));
        }
        catch (OrielError e) { return text + "\n(no picture: " + e.Message + ")"; }
    }

    static string Act(string action, Opts o)
    {
        switch (action)
        {
            case "ping": return "pong";
            case "status":
            {
                var fg = GetForegroundWindow();
                return (Locked() ? "locked (keys and focus won't work; Store apps such as Settings are suspended)" : "unlocked") +
                    ", user idle " + IdleMs() / 1000 + " s, foreground [" + fg + "] " + ProcessOf(fg) + " \"" + Title(fg) + "\"";
            }
            case "windows":
                windowByQuery.Clear();
                return string.Join("\n", Windows().ConvertAll(w => Describe(w)));
            case "tree": return Tree(ResolveWindow(o.S("Window")), o.I("Depth", 12), o.I("Max", 400));
            case "find":
            {
                var w = ResolveWindow(o.S("Window"));
                var sb = new StringBuilder(Header(w));
                foreach (var n in Candidates(w, o.S("Name"), o.S("Type"), o.I("Depth", 12))) sb.Append('\n').Append(Format(n, 0));
                return sb.ToString();
            }
            case "invoke":
            {
                var w = WindowOf(o);
                var el = w == null ? Target(o) : FindTarget(w, o, o.B("Changes", true));
                Mark("target");
                return Report(w, o.B("Changes", true), () => Invoke(el));
            }
            case "set":
            {
                var w = WindowOf(o);
                var el = w == null ? Target(o) : FindTarget(w, o, o.B("Changes", true));
                Mark("target");
                // A missing Value is a mistake, not a request to clear the field: clearing takes Value "".
                if (o.Raw("Value") == null) throw new OrielError("Value is required (\"\" clears the field)");
                var value = Convert.ToString(o.Raw("Value"), CultureInfo.InvariantCulture);
                return Report(w, o.B("Changes", true), () => SetValue(el, value));
            }
            case "text":
            {
                U.IUIAutomationElement el;
                if (o.S("Id") != null || o.S("Name") != null) el = Target(o);
                else
                {
                    var doc = Walk(ResolveWindow(o.S("Window")), o.I("Depth", 12)).Find(n => n.El.CachedControlType == DocumentType || n.El.CachedControlType == EditType);
                    if (doc == null) throw new OrielError("no document or edit field in this window; give Id or Name");
                    el = doc.El;
                }
                if (el.CachedIsPassword != 0) throw new OrielError("refusing to read a password field");
                U.IUIAutomationTextPattern tp; U.IUIAutomationValuePattern vp;
                // LibreOffice's documents offer a TextPattern whose GetText fails with E_FAIL; fall back to the value,
                // the MSAA value, then the name, rather than ending on a COM error.
                try { if ((tp = Pattern<U.IUIAutomationTextPattern>(el, TextPat)) != null) return tp.DocumentRange.GetText(20000); }
                catch (COMException e) { if (e.ErrorCode == TimedOut || e.ErrorCode == NotAvailable) throw; }
                if ((vp = Pattern<U.IUIAutomationValuePattern>(el, ValuePat)) != null) return vp.CurrentValue;
                var legacy = Legacy(el);
                if (legacy != null && !string.IsNullOrEmpty(legacy.CurrentValue)) return legacy.CurrentValue;
                return el.CachedName;
            }
            case "wait":
            {
                // With nothing to wait for, pause (an app still starting) and say what is open afterwards.
                if (o.S("Window") == null)
                {
                    if (o.S("Name") != null) throw new OrielError("Window is required with name");
                    Thread.Sleep((int)(Math.Min(o.D("Timeout", 3), 30) * 1000));
                    windowByQuery.Clear();
                    return "waited " + Math.Min(o.D("Timeout", 3), 30) + " s; open windows:\n" + string.Join("\n", Windows().ConvertAll(x => Describe(x)));
                }
                // Re-check whenever the window raises an accessibility event (at least 15 ms apart, and every 250 ms
                // regardless), so it reacts within a few ms instead of after a poll interval.
                var deadline = DateTime.UtcNow.AddSeconds(o.D("Timeout", 10));
                // A window that has raised no event for 2.5 s is not about to grow the element: answer then instead of
                // at the timeout (on WindowsAgentArena, waits for elements that never came cost agents ~2 h a run).
                double quietMs = 0;
                while (true)
                {
                    WinRef w = null;
                    try
                    {
                        w = ResolveWindow(o.S("Window"));
                        // Without a name, wait for the window itself (an app still starting: an agent on WindowsAgentArena
                        // spent its turns on wait calls that each demanded a name while LibreOffice took minutes to open).
                        if (o.S("Name") == null && o.S("Type") == null)
                            return "window [" + w.H + "] " + ProcessOf(w.H) + " \"" + Title(w.H) + "\" is there";
                        var hits = Candidates(w, o.S("Name"), o.S("Type"), o.I("Depth", 12));
                        if (hits.Count > 0) return Format(hits[0], 0);
                    }
                    catch (OrielError) { if (DateTime.UtcNow > deadline) throw; }  // the window isn't there yet
                    catch (COMException e)
                    {
                        if (e.ErrorCode != NotAvailable) throw;
                        if (w != null) Evict(w.H);  // being built or torn down, or its handle was recycled
                        w = null;
                    }
                    if (DateTime.UtcNow > deadline) throw new OrielError("'" + (o.S("Name") ?? o.S("Window")) + "' did not appear within " + o.D("Timeout", 10) + " s");
                    if (w == null) { Thread.Sleep(50); continue; }
                    if (quietMs >= 2500 && o.S("Name") != null)
                        throw new OrielError("'" + o.S("Name") + "' isn't in that window, and nothing there has changed for 2.5 s; find or tree shows what is there");
                    int before = eventCount;
                    long slice = Stopwatch.GetTimestamp();
                    Listening(w.H, () =>
                    {
                        int seen = eventCount;
                        long since = Stopwatch.GetTimestamp();
                        Pump(250, () => eventCount != seen && Ms(Stopwatch.GetTimestamp() - since) >= 15);
                        return true;
                    });
                    quietMs = eventCount == before ? quietMs + Ms(Stopwatch.GetTimestamp() - slice) : 0;
                }
            }
            case "click":
            case "drag":
            {
                // Window and element coordinates in physical pixels whatever the display scaling, like UIA's.
                IntPtr dpi = IntPtr.Zero;
                try { dpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                try { return action == "click" ? ClickAction(o) : DragAction(o); }
                finally { if (dpi != IntPtr.Zero) SetThreadDpiAwarenessContext(dpi); }
            }
            case "focus":
            {
                var w = ResolveWindow(o.S("Window"));
                Foreground(w);
                return "focused \"" + Title(w.H) + "\"";
            }
            case "scroll":
            {
                var w = WindowOf(o);
                var named = o.S("Id") != null || o.S("Name") != null;
                var el = named ? (w == null ? Target(o) : FindTarget(w, o, o.B("Changes", true))) : null;
                return Report(w, o.B("Changes", true), () => Scroll(w, el, o.S("Direction") ?? "down", o.S("Amount") ?? "page"));
            }
            case "select":
            {
                var option = o.S("Option");
                if (option == null) throw new OrielError("Option is required: the text of the item to pick");
                var w = WindowOf(o);
                var el = w == null ? Target(o) : FindTarget(w, o, o.B("Changes", true));
                return Report(w, o.B("Changes", true), () => SelectOption(el, option));
            }
            case "window":
            {
                var w = ResolveWindow(o.S("Window"));
                return WindowState(w, o);
            }
            case "screenshot":
                return Screenshot(ResolveWindow(o.S("Window") ?? GetForegroundWindow().ToString()), o.I("MaxWidth", 1280));
            case "close":
            {
                var w = ResolveWindow(o.S("Window"));
                var title = Title(w.H);
                var p = Pattern<U.IUIAutomationWindowPattern>(w.El, WindowPat);
                if (p == null) throw new OrielError("\"" + title + "\" has no WindowPattern");
                p.Close();
                return "closed \"" + title + "\"";
            }
            case "launch":
            {
                var path = o.S("Path");
                if (path == null) throw new OrielError("Path is required");
                CheckLaunch(path);
                windowByQuery.Clear();  // the new window may share a title with an old one
                var want = o.S("WaitTitle");
                // Every top-level window, hidden and cloaked ones too: a window brought back from another virtual
                // desktop or from hiding is not new.
                var before = new Dictionary<IntPtr, bool>();
                if (want != null) EnumWindows(delegate (IntPtr h, IntPtr _) { before[h] = true; return true; }, IntPtr.Zero);
                Process.Start(new ProcessStartInfo(path, o.S("Args") ?? "") { UseShellExecute = true });
                lastChange = DateTime.UtcNow;
                if (want == null) return "started " + path;
                // Wait for a window that wasn't there before: an older one with the same title is another instance.
                // Single-instance apps (Settings) bring back their window instead, so after 2 s an old match counts.
                var start = DateTime.UtcNow;
                var deadline = start.AddSeconds(o.D("Timeout", 10));
                while (DateTime.UtcNow < deadline)
                {
                    var matches = Windows().FindAll(x => x.Title.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                    var fresh = matches.Find(x => !before.ContainsKey(x.Handle));
                    if (fresh != null)
                    {
                        var started = "started " + path + ": window " + Describe(fresh);
                        if (!o.B("Tree", false)) return started;
                        // The next call is nearly always a tree of the new window, so include it. A classic dialog
                        // has its controls before it shows (no events follow: 80 ms); XAML and web content build
                        // after it shows, so wait for those events to stop (quiet for 50 ms, at most 2 s).
                        Listening(fresh.Handle, () => AwaitQuiet(80, 50, 2000));
                        return started + "\n" + Tree(ResolveWindow(fresh.Handle.ToString()), o.I("Depth", 12), o.I("Max", 150));
                    }
                    if (matches.Count > 0 && (DateTime.UtcNow - start).TotalSeconds > 2) return "started " + path + ": it reused window " + Describe(matches[0]);
                    Thread.Sleep(30);
                }
                throw new OrielError("started " + path + " but no window titled like '" + want + "' within " + o.D("Timeout", 10) + " s");
            }
            case "keys":
            {
                var keys = o.S("Keys");
                if (keys == null) throw new OrielError("Keys is required");
                // literal: plain text, typed as is ("f(x) = [a+b]"), escaped here rather than by the caller.
                if (o.B("Literal", false))
                {
                    var sb = new StringBuilder();
                    foreach (var ch in keys)
                    {
                        if ("+^%~(){}[]".IndexOf(ch) >= 0) sb.Append('{').Append(ch).Append('}');
                        else if (ch == '\n') sb.Append("{ENTER}");
                        else if (ch == '\t') sb.Append("{TAB}");
                        else if (ch != '\r') sb.Append(ch);
                    }
                    keys = sb.ToString();
                }
                // Keys go to whatever has focus, so never send them without the target window verified in front.
                if (o.S("Window") == null) throw new OrielError("Window is required: keys are only sent to a window verified to be in front");
                AssertUserIdle();
                var w = ResolveWindow(o.S("Window"));
                return Report(w, o.B("Changes", true), () => SendKeysTo(w, o, keys));
            }
            case "batch":
            {
                var steps = o.Raw("Steps") as IEnumerable;
                if (steps == null || steps is string) throw new OrielError("Steps is required: a list of option objects, each with an action");
                var w = o.S("Window") == null ? null : ResolveWindow(o.S("Window"));
                return Report(w, o.B("Changes", true), delegate
                {
                    var sb = new StringBuilder();
                    int i = 0;
                    foreach (var raw in steps)
                    {
                        i++;
                        var step = raw as IDictionary;
                        if (step == null) throw new OrielError("step " + i + " is not an object");
                        var so = o.With(step);
                        var act = so.S("action");
                        if (act == null || act == "batch") throw new OrielError("step " + i + " needs an action other than batch");
                        try { sb.Append(i).Append(". ").Append(Act(act, so)).Append('\n'); }
                        catch (Exception e)
                        {
                            Stale(e);
                            throw new OrielError(sb + "step " + i + " (" + act + ") failed: " + Message(e));
                        }
                    }
                    return sb.ToString().TrimEnd();
                });
            }
            default: throw new OrielError("unknown action '" + action + "'");
        }
    }

    const int NotAvailable = unchecked((int)0x80040201), NotEnabled = unchecked((int)0x80040200), TimedOut = unchecked((int)0x80131505);

    // A UIA call that runs past the transaction timeout fails with UIA_E_TIMEOUT, which the interop raises as a
    // TimeoutException on some calls and a COMException on others; both mean the app is busy or hung.
    static bool IsTimeout(Exception e)
    {
        var com = e as COMException;
        return e is TimeoutException || (com != null && com.ErrorCode == TimedOut);
    }

    public static string Message(Exception e)
    {
        if (e is OrielError) return e.Message;
        var com = e as COMException;
        if (com != null && com.ErrorCode == NotAvailable) return "the element or window went away; run tree again";
        if (com != null && com.ErrorCode == NotEnabled) return "that element is disabled";
        if (e is TimeoutException || (com != null && com.ErrorCode == TimedOut))
            return "the app's accessibility interface did not answer in time (busy or hung); wait and retry, or use keys or a screenshot";
        if (com != null && com.ErrorCode == unchecked((int)0x80004005))  // E_FAIL
            return "the app's accessibility interface failed on that (E_FAIL); try another element, keys or a screenshot";
        return e.GetType().Name + ": " + e.Message;
    }

    // ---------------------------------------------------------------- server

    class Job { public string Line; public string Reply; public readonly ManualResetEventSlim Done = new ManualResetEventSlim(); }

    // The per-user key (%LOCALAPPDATA%\oriel\token, readable only by this user). A connection starts with a handshake
    // in which both sides prove they hold it without sending it: the client sends {"hello": nonce}, the daemon answers
    // {"proof": HMAC(key, "oriel-daemon\n" + nonce), "nonce": its own}, and every request then carries
    // "Auth": HMAC(key, "oriel-client\n" + daemon nonce). Only this user's own clients can make requests, and a client
    // only sends requests to the real daemon. A connection that sends anything but JSON is closed at once.
    static string token;

    public static string Hmac(string key, string message)
    {
        using (var h = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key)))
            return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    public static string Nonce()
    {
        var bytes = new byte[16];
        using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    // Each Windows user gets their own port (two signed-in users each run a daemon): %LOCALAPPDATA%\oriel\port if
    // present, else 47100-47899 from a hash of the user's SID.
    public static int UserPort()
    {
        var file = Path.Combine(Home, "port");
        int port;
        if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out port)) return port;
        uint hash = 2166136261;
        foreach (var c in System.Security.Principal.WindowsIdentity.GetCurrent().User.Value) hash = (hash ^ c) * 16777619;
        return 47100 + (int)(hash % 800);
    }

    // Per-user state: token, ref generation, config.json.
    public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "oriel");

    public static string LoadToken()
    {
        var dir = Home;
        var file = Path.Combine(dir, "token");
        if (File.Exists(file)) { var t = File.ReadAllText(file).Trim(); if (t.Length >= 32) return t; }
        Directory.CreateDirectory(dir);
        var bytes = new byte[32];
        using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) rng.GetBytes(bytes);
        var fresh = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        File.WriteAllText(file, fresh);
        return fresh;
    }

    static bool Same(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];  // constant time
        return diff == 0;
    }

    // Protocol: JSON objects, one per line, e.g. {"action":"invoke","Window":"Calculator","Name":"Seven"}; each reply
    // is text (errors start with "ERROR: "), a timing line, then a line holding only "<EOT>". A connection can carry
    // any number of requests, which saves an SSH channel set-up (~7 ms) per call. Connections are read on their own
    // threads, but all UI Automation runs on this (the calling) thread, the one that created the COM objects, one
    // request at a time.
    // Long-lived use (the daemon, or the MCP server running in the desktop session): refs, policy, a 1 ms timer, warm-up.
    public static void Init()
    {
        refs = true;
        RefPrefix = NewPrefix();
        LoadPolicy();
        // Sleeps and polls round up to the system timer tick (often 15.6 ms) unless this process asks for 1 ms.
        timeBeginPeriod(1);
        Warm();
    }

    public static void Serve(int port)
    {
        Init();
        token = LoadToken();
        var jobs = new BlockingCollection<Job>();
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var acceptor = new Thread(() =>
        {
            while (true)
            {
                var client = listener.AcceptTcpClient();
                var reader = new Thread(() => Connection(client, jobs)) { IsBackground = true };
                reader.Start();
            }
        }) { IsBackground = true };
        acceptor.Start();
        var json = new JavaScriptSerializer();
        Handle("{\"action\":\"ping\"}", json);  // JIT the request path too
        foreach (var job in jobs.GetConsumingEnumerable())
        {
            job.Reply = Handle(job.Line, json);
            job.Done.Set();
        }
    }

    static void Connection(TcpClient client, BlockingCollection<Job> jobs)
    {
        var utf8 = new UTF8Encoding(false);
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var reader = new StreamReader(stream, utf8);
                var json = new JavaScriptSerializer();
                // The handshake (see token): anything else first, or anything but a JSON object later, ends it.
                var hello = json.DeserializeObject(reader.ReadLine() ?? "") as Dictionary<string, object>;
                object nonce;
                if (hello == null || !hello.TryGetValue("hello", out nonce) || !(nonce is string)) return;
                var mine = Nonce();
                var answer = utf8.GetBytes(json.Serialize(new Dictionary<string, object> {
                    { "proof", Hmac(token, "oriel-daemon\n" + nonce) }, { "nonce", mine } }) + "\n");
                stream.Write(answer, 0, answer.Length);
                var expected = Hmac(token, "oriel-client\n" + mine);
                string line;
                while ((line = reader.ReadLine()) != null && line.TrimStart().StartsWith("{"))
                {
                    var req = json.DeserializeObject(line) as Dictionary<string, object>;
                    object auth;
                    if (req == null || !req.TryGetValue("Auth", out auth) || !Same(auth as string, expected)) return;
                    var job = new Job { Line = line };
                    jobs.Add(job);
                    job.Done.Wait();
                    var bytes = utf8.GetBytes(job.Reply);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
        }
        catch (IOException) { }  // the client went away
        catch (ObjectDisposedException) { }
        catch (ArgumentException) { }  // not JSON
        catch (InvalidOperationException) { }
    }

    // Every request gets its own error boundary: nothing a client sends may stop the server.
    static string Handle(string line, JavaScriptSerializer json)
    {
        var clock = Stopwatch.StartNew();
        string action = "?", output;
        trace = null;
        try
        {
            var req = json.DeserializeObject(line) as Dictionary<string, object>;
            if (req == null) throw new OrielError("send one JSON object per line");
            object a, t;
            action = req.TryGetValue("action", out a) ? Convert.ToString(a) : "";
            // A JSON true, or "true" as oriel call sends every option
            if (req.TryGetValue("Trace", out t) && (t is bool ? (bool)t : "true".Equals(t as string, StringComparison.OrdinalIgnoreCase))) { trace = new StringBuilder("\n(trace:"); traceClock.Restart(); }
            output = Run(action, req);
        }
        catch (Exception e)
        {
            output = "ERROR: " + Message(e);
            Stale(e);
        }
        if (trace != null) output += trace.Append(')');
        output = output.Replace("<EOT>", "< EOT>");  // text read from an app must not end the reply early
        return output + "\n(" + action + " took " + clock.ElapsedMilliseconds + " ms)\n<EOT>\n";
    }

    // Run every code path once at start, so the first real request doesn't pay for JIT compiling (~30 ms).
    static void Warm()
    {
        try
        {
            var wins = Windows();
            if (wins.Count == 0) return;
            var w = ResolveWindow(wins[0].Handle.ToString());
            Header(w);
            foreach (var n in Walk(w, 12)) { Body(n.El); Key(n.El); }
            Candidates(w, "x", "Button", 12);
            Report(w, true, () => "");
        }
        catch (Exception) { }  // a window that closes mid-warm-up doesn't matter
    }
}
}
