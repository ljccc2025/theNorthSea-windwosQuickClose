using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace QuickSwitch.Core.Infrastructure;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    /// 默认超时与设计规格一致（`docs/superpowers/specs/2026-10-01-windows-quickswitch-design.md`：
    /// 默认 60 秒，dism / 功能组件类另传 600 秒）。
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// 读流回归的上界。两个地方都要它：杀掉进程树后（杀不干净时管道不会 EOF），
    /// 以及正常退出后（孙进程继承了管道写句柄时，ReadToEndAsync 永远等不到 EOF，
    /// 那张卡就会永久停在"处理中…"并占住闸门）。
    private static readonly TimeSpan StreamDrainTimeout = TimeSpan.FromSeconds(5);

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(-1, string.Empty, $"无法启动进程 '{fileName}'：{ex.Message}");
        }

        // 读取任务不挂 cancellationToken：取消时我们主动杀进程，让管道自然 EOF，
        // 这样既不会抛出未观察的取消异常，也不会在等待流结束时死锁。
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        var effectiveTimeout = timeout ?? DefaultTimeout;
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);

            var timedOutStdout = (await SafeReadAsync(stdout).ConfigureAwait(false)).TrimEnd('\r', '\n');
            var timedOutStderr = (await SafeReadAsync(stderr).ConfigureAwait(false)).TrimEnd('\r', '\n');

            // 外部取消优先于超时：语义是取消，原样抛出，只保证子进程已被杀干净。
            if (cancellationToken.IsCancellationRequested)
                throw;

            var timeoutText = $"进程超时（{effectiveTimeout.TotalSeconds:0.###} 秒），已杀掉整棵进程树。";

            // 超时文案必须留在首行：ErrorText.FirstLine 只取第一行，排在子进程残留的 stderr 后面
            // 就会被吃掉，用户永远看不到"超时"。
            var stderrText = string.IsNullOrEmpty(timedOutStderr) ? timeoutText : timeoutText + Environment.NewLine + timedOutStderr;
            return new ProcessResult(124, timedOutStdout, stderrText);
        }

        return new ProcessResult(
            process.ExitCode,
            (await SafeReadAsync(stdout).ConfigureAwait(false)).TrimEnd('\r', '\n'),
            (await SafeReadAsync(stderr).ConfigureAwait(false)).TrimEnd('\r', '\n'));
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            // 有界等待进程回收；这里不依赖 ReadToEndAsync 完成，避免与流读取互相等待。
            process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // 进程可能已退出、已被系统回收，或 Process.Kill(entireProcessTree) 抛
            // AggregateException（"后代进程没能全部终止"）。杀不干净不是本方法要报的错：
            // 上面的 WaitForExit 有界，调用方拿到的是结构化的超时/取消结果，而不是异常。
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> readTask)
    {
        try
        {
            var completed = await Task.WhenAny(readTask, Task.Delay(StreamDrainTimeout)).ConfigureAwait(false);
            if (completed != readTask)
                return string.Empty;

            return await readTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
