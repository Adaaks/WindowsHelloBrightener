// Windows Hello Brightener: when a Windows Hello / Windows Security prompt (passkeys, app sign-ins)
// appears, fill that monitor with white around the dialog so the camera sees your face better.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Shown by Windows as the app's name (e.g. in notifications and Task Manager)
[assembly: System.Reflection.AssemblyTitle("Windows Hello Brightener")]
[assembly: System.Reflection.AssemblyProduct("Windows Hello Brightener")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]

static class Native
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
    public const uint LWA_ALPHA = 2;
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
}

static class Settings
{
    const string Key = @"Software\HelloBrightener";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "HelloBrightener";
    public const int DefaultStrength = 70;

    public static int Strength
    {
        get { int v = GetInt("Strength", DefaultStrength); return v >= 1 && v <= 100 ? v : DefaultStrength; }
        set { SetInt("Strength", value); }
    }

    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HelloBrightener";
    const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static void EnableAutoStart()
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            k.SetValue(RunName, "\"" + Program.InstalledExe + "\"");
    }

    // List the app in Settings > Apps > Installed apps, so it can be uninstalled like any other app
    public static void RegisterInstalledApp()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Program.Name);
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "Adaaks");
                k.SetValue("DisplayIcon", Program.InstalledExe);
                k.SetValue("InstallLocation", Program.InstallDir);
                k.SetValue("UninstallString", "\"" + Program.UninstallerExe + "\"");
                k.SetValue("URLInfoAbout", "https://github.com/Adaaks/WindowsHelloBrightener");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 40, RegistryValueKind.DWord); // KB
            }
        }
        catch { }
    }

    // Undo every registry change the app makes
    public static void RemoveAll()
    {
        DeleteValue(RunKey, RunName);
        DeleteValue(StartupApprovedKey, RunName); // left behind if auto-start was disabled in Task Manager
        try { Registry.CurrentUser.DeleteSubKeyTree(Key, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
    }

    static void DeleteValue(string key, string name)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key, true))
                if (k != null) k.DeleteValue(name, false);
        }
        catch { }
    }

    static int GetInt(string name, int def)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
            {
                object v = k == null ? null : k.GetValue(name);
                return v is int ? (int)v : def;
            }
        }
        catch { return def; }
    }

    static void SetInt(string name, int value)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                k.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }
}

// A Windows Hello prompt on screen: its bounds and the DPI of the monitor it's on
struct Prompt
{
    public Rectangle Bounds;
    public int Dpi;
}

class Overlay : Form
{
    readonly System.Windows.Forms.Timer fade = new System.Windows.Forms.Timer();
    int alpha;
    int maxAlpha = 255;
    Rectangle lastHole;

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

    // How opaque the white is at full strength (0-255); lower lets the desktop show through.
    public int MaxAlpha
    {
        get { return maxAlpha; }
        set { maxAlpha = value; if (Visible && !fade.Enabled) SetAlpha(maxAlpha); }
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

    public void ShowAround(Prompt prompt)
    {
        Rectangle screen = Screen.FromRectangle(prompt.Bounds).Bounds;
        if (Bounds != screen) Bounds = screen;

        Rectangle hole = prompt.Bounds;
        hole.Offset(-screen.X, -screen.Y);
        if (hole != lastHole || Region == null)
        {
            // Windows 11 dialogs have 8px rounded corners (scaled with the display). Cut a matching
            // rounded hole, pulled in by a pixel, so no gap shows around the edges or corners.
            float scale = prompt.Dpi > 0 ? prompt.Dpi / 96f : 1f;
            int inset = (int)Math.Ceiling(scale);
            hole.Inflate(-inset, -inset);
            float d = 2 * 8 * scale;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(hole.Left, hole.Top, d, d, 180, 90);
                path.AddArc(hole.Right - d, hole.Top, d, d, 270, 90);
                path.AddArc(hole.Right - d, hole.Bottom - d, d, d, 0, 90);
                path.AddArc(hole.Left, hole.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                Region region = new Region(new Rectangle(0, 0, screen.Width, screen.Height));
                region.Exclude(path);
                Region old = Region;
                Region = region;
                if (old != null) old.Dispose();
            }
            lastHole = prompt.Bounds;
            lastHole.Offset(-screen.X, -screen.Y);
        }
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
    const string Title = Program.Name;

    readonly Overlay overlay = new Overlay();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    readonly Dictionary<uint, string> names = new Dictionary<uint, string>();
    readonly ToolStripMenuItem strengthMenu = new ToolStripMenuItem("Brightness");
    DateTime testUntil = DateTime.MinValue;

    public App(bool firstRun)
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        // Brightness: lower values make the white translucent, to suit your monitor and eyes
        for (int s = 100; s >= 10; s -= 10)
        {
            int pct = s;
            ToolStripMenuItem item = new ToolStripMenuItem(pct + "%" + (pct == 100 ? " (solid)" : ""));
            item.Tag = pct;
            item.Click += delegate
            {
                SetStrength(pct);
                testUntil = DateTime.Now.AddSeconds(3); // preview the new strength
            };
            strengthMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(strengthMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { tray.Visible = false; Application.Exit(); });

        tray.Icon = LoadTrayIcon();
        tray.Text = Title;
        tray.ContextMenuStrip = menu;
        tray.Visible = true;

        SetStrength(Settings.Strength);
        Settings.EnableAutoStart();
        Settings.RegisterInstalledApp();
        Program.EnsureUninstaller();
        Program.RemoveLegacyStartupShortcut();

        poll.Interval = 120;
        poll.Tick += delegate { Update(); };
        poll.Start();

        if (firstRun)
            tray.ShowBalloonTip(8000, "Ready",
                "I'll brighten your screen whenever Windows Hello appears. Right-click my icon in the tray for settings.",
                ToolTipIcon.None);
    }

    // The app icon is embedded as a resource; pick the size Windows uses for tray icons
    static Icon LoadTrayIcon()
    {
        try
        {
            using (Stream st = typeof(App).Assembly.GetManifestResourceStream("HelloBrightener.icon.ico"))
                if (st != null) return new Icon(st, SystemInformation.SmallIconSize);
        }
        catch { }
        return SystemIcons.Application;
    }

    void SetStrength(int pct)
    {
        Settings.Strength = pct;
        overlay.MaxAlpha = pct * 255 / 100;
        foreach (ToolStripMenuItem i in strengthMenu.DropDownItems) i.Checked = (int)i.Tag == pct;
        strengthMenu.Text = "Brightness (" + pct + "%)";
    }

    void Update()
    {
        Prompt? target = FindPrompt();
        if (target == null && DateTime.Now < testUntil)
        {
            Rectangle s = Screen.PrimaryScreen.Bounds;
            Prompt test = new Prompt();
            test.Bounds = new Rectangle(s.X + s.Width / 2 - 250, s.Y + s.Height / 2 - 200, 500, 400);
            test.Dpi = 96;
            target = test;
        }
        if (target.HasValue) overlay.ShowAround(target.Value);
        else overlay.HideOverlay();
    }

    Prompt? FindPrompt()
    {
        Prompt? found = null;
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
            Prompt p = new Prompt();
            p.Bounds = rect;
            p.Dpi = (int)Native.GetDpiForWindow(h);
            found = p;
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
    public const string Name = "Windows Hello Brightener";
    public const string Version = "1.3.0";
    public static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelloBrightener");
    public static readonly string InstalledExe = Path.Combine(InstallDir, "HelloBrightener.exe");
    // The uninstaller is a copy of this same exe; it uninstalls when started under this name
    public static readonly string UninstallerExe = Path.Combine(InstallDir, "Uninstall.exe");

    [STAThread]
    static void Main(string[] args)
    {
        try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } // per-monitor v2, so coordinates are real pixels
        Application.EnableVisualStyles();

        string self = Path.GetFullPath(Application.ExecutablePath);
        if (string.Equals(Path.GetFileName(self), "Uninstall.exe", StringComparison.OrdinalIgnoreCase) ||
            Array.IndexOf(args, "--uninstall") >= 0)
        {
            Uninstall();
            return;
        }

        // Self-install: copy to %LOCALAPPDATA%\HelloBrightener and run from there, so the user
        // can launch the download from anywhere and it still starts with Windows afterwards.
        if (!string.Equals(self, Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                StopRunningCopies();
                Directory.CreateDirectory(InstallDir);
                File.Copy(self, InstalledExe, true);
                File.Copy(self, UninstallerExe, true);
                Process.Start(InstalledExe, "--first-run");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't install " + Name + ": " + ex.Message, Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        bool created;
        using (Mutex m = new Mutex(true, "HelloBrightener_SingleInstance", out created))
        {
            if (!created) return;
            bool firstRun = Array.IndexOf(args, "--first-run") >= 0;
            Application.Run(new App(firstRun));
        }
    }

    // Keep Uninstall.exe next to the installed exe (and up to date after an update)
    public static void EnsureUninstaller()
    {
        try
        {
            if (!File.Exists(UninstallerExe) || new FileInfo(UninstallerExe).Length != new FileInfo(InstalledExe).Length ||
                File.GetLastWriteTimeUtc(UninstallerExe) < File.GetLastWriteTimeUtc(InstalledExe))
                File.Copy(InstalledExe, UninstallerExe, true);
        }
        catch { }
    }

    // Earlier versions were started from a shortcut in the Startup folder; the Run key replaces it.
    public static void RemoveLegacyStartupShortcut()
    {
        try
        {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HelloBrightener.lnk");
            if (File.Exists(lnk)) File.Delete(lnk);
        }
        catch { }
    }

    static void StopRunningCopies()
    {
        int me = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcessesByName("HelloBrightener"))
        {
            if (p.Id == me) continue;
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    // Close the app and undo everything it changed: startup entry, settings, the Installed apps
    // entry and the install folder.
    static void Uninstall()
    {
        if (MessageBox.Show("Uninstall " + Name + "?\n\nThis closes it, stops it starting with Windows, and removes its files and settings.",
                Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        StopRunningCopies();
        Settings.RemoveAll();
        RemoveLegacyStartupShortcut();

        MessageBox.Show(Name + " has been uninstalled.", Name, MessageBoxButtons.OK, MessageBoxIcon.Information);

        // This exe may be running from the install folder, so delete the folder once it has exited
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        catch { }
    }
}
