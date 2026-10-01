using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class PowerPlanSwitchTests
{
    private const string CustomGuid = "11111111-2222-3333-4444-555555555555";

    private const string ActiveBalanced = "电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)";

    private const string ChineseList = """
        现有电源使用方案 (* Active)
        -----------------------------------
        电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡) *
        电源方案 GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (高性能)
        电源方案 GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (节能)
        """;

    private readonly FakePowerCfg _powerCfg = new();
    private readonly PowerPlanSwitch _switch;

    public PowerPlanSwitchTests() => _switch = new PowerPlanSwitch(_powerCfg);

    private void RespondWithPlan(string activeScheme, string list = ChineseList)
    {
        _powerCfg.RespondTo("getactivescheme", new ProcessResult(0, activeScheme, string.Empty));
        _powerCfg.RespondTo("list", new ProcessResult(0, list, string.Empty));
    }

    [Fact]
    public void Descriptor_IsChoiceSwitchInPowerGroup()
    {
        Assert.Equal("power-plan", _switch.Descriptor.Id);
        Assert.Equal(SwitchGroup.Power, _switch.Descriptor.Group);
        Assert.Equal("电源计划", _switch.Descriptor.Title);
        Assert.Equal(["高性能", "平衡", "节能"], _switch.Options);
    }

    [Fact]
    public async Task ReadAsync_KnownActiveScheme_IsOnWithOptionName()
    {
        RespondWithPlan(ActiveBalanced);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Equal("当前电源计划：平衡", read.Detail);
        Assert.Equal("平衡", await _switch.ReadSelectedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_EnglishSchemeNames_StillMatchChineseOptions()
    {
        RespondWithPlan(
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)",
            """
            Existing Power Schemes (* Active)
            -----------------------------------
            Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance) *
            """);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Equal("当前电源计划：高性能", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_ListIsUnavailable_FallsBackToBuiltInGuid()
    {
        _powerCfg.RespondTo("getactivescheme", new ProcessResult(0, ActiveBalanced, string.Empty));
        _powerCfg.RespondTo("list", new ProcessResult(1, string.Empty, "powercfg 报错"));

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Equal("当前电源计划：平衡", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_NonZeroExit_IsUnknownWithFirstErrorLine()
    {
        _powerCfg.RespondTo("getactivescheme", new ProcessResult(1, string.Empty, "找不到电源方案。\r\n细节"));

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, read.State);
        Assert.Equal("找不到电源方案。", read.Detail);
        Assert.Null(await _switch.ReadSelectedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_UnparsableOutput_IsUnknown()
    {
        _powerCfg.RespondTo("getactivescheme", new ProcessResult(0, "没有 GUID 的输出", string.Empty));

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, read.State);
        Assert.Equal("无法解析当前电源计划 GUID", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_CustomScheme_IsUnknownWithSchemeNameAndNoSelection()
    {
        RespondWithPlan(
            $"电源方案 GUID: {CustomGuid}  (我的自定义方案)",
            $"""
            电源方案 GUID: {CustomGuid}  (我的自定义方案) *
            电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)
            """);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, read.State);
        Assert.Equal($"未识别的电源计划：我的自定义方案（{CustomGuid}）", read.Detail);
        Assert.Null(await _switch.ReadSelectedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_RenamedSchemeWithCanonicalGuid_StillResolvesByGuid()
    {
        RespondWithPlan(
            $"电源方案 GUID: {PowerSchemeParser.HighPerformanceGuid}  (厂商标配)",
            $"电源方案 GUID: {PowerSchemeParser.HighPerformanceGuid}  (厂商标配) *");

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Equal("当前电源计划：高性能", read.Detail);
    }

    [Fact]
    public async Task SelectAsync_KnownOption_UsesGuidFromList()
    {
        RespondWithPlan(ActiveBalanced);

        var result = await _switch.SelectAsync("平衡", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(_powerCfg.Calls, call => call.SequenceEqual(["setactive", PowerSchemeParser.BalancedGuid]));
    }

    [Fact]
    public async Task SelectAsync_ListUnavailable_UsesBuiltInGuid()
    {
        _powerCfg.RespondTo("list", new ProcessResult(1, string.Empty, "报错"));

        var result = await _switch.SelectAsync("节能", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(_powerCfg.Calls, call => call.SequenceEqual(["setactive", PowerSchemeParser.PowerSaverGuid]));
    }

    [Fact]
    public async Task SelectAsync_UnknownOption_FailsWithoutCallingPowerCfg()
    {
        var result = await _switch.SelectAsync("自定义方案", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("未知的电源计划选项：自定义方案", result.Error);
        Assert.DoesNotContain(_powerCfg.Calls, call => call.Count > 0 && call[0] == "setactive");
    }

    [Fact]
    public async Task SelectAsync_NonZeroExit_IsFailureWithFirstErrorLine()
    {
        RespondWithPlan(ActiveBalanced);
        _powerCfg.RespondTo("setactive", new ProcessResult(1, string.Empty, "无法设置电源方案。\r\n细节"));

        var result = await _switch.SelectAsync("平衡", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("无法设置电源方案。", result.Error);
    }

    [Theory]
    [InlineData(SwitchState.On)]
    [InlineData(SwitchState.Off)]
    [InlineData(SwitchState.Unknown)]
    public async Task ApplyAsync_AlwaysFailsWithGuidance(SwitchState target)
    {
        var result = await _switch.ApplyAsync(target, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("电源计划是三选一，请用方案选择器切换。", result.Error);
        Assert.Empty(_powerCfg.Calls);
    }
}
