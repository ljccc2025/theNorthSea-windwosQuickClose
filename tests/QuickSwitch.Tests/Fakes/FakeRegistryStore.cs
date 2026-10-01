using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests.Fakes;

internal sealed record RecordedWrite(string KeyPath, string ValueName, int Value);

internal sealed class FakeRegistryStore : IRegistryStore
{
    private readonly Dictionary<(string KeyPath, string ValueName), int> _dwords = [];
    private readonly Dictionary<(string KeyPath, string ValueName), string> _strings = [];

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public List<RecordedWrite> Writes { get; } = [];

    public void SeedDword(string keyPath, string valueName, int value) => _dwords[(keyPath, valueName)] = value;

    public void SeedString(string keyPath, string valueName, string value) => _strings[(keyPath, valueName)] = value;

    public int? ReadDword(string subKeyPath, string valueName)
    {
        if (ReadFailure is not null) throw ReadFailure;
        return _dwords.TryGetValue((subKeyPath, valueName), out var value) ? value : null;
    }

    public string? ReadString(string subKeyPath, string valueName)
    {
        if (ReadFailure is not null) throw ReadFailure;
        return _strings.TryGetValue((subKeyPath, valueName), out var value) ? value : null;
    }

    public void WriteDword(string subKeyPath, string valueName, int value)
    {
        if (WriteFailure is not null) throw WriteFailure;

        Writes.Add(new RecordedWrite(subKeyPath, valueName, value));
        _dwords[(subKeyPath, valueName)] = value;
    }
}
