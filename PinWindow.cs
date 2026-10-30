// PinWindow: keep selected windows always on top
// Open the app, pick windows, press OK to pin them
// To unpin: press ESC in any window to unpin all, or select a window here and press Unpin
// Closing the app restores every window it pinned
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x100;
    public const int VK_ESCAPE = 0x1B;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x8;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const uint GW_OWNER = 4;
    public const int DWMWA_CLOAKED = 14;
    public const int SW_RESTORE = 9;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
}

class WinItem
{
    public IntPtr Handle;
    public string Title;
    public bool Topmost;
    public override string ToString() { return (Topmost ? "📌 " : "      ") + Title; }
}

class MainForm : Form
{
    public const string AppTitle = "PinWindow";

    readonly ListBox list = new ListBox();
    readonly Label status = new Label();
    // Windows pinned by this app, restored on close
    readonly HashSet<IntPtr> pinned = new HashSet<IntPtr>();
    IntPtr hook;
    Native.HookProc hookProc; // keep a reference so the GC doesn't collect it

    public MainForm()
    {
        Text = AppTitle;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(520, 420);
        MinimumSize = new Size(380, 300);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var tip = new Label
        {
            Text = "Select the windows to pin and press OK (Ctrl+click to select several, or double-click).\nPress ESC in any window to unpin all.",
            Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 6, 8, 0)
        };

        list.Dock = DockStyle.Fill;
        list.SelectionMode = SelectionMode.MultiExtended;
        list.IntegralHeight = false;
        list.DoubleClick += (s, e) => PinSelected();

        var ok = new Button { Text = "OK", Width = 100, Height = 34 };
        var unpin = new Button { Text = "Unpin", Width = 100, Height = 34 };
        var refresh = new Button { Text = "Refresh", Width = 100, Height = 34 };
        ok.Click += (s, e) => PinSelected();
        unpin.Click += (s, e) => UnpinSelected();
        refresh.Click += (s, e) => RefreshList();
        AcceptButton = ok;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4, 6, 4, 0)
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(unpin);
        buttons.Controls.Add(refresh);

        status.Dock = DockStyle.Bottom;
        status.Height = 26;
        status.Padding = new Padding(8, 4, 8, 0);
        status.ForeColor = Color.DimGray;

        var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        middle.Controls.Add(list);

        Controls.Add(middle);
        Controls.Add(tip);
        Controls.Add(status);
        Controls.Add(buttons);

        // Refresh on open and whenever the app is brought back, so pin states stay current
        Load += (s, e) => RefreshList();
        Activated += (s, e) => RefreshList();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Global keyboard hook: ESC in any window unpins; ESC still reaches that window as usual
        hookProc = KeyboardHook;
        hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc, Native.GetModuleHandle(null), 0);
    }

    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == Native.WM_KEYDOWN && Marshal.ReadInt32(lParam) == Native.VK_ESCAPE
            && (pinned.Count > 0 || ActiveForm == this))
            BeginInvoke(new Action(UnpinAll));
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
        UnpinAll();
        base.OnFormClosing(e);
    }

    static bool IsTopmost(IntPtr h)
    {
        return (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
    }

    static string Title(IntPtr h)
    {
        int len = Native.GetWindowTextLength(h);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        Native.GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    bool SetTopmost(IntPtr h, bool on)
    {
        Native.SetWindowPos(h, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        // Elevated windows may refuse; trust the actual window style
        bool ok = IsTopmost(h) == on;
        if (ok)
        {
            if (on) pinned.Add(h); else pinned.Remove(h);
        }
        return ok;
    }

    List<WinItem> ListWindows()
    {
        var result = new List<WinItem>();
        IntPtr shell = Native.GetShellWindow();
        IntPtr self = Handle;
        Native.EnumWindows((h, l) =>
        {
            if (h == shell || h == self || !Native.IsWindowVisible(h)) return true;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
            if ((Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0) return true;
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;
            string t = Title(h);
            if (t == "" || t == "Program Manager") return true;
            result.Add(new WinItem { Handle = h, Title = t, Topmost = IsTopmost(h) });
            return true;
        }, IntPtr.Zero);
        // Sort by title so the list doesn't jump around after pinning
        result.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCulture));
        return result;
    }

    void RefreshList()
    {
        var selected = new HashSet<IntPtr>();
        foreach (WinItem it in list.SelectedItems) selected.Add(it.Handle);

        list.BeginUpdate();
        list.Items.Clear();
        foreach (var it in ListWindows())
        {
            int i = list.Items.Add(it);
            if (selected.Contains(it.Handle)) list.SetSelected(i, true);
        }
        list.EndUpdate();
    }

    List<WinItem> Selected()
    {
        var r = new List<WinItem>();
        foreach (WinItem it in list.SelectedItems) r.Add(it);
        return r;
    }

    static string Windows(int n) { return n + (n == 1 ? " window" : " windows"); }

    void PinSelected()
    {
        var items = Selected();
        if (items.Count == 0) { status.Text = "Select a window in the list first."; return; }

        int ok = 0, fail = 0;
        foreach (var it in items)
            if (Native.IsWindow(it.Handle) && SetTopmost(it.Handle, true)) ok++; else fail++;

        RefreshList();
        status.Text = "Pinned " + Windows(ok) +
            (fail > 0 ? ", " + fail + " failed (that window may need this app to run as administrator)" : "");
        // Minimize to the taskbar after pinning so it stays out of the way
        if (ok > 0) WindowState = FormWindowState.Minimized;
    }

    void UnpinSelected()
    {
        var items = Selected();
        if (items.Count == 0) { UnpinAll(); return; }

        int ok = 0;
        foreach (var it in items)
            if (Native.IsWindow(it.Handle) && SetTopmost(it.Handle, false)) ok++;
        RefreshList();
        status.Text = "Unpinned " + Windows(ok);
    }

    void UnpinAll()
    {
        int ok = 0;
        foreach (IntPtr h in new List<IntPtr>(pinned))
            if (Native.IsWindow(h) && SetTopmost(h, false)) ok++;
        pinned.Clear();
        if (IsHandleCreated && !IsDisposed)
        {
            RefreshList();
            status.Text = "Unpinned all (" + Windows(ok) + ")";
        }
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        using (new Mutex(true, "PinWindowEn_SingleInstance", out created))
        {
            if (!created)
            {
                // Already running: bring the existing window back
                IntPtr h = Native.FindWindow(null, MainForm.AppTitle);
                if (h != IntPtr.Zero)
                {
                    Native.ShowWindow(h, Native.SW_RESTORE);
                    Native.SetForegroundWindow(h);
                }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
