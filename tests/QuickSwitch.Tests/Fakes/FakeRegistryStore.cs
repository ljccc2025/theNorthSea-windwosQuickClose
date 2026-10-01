using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests.Fakes;

internal sealed record RecordedWrite(RegistryScope Scope, string KeyPath, string ValueName, int Value);

internal sealed class FakeRegistryStore : IRegistryStore
{
    private readonly Dictionary<(RegistryScope Scope, string KeyPath, string ValueName), int> _dwords = [];
    private readonly Dictionary<(RegistryScope Scope, string KeyPath, string ValueName), string> _strings = [];

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public List<RecordedWrite> Writes { get; } = [];

    public void SeedDword(
        string keyPath, string valueName, int value, RegistryScope scope = RegistryScope.CurrentUser) =>
        _dwords[(scope, keyPath, valueName)] = value;

    public void SeedString(
        string keyPath, string valueName, string value, RegistryScope scope = RegistryScope.CurrentUser) =>
        _strings[(scope, keyPath, valueName)] = value;

    public int? ReadDword(RegistryScope scope, string subKeyPath, string valueName)
    {
        if (ReadFailure is not null) throw ReadFailure;
        return _dwords.TryGetValue((scope, subKeyPath, valueName), out var value) ? value : null;
    }

    public string? ReadString(RegistryScope scope, string subKeyPath, string valueName)
    {
        if (ReadFailure is not null) throw ReadFailure;
        return _strings.TryGetValue((scope, subKeyPath, valueName), out var value) ? value : null;
    }

    public void WriteDword(RegistryScope scope, string subKeyPath, string valueName, int value)
    {
        if (WriteFailure is not null) throw WriteFailure;

        Writes.Add(new RecordedWrite(scope, subKeyPath, valueName, value));
        _dwords[(scope, subKeyPath, valueName)] = value;
    }
}
