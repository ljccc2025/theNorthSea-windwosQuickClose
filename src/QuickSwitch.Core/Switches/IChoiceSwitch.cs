namespace QuickSwitch.Core.Switches;

/// 三选一（电源计划）这类开关的契约。硬塞进布尔接口就得编"开 = 高性能 / 关 = 平衡"的谎。
public interface IChoiceSwitch : ISwitch
{
    IReadOnlyList<string> Options { get; }

    /// 当前选中的选项；认不出来返回 null（比如用户自建的方案）。
    Task<string?> ReadSelectedAsync(CancellationToken cancellationToken);

    Task<SwitchApplyResult> SelectAsync(string option, CancellationToken cancellationToken);
}
