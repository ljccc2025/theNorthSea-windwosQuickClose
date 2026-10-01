using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

/// 异常绝不能逃出卡片层：AsyncRelayCommand 会把它抛到 UI 线程，直接击毙托盘进程。
public class CardErrorContainmentTests
{
    private sealed class ThrowingCard : ICardViewModel
    {
        public string Group => "测试";

        public Task RefreshAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("刷新炸了");
    }

    [Fact]
    public async Task RefreshAllAsync_WhenCardThrows_StillRefreshesTheRestAndClearsBusyFlag()
    {
        var healthy = new FakeSwitch { NextReadState = SwitchState.On };
        var viewModel = new MainViewModel(
            [new ThrowingCard(), new SwitchCardViewModel(healthy)],
            isElevated: true);

        await viewModel.RefreshAllCommand.ExecuteAsync(null);

        Assert.Equal(1, healthy.ReadCount);
        Assert.True(Assert.IsType<SwitchCardViewModel>(viewModel.Cards[1]).IsOn);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task RefreshAllAsync_WhenReadThrows_ShowsErrorInSubtitle()
    {
        var broken = new FakeSwitch { ReadException = new InvalidOperationException("管道已关闭") };
        var viewModel = new MainViewModel([new SwitchCardViewModel(broken)], isElevated: true);

        await viewModel.RefreshAllCommand.ExecuteAsync(null);

        var card = Assert.IsType<SwitchCardViewModel>(viewModel.Cards[0]);
        Assert.Equal(SwitchState.Unknown, card.State);
        Assert.Equal("读取失败：管道已关闭", card.Subtitle);
    }

    [Fact]
    public async Task SelectAsync_WhenSelectThrows_ReportsErrorInsteadOfEscaping()
    {
        var fake = new FakeChoiceSwitch
        {
            NextReadState = SwitchState.On,
            NextSelectedOption = "平衡",
            SelectException = new InvalidOperationException("powercfg 炸了"),
        };
        var card = new ChoiceCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.SelectAsync("高性能");

        Assert.Equal("操作失败：powercfg 炸了", card.Subtitle);
        Assert.False(card.IsBusy);
    }
}
