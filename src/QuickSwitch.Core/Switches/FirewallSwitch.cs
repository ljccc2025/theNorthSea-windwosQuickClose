using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class FirewallSwitch : ISwitch
{
    public const string ReadScript =
        "(Get-NetFirewallProfile -Profile Domain,Private,Public | ForEach-Object { '{0}={1}' -f $_.Name, $_.Enabled }) -join ';'";

    private readonly PowerShellRunner _powerShell;

    public FirewallSwitch(PowerShellRunner powerShell) => _powerShell = powerShell;

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "firewall",
        Group: SwitchGroup.Security,
        Title: "Windows 防火墙",
        Subtitle: "域 / 专用 / 公用 三档统一开关");

    /// 必须传裸字符串标记 True/False：$true 会被 PowerShell 绑成 System.Boolean，
    /// 而 -Enabled 参数类型是 GpoBoolean，会抛
    /// "Invalid cast from 'System.Boolean' to GpoBoolean"（本机已验证）。
    public static string BuildApplyScript(SwitchState target)
    {
        var flag = target switch
        {
            SwitchState.On => "True",
            SwitchState.Off => "False",
            _ => throw new ArgumentOutOfRangeException(
                nameof(target), target, "防火墙只接受 On 或 Off。"),
        };

        return $"Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled {flag}";
    }

    public async Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await _powerShell.RunAsync(ReadScript, cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? FirewallStateParser.Parse(result.StandardOutput)
            : new SwitchReadResult(SwitchState.Unknown, ErrorText.FirstLine(result.StandardError));
    }

    public async Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var script = BuildApplyScript(target);
        var result = await _powerShell.RunAsync(script, cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? SwitchApplyResult.Ok()
            : SwitchApplyResult.Fail(ErrorText.FirstLine(result.StandardError));
    }
}
