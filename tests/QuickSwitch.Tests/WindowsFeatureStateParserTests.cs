using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class WindowsFeatureStateParserTests
{
    [Theory]
    [InlineData("Enabled", SwitchState.On)]
    [InlineData("Disabled", SwitchState.Off)]
    [InlineData("EnablePending", SwitchState.PendingRestart)]
    [InlineData("DisablePending", SwitchState.PendingRestart)]
    public void Parse_MapsState(string output, SwitchState expected)
    {
        Assert.Equal(expected, WindowsFeatureStateParser.Parse("Hyper-V", output).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    [InlineData("enabled")]
    public void Parse_Unrecognized_IsUnknown(string output)
    {
        Assert.Equal(SwitchState.Unknown, WindowsFeatureStateParser.Parse("Hyper-V", output).State);
    }

    [Fact]
    public void Parse_PendingDetail_SaysRestart()
    {
        Assert.Contains("重启", WindowsFeatureStateParser.Parse("WSL", "EnablePending").Detail!);
    }
}

