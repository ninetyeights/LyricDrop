using System;
using System.IO;
using System.Text.Json;
using LyricDrop.Models;

namespace LyricDrop.Services;

public sealed class AppSettings
{
    private const long MaxSettingsFileBytes = 1024 * 1024;
    public string LastAudioPath { get; set; } = string.Empty;
    public string LastLrcPath { get; set; } = string.Empty;
    public bool DesktopLyricLocked { get; set; }
    public double LyricFontSize { get; set; } = 28;
    public int LyricLineCount { get; set; } = 3;
    public LyricTheme LyricTheme { get; set; } = LyricTheme.Neon;
    public bool LyricShadowEnabled { get; set; } = true;
    public bool LyricBgAlways { get; set; }
    public bool LyricSmoothScroll { get; set; } = true;
    public bool IsLooping { get; set; } = true;
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public double LyricOffset { get; set; }
    public float PlaybackRate { get; set; } = 1.0f;
    public string LyricFontName { get; set; } = string.Empty;
    public double LyricWidthRatio { get; set; } = 0.45;
    public double LyricWindowX { get; set; } = double.NaN;
    public double LyricWindowY { get; set; } = double.NaN;
    public bool LaunchAtLogin { get; set; }

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricDrop");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                if (new FileInfo(FilePath).Length > MaxSettingsFileBytes) return new AppSettings();
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch { /* fall through to default */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch { /* swallow — settings are best-effort */ }
    }
}
