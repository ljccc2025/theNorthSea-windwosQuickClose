using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests.Fakes;

/// 假的 powercfg：按子命令配置返回值，也支持按调用顺序排队。调用参数全部留痕。
internal sealed class FakePowerCfg : IPowerCfg
{
    private readonly Queue<ProcessResult> _queue = new();

    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// 按 arguments[0]（子命令）配置的返回值，优先级高于队列。
    public Dictionary<string, ProcessResult> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? Failure { get; set; }

    public ProcessResult Fallback { get; set; } = new(0, string.Empty, string.Empty);

    public FakePowerCfg Enqueue(ProcessResult result)
    {
        _queue.Enqueue(result);
        return this;
    }

    public FakePowerCfg EnqueueSuccess(string stdout) => Enqueue(new ProcessResult(0, stdout, string.Empty));

    public FakePowerCfg RespondTo(string subCommand, ProcessResult result)
    {
        Responses[subCommand] = result;
        return this;
    }

    public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);

        if (Failure is not null) throw Failure;

        if (arguments.Count > 0 && Responses.TryGetValue(arguments[0], out var configured))
            return Task.FromResult(configured);

        return Task.FromResult(_queue.Count > 0 ? _queue.Dequeue() : Fallback);
    }
}
