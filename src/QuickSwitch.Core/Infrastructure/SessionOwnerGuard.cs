using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace QuickSwitch.Core.Infrastructure;

public sealed record SessionOwnerState(bool IsForeignAdmin, string? Detail)
{
    public static SessionOwnerState SameAccount { get; } = new(false, null);
}

public interface IUserSidSource
{
    /// 当前进程令牌所属账户（即被提升的账户）的 SID。
    string? GetProcessUserSid();

    /// 当前会话里 explorer.exe 令牌的 SID；取不到返回 null。
    string? GetShellUserSid();
}

/// 提权归属守卫：标准账户用另一个管理员账户提升时，HKCU 指向那个管理员的 hive，
/// 写下去的是别人的配置。判定用 SID 比对，不猜账户名。
public sealed class SessionOwnerGuard
{
    public const string ForeignAdminDetail = "当前以其他管理员账户运行，用户级设置不可用";

    private readonly IUserSidSource _sids;

    public SessionOwnerGuard(IUserSidSource sids) => _sids = sids;

    public SessionOwnerState Evaluate() => Compare(_sids.GetProcessUserSid(), _sids.GetShellUserSid());

    /// 拿不到任一侧 SID 时按同账户处理：探测失败不该把本来能用的开关锁死（规格 §8）。
    public static SessionOwnerState Compare(string? processUserSid, string? shellUserSid)
    {
        if (string.IsNullOrWhiteSpace(processUserSid) || string.IsNullOrWhiteSpace(shellUserSid))
            return SessionOwnerState.SameAccount;

        return string.Equals(processUserSid, shellUserSid, StringComparison.OrdinalIgnoreCase)
            ? SessionOwnerState.SameAccount
            : new SessionOwnerState(true, ForeignAdminDetail);
    }
}

public sealed class WindowsUserSidSource : IUserSidSource
{
    public string? GetProcessUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }

    public string? GetShellUserSid()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                if (!IsInSession(process, sessionId)) continue;

                var sid = TryGetOwnerSid(process.Id);
                if (sid is not null) return sid;
            }
        }

        return null;
    }

    private static bool IsInSession(Process process, int sessionId)
    {
        try
        {
            return process.SessionId == sessionId;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static string? TryGetOwnerSid(int processId)
    {
        var processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero) return null;

        try
        {
            if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TokenQuery, out var token) || token == IntPtr.Zero)
                return null;

            try
            {
                NativeMethods.GetTokenInformation(token, NativeMethods.TokenUser, IntPtr.Zero, 0, out var size);
                if (size <= 0) return null;

                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenUser, buffer, size, out _))
                        return null;

                    // TOKEN_USER 的第一个字段就是 SID 指针。
                    return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }
}
