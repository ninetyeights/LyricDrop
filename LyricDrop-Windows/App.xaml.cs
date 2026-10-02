using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using LyricDrop.ViewModels;
using LyricDrop.Views;

namespace LyricDrop;

public partial class App : Application
{
    private const long MaxCrashLogBytes = 1024 * 1024;
    private const int MaxCrashEntryChars = 64 * 1024;
    private static readonly object CrashLogLock = new();
    public static bool IsShuttingDown { get; private set; }

    public LyricPlayer Player { get; private set; } = null!;
    private TaskbarIcon? _tray;
    private PlayerWindow? _playerWindow;
    private DesktopLyricWindow? _lyricWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogFatal(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogFatal(args.Exception);
            // Continuing after an unknown UI-thread failure can leave playback or window
            // state inconsistent. Let WPF terminate after the sanitized diagnostic is saved.
            args.Handled = false;
        };

        Player = new LyricPlayer();

        _tray = new TaskbarIcon
        {
            Icon = BuildTrayIcon(),
            ToolTipText = "LyricDrop",
            NoLeftClickDelay = true,
            ContextMenu = BuildTrayMenu()
        };
        _tray.LeftClickCommand = new RelayCommand(_ => TogglePlayerPanel());
        _tray.DoubleClickCommand = new RelayCommand(_ => TogglePlayerPanel());
        _tray.ForceCreate();

        Player.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LyricPlayer.CurrentLine) && _tray is not null)
            {
                var text = string.IsNullOrWhiteSpace(Player.CurrentLine) ? "LyricDrop" : "♪ " + Player.CurrentLine;
                if (text.Length > 127) text = text[..127];
                _tray.ToolTipText = text;
            }
        };

        _lyricWindow = new DesktopLyricWindow(Player);
        _lyricWindow.Show();

        // Show the player panel on startup so the user immediately sees the UI
        // even if Win11 has hidden the tray icon in the overflow menu.
        TogglePlayerPanel();
    }

    private static void LogFatal(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricDrop");
            System.IO.Directory.CreateDirectory(dir);
            var logPath = System.IO.Path.Combine(dir, "crash.log");
            var previousLogPath = System.IO.Path.Combine(dir, "crash.previous.log");
            var entry = FormatExceptionForLog(ex);

            lock (CrashLogLock)
            {
                if (System.IO.File.Exists(logPath) && new FileInfo(logPath).Length >= MaxCrashLogBytes)
                    System.IO.File.Move(logPath, previousLogPath, overwrite: true);

                System.IO.File.AppendAllText(logPath, entry, Encoding.UTF8);
            }
        }
        catch { }
    }

    internal static string FormatExceptionForLog(Exception ex)
    {
        var output = new StringBuilder();
        output.AppendLine($"[{DateTimeOffset.UtcNow:O}] Unhandled exception");

        Exception? current = ex;
        for (var depth = 0; current is not null && depth < 5; depth++, current = current.InnerException)
        {
            output.AppendLine($"Exception[{depth}]: {current.GetType().FullName}");
            output.AppendLine($"HResult[{depth}]: 0x{current.HResult:X8}");
            if (current.TargetSite is not null)
                output.AppendLine($"Target[{depth}]: {current.TargetSite.DeclaringType?.FullName}.{current.TargetSite.Name}");

            // Request no source-file information so local user names and workspace paths
            // cannot leak when a development build writes a crash report.
            var stack = new StackTrace(current, fNeedFileInfo: false).ToString();
            if (!string.IsNullOrWhiteSpace(stack)) output.AppendLine(stack);
        }

        output.AppendLine();
        var entry = output.ToString();
        return entry.Length <= MaxCrashEntryChars ? entry : entry[..MaxCrashEntryChars] + "\n[truncated]\n";
    }

    private void TogglePlayerPanel()
    {
        if (_playerWindow is null)
        {
            _playerWindow = new PlayerWindow(Player) { Width = 440 };
            // SourceInitialized fires once the window has a native handle but before
            // it's shown, so we can reposition it via raw Win32 physical-pixel
            // coordinates with no visible jump.
            _playerWindow.SourceInitialized += (_, _) => AnchorToTray(_playerWindow!);
            _playerWindow.Closed += (_, _) => _playerWindow = null;
        }

        if (_playerWindow.IsVisible)
        {
            _playerWindow.Hide();
        }
        else
        {
            _playerWindow.Show();
            if (_playerWindow.WindowState == WindowState.Minimized)
                _playerWindow.WindowState = WindowState.Normal;
            _playerWindow.Activate();
        }
    }

    private System.Windows.Controls.ContextMenu BuildTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "打开播放器面板" };
        openItem.Click += (_, _) => TogglePlayerPanel();
        menu.Items.Add(openItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "歌词外观设置…" };
        settingsItem.Click += (_, _) => SettingsWindow.ShowSingleton(Player);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) =>
        {
            IsShuttingDown = true;
            Player.SaveSettings();
            Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private static Icon BuildTrayIcon()
    {
        // Render a simple "♪" glyph as a 32x32 icon at runtime, so we don't need a binary .ico file.
        const int size = 32;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var bg = new SolidBrush(System.Drawing.Color.FromArgb(0, 0, 0, 0));
            g.Clear(System.Drawing.Color.Transparent);
            using var fg = new SolidBrush(System.Drawing.Color.White);
            using var font = new Font("Segoe UI Symbol", 22, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("♪", font, fg, new RectangleF(0, 0, size, size), sf);
        }
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // Anchors the player panel to the corner of the taskbar nearest the notification
    // area, using raw Win32 physical-pixel coordinates throughout. This sidesteps
    // WPF's DIP-based Window.Left/Top, which is only reliably correct for the
    // primary monitor on PerMonitorV2-aware apps — going through Win32 instead keeps
    // this correct regardless of monitor count, arrangement, or per-monitor DPI.
    private static void AnchorToTray(Window w)
    {
        try
        {
            var trayHwnd = FindWindow("Shell_TrayWnd", null);
            var monitor = trayHwnd != IntPtr.Zero
                ? MonitorFromWindow(trayHwnd, MONITOR_DEFAULTTOPRIMARY)
                : MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return;

            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var winRect)) return;

            var winW = winRect.Right - winRect.Left;
            var winH = winRect.Bottom - winRect.Top;
            var work = info.rcWork;
            var full = info.rcMonitor;
            const int margin = 12;
            int left, top;

            if (work.Top > full.Top)
            {
                // Taskbar on top edge — tray sits at its right end.
                left = work.Right - winW - margin;
                top = work.Top + margin;
            }
            else if (work.Right < full.Right)
            {
                // Taskbar on right edge — tray sits at its bottom end.
                left = work.Right - winW - margin;
                top = work.Bottom - winH - margin;
            }
            else if (work.Left > full.Left)
            {
                // Taskbar on left edge — tray sits at its bottom end.
                left = work.Left + margin;
                top = work.Bottom - winH - margin;
            }
            else
            {
                // Bottom edge (default) or undetectable (e.g. auto-hidden) — anchor bottom-right.
                left = work.Right - winW - margin;
                top = work.Bottom - winH - margin;
            }

            SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch
        {
            // Best-effort placement — leave the window at its default location.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Player?.SaveSettings();
        Player?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}

public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _action;
    private readonly Func<object?, bool>? _canExecute;
    public RelayCommand(Action<object?> action, Func<object?, bool>? canExecute = null)
    { _action = action; _canExecute = canExecute; }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _action(parameter);
    public event EventHandler? CanExecuteChanged
    { add => System.Windows.Input.CommandManager.RequerySuggested += value; remove => System.Windows.Input.CommandManager.RequerySuggested -= value; }
}
