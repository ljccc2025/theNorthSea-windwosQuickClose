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

    [Theory]
    [InlineData("EnablePending", true, "启用")]
    [InlineData("DisablePending", false, "禁用")]
    public void Parse_PendingState_CarriesDirection(string output, bool expectedPendingOn, string expectedWord)
    {
        var result = WindowsFeatureStateParser.Parse("Hyper-V", output);

        Assert.Equal(SwitchState.PendingRestart, result.State);
        Assert.Equal(expectedPendingOn, result.PendingOn);
        Assert.Contains(expectedWord, result.Detail!);
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Disabled")]
    public void Parse_SettledState_HasNoPendingDirection(string output)
    {
        Assert.Null(WindowsFeatureStateParser.Parse("Hyper-V", output).PendingOn);
    }
}

