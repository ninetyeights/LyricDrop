using System;
using LyricDrop.ViewModels;
using Xunit;

namespace LyricDrop.Windows.Tests;

public class SecurityLoggingTests
{
    [Fact]
    public void FormatExceptionForLog_DoesNotIncludeMessageOrSourcePath()
    {
        const string secret = "https://user:password@example.test/private";
        const string sourcePath = @"C:\Users\Private\source.cs";
        Exception error;
        try
        {
            throw new InvalidOperationException($"{secret} {sourcePath}");
        }
        catch (Exception ex)
        {
            error = ex;
        }

        var log = App.FormatExceptionForLog(error);

        Assert.Contains(typeof(InvalidOperationException).FullName!, log);
        Assert.DoesNotContain(secret, log);
        Assert.DoesNotContain(sourcePath, log);
        Assert.DoesNotContain("password", log, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://user:password@example.com/audio.mp3", true)]
    [InlineData("https://user@example.com/audio.mp3", true)]
    [InlineData("https://example.com/audio.mp3", false)]
    public void HasEmbeddedCredentials_DetectsUserInfo(string value, bool expected)
    {
        Assert.Equal(expected, LyricPlayer.HasEmbeddedCredentials(new Uri(value)));
    }
}
