namespace QuickSwitch.Core.Switches;

/// PendingOn 只在 PendingRestart 时有意义：这次「将于重启后生效」的改动到底是开还是关。
/// 少了它，界面没法诚实表示待生效的启用（渲染成"关"），用户也无从撤销。
public sealed record SwitchReadResult(SwitchState State, string? Detail = null, bool? PendingOn = null);

public sealed record SwitchApplyResult(bool Success, string? Error = null)
{
    public static SwitchApplyResult Ok() => new(true);

    public static SwitchApplyResult Fail(string error) => new(false, error);
}

public interface ISwitch
{
    SwitchDescriptor Descriptor { get; }

    Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken);

    Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken);
}
