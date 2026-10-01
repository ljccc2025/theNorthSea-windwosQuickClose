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
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

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
            var stderrText = string.IsNullOrEmpty(timedOutStderr) ? timeoutText : timedOutStderr + Environment.NewLine + timeoutText;
            return new ProcessResult(124, timedOutStdout, stderrText);
        }

        return new ProcessResult(
            process.ExitCode,
            (await stdout.ConfigureAwait(false)).TrimEnd('\r', '\n'),
            (await stderr.ConfigureAwait(false)).TrimEnd('\r', '\n'));
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
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // 进程可能已退出或已被系统回收，此时无需再杀。
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
