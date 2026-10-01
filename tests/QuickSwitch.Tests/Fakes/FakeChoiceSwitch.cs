using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests.Fakes;

internal sealed class FakeChoiceSwitch : IChoiceSwitch
{
    public SwitchDescriptor Descriptor { get; set; } = new("fake-choice", "电源", "假三选一", "测试用卡片");

    public IReadOnlyList<string> Options { get; set; } = ["高性能", "平衡", "节能"];

    public SwitchState NextReadState { get; set; } = SwitchState.Unknown;

    public string? NextReadDetail { get; set; }

    public string? NextSelectedOption { get; set; }

    public SwitchApplyResult SelectResult { get; set; } = SwitchApplyResult.Ok();

    /// 三选一开关的布尔写入通道；真实现里恒失败，测试按需覆盖。
    public SwitchApplyResult ApplyResult { get; set; } = SwitchApplyResult.Ok();

    public SwitchState? LastApplyTarget { get; private set; }

    public int ApplyCount { get; private set; }

    /// 切换成功后把状态与选中项改成"切好了"的样子，模拟真开关的权威回读。
    public bool ApplyOnSelect { get; set; }

    public string? LastSelectedOption { get; private set; }

    public int ReadCount { get; private set; }

    public int ReadSelectedCount { get; private set; }

    public int SelectCount { get; private set; }

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        ReadCount++;
        return Task.FromResult(new SwitchReadResult(NextReadState, NextReadDetail));
    }

    public Task<string?> ReadSelectedAsync(CancellationToken cancellationToken)
    {
        ReadSelectedCount++;
        return Task.FromResult(NextSelectedOption);
    }

    public Task<SwitchApplyResult> SelectAsync(string option, CancellationToken cancellationToken)
    {
        SelectCount++;
        LastSelectedOption = option;

        if (ApplyOnSelect && SelectResult.Success)
        {
            NextReadState = SwitchState.On;
            NextReadDetail = $"当前电源计划：{option}";
            NextSelectedOption = option;
        }

        return Task.FromResult(SelectResult);
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        ApplyCount++;
        LastApplyTarget = target;
        return Task.FromResult(ApplyResult);
    }
}
