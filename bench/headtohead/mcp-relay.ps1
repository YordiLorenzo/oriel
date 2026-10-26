# mcp-relay.ps1 <port> <exe> <args>: benchmark helper. In the desktop session, accept TCP connections on loopback and
# run "<exe> <args>" (a stdio MCP server) per connection, relaying bytes both ways. Cua Driver's daemon refuses the
# SSH session's pipe client, so its own stdio MCP proxy runs here instead and the Mac reaches it over an SSH tunnel.
param([int]$Port, [string]$Exe, [string]$Arguments)
Add-Type -TypeDefinition @'
using System; using System.Diagnostics; using System.IO; using System.Net; using System.Net.Sockets; using System.Threading;
public static class McpRelay {
    public static void Serve(int port, string exe, string args) {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        while (true) {
            var client = listener.AcceptTcpClient();
            client.NoDelay = true;
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true };
            var proc = Process.Start(psi);
            var net = client.GetStream();
            new Thread(() => Pump(net, proc.StandardInput.BaseStream)) { IsBackground = true }.Start();
            new Thread(() => { Pump(proc.StandardOutput.BaseStream, net); client.Close(); }) { IsBackground = true }.Start();
        }
    }
    static void Pump(Stream from, Stream to) {
        var buf = new byte[65536];
        try { int n; while ((n = from.Read(buf, 0, buf.Length)) > 0) { to.Write(buf, 0, n); to.Flush(); } } catch (Exception) { }
        try { to.Close(); } catch (Exception) { }
    }
}
'@
[McpRelay]::Serve($Port, $Exe, $Arguments)
