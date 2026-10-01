using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests;

public class PowerSchemeParserTests
{
    private const string ChineseActive = "电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)";

    private const string EnglishActive = "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)";

    private const string ChineseList = """
        现有电源使用方案 (* Active)
        -----------------------------------
        电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡) *
        电源方案 GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (高性能)
        电源方案 GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (节能)
        """;

    private const string EnglishList = """
        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
        Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)
        """;

    [Fact]
    public void ParseActiveGuid_ChineseOutput_ReturnsLowercaseGuid()
    {
        Assert.Equal(PowerSchemeParser.BalancedGuid, PowerSchemeParser.ParseActiveGuid(ChineseActive));
    }

    [Fact]
    public void ParseActiveGuid_EnglishOutput_ReturnsGuid()
    {
        Assert.Equal(PowerSchemeParser.BalancedGuid, PowerSchemeParser.ParseActiveGuid(EnglishActive));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("powercfg 用不了")]
    public void ParseActiveGuid_Unparsable_ReturnsNull(string? stdout)
    {
        Assert.Null(PowerSchemeParser.ParseActiveGuid(stdout));
    }

    [Fact]
    public void ParseActiveName_ChineseOutput_ReturnsLocalizedName()
    {
        Assert.Equal("平衡", PowerSchemeParser.ParseActiveName(ChineseActive));
    }

    [Fact]
    public void ParseActiveName_EnglishOutput_ReturnsEnglishName()
    {
        Assert.Equal("Balanced", PowerSchemeParser.ParseActiveName(EnglishActive));
    }

    [Fact]
    public void ParseActiveName_NoParentheses_ReturnsNull()
    {
        Assert.Null(PowerSchemeParser.ParseActiveName("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e"));
    }

    [Fact]
    public void ParseList_ChineseOutput_ParsesEverySchemeAndMarksActive()
    {
        var schemes = PowerSchemeParser.ParseList(ChineseList);

        Assert.Equal(3, schemes.Count);
        Assert.Equal([PowerSchemeParser.BalancedGuid, PowerSchemeParser.HighPerformanceGuid, PowerSchemeParser.PowerSaverGuid],
            schemes.Select(scheme => scheme.Guid));
        Assert.Equal(["平衡", "高性能", "节能"], schemes.Select(scheme => scheme.Name));
        Assert.Equal([true, false, false], schemes.Select(scheme => scheme.IsActive));
    }

    [Fact]
    public void ParseList_EnglishOutput_ParsesEveryScheme()
    {
        var schemes = PowerSchemeParser.ParseList(EnglishList);

        Assert.Equal(["Balanced", "High performance", "Power saver"], schemes.Select(scheme => scheme.Name));
        Assert.True(schemes[0].IsActive);
    }

    [Fact]
    public void ParseList_NoActiveMarker_AllInactive()
    {
        var schemes = PowerSchemeParser.ParseList(ChineseList.Replace(") *", ")"));

        Assert.All(schemes, scheme => Assert.False(scheme.IsActive));
    }

    [Fact]
    public void ParseList_LineWithoutParentheses_FallsBackToGuidAsName()
    {
        var schemes = PowerSchemeParser.ParseList("电源方案 GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c *");

        var scheme = Assert.Single(schemes);
        Assert.Equal(PowerSchemeParser.HighPerformanceGuid, scheme.Name);
        Assert.True(scheme.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseList_EmptyInput_ReturnsEmpty(string? stdout)
    {
        Assert.Empty(PowerSchemeParser.ParseList(stdout));
    }

    [Fact]
    public void CanonicalOptions_AreThreeChineseNames()
    {
        Assert.Equal(["高性能", "平衡", "节能"], PowerSchemeParser.CanonicalOptions);
    }

    [Theory]
    [InlineData("高性能", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")]
    [InlineData("平衡", "381b4222-f694-41f0-9685-ff5bb260df2e")]
    [InlineData("节能", "a1841308-3541-4fab-bc81-f71556f20b4a")]
    [InlineData(" 平衡 ", "381b4222-f694-41f0-9685-ff5bb260df2e")]
    public void CanonicalGuid_KnownOption_ReturnsBuiltInGuid(string option, string expected)
    {
        Assert.Equal(expected, PowerSchemeParser.CanonicalGuid(option));
    }

    [Theory]
    [InlineData("自定义方案")]
    [InlineData("")]
    [InlineData(null)]
    public void CanonicalGuid_UnknownOption_ReturnsNull(string? option)
    {
        Assert.Null(PowerSchemeParser.CanonicalGuid(option));
    }

    [Theory]
    [InlineData("高性能", "高性能")]
    [InlineData("高性能", "High performance")]
    [InlineData("高性能", "HIGH PERFORMANCE")]
    [InlineData("平衡", "平衡")]
    [InlineData("平衡", "balanced")]
    [InlineData("节能", "Power saver")]
    [InlineData("节能", "节能")]
    public void MatchesOption_Synonyms_Match(string option, string schemeName)
    {
        Assert.True(PowerSchemeParser.MatchesOption(option, schemeName));
    }

    [Theory]
    [InlineData("高性能", "平衡")]
    [InlineData("平衡", "Balanced (推荐)")]
    [InlineData("自定义", "平衡")]
    [InlineData("平衡", "")]
    [InlineData("平衡", null)]
    public void MatchesOption_UnrelatedNames_DoNotMatch(string option, string? schemeName)
    {
        Assert.False(PowerSchemeParser.MatchesOption(option, schemeName));
    }
}
