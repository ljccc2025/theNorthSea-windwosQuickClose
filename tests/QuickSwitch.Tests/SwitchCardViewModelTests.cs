using System.ComponentModel;
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

    [Fact]
    public async Task RefreshAsync_WhileToggleIsInFlight_DoesNotOverwriteAuthoritativeState()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Off };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        var staleRead = new TaskCompletionSource();
        fake.NextReadGate = staleRead;

        var refreshing = card.RefreshAsync(CancellationToken.None);
        var toggling = card.ToggleAsync();

        staleRead.SetResult();

        await refreshing;
        await toggling;

        Assert.Equal(SwitchState.On, fake.LastTarget);
        Assert.Equal(SwitchState.On, card.State);
        Assert.True(card.IsOn);
        Assert.False(card.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_WhenReadThrows_ReportsErrorInsteadOfEscaping()
    {
        var fake = new FakeSwitch
        {
            NextReadState = SwitchState.On,
            ReadException = new InvalidOperationException("管道已关闭"),
        };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, card.State);
        Assert.False(card.IsOn);
        Assert.False(card.CanToggle);
        Assert.Equal("读取失败：管道已关闭", card.Subtitle);
    }

    [Fact]
    public async Task ToggleAsync_WhenApplyThrows_ReportsErrorAndBouncesBack()
    {
        var fake = new FakeSwitch
        {
            NextReadState = SwitchState.On,
            ApplyException = new Win32Exception("无法启动进程 'powershell'"),
        };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, card.State);
        Assert.True(card.IsOn);
        Assert.Equal("操作失败：无法启动进程 'powershell'", card.Subtitle);
        Assert.False(card.IsBusy);
    }

    [Fact]
    public async Task RefreshAsync_WhenPendingRestart_LightsBadge()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.PendingRestart, NextReadDetail = "重启后生效" };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.Equal(SwitchState.PendingRestart, card.State);
        Assert.True(card.ShowRestartBadge);
    }

    [Fact]
    public async Task RefreshAsync_WhenPlainState_ClearsBadge()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.PendingRestart };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        fake.NextReadState = SwitchState.On;
        await card.RefreshAsync(CancellationToken.None);

        Assert.False(card.ShowRestartBadge);
    }

    [Fact]
    public async Task ToggleAsync_SuccessOnRequiresRestartSwitch_LightsBadge()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor("slow", SwitchGroup.System, "慢开关", "要重启", RequiresRestart: true),
            NextReadState = SwitchState.Off,
        };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, card.State);
        Assert.True(card.ShowRestartBadge);
    }

    [Fact]
    public async Task ToggleAsync_WhenApplyFails_DoesNotLightBadge()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor("slow", SwitchGroup.System, "慢开关", "要重启", RequiresRestart: true),
            NextReadState = SwitchState.On,
            ApplyResult = SwitchApplyResult.Fail("拒绝访问。"),
        };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.False(card.ShowRestartBadge);
    }

    [Fact]
    public async Task ToggleAsync_DestructiveOff_WhenUserCancels_DoesNothing()
    {
        var fake = DestructiveSwitch(SwitchState.On);
        var prompt = new FakeConfirmationPrompt { Answer = false };
        var card = new SwitchCardViewModel(fake, prompt);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Null(fake.LastTarget);
        Assert.Equal(SwitchState.On, card.State);
        Assert.True(card.IsOn);
        Assert.False(card.IsBusy);
        Assert.Equal(["关掉就回不来了，确定吗？"], prompt.Questions);
    }

    [Fact]
    public async Task ToggleAsync_DestructiveOff_WhenUserConfirms_Applies()
    {
        var fake = DestructiveSwitch(SwitchState.On);
        var prompt = new FakeConfirmationPrompt { Answer = true };
        var card = new SwitchCardViewModel(fake, prompt);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.Off, fake.LastTarget);
        Assert.Equal(SwitchState.Off, card.State);
        Assert.Single(prompt.Questions);
    }

    [Fact]
    public async Task ToggleAsync_DestructiveButTurningOn_DoesNotPrompt()
    {
        var fake = DestructiveSwitch(SwitchState.Off);
        var prompt = new FakeConfirmationPrompt { Answer = false };
        var card = new SwitchCardViewModel(fake, prompt);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, fake.LastTarget);
        Assert.Empty(prompt.Questions);
    }

    [Fact]
    public async Task ToggleAsync_ConfirmedReadBackThatIsStillOn_ReportsApplyError()
    {
        var fake = DestructiveSwitch(SwitchState.On);
        fake.ApplyResult = SwitchApplyResult.Fail("UAC 被策略锁定。");
        var card = new SwitchCardViewModel(fake, new FakeConfirmationPrompt { Answer = true });
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, card.State);
        Assert.Equal("操作失败：UAC 被策略锁定。", card.Subtitle);
    }

    [Fact]
    public async Task ToggleAsync_DestructiveOff_WhenUserCancels_PushesAuthoritativeStateBack()
    {
        var fake = DestructiveSwitch(SwitchState.On);
        var card = new SwitchCardViewModel(fake, new FakeConfirmationPrompt { Answer = false });
        await card.RefreshAsync(CancellationToken.None);

        var notified = new List<string>();
        card.PropertyChanged += (_, args) => notified.Add(args.PropertyName ?? string.Empty);

        await card.ToggleAsync();

        Assert.Contains(nameof(SwitchCardViewModel.IsOn), notified);
    }

    [Fact]
    public async Task RefreshAsync_WhenPendingRestartEnable_ShowsOnWithBadge()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor("slow", SwitchGroup.System, "慢开关", "要重启", RequiresRestart: true),
            NextReadState = SwitchState.PendingRestart,
            NextReadDetail = "Hyper-V 将于重启后启用",
            NextReadPendingOn = true,
        };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.True(card.IsOn);
        Assert.True(card.ShowRestartBadge);
        Assert.True(card.CanToggle);
    }

    [Fact]
    public async Task RefreshAsync_WhenPendingRestartDisable_ShowsOffWithBadge()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor("slow", SwitchGroup.System, "慢开关", "要重启", RequiresRestart: true),
            NextReadState = SwitchState.PendingRestart,
            NextReadPendingOn = false,
        };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.False(card.IsOn);
        Assert.True(card.ShowRestartBadge);
    }

    [Fact]
    public async Task ToggleAsync_PendingRestartEnable_TargetsOffSoItCanBeUndone()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor("slow", SwitchGroup.System, "慢开关", "要重启", RequiresRestart: true),
            NextReadState = SwitchState.PendingRestart,
            NextReadPendingOn = true,
        };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.Off, fake.LastTarget);
    }

    [Fact]
    public async Task ToggleAsync_IdempotentRewrite_DoesNotLightBadge()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor("slow", SwitchGroup.System, "慢开关", "要重启", RequiresRestart: true),
            NextReadState = SwitchState.On,
            ApplyIsNoOp = true,
        };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, card.State);
        Assert.True(card.IsOn);
        Assert.False(card.ShowRestartBadge);
    }

    private static FakeSwitch DestructiveSwitch(SwitchState state) => new()
    {
        Descriptor = new SwitchDescriptor(
            "destructive", SwitchGroup.Security, "破坏性开关", "原始副标题",
            RequiresRestart: true,
            IsDestructive: true,
            ConfirmText: "关掉就回不来了，确定吗？"),
        NextReadState = state,
    };
}
