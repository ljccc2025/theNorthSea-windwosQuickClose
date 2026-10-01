using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Constructor_ExposesCardsInOrder()
    {
        var viewModel = new MainViewModel(
            [new SwitchCardViewModel(new FakeSwitch()), new SwitchCardViewModel(new FakeSwitch())],
            isElevated: true);

        Assert.Equal(2, viewModel.Cards.Count);
        Assert.True(viewModel.IsElevated);
    }

    [Fact]
    public void ElevationBadge_ReflectsElevation()
    {
        var elevated = new MainViewModel([], isElevated: true);
        var plain = new MainViewModel([], isElevated: false);

        Assert.Equal("管理员模式", elevated.ElevationBadge);
        Assert.Equal("未提权：部分开关不可用", plain.ElevationBadge);
    }

    [Fact]
    public async Task RefreshAllAsync_RefreshesEveryCard()
    {
        var first = new FakeSwitch { NextReadState = SwitchState.On };
        var second = new FakeSwitch { NextReadState = SwitchState.Off };
        var viewModel = new MainViewModel(
            [new SwitchCardViewModel(first), new SwitchCardViewModel(second)],
            isElevated: true);

        await viewModel.RefreshAllCommand.ExecuteAsync(null);

        Assert.Equal(1, first.ReadCount);
        Assert.Equal(1, second.ReadCount);
        Assert.True(Assert.IsType<SwitchCardViewModel>(viewModel.Cards[0]).IsOn);
        Assert.False(Assert.IsType<SwitchCardViewModel>(viewModel.Cards[1]).IsOn);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task RefreshAllAsync_StartsEveryCardBeforeAnyOfThemFinishes()
    {
        var slowestGate = new TaskCompletionSource();
        var quickGate = new TaskCompletionSource();
        var slowest = new FakeSwitch { NextReadGate = slowestGate };
        var quick = new FakeSwitch { NextReadGate = quickGate };
        var viewModel = new MainViewModel(
            [new SwitchCardViewModel(slowest), new SwitchCardViewModel(quick)],
            isElevated: true);

        var refresh = viewModel.RefreshAllCommand.ExecuteAsync(null);

        await Task.Delay(100);

        Assert.Equal(1, slowest.ReadCount);
        Assert.Equal(1, quick.ReadCount);
        Assert.True(viewModel.IsRefreshing);

        quickGate.SetResult();
        slowestGate.SetResult();
        await refresh;

        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task RefreshAllAsync_AlsoRefreshesChoiceCard()
    {
        var choice = new FakeChoiceSwitch { NextReadState = SwitchState.On, NextSelectedOption = "平衡" };
        var viewModel = new MainViewModel([new ChoiceCardViewModel(choice)], isElevated: true);

        await viewModel.RefreshAllCommand.ExecuteAsync(null);

        Assert.Equal(1, choice.ReadCount);
        Assert.Equal(1, choice.ReadSelectedCount);
        Assert.Equal("平衡", Assert.IsType<ChoiceCardViewModel>(viewModel.Cards[0]).SelectedOption);
    }
}
