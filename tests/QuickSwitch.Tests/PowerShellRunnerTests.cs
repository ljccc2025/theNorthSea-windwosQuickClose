using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests;

public class PowerShellRunnerTests
{
    private static PowerShellRunner CreateRunner() => new(new ProcessRunner());

    [Fact]
    public async Task RunAsync_Echo_ReturnsStdoutAndZeroExit()
    {
        var result = await CreateRunner().RunAsync("Write-Output 'hello'", CancellationToken.None);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("hello", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ChineseOutput_SurvivesRoundTrip()
    {
        var result = await CreateRunner().RunAsync("Write-Output '中文测试·防火墙'", CancellationToken.None);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("中文测试·防火墙", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_TerminatingError_ReturnsNonZeroExitAndErrorText()
    {
        var result = await CreateRunner().RunAsync("throw 'boom'", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("boom", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_WritesToStdErr_KeepsStreamsSeparate()
    {
        var result = await CreateRunner().RunAsync(
            "Write-Output 'out'; [Console]::Error.WriteLine('err')",
            CancellationToken.None);

        Assert.Equal("out", result.StandardOutput);
        Assert.Contains("err", result.StandardError);
    }
}
