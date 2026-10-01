namespace QuickSwitch.Core.Infrastructure;

/// powercfg 出口。开关只依赖这个接口，单测里塞假实现就能验全套分支。
public interface IPowerCfg
{
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class PowerCfg : IPowerCfg
{
    public const string ExecutableName = "powercfg.exe";

    private readonly IProcessRunner _runner;

    public PowerCfg(IProcessRunner runner) => _runner = runner;

    public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(ExecutableName, arguments, cancellationToken);
}
