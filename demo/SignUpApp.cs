// SignUpApp.cs: the small sign-up form that Claude Code tests through oriel in the demo video (demo/README.md).
// It has one deliberate bug for the test to find: the check for an email that is already a member compares
// case-sensitively, so ADA@example.com signs up again after ada@example.com.
// Build: powershell -ExecutionPolicy Bypass -File demo\build.ps1   (C# 5, the compiler that comes with Windows)
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// What Visual Studio writes for a .NET Framework 4.8 project. Without it the app runs with WinForms' legacy
// accessibility, where the combo box offers UI Automation no items to select (checked with oriel 0.1.0, 2026-11-10).
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace OrielDemo
{
static class App
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();  // sharp text on a scaled screen instead of a stretched bitmap
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SignUpForm());
    }
}

class Member
{
    public string Name, Email, Plan;
    public Member(string name, string email, string plan) { Name = name; Email = email; Plan = plan; }
    public override string ToString() { return Name + " <" + Email + ">, " + Plan; }
}

class SignUpForm : Form
{
    const int FieldWidth = 460;  // in 96 dpi pixels; AutoScaleMode.Dpi scales it to the screen
    static readonly string[] Seed =
    {
        "Grace Hopper", "Alan Turing", "Katherine Johnson", "Edsger Dijkstra", "Frances Allen", "Dennis Ritchie",
        "John von Neumann", "Hedy Lamarr", "Claude Shannon", "Kathleen Booth", "Charles Babbage", "Jean Bartik",
        "Kristen Nygaard", "John Backus", "Seymour Cray", "Mary Kenneth Keller", "Gary Kildall", "Niklaus Wirth",
    };
    static readonly string[] Plans = { "Free", "Pro", "Team" };

    readonly TextBox name = new TextBox { AccessibleName = "Name", Width = FieldWidth };
    readonly TextBox email = new TextBox { AccessibleName = "Email", Width = FieldWidth };
    readonly ComboBox plan = new ComboBox { AccessibleName = "Plan", Width = FieldWidth, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox terms = new CheckBox { Text = "I accept the terms", AutoSize = true };
    readonly Button signUp = new Button { Text = "Sign up", Width = 140, Height = 34 };
    readonly Label status = new Label { AutoSize = false, Width = FieldWidth, Height = 52, TextAlign = ContentAlignment.MiddleLeft };
    readonly ListBox members = new ListBox { AccessibleName = "Members", Width = FieldWidth, Height = 210 };
    readonly Button remove = new Button { Text = "Remove selected member", Width = 240, Height = 34 };

    public SignUpForm()
    {
        SuspendLayout();
        Text = "oriel demo: sign up";
        Font = new Font("Segoe UI", 11F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        plan.Items.AddRange(Plans);
        for (int i = 0; i < Seed.Length; i++)
            members.Items.Add(new Member(Seed[i], Seed[i].ToLowerInvariant().Replace(' ', '.') + "@example.com", Plans[i % Plans.Length]));

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(16), Dock = DockStyle.Fill };
        int row = 0;
        Row(grid, row++, "Name:", name);
        Row(grid, row++, "Email:", email);
        Row(grid, row++, "Plan:", plan);
        Row(grid, row++, null, terms);
        Row(grid, row++, null, signUp);
        Row(grid, row++, "Status:", status);
        Row(grid, row++, "Members:", members);
        Row(grid, row++, null, remove);
        Controls.Add(grid);
        AcceptButton = signUp;
        signUp.Click += delegate { SignUp(); };
        remove.Click += delegate { Remove(); };
        Say("Fill in the form, then press Sign up.", false);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    static void Row(TableLayoutPanel grid, int row, string caption, Control field)
    {
        if (caption != null)
            grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 16, 3) }, 0, row);
        grid.Controls.Add(field, 1, row);
    }

    void Say(string text, bool error)
    {
        status.Text = text;
        status.ForeColor = error ? Color.Firebrick : Color.DarkGreen;
    }

    void SignUp()
    {
        var who = name.Text.Trim();
        var address = email.Text.Trim();
        if (who.Length == 0) { Say("Error: enter a name.", true); return; }
        if (!ValidEmail(address)) { Say("Error: enter a valid email address.", true); return; }
        if (plan.SelectedIndex < 0) { Say("Error: choose a plan.", true); return; }
        if (!terms.Checked) { Say("Error: accept the terms to sign up.", true); return; }
        foreach (Member m in members.Items)
            if (m.Email == address) { Say("Error: " + address + " is already a member.", true); return; }

        var added = new Member(who, address, (string)plan.SelectedItem);
        members.Items.Add(added);
        Say("Welcome, " + who + " (" + address + "), on the " + added.Plan + " plan. " + members.Items.Count + " members now.", false);
        name.Text = "";
        email.Text = "";
        plan.SelectedIndex = -1;
        terms.Checked = false;
    }

    void Remove()
    {
        var m = members.SelectedItem as Member;
        if (m == null) { Say("Error: select a member first.", true); return; }
        members.Items.Remove(m);
        Say("Removed " + m.Name + ".", false);
    }

    static bool ValidEmail(string s)
    {
        int at = s.IndexOf('@');
        if (at < 1 || at != s.LastIndexOf('@') || s.IndexOf(' ') >= 0) return false;
        var domain = s.Substring(at + 1);
        int dot = domain.IndexOf('.');
        return dot > 0 && !domain.EndsWith(".");
    }
}
}
