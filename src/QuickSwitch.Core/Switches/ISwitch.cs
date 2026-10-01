namespace QuickSwitch.Core.Switches;

public sealed record SwitchReadResult(SwitchState State, string? Detail = null);

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
