using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LyricDrop.Models;

namespace LyricDrop.Services;

public static class LrcParser
{
    private const long MaxLrcFileBytes = 5 * 1024 * 1024;
    // Matches a single leading [mm:ss.xx] timestamp token (no trailing lyric capture,
    // so multiple timestamps on one line can be peeled off one at a time).
    private static readonly Regex StampPattern = new(
        @"^\[(\d{1,2}):(\d{2})(?:[\.:](\d{2,3}))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<LyricLine> Parse(string content)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrEmpty(content)) return result;

        foreach (var raw in content.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            // A single LRC line can carry multiple timestamps sharing one body,
            // e.g. [00:01.00][00:05.00]chorus. Peel each leading [mm:ss.xx] off in turn.
            var lineText = raw;
            var stamps = new List<double>();
            while (true)
            {
                var m = StampPattern.Match(lineText);
                if (!m.Success) break;

                var min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double frac = 0;
                if (m.Groups[3].Success)
                {
                    var fs = m.Groups[3].Value;
                    var v = int.Parse(fs, CultureInfo.InvariantCulture);
                    frac = fs.Length == 2 ? v / 100.0 : v / 1000.0;
                }
                stamps.Add(min * 60 + sec + frac);
                lineText = lineText[m.Length..];
            }

            if (stamps.Count == 0) continue;

            // Whatever remains after the leading timestamps is the lyric body.
            var body = lineText.Trim();
            if (string.IsNullOrEmpty(body)) continue;

            // Strip per-character timestamps used by Enhanced LRC (e.g. <00:12.34>)
            body = Regex.Replace(body, @"<\d{1,2}:\d{2}(?:[\.:]\d{2,3})?>", string.Empty).Trim();
            if (string.IsNullOrEmpty(body)) continue;

            foreach (var t in stamps)
            {
                result.Add(new LyricLine { Time = t, Text = body });
            }
        }

        return result.OrderBy(l => l.Time).ToList();
    }

    public static string ReadFileWithEncodingFallback(string path)
    {
        if (new FileInfo(path).Length > MaxLrcFileBytes)
            throw new InvalidDataException("LRC 文件超过 5 MiB 限制");
        var bytes = File.ReadAllBytes(path);
        return DecodeBytes(bytes);
    }

    public static string DecodeBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // Try UTF-8 strict
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            return utf8.GetString(bytes);
        }
        catch
        {
            // Fall back to GB18030 (common for Chinese LRC files)
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding("GB18030").GetString(bytes);
            }
            catch
            {
                return Encoding.Default.GetString(bytes);
            }
        }
    }
}
