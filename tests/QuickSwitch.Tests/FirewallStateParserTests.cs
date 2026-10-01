using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class FirewallStateParserTests
{
    [Fact]
    public void Parse_AllProfilesDisabled_ReturnsOff()
    {
        var result = FirewallStateParser.Parse("Domain=False;Private=False;Public=False");

        Assert.Equal(SwitchState.Off, result.State);
        Assert.Equal("域 / 专用 / 公用 三档全部关闭", result.Detail);
    }

    [Fact]
    public void Parse_AllProfilesEnabled_ReturnsOn()
    {
        var result = FirewallStateParser.Parse("Domain=True;Private=True;Public=True");

        Assert.Equal(SwitchState.On, result.State);
        Assert.Equal("域 / 专用 / 公用 三档全部开启", result.Detail);
    }

    [Fact]
    public void Parse_PartiallyEnabled_ReturnsMixedListingEnabledProfiles()
    {
        var result = FirewallStateParser.Parse("Domain=True;Private=False;Public=True");

        Assert.Equal(SwitchState.Mixed, result.State);
        Assert.Equal("已开启：域 / 公用", result.Detail);
    }

    [Fact]
    public void Parse_NewlineSeparatedAndLowercase_IsAccepted()
    {
        var result = FirewallStateParser.Parse("domain=true\r\nprivate=true\npublic=true");

        Assert.Equal(SwitchState.On, result.State);
    }

    [Fact]
    public void Parse_Empty_ReturnsUnknown()
    {
        var result = FirewallStateParser.Parse(string.Empty);

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public void Parse_MissingProfile_ReturnsUnknown()
    {
        var result = FirewallStateParser.Parse("Domain=True;Private=True");

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.Contains("Public", result.Detail);
    }

    [Fact]
    public void Parse_Garbage_ReturnsUnknownWithRawOutput()
    {
        var result = FirewallStateParser.Parse("Get-NetFirewallProfile : 无法识别");

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.Contains("无法识别", result.Detail);
    }
}
