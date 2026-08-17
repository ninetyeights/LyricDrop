using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using LyricDrop.Models;
using LyricDrop.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace LyricDrop.Views;

public partial class PlayerWindow : FluentWindow
{
    private static readonly string[] AudioExtensions =
        { ".mp3", ".wav", ".aiff", ".aif", ".aac", ".m4a", ".flac", ".ogg", ".wma", ".mp4" };

    private readonly LyricPlayer _player;
    private bool _seekDragging;
    private bool _resumeAfterSeek;

    public PlayerWindow(LyricPlayer player)
    {
        InitializeComponent();
        _player = player;
        DataContext = player;

        SyncPlayPauseGlyph();
        SyncMuteGlyph();
        SyncRateBox();
        ProgressSlider.Value = _player.CurrentTime;

        // Slider and Thumb class handlers mark some mouse events as handled.
        // handledEventsToo keeps playback ticks from overwriting the value between
        // the user's press and release, including plain clicks on the track.
        ProgressSlider.AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnProgressMouseDown),
            handledEventsToo: true);
        ProgressSlider.AddHandler(
            Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler(OnProgressMouseUp),
            handledEventsToo: true);
        ProgressSlider.ValueChanged += OnProgressValueChanged;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };

        player.PropertyChanged += OnPlayerChanged;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LyricPlayer.IsPlaying):
                SyncPlayPauseGlyph();
                break;
            case nameof(LyricPlayer.IsMuted):
                SyncMuteGlyph();
                break;
            case nameof(LyricPlayer.PlaybackRate):
                SyncRateBox();
                break;
            case nameof(LyricPlayer.CurrentIndex):
                ScrollToCurrent();
                break;
            case nameof(LyricPlayer.CurrentTime):
                // Skip while the user is dragging — otherwise the ~80ms playback
                // timer snaps the thumb back to the live position mid-drag.
                if (!_seekDragging) ProgressSlider.Value = _player.CurrentTime;
                break;
        }
    }

    private void SyncPlayPauseGlyph() => PlayPauseBtn.Content = _player.IsPlaying ? "❚❚" : "▶";
    private void SyncMuteGlyph() => MuteToggle.Content = _player.IsMuted ? "🔈" : "🔊";

    private void SyncRateBox()
    {
        var current = _player.PlaybackRate.ToString("0.##", CultureInfo.InvariantCulture);
        foreach (ComboBoxItem item in RateBox.Items)
        {
            if ((string?)item.Tag == current) { RateBox.SelectedItem = item; return; }
        }
        RateBox.SelectedIndex = 2;
    }

    private void ScrollToCurrent()
    {
        if (_player.CurrentIndex < 0 || _player.CurrentIndex >= _player.Lines.Count) return;
        LyricList.ScrollIntoView(_player.Lines[_player.CurrentIndex]);
    }

    // -- File loading --

    private void OnOpenAudioClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "音频文件|*.mp3;*.wav;*.aiff;*.aif;*.aac;*.m4a;*.flac;*.ogg;*.wma;*.mp4|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == true) _player.LoadAudio(dlg.FileName);
        Activate();
    }

    private void OnOpenLrcClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "LRC 歌词|*.lrc|所有文件|*.*" };
        if (dlg.ShowDialog(this) == true) _player.LoadLrc(dlg.FileName);
        Activate();
    }

    private async void OnLoadUrlClick(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text;
        if (string.IsNullOrWhiteSpace(url)) return;
        await _player.LoadAudioFromUrlAsync(url);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".lrc") _player.LoadLrc(f);
            else if (AudioExtensions.Contains(ext)) _player.LoadAudio(f);
        }
    }

    // -- Transport --

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _player.PlayPause();
    private void OnStopClick(object sender, RoutedEventArgs e) => _player.Stop();
    private void OnSkipBackClick(object sender, RoutedEventArgs e) => _player.SkipBackward();
    private void OnSkipFwdClick(object sender, RoutedEventArgs e) => _player.SkipForward();

    private void OnProgressMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_seekDragging) return;

        _seekDragging = true;
        _resumeAfterSeek = _player.IsPlaying;
        if (_resumeAfterSeek) _player.PlayPause();
    }

    private void OnProgressMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Compute the target value directly from the release point instead of
        // trusting Slider.Value: a plain click (no drag movement) doesn't always
        // reach the target through the Track's built-in "move to point" handling,
        // which left it seeking back to wherever it already was.
        if (ProgressSlider.Template.FindName("PART_Track", ProgressSlider) is Track track)
        {
            ProgressSlider.Value = track.ValueFromPoint(e.GetPosition(track));
        }

        _seekDragging = false;
        _player.Seek(ProgressSlider.Value);

        if (_resumeAfterSeek)
        {
            _resumeAfterSeek = false;
            _player.PlayPause();
        }
    }

    private void OnProgressValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seekDragging)
            _player.Seek(e.NewValue);
    }

    private void OnRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RateBox.SelectedItem is not ComboBoxItem item) return;
        if (float.TryParse((string?)item.Tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            _player.PlaybackRate = v;
    }

    private void OnOffsetMinusClick(object sender, RoutedEventArgs e) => _player.AdjustOffset(-0.5);
    private void OnOffsetPlusClick(object sender, RoutedEventArgs e) => _player.AdjustOffset(0.5);
    private void OnOffsetResetClick(object sender, RoutedEventArgs e) => _player.ResetOffset();

    private void OnLyricDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LyricList.SelectedItem is LyricLine line)
        {
            _player.Seek(line.Time);
            if (!_player.IsPlaying) _player.PlayPause();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => SettingsWindow.ShowSingleton(_player, owner: this);

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _player.PropertyChanged -= OnPlayerChanged;
        base.OnClosed(e);
    }
}

public sealed class TimeConverter : IValueConverter
{
    public static readonly TimeConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => LyricPlayer.FormatTime(value is double d ? d : 0);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
