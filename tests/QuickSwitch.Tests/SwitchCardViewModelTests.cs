using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class SwitchCardViewModelTests
{
    [Fact]
    public async Task RefreshAsync_AdoptsStateAndDetail()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Mixed, NextReadDetail = "已开启：域 / 公用" };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Mixed, card.State);
        Assert.Equal("已开启：域 / 公用", card.Subtitle);
        Assert.False(card.IsOn);
    }

    [Fact]
    public async Task RefreshAsync_WithoutDetail_FallsBackToDescriptorSubtitle()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.On, NextReadDetail = null };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.True(card.IsOn);
        Assert.Equal("原始副标题", card.Subtitle);
    }

    [Fact]
    public async Task ToggleAsync_WhenOff_TargetsOnAndAdoptsReadBack()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Off };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, fake.LastTarget);
        Assert.Equal(SwitchState.On, card.State);
        Assert.True(card.IsOn);
        Assert.False(card.IsBusy);
    }

    [Fact]
    public async Task ToggleAsync_WhenMixed_TargetsOn()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Mixed };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, fake.LastTarget);
    }

    [Fact]
    public async Task ToggleAsync_WhenApplyFails_KeepsStateAndShowsRawError()
    {
        var fake = new FakeSwitch
        {
            NextReadState = SwitchState.Off,
            ApplyResult = SwitchApplyResult.Fail("The requested operation requires elevation (Run as administrator)."),
        };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.Off, card.State);
        Assert.False(card.IsOn);
        Assert.Equal("操作失败：The requested operation requires elevation (Run as administrator).", card.Subtitle);
    }

    [Fact]
    public async Task ToggleAsync_WhenStateUnknown_DoesNothing()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Unknown };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        Assert.False(card.CanToggle);

        await card.ToggleAsync();

        Assert.Null(fake.LastTarget);
    }

    [Fact]
    public async Task RefreshAsync_WhenFailingRead_ShowsErrorAsSubtitle()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Unknown, NextReadDetail = "无法解析防火墙状态" };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.Equal("无法解析防火墙状态", card.Subtitle);
    }

    [Fact]
    public async Task RefreshAsync_WhenBlockedByOwnerGuard_ShowsReasonAndDisablesToggle()
    {
        var inner = new FakeSwitch { NextReadState = SwitchState.On };
        var card = new SwitchCardViewModel(new BlockedSwitch(inner, SessionOwnerGuard.ForeignAdminDetail));

        await card.RefreshAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Blocked, card.State);
        Assert.Equal("当前以其他管理员账户运行，用户级设置不可用", card.Subtitle);
        Assert.False(card.CanToggle);
        Assert.False(card.IsOn);

        await card.ToggleAsync();

        Assert.Null(inner.LastTarget);
    }
}
