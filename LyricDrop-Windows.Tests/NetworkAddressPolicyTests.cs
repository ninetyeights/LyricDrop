using System.Net;
using LyricDrop.ViewModels;
using Xunit;

namespace LyricDrop.Windows.Tests;

public class NetworkAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    public void IsPublicIpAddress_LocalOrPrivateAddress_IsRejected(string value)
    {
        Assert.False(LyricPlayer.IsPublicIpAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublicIpAddress_PublicAddress_IsAllowed(string value)
    {
        Assert.True(LyricPlayer.IsPublicIpAddress(IPAddress.Parse(value)));
    }
}
