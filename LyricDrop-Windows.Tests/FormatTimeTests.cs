using LyricDrop.ViewModels;
using Xunit;

namespace LyricDrop.Windows.Tests;

public class FormatTimeTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(125, "2:05")]
    [InlineData(3599, "59:59")]
    public void FormatTime_FormatsMinutesAndSeconds(double seconds, string expected)
    {
        Assert.Equal(expected, LyricPlayer.FormatTime(seconds));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FormatTime_InvalidValues_ClampToZero(double seconds)
    {
        Assert.Equal("0:00", LyricPlayer.FormatTime(seconds));
    }
}
