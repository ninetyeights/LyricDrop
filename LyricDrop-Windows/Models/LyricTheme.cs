using System.Collections.Generic;
using System.Windows.Media;

namespace LyricDrop.Models;

public enum LyricTheme
{
    Neon,
    Sunset,
    Ocean,
    Forest,
    PureWhite,
    Sakura,
    SnowyNight
}

public static class LyricThemeExtensions
{
    public static string Label(this LyricTheme theme) => theme switch
    {
        LyricTheme.Neon => "霓虹",
        LyricTheme.Sunset => "日落",
        LyricTheme.Ocean => "海洋",
        LyricTheme.Forest => "森林",
        LyricTheme.PureWhite => "纯白",
        LyricTheme.Sakura => "樱花",
        LyricTheme.SnowyNight => "雪夜",
        _ => theme.ToString()
    };

    public static GradientStopCollection Stops(this LyricTheme theme)
    {
        var colors = theme switch
        {
            LyricTheme.Neon => new[] { Color.FromRgb(168, 85, 247), Color.FromRgb(236, 72, 153), Color.FromRgb(34, 211, 238) },
            LyricTheme.Sunset => new[] { Color.FromRgb(253, 224, 71), Color.FromRgb(249, 115, 22), Color.FromRgb(239, 68, 68) },
            LyricTheme.Ocean => new[] { Color.FromRgb(34, 211, 238), Color.FromRgb(59, 130, 246), Color.FromRgb(99, 102, 241) },
            LyricTheme.Forest => new[] { Color.FromRgb(34, 197, 94), Color.FromRgb(110, 231, 183), Color.FromRgb(20, 184, 166) },
            LyricTheme.PureWhite => new[] { Colors.White, Colors.White },
            LyricTheme.Sakura => new[] { Color.FromRgb(244, 114, 182), Colors.White, Color.FromRgb(244, 114, 182) },
            LyricTheme.SnowyNight => new[] { Color.FromRgb(191, 219, 254), Color.FromRgb(226, 232, 240), Color.FromRgb(148, 163, 184) },
            _ => new[] { Colors.White, Colors.White }
        };
        var stops = new GradientStopCollection();
        for (int i = 0; i < colors.Length; i++)
        {
            double offset = colors.Length == 1 ? 0 : (double)i / (colors.Length - 1);
            stops.Add(new GradientStop(colors[i], offset));
        }
        return stops;
    }

    public static LinearGradientBrush Brush(this LyricTheme theme, double? lineWidth = null)
    {
        var brush = new LinearGradientBrush
        {
            GradientStops = theme.Stops()
        };

        if (lineWidth is > 0)
        {
            // Foreground brushes using RelativeToBoundingBox are mapped once per
            // glyph by WPF. Absolute coordinates keep one continuous gradient
            // across the measured width of the complete lyric line.
            brush.MappingMode = BrushMappingMode.Absolute;
            brush.StartPoint = new System.Windows.Point(0, 0);
            brush.EndPoint = new System.Windows.Point(lineWidth.Value, 0);
        }
        else
        {
            brush.StartPoint = new System.Windows.Point(0, 0.5);
            brush.EndPoint = new System.Windows.Point(1, 0.5);
        }

        brush.Freeze();
        return brush;
    }

    public static IEnumerable<LyricTheme> All =>
        new[] { LyricTheme.Neon, LyricTheme.Sunset, LyricTheme.Ocean, LyricTheme.Forest, LyricTheme.PureWhite, LyricTheme.Sakura, LyricTheme.SnowyNight };
}
