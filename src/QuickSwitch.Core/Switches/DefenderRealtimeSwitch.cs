using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class DefenderRealtimeSwitch : ISwitch
{
    public const string ReadScript =
        "$s = Get-MpComputerStatus; '{0}|{1}' -f $s.RealTimeProtectionEnabled, $s.IsTamperProtected";

    private readonly PowerShellRunner _powerShell;

    public DefenderRealtimeSwitch(PowerShellRunner powerShell) => _powerShell = powerShell;

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "defender-realtime",
        Group: SwitchGroup.Security,
        Title: "实时防护",
        Subtitle: "Microsoft Defender 实时监控；篡改防护开启时无法从外部切换");

    public static string BuildApplyScript(SwitchState target)
    {
        var flag = target switch
        {
            SwitchState.On => "$false",
            SwitchState.Off => "$true",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "实时防护只接受 On 或 Off。"),
        };

        return $"Set-MpPreference -DisableRealtimeMonitoring {flag}";
    }

    public async Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await _powerShell.RunAsync(ReadScript, cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? DefenderStatusParser.Parse(result.StandardOutput)
            : new SwitchReadResult(SwitchState.Unknown, ErrorText.FirstLine(result.StandardError));
    }

    public async Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var result = await _powerShell.RunAsync(BuildApplyScript(target), cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? SwitchApplyResult.Ok()
            : SwitchApplyResult.Fail(ErrorText.FirstLine(result.StandardError));
    }
}

