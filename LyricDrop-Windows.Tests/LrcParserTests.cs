using System.IO;
using System.Linq;
using System.Text;
using LyricDrop.Services;
using Xunit;

namespace LyricDrop.Windows.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parse_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(LrcParser.Parse(""));
        Assert.Empty(LrcParser.Parse(null!));
    }

    [Fact]
    public void Parse_SingleLine_ExtractsTimeAndText()
    {
        var lines = LrcParser.Parse("[00:12.50]Hello world");

        var line = Assert.Single(lines);
        Assert.Equal(12.5, line.Time, 3);
        Assert.Equal("Hello world", line.Text);
    }

    [Fact]
    public void Parse_ThreeDigitMilliseconds_ParsedAsThousandths()
    {
        var line = Assert.Single(LrcParser.Parse("[01:02.345]hi"));
        Assert.Equal(62.345, line.Time, 3);
    }

    [Fact]
    public void Parse_ColonFractionSeparator_Supported()
    {
        var line = Assert.Single(LrcParser.Parse("[00:12:34]hi"));
        Assert.Equal(12.34, line.Time, 3);
    }

    [Fact]
    public void Parse_MultipleLines_SortedByTime()
    {
        var content = "[00:05.00]second\n[00:01.00]first\n[00:10.00]third";
        var lines = LrcParser.Parse(content);

        Assert.Equal(new[] { "first", "second", "third" }, lines.Select(l => l.Text));
        Assert.Equal(new[] { 1.0, 5.0, 10.0 }, lines.Select(l => l.Time));
    }

    [Fact]
    public void Parse_MetadataAndBlankLines_Ignored()
    {
        var content = "[ti:Song Title]\n[ar:Artist]\n[00:01.00]real line\n[00:02.00]";
        var lines = LrcParser.Parse(content);

        var line = Assert.Single(lines);
        Assert.Equal("real line", line.Text);
    }

    [Fact]
    public void Parse_EnhancedLrcWordTimestamps_Stripped()
    {
        var line = Assert.Single(LrcParser.Parse("[00:01.00]<00:01.00>He<00:01.50>llo"));
        Assert.Equal("Hello", line.Text);
    }

    [Fact]
    public void Parse_MultipleTimestampsOnOneLine_ExpandsToMultipleEntries()
    {
        // A single LRC line may carry several timestamps sharing one lyric body:
        // [00:01.00][00:05.00]chorus  ->  two entries at t=1 and t=5.
        var lines = LrcParser.Parse("[00:01.00][00:05.00]chorus");

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("chorus", l.Text));
        Assert.Equal(new[] { 1.0, 5.0 }, lines.Select(l => l.Time));
    }

    [Fact]
    public void DecodeBytes_Utf8Bom_Decoded()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("héllo 你好")).ToArray();
        Assert.Equal("héllo 你好", LrcParser.DecodeBytes(bytes));
    }

    [Fact]
    public void DecodeBytes_Utf16LeBom_Decoded()
    {
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("你好世界")).ToArray();
        Assert.Equal("你好世界", LrcParser.DecodeBytes(bytes));
    }

    [Fact]
    public void DecodeBytes_PlainUtf8_Decoded()
    {
        Assert.Equal("plain utf8", LrcParser.DecodeBytes(Encoding.UTF8.GetBytes("plain utf8")));
    }

    [Fact]
    public void DecodeBytes_Gb18030_FallbackDecodes()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb = Encoding.GetEncoding("GB18030");
        var bytes = gb.GetBytes("你好世界，这是一段中文歌词");

        Assert.Equal("你好世界，这是一段中文歌词", LrcParser.DecodeBytes(bytes));
    }

    [Fact]
    public void ReadFileWithEncodingFallback_OversizedFile_IsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyricdrop-test-{System.Guid.NewGuid():N}.lrc");
        try
        {
            using (var file = File.Create(path)) file.SetLength(5L * 1024 * 1024 + 1);
            var error = Assert.Throws<InvalidDataException>(() => LrcParser.ReadFileWithEncodingFallback(path));
            Assert.Contains("5 MiB", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
