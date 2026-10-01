using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class PowerPlanSwitch : IChoiceSwitch
{
    private readonly IPowerCfg _powerCfg;

    public PowerPlanSwitch(IPowerCfg powerCfg) => _powerCfg = powerCfg;

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "power-plan",
        Group: SwitchGroup.Power,
        Title: "电源计划",
        Subtitle: "高性能 / 平衡 / 节能 三选一");

    public IReadOnlyList<string> Options => PowerSchemeParser.CanonicalOptions;

    public async Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken) =>
        (await ReadPlanAsync(cancellationToken).ConfigureAwait(false)).Read;

    public async Task<string?> ReadSelectedAsync(CancellationToken cancellationToken) =>
        (await ReadPlanAsync(cancellationToken).ConfigureAwait(false)).Option;

    public async Task<SwitchApplyResult> SelectAsync(string option, CancellationToken cancellationToken)
    {
        var guid = await ResolveGuidAsync(option, cancellationToken).ConfigureAwait(false);
        if (guid is null)
            return SwitchApplyResult.Fail($"未知的电源计划选项：{option}");

        var result = await _powerCfg.RunAsync(["setactive", guid], cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? SwitchApplyResult.Ok()
            : SwitchApplyResult.Fail(ErrorText.FirstLine(result.StandardError));
    }

    /// 电源计划不是开关，布尔写入在这儿没有意义——如实报错，不编一个"开 = 高性能"的映射。
    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken) =>
        Task.FromResult(SwitchApplyResult.Fail("电源计划是三选一，请用方案选择器切换。"));

    private async Task<(SwitchReadResult Read, string? Option)> ReadPlanAsync(CancellationToken cancellationToken)
    {
        var active = await _powerCfg.RunAsync(["getactivescheme"], cancellationToken).ConfigureAwait(false);
        if (!active.Succeeded)
            return (new SwitchReadResult(SwitchState.Unknown, ErrorText.FirstLine(active.StandardError)), null);

        var guid = PowerSchemeParser.ParseActiveGuid(active.StandardOutput);
        if (guid is null)
            return (new SwitchReadResult(SwitchState.Unknown, "无法解析当前电源计划 GUID"), null);

        var name = PowerSchemeParser.ParseActiveName(active.StandardOutput) ?? guid;

        var option = await MatchOptionAsync(guid, cancellationToken).ConfigureAwait(false);
        return option is null
            ? (new SwitchReadResult(SwitchState.Unknown, $"未识别的电源计划：{name}（{guid}）"), null)
            : (new SwitchReadResult(SwitchState.On, $"当前电源计划：{option}"), option);
    }

    private async Task<string?> MatchOptionAsync(string activeGuid, CancellationToken cancellationToken)
    {
        var listed = await ListSchemesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var option in Options)
        {
            var resolved = listed
                .FirstOrDefault(scheme => PowerSchemeParser.MatchesOption(option, scheme.Name))?.Guid
                ?? PowerSchemeParser.CanonicalGuid(option);

            if (string.Equals(resolved, activeGuid, StringComparison.OrdinalIgnoreCase))
                return option;
        }

        return null;
    }

    private async Task<string?> ResolveGuidAsync(string option, CancellationToken cancellationToken)
    {
        if (!PowerSchemeParser.CanonicalOptions.Contains(option)) return null;

        // 优先用 /list 里的真实名字匹配（用户可能换了显示名），匹配不到再用内置 GUID 兜底。
        var listed = await ListSchemesAsync(cancellationToken).ConfigureAwait(false);
        return listed.FirstOrDefault(scheme => PowerSchemeParser.MatchesOption(option, scheme.Name))?.Guid
            ?? PowerSchemeParser.CanonicalGuid(option);
    }

    private async Task<IReadOnlyList<PowerScheme>> ListSchemesAsync(CancellationToken cancellationToken)
    {
        var listed = await _powerCfg.RunAsync(["list"], cancellationToken).ConfigureAwait(false);
        return listed.Succeeded ? PowerSchemeParser.ParseList(listed.StandardOutput) : [];
    }
}
