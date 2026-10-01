using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class WindowsFeatureSwitch : ISwitch
{
    /// dism 系操作可能跑十几分钟，规格给这一类单独放宽到 600 秒（§7）。
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);

    private readonly PowerShellRunner _powerShell;
    private readonly WindowsFeatureSpec _spec;

    public WindowsFeatureSwitch(PowerShellRunner powerShell, WindowsFeatureSpec spec)
    {
        _powerShell = powerShell;
        _spec = spec;
        Descriptor = new SwitchDescriptor(
            Id: spec.Id,
            Group: SwitchGroup.System,
            Title: spec.Title,
            Subtitle: spec.Subtitle,
            RequiresRestart: true,
            IsDestructive: true,
            ConfirmText: $"关闭 {spec.Title} 需要重启才生效，依赖它的功能会立即不可用。确定要关闭吗？");
    }

    public SwitchDescriptor Descriptor { get; }

    /// 功能名要拼进 PowerShell 脚本，唯一来源是本进程的常量表；仍然挡一道，防以后有人从配置读名字。
    public static void EnsureSafeFeatureName(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName) || !featureName.All(IsSafeChar))
            throw new ArgumentException($"功能名包含非法字符：{featureName}", nameof(featureName));
    }

    public static string BuildReadScript(string featureName)
    {
        EnsureSafeFeatureName(featureName);
        return $"(Get-WindowsOptionalFeature -Online -FeatureName '{featureName}').State";
    }

    public static string BuildApplyScript(string featureName, SwitchState target)
    {
        EnsureSafeFeatureName(featureName);

        var command = target switch
        {
            SwitchState.On => "Enable-WindowsOptionalFeature",
            SwitchState.Off => "Disable-WindowsOptionalFeature",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "功能组件只接受 On 或 Off。"),
        };

        return $"{command} -Online -FeatureName '{featureName}' -NoRestart | Out-Null";
    }

    public async Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await _powerShell
            .RunAsync(BuildReadScript(_spec.FeatureName), cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded
            ? WindowsFeatureStateParser.Parse(_spec.Title, result.StandardOutput)
            : new SwitchReadResult(SwitchState.Unknown, ErrorText.FirstLine(result.StandardError));
    }

    public async Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var result = await _powerShell
            .RunAsync(BuildApplyScript(_spec.FeatureName, target), cancellationToken, OperationTimeout)
            .ConfigureAwait(false);

        return result.Succeeded
            ? SwitchApplyResult.Ok()
            : SwitchApplyResult.Fail(ErrorText.FirstLine(result.StandardError));
    }

    private static bool IsSafeChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_';
}

