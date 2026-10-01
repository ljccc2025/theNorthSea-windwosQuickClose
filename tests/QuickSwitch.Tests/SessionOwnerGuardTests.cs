using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests;

public class SessionOwnerGuardTests
{
    private const string AdminSid = "S-1-5-21-1004336348-1177238915-682003330-500";
    private const string UserSid = "S-1-5-21-1004336348-1177238915-682003330-1001";

    private sealed class FakeSidSource(string? processSid, string? shellSid) : IUserSidSource
    {
        public string? GetProcessUserSid() => processSid;

        public string? GetShellUserSid() => shellSid;
    }

    [Fact]
    public void Compare_SameSid_IsNotForeignAdmin()
    {
        var state = SessionOwnerGuard.Compare(AdminSid, AdminSid);

        Assert.False(state.IsForeignAdmin);
        Assert.Null(state.Detail);
    }

    [Fact]
    public void Compare_SidCaseDiffers_IsNotForeignAdmin()
    {
        var state = SessionOwnerGuard.Compare(AdminSid, AdminSid.ToLowerInvariant());

        Assert.False(state.IsForeignAdmin);
    }

    [Fact]
    public void Compare_DifferentSid_IsForeignAdminWithReason()
    {
        var state = SessionOwnerGuard.Compare(AdminSid, UserSid);

        Assert.True(state.IsForeignAdmin);
        Assert.Equal(SessionOwnerGuard.ForeignAdminDetail, state.Detail);
    }

    [Theory]
    [InlineData(null, UserSid)]
    [InlineData(AdminSid, null)]
    [InlineData("", "")]
    [InlineData("   ", UserSid)]
    public void Compare_MissingSid_TreatsAsSameAccount(string? processSid, string? shellSid)
    {
        var state = SessionOwnerGuard.Compare(processSid, shellSid);

        Assert.False(state.IsForeignAdmin);
        Assert.Null(state.Detail);
    }

    [Fact]
    public void Evaluate_UsesBothSidsFromSource()
    {
        var foreign = new SessionOwnerGuard(new FakeSidSource(AdminSid, UserSid));
        var same = new SessionOwnerGuard(new FakeSidSource(UserSid, UserSid));
        var shellProbeFailed = new SessionOwnerGuard(new FakeSidSource(AdminSid, null));

        Assert.True(foreign.Evaluate().IsForeignAdmin);
        Assert.False(same.Evaluate().IsForeignAdmin);
        Assert.False(shellProbeFailed.Evaluate().IsForeignAdmin);
    }

    [Fact]
    public void WindowsUserSidSource_ProcessSid_IsWellFormed()
    {
        var sid = new WindowsUserSidSource().GetProcessUserSid();

        Assert.False(string.IsNullOrWhiteSpace(sid));
        Assert.StartsWith("S-1-", sid!);
    }

    [Fact]
    public void WindowsUserSidSource_ShellSid_WhenFound_IsWellFormed()
    {
        var sid = new WindowsUserSidSource().GetShellUserSid();

        if (sid is not null)
            Assert.StartsWith("S-1-", sid);
    }
}
