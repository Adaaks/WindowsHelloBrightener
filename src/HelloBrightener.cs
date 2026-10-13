// HelloBrightener: when a Windows Hello / Windows Security prompt (passkeys, app sign-ins)
// appears, fill that monitor with white around the dialog so the camera sees your face better.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Native
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
    public const uint LWA_ALPHA = 2;
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
}

class Overlay : Form
{
    readonly System.Windows.Forms.Timer fade = new System.Windows.Forms.Timer();
    int alpha;
    int maxAlpha = 255;

    // How opaque the white is at full strength (0-255); lower lets the desktop show through.
    public int MaxAlpha
    {
        get { return maxAlpha; }
        set { maxAlpha = value; if (Visible && !fade.Enabled) SetAlpha(maxAlpha); }
    }

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        StartPosition = FormStartPosition.Manual;
        fade.Interval = 15;
        fade.Tick += delegate { SetAlpha(Math.Min(maxAlpha, alpha + 30)); if (alpha >= maxAlpha) fade.Stop(); };
    }

    // Set alpha directly: WinForms' Opacity property drops the layered alpha when it reaches 1.0,
    // leaving the window stuck slightly see-through.
    void SetAlpha(int a)
    {
        alpha = a;
        Native.SetLayeredWindowAttributes(Handle, 0, (byte)a, Native.LWA_ALPHA);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // toolwindow | noactivate | click-through | layered | topmost
            cp.ExStyle |= 0x80 | 0x08000000 | 0x20 | 0x80000 | 0x8;
            return cp;
        }
    }

    public void ShowAround(Rectangle hole)
    {
        Rectangle screen = Screen.FromRectangle(hole).Bounds;
        if (Bounds != screen) Bounds = screen;
        hole.Offset(-screen.X, -screen.Y);
        Region region = new Region(new Rectangle(0, 0, screen.Width, screen.Height));
        region.Exclude(hole);
        Region old = Region;
        Region = region;
        if (old != null) old.Dispose();
        if (!Visible) { SetAlpha(0); Show(); fade.Start(); }
    }

    public void HideOverlay()
    {
        if (Visible) { fade.Stop(); Hide(); }
    }
}

class App : ApplicationContext
{
    static readonly string[] Processes = { "CredentialUIBroker" };
    static readonly string[] Classes = { "Credential Dialog Xaml Host" };

    readonly Overlay overlay = new Overlay();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    readonly Dictionary<uint, string> names = new Dictionary<uint, string>();
    DateTime testUntil = DateTime.MinValue;

    const string SettingsKey = @"Software\HelloBrightener";
    static readonly int[] Strengths = { 100, 90, 75, 50, 25 };

    public App()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Test white frame (3 s)", null, delegate { testUntil = DateTime.Now.AddSeconds(3); });

        // White strength: lower values make the frame translucent, to calibrate for your monitor
        ToolStripMenuItem strength = new ToolStripMenuItem("White strength");
        int saved = LoadStrength();
        foreach (int s in Strengths)
        {
            int pct = s;
            ToolStripMenuItem item = new ToolStripMenuItem(pct + "%" + (pct == 100 ? " (solid)" : ""));
            item.Checked = pct == saved;
            item.Click += delegate
            {
                foreach (ToolStripMenuItem other in strength.DropDownItems) other.Checked = false;
                item.Checked = true;
                ApplyStrength(pct);
                SaveStrength(pct);
                testUntil = DateTime.Now.AddSeconds(3); // preview the new strength
            };
            strength.DropDownItems.Add(item);
        }
        ApplyStrength(saved);
        menu.Items.Add(strength);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { tray.Visible = false; Application.Exit(); });
        tray.Icon = SystemIcons.Shield;
        tray.Text = "Hello Brightener";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;

        poll.Interval = 120;
        poll.Tick += delegate { Update(); };
        poll.Start();
    }

    void ApplyStrength(int pct) { overlay.MaxAlpha = pct * 255 / 100; }

    static int LoadStrength()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                object v = k == null ? null : k.GetValue("Strength");
                if (v is int && Array.IndexOf(Strengths, (int)v) >= 0) return (int)v;
            }
        }
        catch { }
        return 100;
    }

    static void SaveStrength(int pct)
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey))
                k.SetValue("Strength", pct, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }
    }

    void Update()
    {
        Rectangle? target = FindPrompt();
        if (target == null && DateTime.Now < testUntil)
        {
            Rectangle s = Screen.PrimaryScreen.Bounds;
            target = new Rectangle(s.X + s.Width / 2 - 250, s.Y + s.Height / 2 - 200, 500, 400);
        }
        if (target.HasValue) overlay.ShowAround(target.Value);
        else overlay.HideOverlay();
    }

    Rectangle? FindPrompt()
    {
        Rectangle? found = null;
        Native.EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;
            if (!IsPromptWindow(h)) return true;
            Native.RECT r;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0) return true;
            Rectangle rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rect.Width < 150 || rect.Height < 150) return true;
            found = rect;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    bool IsPromptWindow(IntPtr h)
    {
        StringBuilder cls = new StringBuilder(256);
        Native.GetClassName(h, cls, cls.Capacity);
        foreach (string c in Classes) if (cls.ToString() == c) return true;

        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        string name;
        if (!names.TryGetValue(pid, out name))
        {
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { name = ""; }
            if (names.Count > 500) names.Clear();
            names[pid] = name;
        }
        foreach (string p in Processes) if (string.Equals(name, p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        using (Mutex m = new Mutex(true, "HelloBrightener_SingleInstance", out created))
        {
            if (!created) return;
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } // per-monitor v2, so coordinates are real pixels
            Application.EnableVisualStyles();
            Application.Run(new App());
        }
    }
}
