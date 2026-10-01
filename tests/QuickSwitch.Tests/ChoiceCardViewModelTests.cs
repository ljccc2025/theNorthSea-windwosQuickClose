using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class ChoiceCardViewModelTests
{
    private readonly FakeChoiceSwitch _switch = new();

    private ChoiceCardViewModel CreateViewModel() => new(_switch);

    [Fact]
    public void Constructor_ExposesDescriptorAndOptions()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("假三选一", viewModel.Title);
        Assert.Equal("电源", viewModel.Group);
        Assert.Equal("测试用卡片", viewModel.Subtitle);
        Assert.Equal(["高性能", "平衡", "节能"], viewModel.Options);
        Assert.Equal(SwitchState.Unknown, viewModel.State);
    }

    [Fact]
    public async Task RefreshAsync_AppliesStateSubtitleAndSelection()
    {
        _switch.NextReadState = SwitchState.On;
        _switch.NextReadDetail = "当前电源计划：平衡";
        _switch.NextSelectedOption = "平衡";
        var viewModel = CreateViewModel();

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, viewModel.State);
        Assert.Equal("当前电源计划：平衡", viewModel.Subtitle);
        Assert.Equal("平衡", viewModel.SelectedOption);
        // 刷新时同步回选中项不能反过来触发切换。
        Assert.Equal(0, _switch.SelectCount);
    }

    [Fact]
    public async Task RefreshAsync_NoDetail_FallsBackToDescriptorSubtitle()
    {
        _switch.NextReadState = SwitchState.On;
        var viewModel = CreateViewModel();

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal("测试用卡片", viewModel.Subtitle);
    }

    [Fact]
    public async Task RefreshAsync_UnrecognizedScheme_KeepsSelectionUsable()
    {
        _switch.NextReadState = SwitchState.Unknown;
        _switch.NextReadDetail = "未识别的电源计划：我的方案（11111111-2222-3333-4444-555555555555）";
        var viewModel = CreateViewModel();

        await viewModel.RefreshAsync(CancellationToken.None);

        // 认不出的方案不该把用户锁死：还能切回三选一。
        Assert.True(viewModel.CanSelect);
        Assert.Null(viewModel.SelectedOption);
    }

    [Fact]
    public async Task RefreshAsync_BlockedByOwnerGuard_DisablesSelection()
    {
        _switch.NextReadState = SwitchState.Blocked;
        _switch.NextReadDetail = "当前以其他管理员账户运行，用户级设置不可用";
        var viewModel = CreateViewModel();

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.False(viewModel.CanSelect);
        Assert.Equal("当前以其他管理员账户运行，用户级设置不可用", viewModel.Subtitle);
    }

    [Fact]
    public async Task SelectAsync_AppliesOptionAndReloadsAuthoritativeState()
    {
        _switch.NextReadState = SwitchState.On;
        _switch.NextSelectedOption = "平衡";
        _switch.ApplyOnSelect = true;
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.SelectAsync("高性能");

        Assert.Equal("高性能", _switch.LastSelectedOption);
        Assert.Equal("高性能", viewModel.SelectedOption);
        Assert.Equal("当前电源计划：高性能", viewModel.Subtitle);
        Assert.Equal(SwitchState.On, viewModel.State);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SelectAsync_Failure_ShowsErrorAndRevertsSelection()
    {
        _switch.NextReadState = SwitchState.On;
        _switch.NextReadDetail = "当前电源计划：平衡";
        _switch.NextSelectedOption = "平衡";
        _switch.SelectResult = SwitchApplyResult.Fail("无法设置电源方案。");
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.SelectAsync("节能");

        Assert.Equal("操作失败：无法设置电源方案。", viewModel.Subtitle);
        Assert.Equal("平衡", viewModel.SelectedOption);
    }

    [Fact]
    public async Task SelectAsync_FailureWithoutMessage_UsesFallbackText()
    {
        _switch.NextReadState = SwitchState.On;
        _switch.NextSelectedOption = "平衡";
        _switch.SelectResult = new SwitchApplyResult(false);
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.SelectAsync("节能");

        Assert.Equal("操作失败：未知错误", viewModel.Subtitle);
    }

    [Fact]
    public async Task SelectAsync_WhenBlocked_DoesNothing()
    {
        _switch.NextReadState = SwitchState.Blocked;
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.SelectAsync("平衡");

        Assert.Equal(0, _switch.SelectCount);
        Assert.Null(_switch.LastSelectedOption);
    }

    [Fact]
    public async Task SelectAsync_EmptyOption_DoesNothing()
    {
        var viewModel = CreateViewModel();

        await viewModel.SelectAsync(null);

        Assert.Equal(0, _switch.SelectCount);
    }

    [Fact]
    public async Task SettingSelectedOption_TriggersSelection()
    {
        _switch.NextReadState = SwitchState.On;
        _switch.ApplyOnSelect = true;
        var viewModel = CreateViewModel();
        await viewModel.RefreshAsync(CancellationToken.None);

        // 模拟下拉框用户操作。
        viewModel.SelectedOption = "节能";

        Assert.Equal(1, _switch.SelectCount);
        Assert.Equal("节能", _switch.LastSelectedOption);
    }
}
