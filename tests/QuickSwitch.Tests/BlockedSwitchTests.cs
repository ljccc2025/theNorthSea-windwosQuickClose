using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class BlockedSwitchTests
{
    private const string Reason = "当前以其他管理员账户运行，用户级设置不可用";

    private static FakeSwitch CreateInner() => new()
    {
        Descriptor = new SwitchDescriptor("blocked-target", SwitchGroup.Network, "系统代理", "当前用户的 WinINET 代理"),
    };

    [Fact]
    public void Descriptor_IsPassedThrough()
    {
        var inner = CreateInner();

        var blocked = new BlockedSwitch(inner, Reason);

        Assert.Equal(inner.Descriptor, blocked.Descriptor);
    }

    [Fact]
    public void Constructor_BlankReason_Throws()
    {
        Assert.Throws<ArgumentException>(() => new BlockedSwitch(CreateInner(), "   "));
    }

    [Fact]
    public async Task ReadAsync_IsAlwaysBlockedWithReason()
    {
        var inner = CreateInner();
        inner.NextReadState = SwitchState.On;

        var result = await new BlockedSwitch(inner, Reason).ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Blocked, result.State);
        Assert.Equal(Reason, result.Detail);
        Assert.Equal(0, inner.ReadCount);
    }

    [Fact]
    public async Task ApplyAsync_FailsWithReasonAndNeverTouchesInner()
    {
        var inner = CreateInner();

        var result = await new BlockedSwitch(inner, Reason).ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(Reason, result.Error);
        Assert.Null(inner.LastTarget);
    }
}
