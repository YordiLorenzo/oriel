// walkprobe: time one window's Subtree cache walk with different property sets, filters and element modes, to see
// what a change report's walk pays for. Built in a WAA VM against oriel's interop and run in the desktop session:
//   csc /link:Interop.UIAutomationClient.dll walkprobe.cs && walkprobe.exe "<title part>" [rounds]
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using U = Interop.UIAutomationClient;

static class WalkProbe
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

    static readonly int[] All = { 30000, 30001, 30002, 30003, 30005, 30010, 30011, 30019, 30020, 30022, 30028, 30031, 30036,
        30041, 30043, 30045, 30046, 30070, 30079, 30086, 30033, 30034, 30035, 30047, 30048, 30049, 30050, 30053, 30054 };
    // What a line of the tree prints, without the range and scroll details.
    static readonly int[] Lines = { 30000, 30001, 30003, 30005, 30010, 30011, 30022, 30028, 30031, 30036, 30041, 30043,
        30045, 30070, 30079, 30086 };
    // Enough to tell what changed: identity, type, name, value, state.
    static readonly int[] Diff = { 30000, 30003, 30005, 30045, 30070, 30079, 30086, 30010 };

    static int Count(U.IUIAutomationElement e)
    {
        var kids = e.GetCachedChildren();
        int n = 1;
        if (kids != null) for (int i = 0; i < kids.Length; i++) n += Count(kids.GetElement(i));
        return n;
    }

    static void Time(U.IUIAutomation auto, U.IUIAutomationElement root, string label, int[] props, U.IUIAutomationCondition filter, bool full, int rounds)
    {
        var c = auto.CreateCacheRequest();
        foreach (var p in props) c.AddProperty(p);
        c.TreeScope = U.TreeScope.TreeScope_Subtree;
        c.TreeFilter = filter;
        if (!full) c.AutomationElementMode = U.AutomationElementMode.AutomationElementMode_None;
        var times = new StringBuilder();
        int n = 0;
        for (int r = 0; r < rounds; r++)
        {
            var sw = Stopwatch.StartNew();
            var top = root.BuildUpdatedCache(c);
            n = Count(top);
            times.Append(sw.ElapsedMilliseconds).Append(' ');
        }
        Console.WriteLine("{0,-34} {1,5} elements  ms: {2}", label, n, times);
    }

    static void Split(U.IUIAutomationElement e, U.IUIAutomationCacheRequest c, int depth)
    {
        var kids = e.GetCachedChildren();
        if (kids == null) return;
        for (int i = 0; i < kids.Length; i++)
        {
            var k = kids.GetElement(i);
            var sw = Stopwatch.StartNew();
            int n = Count(k.BuildUpdatedCache(c));
            Console.WriteLine("{0}{1,-40} hwnd={2,-8} {3,5} elements {4,6} ms", new string(' ', 2 * depth + 2),
                (k.CachedClassName + " " + k.CachedName).Substring(0, Math.Min(40, (k.CachedClassName + " " + k.CachedName).Length)), k.CachedNativeWindowHandle, n, sw.ElapsedMilliseconds);
            if (depth < 1 && n > 15) Split(k, c, depth + 1);
        }
    }

    // "sta" as the third argument runs the walks on an STA thread, as oriel's daemon does; the default is MTA.
    static int Main(string[] args)
    {
        int code = 0;
        var t = new System.Threading.Thread(() => code = Probe(args));
        t.SetApartmentState(args.Length > 2 && args[2] == "sta" ? System.Threading.ApartmentState.STA : System.Threading.ApartmentState.MTA);
        t.Start();
        t.Join();
        return code;
    }

    static int Probe(string[] args)
    {
        var want = args[0];
        int rounds = args.Length > 1 ? int.Parse(args[1]) : 3;
        IntPtr hit = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            if (IsWindowVisible(h) && sb.ToString().IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) { hit = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (hit == IntPtr.Zero) { Console.WriteLine("no window like " + want); return 1; }
        var auto = (U.IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("e22ad333-b25f-460c-83d0-0581107395c9")));
        ((U.IUIAutomation2)auto).AutoSetFocus = 0;
        var root = auto.ElementFromHandle(hit);
        var onScreen = auto.CreateAndCondition(auto.ControlViewCondition, auto.CreatePropertyCondition(30022, false));
        Console.WriteLine("window " + root.CurrentName + " on " + System.Threading.Thread.CurrentThread.GetApartmentState());
        Time(auto, root, "29 props, control+onscreen (now)", All, onScreen, true, rounds);
        Time(auto, root, "16 props (tree line)", Lines, onScreen, true, rounds);
        Time(auto, root, "8 props (diff)", Diff, onScreen, true, rounds);
        Time(auto, root, "8 props, mode None", Diff, onScreen, false, rounds);
        Time(auto, root, "29 props, mode None", All, onScreen, false, rounds);
        Time(auto, root, "29 props, control view only", All, auto.ControlViewCondition, true, rounds);
        Time(auto, root, "1 prop (runtime id)", new[] { 30000 }, onScreen, true, rounds);
        // Per part: each child subtree of the window (two levels down), with its class and window handle.
        var parts = auto.CreateCacheRequest();
        foreach (var p in Diff) parts.AddProperty(p);
        parts.AddProperty(30012); parts.AddProperty(30020);  // ClassName, NativeWindowHandle
        parts.TreeScope = U.TreeScope.TreeScope_Subtree;
        parts.TreeFilter = onScreen;
        Split(root.BuildUpdatedCache(parts), parts, 0);
        return 0;
    }
}
