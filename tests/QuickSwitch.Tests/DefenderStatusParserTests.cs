using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class DefenderStatusParserTests
{
    [Theory]
    [InlineData("True|False", SwitchState.On)]
    [InlineData("False|False", SwitchState.Off)]
    [InlineData("True|True", SwitchState.Blocked)]
    [InlineData("False|True", SwitchState.Blocked)]
    public void Parse_MapsState(string output, SwitchState expected)
    {
        Assert.Equal(expected, DefenderStatusParser.Parse(output).State);
    }

    [Theory]
    [InlineData("True|True")]
    [InlineData("False|True")]
    public void Parse_TamperProtected_ExplainsRefusal(string output)
    {
        var result = DefenderStatusParser.Parse(output);

        Assert.Contains("篡改防护", result.Detail!);
    }

    [Fact]
    public void Parse_Off_SaysNotIntercepting()
    {
        var result = DefenderStatusParser.Parse("False|False");

        Assert.Contains("不再实时拦截", result.Detail!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("True")]
    [InlineData("whatever")]
    [InlineData("True|notabool")]
    public void Parse_Garbage_IsUnknown(string output)
    {
        Assert.Equal(SwitchState.Unknown, DefenderStatusParser.Parse(output).State);
    }
}

