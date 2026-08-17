using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LyricDrop.Models;
using LyricDrop.ViewModels;

namespace LyricDrop.Views;

public partial class DesktopLyricWindow : Window
{
    private static readonly string[] AudioExtensions =
        { ".mp3", ".wav", ".aiff", ".aif", ".aac", ".m4a", ".flac", ".ogg", ".wma", ".mp4" };

    private readonly LyricPlayer _player;
    private bool _isHovering;
    private bool _suspendLocationSave = true;

    public DesktopLyricWindow(LyricPlayer player)
    {
        InitializeComponent();
        _player = player;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInit;
        Closing += OnClosing;
        LocationChanged += OnLocationChanged;

        player.PropertyChanged += OnPlayerChanged;
        Render();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResizeAndPosition();
        ApplyLockState();
        // Allow LocationChanged to persist the position only after the window
        // has been moved to its restored/default location. Any LocationChanged
        // events fired during early WPF/OS placement would otherwise overwrite
        // the saved position with a transient default.
        _suspendLocationSave = false;
    }

    private void OnSourceInit(object? sender, EventArgs e)
    {
        // Tool window so it doesn't appear in alt-tab; we already have ShowInTaskbar=False.
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _player.PropertyChanged -= OnPlayerChanged;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_suspendLocationSave) return;
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        _player.Settings.LyricWindowX = Left;
        _player.Settings.LyricWindowY = Top;
        _player.SaveSettings();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LyricPlayer.LyricFontSize):
            case nameof(LyricPlayer.LyricLineCount):
            case nameof(LyricPlayer.LyricWidthRatio):
                ResizeAndPosition();
                Render();
                break;
            case nameof(LyricPlayer.DesktopLyricLocked):
                ApplyLockState();
                Render();
                break;
            case nameof(LyricPlayer.LyricBgAlways):
                UpdateBackgroundVisibility();
                break;
            case nameof(LyricPlayer.CurrentLine):
            case nameof(LyricPlayer.CurrentIndex):
            case nameof(LyricPlayer.LyricTheme):
            case nameof(LyricPlayer.LyricFontName):
            case nameof(LyricPlayer.LyricShadowEnabled):
                Render();
                break;
        }
    }

    private void ResizeAndPosition()
    {
        var wasSuspended = _suspendLocationSave;
        // Suspend persistence while we adjust size and reposition: setting Left
        // then Top fires LocationChanged twice with a transient (newLeft, oldTop)
        // pair in between, which would otherwise be written back to settings.
        _suspendLocationSave = true;
        try
        {
            var screenW = SystemParameters.PrimaryScreenWidth;
            var newW = Math.Max(360, screenW * _player.LyricWidthRatio);
            var newH = IdealHeight();
            Width = newW;
            Height = newH;

            if (!double.IsNaN(_player.Settings.LyricWindowX) && !double.IsNaN(_player.Settings.LyricWindowY))
            {
                Left = _player.Settings.LyricWindowX;
                Top = _player.Settings.LyricWindowY;
            }
            else
            {
                Left = (screenW - Width) / 2;
                Top = SystemParameters.PrimaryScreenHeight * 0.78;
            }
        }
        finally
        {
            _suspendLocationSave = wasSuspended;
        }
    }

    private double IdealHeight()
    {
        var fs = _player.LyricFontSize;
        var lc = _player.LyricLineCount;
        if (lc >= 3)
        {
            var main = fs * 1.4;
            var sides = fs * 0.65 * 1.3 * (lc - 1);
            return main + sides + 32;
        }
        return fs * 1.4 + 28;
    }

    // -- Rendering --

    private void Render()
    {
        UpdateBackgroundVisibility();

        var line = _player.CurrentLine;

        if (string.IsNullOrEmpty(line))
        {
            LyricHost.Content = MakeText("♪ LyricDrop", _player.LyricFontSize, dim: true);
            return;
        }

        if (_player.LyricLineCount >= 3)
        {
            LyricHost.Content = BuildSmoothScroll();
            return;
        }

        var t = MakeText(line, _player.LyricFontSize, dim: false);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        LyricHost.Content = t;
    }

    private FrameworkElement BuildSmoothScroll()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        var range = _player.LyricLineCount / 2;
        var nearby = _player.NearbyLines(range);
        var smallSize = _player.LyricFontSize * 0.65;

        foreach (var (idx, item) in nearby)
        {
            var isCurrent = idx == _player.CurrentIndex;
            var fontSize = isCurrent ? _player.LyricFontSize : smallSize;
            var t = MakeText(item.Text, fontSize, dim: !isCurrent);
            t.Margin = new Thickness(0, 2, 0, 2);
            stack.Children.Add(t);
        }

        return stack;
    }

    private FrameworkElement MakeText(string text, double fontSize, bool dim)
    {
        TextBlock CreateTextBlock() => new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            FontFamily = ResolveFont(),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        var foreground = CreateTextBlock();
        TextOptions.SetTextFormattingMode(foreground, TextFormattingMode.Display);
        // AllowsTransparency windows cannot render true ClearType. Grayscale with
        // fixed hinting avoids the soft/fringed fallback and keeps glyphs aligned
        // to the pixel grid while still supporting a gradient foreground.
        TextOptions.SetTextRenderingMode(foreground, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(foreground, TextHintingMode.Fixed);

        if (dim)
        {
            foreground.Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        }
        else
        {
            foreground.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            foreground.Foreground = _player.LyricTheme.Brush(foreground.DesiredSize.Width);
        }

        // Dim surrounding lines are already de-emphasized with transparency.
        // A one-pixel shadow is covered differently by each glyph and produces
        // patchy dark edges, so reserve the shadow for the active line only.
        if (!_player.LyricShadowEnabled || dim) return foreground;

        // Applying an Effect directly to the foreground forces WPF to rasterize
        // the entire TextBlock into an intermediate bitmap, softening the glyphs.
        // Render the shadow on a separate layer so the foreground stays vector-sharp.
        var shadow = CreateTextBlock();
        shadow.Foreground = Brushes.Black;
        shadow.Opacity = 0.5;
        shadow.RenderTransform = new TranslateTransform(1, 1);

        var layers = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        layers.Children.Add(shadow);
        layers.Children.Add(foreground);
        return layers;
    }

    private FontFamily ResolveFont()
    {
        var name = _player.LyricFontName;
        if (string.IsNullOrWhiteSpace(name)) return new FontFamily("Microsoft YaHei UI, Segoe UI, Arial");
        try { return new FontFamily(name); }
        catch { return new FontFamily("Microsoft YaHei UI, Segoe UI, Arial"); }
    }

    private void UpdateBackgroundVisibility()
    {
        var show = _player.LyricBgAlways || (_isHovering && !_player.DesktopLyricLocked);
        BgHover.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // -- Hover / drag / lock --

    private void OnMouseEnterWindow(object sender, MouseEventArgs e)
    {
        _isHovering = true;
        UpdateBackgroundVisibility();
        Toolbar.Visibility = Visibility.Visible;
    }

    private void OnMouseLeaveWindow(object sender, MouseEventArgs e)
    {
        _isHovering = false;
        UpdateBackgroundVisibility();
        Toolbar.Visibility = Visibility.Collapsed;
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_player.DesktopLyricLocked) return;
        // Only drag from blank lyric area, not from toolbar buttons
        if (e.OriginalSource is Button) return;
        try { DragMove(); } catch { /* DragMove may throw when not in left-down state */ }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => SettingsWindow.ShowSingleton(_player);

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        _player.DesktopLyricLocked = true;
    }

    private void ApplyLockState()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (_player.DesktopLyricLocked)
        {
            ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        }
        else
        {
            ex &= ~WS_EX_TRANSPARENT;
            ex |= WS_EX_LAYERED;
        }
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_player.DesktopLyricLocked) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (_player.DesktopLyricLocked) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".lrc") _player.LoadLrc(f);
            else if (AudioExtensions.Contains(ext)) _player.LoadAudio(f);
        }
    }

    // -- Win32 --

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
