using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests.Fakes;

internal sealed class FakeSwitch : ISwitch
{
    public SwitchDescriptor Descriptor { get; init; } = new("fake", SwitchGroup.Security, "假开关", "原始副标题");

    public SwitchState NextReadState { get; set; } = SwitchState.Off;

    public string? NextReadDetail { get; set; }

    public SwitchApplyResult ApplyResult { get; set; } = SwitchApplyResult.Ok();

    public SwitchState? LastTarget { get; private set; }

    public int ReadCount { get; private set; }

    /// 设了就只卡住接下来那一次读：真值在进闸门前就抓在手里，放行后返回的是那一刻的旧值。
    public TaskCompletionSource? NextReadGate { get; set; }

    public Exception? ReadException { get; set; }

    public Exception? ApplyException { get; set; }

    public async Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        ReadCount++;
        var state = NextReadState;
        var detail = NextReadDetail;

        var gate = NextReadGate;
        if (gate is not null)
        {
            NextReadGate = null;
            await gate.Task.ConfigureAwait(false);
        }

        if (ReadException is not null) throw ReadException;

        return new SwitchReadResult(state, detail);
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        LastTarget = target;

        if (ApplyException is not null) throw ApplyException;

        if (ApplyResult.Success) NextReadState = target;

        return Task.FromResult(ApplyResult);
    }
}
