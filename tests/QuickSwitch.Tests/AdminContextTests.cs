using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests;

public class AdminContextTests
{
    [Fact]
    public async Task IsElevated_MatchesIndependentPowerShellProbe()
    {
        var powerShell = new PowerShellRunner(new ProcessRunner());
        var probe = await powerShell.RunAsync(
            "([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)",
            CancellationToken.None);

        Assert.True(probe.Succeeded, probe.StandardError);
        Assert.Equal(probe.StandardOutput.Trim().Equals("True", StringComparison.OrdinalIgnoreCase), AdminContext.IsElevated());
    }
}
