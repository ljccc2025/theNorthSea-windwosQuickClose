using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class SwitchRegistryTests
{
    private static SwitchRegistry CreateRegistry() =>
        SwitchRegistry.CreateDefault(new PowerShellRunner(new ProcessRunner()));

    [Fact]
    public void CreateDefault_ContainsFirewallSwitch()
    {
        var registry = CreateRegistry();

        var descriptor = Assert.Single(registry.All).Descriptor;
        Assert.Equal("firewall", descriptor.Id);
        Assert.Equal(SwitchGroup.Security, descriptor.Group);
    }

    [Fact]
    public void CreateDefault_EveryDescriptorIsFullyPopulated()
    {
        foreach (var item in CreateRegistry().All)
        {
            var descriptor = item.Descriptor;
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Id));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Group));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Title));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Subtitle));
        }
    }

    [Fact]
    public void Constructor_DuplicateIds_Throws()
    {
        var powerShell = new PowerShellRunner(new ProcessRunner());

        Assert.Throws<ArgumentException>(() => new SwitchRegistry(
            [new FirewallSwitch(powerShell), new FirewallSwitch(powerShell)]));
    }
}
