using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using LyricDrop.Models;
using LyricDrop.Services;

namespace LyricDrop.ViewModels;

public sealed class LyricPlayer : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly MediaPlayer _media = new();
    private readonly DispatcherTimer _timer;
    private readonly AppSettings _settings;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true });

    private string _audioFileName = string.Empty;
    private string _lrcFileName = string.Empty;
    private string _currentLine = string.Empty;
    private int _currentIndex = -1;
    private bool _isPlaying;
    private bool _isReady;
    private double _currentTime;
    private double _duration;
    private bool _isLoadingUrl;
    private string? _downloadedAudioPath;

    public ObservableCollection<LyricLine> Lines { get; } = new();

    public string AudioFileName { get => _audioFileName; private set => Set(ref _audioFileName, value); }
    public string LrcFileName { get => _lrcFileName; private set => Set(ref _lrcFileName, value); }
    public string CurrentLine { get => _currentLine; private set => Set(ref _currentLine, value); }
    public int CurrentIndex { get => _currentIndex; private set => Set(ref _currentIndex, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public bool IsReady { get => _isReady; private set => Set(ref _isReady, value); }
    public double CurrentTime { get => _currentTime; private set => Set(ref _currentTime, value); }
    public double Duration { get => _duration; private set => Set(ref _duration, value); }
    public bool IsLoadingUrl { get => _isLoadingUrl; private set => Set(ref _isLoadingUrl, value); }

    public bool IsLooping
    {
        get => _settings.IsLooping;
        set { if (_settings.IsLooping != value) { _settings.IsLooping = value; _settings.Save(); OnChanged(); } }
    }

    public float Volume
    {
        get => _settings.Volume;
        set
        {
            if (Math.Abs(_settings.Volume - value) < 0.001) return;
            _settings.Volume = Math.Clamp(value, 0f, 1f);
            _media.Volume = IsMuted ? 0 : _settings.Volume;
            _settings.Save();
            OnChanged();
        }
    }

    public bool IsMuted
    {
        get => _settings.IsMuted;
        set { if (_settings.IsMuted != value) { _settings.IsMuted = value; _media.Volume = value ? 0 : _settings.Volume; _settings.Save(); OnChanged(); } }
    }

    public float PlaybackRate
    {
        get => _settings.PlaybackRate;
        set
        {
            if (Math.Abs(_settings.PlaybackRate - value) < 0.001) return;
            _settings.PlaybackRate = value;
            _media.SpeedRatio = value;
            _settings.Save();
            OnChanged();
        }
    }

    public double LyricOffset
    {
        get => _settings.LyricOffset;
        set { if (Math.Abs(_settings.LyricOffset - value) > 0.0001) { _settings.LyricOffset = value; _settings.Save(); OnChanged(); UpdateLyric(force: true); } }
    }

    public bool DesktopLyricLocked
    {
        get => _settings.DesktopLyricLocked;
        set { if (_settings.DesktopLyricLocked != value) { _settings.DesktopLyricLocked = value; _settings.Save(); OnChanged(); } }
    }

    public double LyricFontSize
    {
        get => _settings.LyricFontSize;
        set { if (Math.Abs(_settings.LyricFontSize - value) > 0.01) { _settings.LyricFontSize = value; _settings.Save(); OnChanged(); } }
    }

    public int LyricLineCount
    {
        get => _settings.LyricLineCount;
        set { if (_settings.LyricLineCount != value) { _settings.LyricLineCount = value; _settings.Save(); OnChanged(); } }
    }

    public LyricTheme LyricTheme
    {
        get => _settings.LyricTheme;
        set { if (_settings.LyricTheme != value) { _settings.LyricTheme = value; _settings.Save(); OnChanged(); OnChanged(nameof(ThemeBrush)); } }
    }

    public Brush ThemeBrush => _settings.LyricTheme.Brush();

    public bool LyricShadowEnabled
    {
        get => _settings.LyricShadowEnabled;
        set { if (_settings.LyricShadowEnabled != value) { _settings.LyricShadowEnabled = value; _settings.Save(); OnChanged(); } }
    }

    public bool LyricBgAlways
    {
        get => _settings.LyricBgAlways;
        set { if (_settings.LyricBgAlways != value) { _settings.LyricBgAlways = value; _settings.Save(); OnChanged(); } }
    }

    public double LyricWidthRatio
    {
        get => _settings.LyricWidthRatio;
        set { if (Math.Abs(_settings.LyricWidthRatio - value) > 0.001) { _settings.LyricWidthRatio = Math.Clamp(value, 0.3, 0.95); _settings.Save(); OnChanged(); } }
    }

    public string LyricFontName
    {
        get => _settings.LyricFontName;
        set { if (_settings.LyricFontName != value) { _settings.LyricFontName = value ?? string.Empty; _settings.Save(); OnChanged(); } }
    }

    public AppSettings Settings => _settings;

    public LyricPlayer()
    {
        _settings = AppSettings.Load();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _timer.Tick += (_, _) => Tick();

        _media.Volume = IsMuted ? 0 : Volume;
        _media.SpeedRatio = PlaybackRate;
        _media.MediaOpened += (_, _) =>
        {
            try
            {
                Duration = _media.NaturalDuration.HasTimeSpan
                    ? _media.NaturalDuration.TimeSpan.TotalSeconds
                    : 0;
                IsReady = true;
            }
            catch { Duration = 0; }
        };
        _media.MediaFailed += (_, e) =>
        {
            IsReady = false;
            IsPlaying = false;
            _timer.Stop();
            AudioFileName = $"加载失败: {e.ErrorException?.Message ?? "未知错误"}";
        };
        _media.MediaEnded += (_, _) =>
        {
            if (IsLooping)
            {
                _media.Position = TimeSpan.Zero;
                CurrentTime = 0;
                UpdateLyric(force: true);
                _media.Play();
                IsPlaying = true;
            }
            else
            {
                IsPlaying = false;
                _timer.Stop();
            }
        };

        RestoreLastFiles();
    }

    public LyricLine? NextLine =>
        CurrentIndex >= 0 && CurrentIndex + 1 < Lines.Count ? Lines[CurrentIndex + 1] : null;

    public IReadOnlyList<(int index, LyricLine line)> NearbyLines(int range = 2)
    {
        if (CurrentIndex < 0) return Array.Empty<(int, LyricLine)>();
        var result = new List<(int, LyricLine)>();
        int start = Math.Max(0, CurrentIndex - range);
        int end = Math.Min(Lines.Count - 1, CurrentIndex + range);
        for (int i = start; i <= end; i++) result.Add((i, Lines[i]));
        return result;
    }

    public void LoadAudio(string path)
    {
        try
        {
            _media.Stop();
            _media.Close();
            DeleteDownloadedAudio();
            IsPlaying = false;
            IsReady = false;
            CurrentTime = 0;
            CurrentIndex = -1;
            CurrentLine = string.Empty;
            Lines.Clear();
            LrcFileName = string.Empty;
            _settings.LastLrcPath = string.Empty;
            _media.Open(new Uri(path, UriKind.Absolute));
            AudioFileName = Path.GetFileName(path);
            _settings.LastAudioPath = path;
            _settings.Save();

            // Auto-load same-name .lrc
            var lrc = Path.ChangeExtension(path, ".lrc");
            if (File.Exists(lrc))
            {
                LoadLrc(lrc);
            }
        }
        catch (Exception ex)
        {
            AudioFileName = $"加载失败: {ex.Message}";
        }
    }

    public void LoadLrc(string path)
    {
        try
        {
            var content = LrcParser.ReadFileWithEncodingFallback(path);
            ApplyLrcContent(content, Path.GetFileName(path));
            _settings.LastLrcPath = path;
            _settings.Save();
        }
        catch (Exception ex)
        {
            LrcFileName = $"LRC 加载失败: {ex.Message}";
        }
    }

    public void ApplyLrcContent(string content, string displayName)
    {
        var parsed = LrcParser.Parse(content);
        Lines.Clear();
        foreach (var l in parsed) Lines.Add(l);
        CurrentIndex = -1;
        CurrentLine = string.Empty;
        LrcFileName = displayName;
        OnChanged(nameof(NextLine));
    }

    public async Task LoadAudioFromUrlAsync(string urlString)
    {
        urlString = (urlString ?? string.Empty).Trim();
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url)) return;

        IsLoadingUrl = true;
        AudioFileName = "加载中…";
        string? pendingTempPath = null;

        try
        {
            Uri audioUrl = url;
            Uri? lrcUrl = null;
            var ext = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
            if (ext is "html" or "htm" or "")
            {
                var parsed = await TryParseWebpageAsync(url);
                if (parsed.audio is not null)
                {
                    audioUrl = parsed.audio;
                    lrcUrl = parsed.lrc;
                }
            }

            // Download audio to temp; MediaPlayer.Open over http works but local file is more reliable
            var tempPath = Path.Combine(Path.GetTempPath(),
                $"lyricdrop_{Guid.NewGuid():N}{(string.IsNullOrEmpty(Path.GetExtension(audioUrl.AbsolutePath)) ? ".mp3" : Path.GetExtension(audioUrl.AbsolutePath))}");
            pendingTempPath = tempPath;
            using (var resp = await Http.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tempPath);
                await resp.Content.CopyToAsync(fs);
            }

            _media.Stop();
            _media.Close();
            DeleteDownloadedAudio();
            Lines.Clear();
            LrcFileName = string.Empty;
            _settings.LastLrcPath = string.Empty;
            _media.Open(new Uri(tempPath, UriKind.Absolute));
            _downloadedAudioPath = tempPath;
            pendingTempPath = null;
            AudioFileName = Path.GetFileName(audioUrl.AbsolutePath);
            _settings.LastAudioPath = url.ToString();
            _settings.Save();
            CurrentTime = 0;
            CurrentIndex = -1;
            CurrentLine = string.Empty;

            if (lrcUrl is not null)
            {
                await TryDownloadLrcAsync(lrcUrl);
            }
            else
            {
                await TrySiblingLrcAsync(audioUrl);
            }
        }
        catch (Exception ex)
        {
            AudioFileName = $"加载失败: {ex.Message}";
        }
        finally
        {
            if (!string.IsNullOrEmpty(pendingTempPath))
            {
                try { File.Delete(pendingTempPath); }
                catch { /* best-effort cleanup of a partial download */ }
            }
            IsLoadingUrl = false;
        }
    }

    private async Task<(Uri? audio, Uri? lrc)> TryParseWebpageAsync(Uri url)
    {
        try
        {
            var html = await Http.GetStringAsync(url);
            var highUri = ExtractJs(html, "\"high_uri\"\\s*:\\s*\"([^\"]+)\"");
            var lrcUri = ExtractJs(html, "\"lrc_uri\"\\s*:\\s*\"([^\"]+)\"");
            var highFile = ExtractJs(html, "\"high\"\\s*:\\s*\"([^\"]+)\"");
            var lrcFile = ExtractJs(html, "\"lrc\"\\s*:\\s*\"([^\"]+)\"");

            if (highUri is null || highFile is null) return (null, null);
            if (!Uri.TryCreate(highUri + highFile, UriKind.Absolute, out var audioUrl)) return (null, null);

            Uri? lrc = null;
            if (lrcUri is not null && lrcFile is not null)
                Uri.TryCreate(lrcUri + lrcFile, UriKind.Absolute, out lrc);
            return (audioUrl, lrc);
        }
        catch { return (null, null); }
    }

    private static string? ExtractJs(string html, string pattern)
    {
        var m = Regex.Match(html, pattern);
        if (!m.Success || m.Groups.Count < 2) return null;
        return m.Groups[1].Value.Replace("\\/", "/");
    }

    private async Task TryDownloadLrcAsync(Uri lrcUrl)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(lrcUrl);
            var content = LrcParser.DecodeBytes(bytes);
            ApplyLrcContent(content, Path.GetFileName(lrcUrl.AbsolutePath));
        }
        catch { /* ignore */ }
    }

    private async Task TrySiblingLrcAsync(Uri audioUrl)
    {
        var baseUri = audioUrl.AbsoluteUri;
        var ext = Path.GetExtension(audioUrl.AbsolutePath);
        var candidates = new List<Uri>();
        var lrc1 = baseUri[..(baseUri.Length - ext.Length)] + ".lrc";
        if (Uri.TryCreate(lrc1, UriKind.Absolute, out var u1)) candidates.Add(u1);
        if (baseUri.Contains("/audio/"))
        {
            var lrc2 = baseUri.Replace("/audio/", "/lrc/").Replace(ext, ".lrc");
            if (Uri.TryCreate(lrc2, UriKind.Absolute, out var u2)) candidates.Add(u2);
        }
        foreach (var c in candidates)
        {
            try
            {
                using var resp = await Http.GetAsync(c);
                if (!resp.IsSuccessStatusCode) continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var content = LrcParser.DecodeBytes(bytes);
                ApplyLrcContent(content, Path.GetFileNameWithoutExtension(audioUrl.AbsolutePath) + ".lrc");
                return;
            }
            catch { /* try next */ }
        }
    }

    public void PlayPause()
    {
        if (!IsReady) return;
        if (IsPlaying)
        {
            _media.Pause();
            IsPlaying = false;
            _timer.Stop();
        }
        else
        {
            _media.Play();
            IsPlaying = true;
            _timer.Start();
        }
    }

    public void Stop()
    {
        _media.Stop();
        IsPlaying = false;
        _timer.Stop();
        CurrentTime = 0;
        CurrentIndex = -1;
        CurrentLine = string.Empty;
    }

    public void Seek(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, Math.Max(0, Duration));
        _media.Position = TimeSpan.FromSeconds(seconds);
        CurrentTime = seconds;
        UpdateLyric(force: true);
    }

    public void SkipForward(double sec = 5) => Seek(CurrentTime + sec);
    public void SkipBackward(double sec = 5) => Seek(CurrentTime - sec);

    public void AdjustOffset(double delta)
    {
        LyricOffset = Math.Round((LyricOffset + delta) * 100) / 100.0;
    }

    public void ResetOffset() => LyricOffset = 0;

    public void ClearAll()
    {
        Stop();
        _media.Close();
        DeleteDownloadedAudio();
        AudioFileName = string.Empty;
        LrcFileName = string.Empty;
        Lines.Clear();
        Duration = 0;
        IsReady = false;
        _settings.LastAudioPath = string.Empty;
        _settings.LastLrcPath = string.Empty;
        _settings.Save();
    }

    public void SaveSettings() => _settings.Save();

    public void Dispose()
    {
        _timer.Stop();
        _media.Close();
        DeleteDownloadedAudio();
    }

    private void DeleteDownloadedAudio()
    {
        if (string.IsNullOrEmpty(_downloadedAudioPath)) return;

        try { File.Delete(_downloadedAudioPath); }
        catch { /* best-effort cleanup; the media backend may still hold the file briefly */ }
        _downloadedAudioPath = null;
    }

    private void Tick()
    {
        try
        {
            CurrentTime = _media.Position.TotalSeconds;
        }
        catch { }
        UpdateLyric();
    }

    private void UpdateLyric(bool force = false)
    {
        if (Lines.Count == 0)
        {
            if (!string.IsNullOrEmpty(CurrentLine)) CurrentLine = string.Empty;
            if (CurrentIndex != -1) CurrentIndex = -1;
            return;
        }

        var adjusted = CurrentTime + LyricOffset;
        int newIndex = -1;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Time <= adjusted) newIndex = i;
            else break;
        }

        if (newIndex != CurrentIndex || force)
        {
            CurrentIndex = newIndex;
            CurrentLine = newIndex >= 0 ? Lines[newIndex].Text : string.Empty;
            OnChanged(nameof(NextLine));
        }
    }

    private void RestoreLastFiles()
    {
        var path = _settings.LastAudioPath;
        if (string.IsNullOrEmpty(path)) return;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadAudioFromUrlAsync(path);
        }
        else if (File.Exists(path))
        {
            LoadAudio(path);
        }

        var lrcPath = _settings.LastLrcPath;
        if (!string.IsNullOrEmpty(lrcPath) && File.Exists(lrcPath) && Lines.Count == 0)
        {
            LoadLrc(lrcPath);
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

    public static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }
}
