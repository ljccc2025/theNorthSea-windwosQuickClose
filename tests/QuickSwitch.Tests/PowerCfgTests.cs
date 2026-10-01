using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests;

public class PowerCfgTests
{
    [Fact]
    public void BuildScript_WrapsEveryArgumentInSingleQuotes()
    {
        Assert.Equal("& powercfg.exe '/l'", PowerCfg.BuildScript(["/l"]));
    }

    [Fact]
    public void BuildScript_WithoutArguments_IsBareExecutable()
    {
        Assert.Equal("& powercfg.exe", PowerCfg.BuildScript([]));
    }

    [Fact]
    public void BuildScript_EscapesEmbeddedSingleQuote()
    {
        Assert.Equal("& powercfg.exe '/setactive' 'a''b'", PowerCfg.BuildScript(["/setactive", "a'b"]));
    }

    /// F1 回归：直启 powercfg.exe 时中文按 CP936 输出、却被 ProcessRunner 按 UTF-8 解码，
    /// 方案名全成乱码 → 「名字优先、GUID 兜底」静默反转、失败回显也给用户看乱码。
    [Fact]
    public async Task RunList_OnRealMachine_DecodesChineseSchemeNames()
    {
        var powerCfg = new PowerCfg(new PowerShellRunner(new ProcessRunner()));

        var result = await powerCfg.RunAsync(["/l"], CancellationToken.None);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.DoesNotContain('\uFFFD', result.StandardOutput);

        var schemes = PowerSchemeParser.ParseList(result.StandardOutput);
        Assert.NotEmpty(schemes);

        var active = Assert.Single(schemes, scheme => scheme.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(active.Name));
        Assert.DoesNotContain('\uFFFD', active.Name);
        Assert.Contains(
            PowerSchemeParser.CanonicalOptions,
            option => PowerSchemeParser.MatchesOption(option, active.Name));
    }
}
