// Mcp.cs: the MCP server (stdio, newline-delimited JSON-RPC). It needs only initialize, tools/list and tools/call, so
// it is written by hand: no SDK, under a millisecond per call. Each tool maps onto one engine action; the backend is
// the engine itself (in the desktop session) or the daemon over loopback (from SSH or a service session).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace Oriel
{
public static class Mcp
{
    const string Instructions =
        "Drive Windows apps through the accessibility tree, as text. Start with windows, then tree or find on one " +
        "window; every element comes with a ref like kqm17 that invoke, set and text take as id (no window needed). " +
        "Lines list actions in {} only when they differ from the type's usual one (buttons, links and menu items " +
        "invoke; check boxes toggle; list, tab and tree items and radio buttons select; combo boxes expand; edits set). " +
        "invoke, set, keys and batch reply with what changed in the window (+ new, ~ changed, - gone, + window opened " +
        "with its tree; a page that changed a lot comes back as its new tree), so you rarely need tree again. launch " +
        "replies with the new window's tree. Put predictable steps in one batch call. Use wait instead of polling. " +
        "status says whether the PC is locked (then keys and focus fail and Store apps such as Settings show no " +
        "controls). Where an app shows no accessible controls (a spreadsheet's cells, a canvas, a custom widget), use keys " +
        "for shortcuts and typing (literal=true for plain text) and click or drag on screen points; a screenshot says how " +
        "its pixels map to the window pixels those take. When an app is busy or hung, keys still work. invoke, set, keys, " +
        "click, drag and batch take screenshot=true to add a picture of the window after the action to their reply, " +
        "instead of a separate screenshot call. " +
        "Password fields, the lock screen, UAC prompts and password managers are off limits. File Explorer " +
        "hides known extensions: an item shown as 'draft' may be draft.txt, and renaming it means setting 'final', not " +
        "'final.txt' (that gives final.txt.txt).";

    class Tool
    {
        public string Name, Description, Action;
        public string[] Required;
        public Dictionary<string, string> Props;   // argument name -> JSON type
        public Dictionary<string, string> Option;  // argument name -> engine option name
    }

    static Tool T(string name, string action, string description, string props, string required)
    {
        var t = new Tool { Name = name, Action = action, Description = description, Props = new Dictionary<string, string>(), Option = new Dictionary<string, string>() };
        t.Required = required == "" ? new string[0] : required.Split(',');
        // "window:string=Window,depth:integer=Depth": argument, JSON type, engine option.
        if (props != "")
            foreach (var spec in props.Split(','))
            {
                var nameType = spec.Split('=')[0].Split(':');
                t.Props[nameType[0]] = nameType[1];
                t.Option[nameType[0]] = spec.Split('=')[1];
            }
        return t;
    }

    static readonly Tool[] Tools = {
        T("status", "status", "Whether the PC is locked, seconds since the user last touched it, and the foreground window.", "", ""),
        T("windows", "windows", "Visible windows, front first, dialogs included: [handle] process \"title\".", "", ""),
        T("tree", "tree", "Named or actionable on-screen elements of one window, indented by nesting: [ref] type \"name\", " +
          "value, state (on/off, selected, collapsed, disabled) and actions in {} when they differ from the type's usual one. " +
          "window = part of the title, a process name (chrome, notepad) or a handle.",
          "window:string=Window,depth:integer=Depth,max_elements:integer=Max", "window"),
        T("find", "find", "Elements of a window whose name contains `name` (case-insensitive); type narrows it (Button, Edit, ...).",
          "window:string=Window,name:string=Name,type:string=Type", "window"),
        T("invoke", "invoke", "Press, toggle, select or expand an element: id = a ref from tree/find (e.g. kqm17), or window + name " +
          "(best match: exact and enabled first; a miss lists the closest names). Replies with what changed once the window " +
          "settles (about 40 ms; changes=false skips that).",
          "id:string=Id,window:string=Window,name:string=Name,type:string=Type,changes:boolean=Changes,screenshot:boolean=Screenshot", ""),
        T("set", "set", "Fill a text field (replaces its content; \"\" clears it), by ref id or window + name. Refuses password " +
          "fields. Read the field with text first if it may hold the user's text. Replies with what changed (changes=false skips).",
          "value:string=Value,id:string=Id,window:string=Window,name:string=Name,type:string=Type,changes:boolean=Changes,screenshot:boolean=Screenshot", "value"),
        T("text", "text", "Text of a document or field, by ref id or window + name (the first document/edit of the window when " +
          "only window is given).", "id:string=Id,window:string=Window,name:string=Name,type:string=Type", ""),
        T("wait", "wait", "Wait until a window matching `window` appears, or with `name`, until an element whose name contains " +
          "`name` appears in it (reacts to the window's own events; default timeout 10 s). With neither, pause `timeout` " +
          "seconds (default 3) and list the open windows.",
          "window:string=Window,name:string=Name,type:string=Type,timeout:number=Timeout", ""),
        T("scroll", "scroll", "Scroll: an element into view (id or name of an item), or a container (its id or name, else the " +
          "window's first scrollable area) by a page or line. Replies with what came into view.",
          "window:string=Window,id:string=Id,name:string=Name,direction:string=Direction,amount:string=Amount,changes:boolean=Changes", ""),
        T("select", "select", "Pick an item by its text in a combo box, list, tab strip or menu (id or window + name of the box): " +
          "opens it if needed, selects the item (exact name first, else one containing the text) and closes it. Replies with what changed.",
          "option:string=Option,id:string=Id,window:string=Window,name:string=Name,type:string=Type,changes:boolean=Changes", "option"),
        T("click", "click", "Click with the real mouse, for what has no UIA action (a sheet's cells, a canvas): an element (id = a ref, " +
          "or window + name: its center) or a point (window + x, y: window pixels from its top-left corner; a screenshot says " +
          "how its pixels map). The window comes to the front first; refused while the user is active or the PC is locked. " +
          "button left/right, double. Prefer invoke for buttons and links: it doesn't take the mouse. Replies with what changed.",
          "id:string=Id,window:string=Window,name:string=Name,type:string=Type,x:integer=X,y:integer=Y,button:string=Button," +
          "double:boolean=Double,changes:boolean=Changes,screenshot:boolean=Screenshot", ""),
        T("drag", "drag", "Press the left mouse button at (x1, y1) and release it at (x2, y2), window pixels from its top-left corner: " +
          "drawing, sliders, selecting a range of cells or text. Same checks as click. Replies with what changed.",
          "window:string=Window,x1:integer=X1,y1:integer=Y1,x2:integer=X2,y2:integer=Y2,changes:boolean=Changes,screenshot:boolean=Screenshot", "window,x1,y1,x2,y2"),
        T("window", "window", "Minimise, maximise or restore a window (state) and/or move it (x, y) or resize it (width, height).",
          "window:string=Window,state:string=State,x:number=X,y:number=Y,width:number=Width,height:number=Height", "window"),
        T("screenshot", "screenshot", "A picture of one window (default: the one in front), for what text can't show (canvases, games, charts, images). " +
          "Costs far more tokens than tree: use it only when the tree can't answer.",
          "window:string=Window,max_width:integer=MaxWidth", ""),
        T("focus", "focus", "Restore a window if minimised and bring it to the front (fails while the PC is locked).",
          "window:string=Window", "window"),
        T("close", "close", "Close a window through its WindowPattern (an app may still ask to save).", "window:string=Window", "window"),
        T("launch", "launch", "Start a program, file, folder or URI (notepad.exe, explorer.exe, ms-settings:display). With wait_title " +
          "it waits until a new window whose title contains it exists, then (tree=true) for its controls to stop arriving, and " +
          "replies with that window's tree.",
          "path:string=Path,args:string=Args,wait_title:string=WaitTitle,timeout:number=Timeout,tree:boolean=Tree,max_elements:integer=Max", "path"),
        T("keys", "keys", "Send keystrokes in SendKeys syntax to `window` (and its element `name`/`id`), after checking the window is " +
          "in front and the focus is not a password field: {ENTER} {TAB} {ESC} {F5}, ^a = Ctrl+A, %{F4} = Alt+F4, +{TAB} = " +
          "Shift+Tab; plain text types as is (escape + ^ % ~ { in braces, e.g. {+}; ( ) [ ] } type as themselves). Text may only come before the " +
          "first key that can move the focus: 'hello{ENTER}' and '^ahello' work, '{TAB}secret' is refused (send the text in " +
          "its own call, or use set); arrows, page keys and Ctrl/Shift+Home/End count as editing keys. literal=true types `keys` " +
          "as plain text (newlines as Enter), no escaping needed. Refused while the user is typing and while the PC is locked. " +
          "Replies with what changed.",
          "keys:string=Keys,window:string=Window,name:string=Name,type:string=Type,id:string=Id,literal:boolean=Literal,changes:boolean=Changes,screenshot:boolean=Screenshot", "keys,window"),
        T("batch", "batch", "Run several actions in one call, in order, stopping at the first error (the reply shows what ran). Each " +
          "step is an object with \"action\" (tree, find, invoke, set, text, wait, scroll, select, window, focus, close, launch, " +
          "keys) and that action's options with capitalised names: Window, Id, Name, Type, Value, Option, Keys, Path, Args, WaitTitle, " +
          "Timeout, Direction, Amount, State. `window` is the " +
          "default Window for every step. Example, 7 + 8 in Calculator: window=\"Calculator\", steps=[{\"action\":\"invoke\"," +
          "\"Name\":\"Seven\"}, {\"action\":\"invoke\",\"Name\":\"Plus\"}, {\"action\":\"invoke\",\"Name\":\"Eight\"}, " +
          "{\"action\":\"invoke\",\"Name\":\"Equals\"}]. After a step that opens a window or loads a page, add a wait step. With " +
          "a window, the reply ends with what the whole batch changed there.",
          "steps:array=Steps,window:string=Window,changes:boolean=Changes,screenshot:boolean=Screenshot", "steps"),
    };

    static readonly string[] PictureActions = { "invoke", "set", "keys", "click", "drag", "batch" };

    // 2025-03-26 is left out: it requires JSON-RPC batches, which this server doesn't take.
    static readonly string[] Versions = { "2024-11-05", "2025-06-18", "2025-11-25" };
    // What a batch step must carry, by action (the engine would read a missing Value as "clear the field").
    static readonly Dictionary<string, string[]> StepRequired = new Dictionary<string, string[]> {
        { "set", new[] { "Value" } }, { "keys", new[] { "Keys" } }, { "launch", new[] { "Path" } },
        { "select", new[] { "Option" } } };

    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static TextWriter output;

    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);

    // backend(options with "action") returns the action's text, or throws OrielError with the problem.
    public static void Serve(Func<Dictionary<string, object>, string> backend, string version)
    {
        // Read stdin through a FileStream on the raw handle, not Console.OpenStandardInput(): under Windows OpenSSH the
        // .NET Framework's console stream reports the end of input as soon as a line arrives after a pause (measured:
        // the second message never came through; PowerShell's [Console]::In does the same), so an MCP server over
        // "ssh pc oriel mcp" would stop after its first reply.
        var utf8 = new UTF8Encoding(false);
        var stdin = new Microsoft.Win32.SafeHandles.SafeFileHandle(GetStdHandle(-10), false);  // STD_INPUT_HANDLE
        var input = new StreamReader(new FileStream(stdin, FileAccess.Read, 1), utf8);
        output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
        string line;
        while ((line = input.ReadLine()) != null)
        {
            if (line.Trim() == "") continue;
            object parsed;
            try { parsed = Json.DeserializeObject(line); }
            catch (ArgumentException) { Send(null, null, Error(-32700, "parse error")); continue; }
            var msg = parsed as Dictionary<string, object>;
            if (msg == null) { Send(null, null, Error(-32600, "invalid request")); continue; }
            object id;
            msg.TryGetValue("id", out id);
            try { Handle(msg, id, backend, version); }
            catch (Exception e) { Send(id, null, Error(-32603, "internal error: " + e.Message)); }  // one bad message must not end the server
        }
    }

    static Dictionary<string, object> Error(int code, string message)
    {
        return new Dictionary<string, object> { { "code", code }, { "message", message } };
    }

    static void Send(object id, object result, Dictionary<string, object> error)
    {
        var msg = new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id } };
        if (error != null) msg["error"] = error; else msg["result"] = result;
        output.Write(Json.Serialize(msg) + "\n");
    }

    static void Handle(Dictionary<string, object> msg, object id, Func<Dictionary<string, object>, string> backend, string version)
    {
        if (id == null) return;  // notifications: initialized, cancelled
        object m, p;
        msg.TryGetValue("method", out m);
        msg.TryGetValue("params", out p);
        var method = m as string;
        var parameters = p as Dictionary<string, object>;
        if (p != null && parameters == null) { Send(id, null, Error(-32602, "params must be an object")); return; }
        if (method == "initialize")
        {
            object asked = null;
            if (parameters != null) parameters.TryGetValue("protocolVersion", out asked);
            var v = Array.IndexOf(Versions, asked as string) >= 0 ? (string)asked : Versions[Versions.Length - 1];
            Send(id, new Dictionary<string, object> {
                { "protocolVersion", v },
                { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                { "serverInfo", new Dictionary<string, object> { { "name", "oriel" }, { "version", version } } },
                { "instructions", Instructions } }, null);
        }
        else if (method == "tools/list") Send(id, new Dictionary<string, object> { { "tools", Listed() } }, null);
        else if (method == "tools/call") Call(id, parameters, backend);
        else if (method == "ping") Send(id, new Dictionary<string, object>(), null);
        else Send(id, null, Error(-32601, "no method " + method));
    }

    static List<object> Listed()
    {
        var list = new List<object>();
        foreach (var t in Tools)
        {
            var props = new Dictionary<string, object>();
            foreach (var kv in t.Props)
            {
                var spec = new Dictionary<string, object> { { "type", kv.Value } };
                if (kv.Value == "array") spec["items"] = new Dictionary<string, object> { { "type", "object" } };
                props[kv.Key] = spec;
            }
            // The whole tool list is small, so clients that defer MCP tools behind a search (Claude Code) load it up front:
            // that saves a tool-search round trip, a model turn, at the start of every session.
            list.Add(new Dictionary<string, object> {
                { "name", t.Name }, { "description", t.Description },
                { "inputSchema", new Dictionary<string, object> { { "type", "object" }, { "properties", props }, { "required", t.Required } } },
                { "_meta", new Dictionary<string, object> { { "anthropic/alwaysLoad", true } } } });
        }
        return list;
    }

    // Why the arguments don't fit the tool's schema, or null.
    static string Invalid(Tool t, Dictionary<string, object> args)
    {
        foreach (var name in t.Required)
        {
            object v;
            if (!args.TryGetValue(name, out v) || v == null) return "missing argument " + name;
        }
        foreach (var kv in args)
        {
            string type;
            if (!t.Props.TryGetValue(kv.Key, out type)) return "unknown argument " + kv.Key;
            var v = kv.Value;
            if (v == null) continue;
            bool ok = type == "string" ? v is string
                : type == "boolean" ? v is bool
                : type == "integer" ? v is int || v is long
                : type == "number" ? v is int || v is long || v is decimal || v is double
                : type == "array" ? v is object[] || v is ArrayList
                : true;
            if (!ok) return kv.Key + " must be a " + type;
        }
        object steps;
        if (args.TryGetValue("steps", out steps) && steps is IEnumerable)
        {
            int i = 0;
            foreach (var raw in (IEnumerable)steps)
            {
                i++;
                var step = raw as Dictionary<string, object>;
                object action;
                if (step == null || !step.TryGetValue("action", out action) || !(action is string)) return "step " + i + " must be an object with an action";
                string[] need;
                if (StepRequired.TryGetValue((string)action, out need))
                    foreach (var n in need)
                    {
                        object v;
                        if (!step.TryGetValue(n, out v) || !(v is string)) return "step " + i + " (" + action + ") needs " + n + " as a string";
                    }
            }
        }
        return null;
    }

    static void Call(object id, Dictionary<string, object> parameters, Func<Dictionary<string, object>, string> backend)
    {
        object n, a;
        Tool tool = null;
        if (parameters != null && parameters.TryGetValue("name", out n))
            foreach (var t in Tools) if (t.Name == n as string) tool = t;
        if (tool == null) { Send(id, null, Error(-32602, "unknown tool")); return; }
        parameters.TryGetValue("arguments", out a);
        var args = a as Dictionary<string, object> ?? new Dictionary<string, object>();
        var problem = Invalid(tool, args);
        string text;
        bool isError = problem != null;
        if (isError) text = problem;  // a tool error, not a protocol error, so the model reads it and corrects the call
        else
        {
            var options = new Dictionary<string, object> { { "action", tool.Action } };
            if (tool.Action == "launch") options["Tree"] = true;  // the documented default: reply with the new window's tree
            foreach (var kv in args) if (kv.Value != null) options[tool.Option[kv.Key]] = kv.Value;
            var clock = Stopwatch.StartNew();
            try { text = backend(options); }
            catch (Exception e) { text = Engine.Message(e); isError = true; }
            var took = "(" + tool.Action + " took " + clock.ElapsedMilliseconds + " ms)";
            // A picture comes back as "IMAGE:<mime>;<base64>\n<what its pixels mean>": alone from screenshot, or after
            // an action's text when the action was asked for one (screenshot=true). Send it as an image result.
            int at = text.StartsWith("IMAGE:", StringComparison.Ordinal) ? 0 : text.IndexOf("\nIMAGE:image/", StringComparison.Ordinal);
            if (!isError && at >= 0 && (tool.Action == "screenshot" || Array.IndexOf(PictureActions, tool.Action) >= 0))
            {
                var before = at == 0 ? "" : text.Substring(0, at) + "\n";
                var image = at == 0 ? text : text.Substring(at + 1);
                int semi = image.IndexOf(';'), nl = image.IndexOf('\n', semi);
                var data = nl < 0 ? image.Substring(semi + 1) : image.Substring(semi + 1, nl - semi - 1);
                var note = nl < 0 ? "" : image.Substring(nl + 1) + "\n";
                Send(id, new Dictionary<string, object> {
                    { "content", new object[] {
                        new Dictionary<string, object> { { "type", "text" }, { "text", before + note + took } },
                        new Dictionary<string, object> { { "type", "image" }, { "mimeType", image.Substring(6, semi - 6) }, { "data", data } } } },
                    { "isError", false } }, null);
                return;
            }
            text += "\n" + took;
        }
        Send(id, new Dictionary<string, object> {
            { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } },
            { "isError", isError } }, null);
    }
}
}
