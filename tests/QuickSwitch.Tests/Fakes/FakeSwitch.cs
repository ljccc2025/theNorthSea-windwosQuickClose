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

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        ReadCount++;
        return Task.FromResult(new SwitchReadResult(NextReadState, NextReadDetail));
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        LastTarget = target;
        if (ApplyResult.Success) NextReadState = target;
        return Task.FromResult(ApplyResult);
    }
}
