using System.Diagnostics;
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

    [Fact]
    public async Task RunAsync_NativeCommandNonZeroExit_PropagatesRealExitCode()
    {
        var result = await CreateRunner().RunAsync("cmd /c exit 7", CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_NativeCommandNonZeroExit_KeepsStdoutOfFollowingCmdlet()
    {
        var result = await CreateRunner().RunAsync("cmd /c exit 7; Write-Output tail", CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("tail", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_PureCmdletWithoutNativeCommand_ExitsZero()
    {
        var result = await CreateRunner().RunAsync("Write-Output ok", CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Succeeded, result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ScriptWithSpacesAndMetaCharacters_IsPassedVerbatim()
    {
        const string payload = "a b; c|d & e > f";

        var result = await CreateRunner().RunAsync($"Write-Output '{payload}'", CancellationToken.None);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(payload, result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessTreeAndReturnsStructuredResult()
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await CreateRunner().RunAsync(
            "Start-Sleep -Seconds 30",
            CancellationToken.None,
            TimeSpan.FromSeconds(1));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"超时未生效，耗时 {stopwatch.Elapsed}");
        Assert.False(result.Succeeded);
        Assert.Equal(124, result.ExitCode);
        Assert.Contains("超时", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ScriptEndingWithComment_StillPropagatesRealExitCode()
    {
        var result = await CreateRunner().RunAsync("cmd /c exit 7 # 尾注释", CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_TimeoutWithPriorStderr_PutsTimeoutTextOnFirstLine()
    {
        var result = await CreateRunner().RunAsync(
            "[Console]::Error.WriteLine('partial'); Start-Sleep -Seconds 30",
            CancellationToken.None,
            TimeSpan.FromSeconds(1));

        Assert.Equal(124, result.ExitCode);
        Assert.StartsWith("进程超时", result.StandardError);
        Assert.Contains("partial", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessTreeBeforeMarkerIsWritten()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), $"quickswitch-cancel-{Guid.NewGuid():N}.txt");
        var script = $"Start-Sleep -Seconds 3; Set-Content -LiteralPath '{markerPath}' -Value alive";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateRunner().RunAsync(script, cts.Token, TimeSpan.FromSeconds(30)));

            await Task.Delay(TimeSpan.FromSeconds(4));

            Assert.False(File.Exists(markerPath), "子进程未被杀死：标记文件已被写出");
        }
        finally
        {
            if (File.Exists(markerPath))
                File.Delete(markerPath);
        }
    }
}
