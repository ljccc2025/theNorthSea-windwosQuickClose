namespace QuickSwitch.Core.Switches;

/// 提权归属守卫命中时的占位实现：卡片照常占位，但读写都不落到用户配置单元上。
public sealed class BlockedSwitch : ISwitch
{
    private readonly ISwitch _inner;
    private readonly string _reason;

    public BlockedSwitch(ISwitch inner, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("封锁原因不能为空。", nameof(reason));

        _inner = inner;
        _reason = reason;
    }

    public SwitchDescriptor Descriptor => _inner.Descriptor;

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SwitchReadResult(SwitchState.Blocked, _reason));

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken) =>
        Task.FromResult(SwitchApplyResult.Fail(_reason));
}
