# Windows 快捷开关 —— M0 + M1 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 交付一个常驻托盘的 Windows 11 提权工具，先打通「防火墙三档一键开关」这一条完整竖切：骨架 + manifest 提权 + 托盘宿主 + `ISwitch` 抽象 + 分组卡片 UI + 真实读写 + 权威回读。

**架构：** 三个项目。`QuickSwitch.Core`（`net10.0-windows` 类库，无 WPF）承载领域模型 `ISwitch`/`SwitchState`/`SwitchDescriptor`、唯一的进程出口 `ProcessRunner`/`PowerShellRunner`、以及纯逻辑 ViewModel（可单测）；`QuickSwitch`（WPF + WinForms 托盘宿主）只放 XAML 与窗口/托盘接线；`QuickSwitch.Tests`（xUnit）跑纯逻辑单测与少量真机集成测试。所有写操作走「点击 → busy → 执行 → 权威回读」，失败把系统原文放卡片副标题行。

**技术栈：** .NET 10（`net10.0-windows`）、WPF、WinForms `NotifyIcon`、CommunityToolkit.Mvvm、xUnit、PowerShell 5.1 子进程（`powershell.exe -NoProfile -NonInteractive`）。

---

## 范围说明（先读这一条）

设计规格 `docs/superpowers/specs/2026-10-01-windows-quickswitch-design.md` 覆盖九个开关（M0–M4）。本计划**只覆盖 M0 + M1**：基础设施 + 防火墙全链路。理由：M0+M1 本身就是可独立运行、可测试、可发布的软件；M2（代理 + 剪贴板 + 提权归属守卫）、M3（休眠 + 快速启动 + 电源计划 `IChoiceSwitch`）、M4（实时防护 + 功能组件 + UAC）在骨架验收后各自出独立计划，边际成本主要是新增 `ISwitch` 实现。

因此本计划**不定义** `IChoiceSwitch`、`RequiresRestart`、`IsDestructive`、`ConfirmText`、提权归属守卫（`SessionOwnerGuard`）——它们分别在 M3、M4、M2 的计划里随首个使用者一起落地。`SwitchState` 枚举保留全部六个值（`Unknown`/`On`/`Off`/`Mixed`/`Blocked`/`PendingRestart`），M1 实际产出 `Unknown`/`On`/`Off`/`Mixed`。

**不建 worktree：** 仓库是当天新建的空仓库，只有一条 `main` 分支，无并行工作，直接在主工作区执行。

## 已在本机验证过的事实（写代码前必须知道）

1. 本机防火墙当前三档状态均为 **OFF**（`netsh advfirewall show allprofiles state` → Domain/Private/Public 全 `State OFF`）。
2. `Get-NetFirewallProfile` 输出 `Name` = `Domain`/`Private`/`Public`、`Enabled` = `True`/`False`，**非提权也可读**。
3. **坑：`Set-NetFirewallProfile -Enabled $true` 会抛类型错误** —— `Cannot process argument transformation on parameter 'Enabled'. Cannot convert value "True" to type "Microsoft.PowerShell.Cmdletization.GeneratedTypes.NetSecurity.GpoBoolean. Error: "Invalid cast from 'System.Boolean' to 'GpoBoolean'."` 必须传**裸字符串标记** `-Enabled True` / `-Enabled False`（参数模式下的裸 `True` 会被解析成字符串，可正常转换成 `GpoBoolean`）。已验证 `-Enabled False` 绑定成功。
4. `netsh advfirewall set allprofiles state off` 非提权时报 `The requested operation requires elevation (Run as administrator).`（退出码 1）——这是错误文案的形状参考。
5. 当前 shell **未提权**；`dotnet test` 也将在未提权下运行，因此测试不得依赖管理员权限。
6. .NET 10 SDK (10.0.300) 模板可用：`wpf`、`xunit`、`classlib`、`sln`。
7. 教训（已踩）：用 `pwsh -Command` 再把字符串转交给 `powershell.exe` 时，内层双引号会被剥掉，脚本语法报错。所以生产代码必须用 `ProcessStartInfo.ArgumentList` 逐参数传递（不经过任何 shell 重引用），永远不要手拼命令行字符串。
8. 测试模板差异：本计划所有异步测试用 `TestContext.Current.CancellationToken`（xUnit v3）。若 `dotnet new xunit` 生成的是 xUnit v2，全局替换为 `CancellationToken.None` 即可。

## 文件结构

创建：

| 路径 | 职责 |
|---|---|
| `QuickSwitch.sln` | 解决方案（若 SDK 生成 `.slnx`，则按实际文件名替换后续命令） |
| `Directory.Build.props` | 三项目共享属性（TFM / Nullable / ImplicitUsings / LangVersion） |
| `src/QuickSwitch.Core/QuickSwitch.Core.csproj` | 类库，无 WPF，可被单测直接引用 |
| `src/QuickSwitch.Core/Switches/SwitchState.cs` | 六态枚举 |
| `src/QuickSwitch.Core/Switches/SwitchGroup.cs` | 四个 UI 分组的字符串常量 |
| `src/QuickSwitch.Core/Switches/SwitchDescriptor.cs` | 卡片元数据（Id / Group / Title / Subtitle） |
| `src/QuickSwitch.Core/Switches/ISwitch.cs` | 开关契约 + `SwitchReadResult` / `SwitchApplyResult` |
| `src/QuickSwitch.Core/Switches/FirewallStateParser.cs` | 纯函数：解析 `Get-NetFirewallProfile` 输出 → 状态 + 副标题 |
| `src/QuickSwitch.Core/Switches/FirewallSwitch.cs` | 防火墙开关实现（读脚本 / 写脚本 / 回读） |
| `src/QuickSwitch.Core/Switches/SwitchRegistry.cs` | 显式 new 的注册表，不用反射 |
| `src/QuickSwitch.Core/Infrastructure/ProcessResult.cs` | 子进程结果值对象 |
| `src/QuickSwitch.Core/Infrastructure/ProcessRunner.cs` | **唯一**进程出口，UTF-8 管道 |
| `src/QuickSwitch.Core/Infrastructure/PowerShellRunner.cs` | 唯一 PowerShell 出口，注入编码前缀 |
| `src/QuickSwitch.Core/Infrastructure/ErrorText.cs` | 从 stderr 提炼一行人类可读错误 |
| `src/QuickSwitch.Core/Infrastructure/AdminContext.cs` | `IsElevated()` |
| `src/QuickSwitch.Core/ViewModels/SwitchCardViewModel.cs` | 单卡 VM（纯逻辑，无 WPF 类型） |
| `src/QuickSwitch.Core/ViewModels/MainViewModel.cs` | 卡片集合 + 全局刷新 + 提权徽标 |
| `src/QuickSwitch/QuickSwitch.csproj` | WPF `WinExe`，引用 Core |
| `src/QuickSwitch/app.manifest` | `requireAdministrator` + PerMonitorV2 DPI |
| `src/QuickSwitch/App.xaml` / `App.xaml.cs` | 启动接线（无 StartupUri）、退出清理 |
| `src/QuickSwitch/MainWindow.xaml` / `.xaml.cs` | 分组卡片列表；关闭即隐藏 |
| `src/QuickSwitch/Styles/ToggleSwitch.xaml` | `SwitchToggleStyle`（胶囊开关模板） |
| `src/QuickSwitch/Converters/InverseBoolConverter.cs` | WPF 无内置反向布尔转换器，刷新按钮需要 |
| `src/QuickSwitch/TrayHost.cs` | `NotifyIcon` 托盘宿主 |
| `tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj` | xUnit，引用 Core |
| `tests/QuickSwitch.Tests/Fakes/FakeSwitch.cs` | 可控的 `ISwitch` 测试替身 |
| `tests/QuickSwitch.Tests/FirewallStateParserTests.cs` | 解析器单测 |
| `tests/QuickSwitch.Tests/PowerShellRunnerTests.cs` | 进程出口单测（含中文回环） |
| `tests/QuickSwitch.Tests/FirewallSwitchTests.cs` | 写脚本字符串 + 真机只读烟雾测试 |
| `tests/QuickSwitch.Tests/SwitchRegistryTests.cs` | 注册表完整性 |
| `tests/QuickSwitch.Tests/SwitchCardViewModelTests.cs` | 卡片 VM 行为 |
| `tests/QuickSwitch.Tests/MainViewModelTests.cs` | 全局刷新与徽标 |

修改：

| 路径 | 变更 |
|---|---|
| `README.md` | 补「构建与运行」小节（UAC 提示、发布命令、开发期 `dotnet test` 无需提权） |
| `.gitignore` | 已覆盖 `bin/` `obj/` `publish/`，无需改动 |

---

### 任务 0：解决方案与项目骨架

**文件：**
- 创建：`Directory.Build.props`、`QuickSwitch.sln`、`src/QuickSwitch.Core/QuickSwitch.Core.csproj`、`src/QuickSwitch/QuickSwitch.csproj`、`tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj`

- [ ] **步骤 1：生成三个项目和解决方案**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet new sln -n QuickSwitch
dotnet new classlib -n QuickSwitch.Core -o src/QuickSwitch.Core
dotnet new wpf -n QuickSwitch -o src/QuickSwitch
dotnet new xunit -n QuickSwitch.Tests -o tests/QuickSwitch.Tests
Get-ChildItem -Filter 'QuickSwitch.sln*' | Select-Object -ExpandProperty Name
```

预期：最后一行输出 `QuickSwitch.sln`（若输出 `QuickSwitch.slnx`，后续所有 `QuickSwitch.sln` 一律替换为 `QuickSwitch.slnx`）。

- [ ] **步骤 2：写入 `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
  </PropertyGroup>
</Project>
```

- [ ] **步骤 3：清掉模板里重复的 TFM 行与占位文件**

删除 `src/QuickSwitch.Core/Class1.cs`、`tests/QuickSwitch.Tests/UnitTest1.cs`。
在 `src/QuickSwitch.Core/QuickSwitch.Core.csproj`、`src/QuickSwitch/QuickSwitch.csproj`、`tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj` 中删掉 `<TargetFramework>…</TargetFramework>` 行（TFM 由 `Directory.Build.props` 提供）。

`src/QuickSwitch/QuickSwitch.csproj` 最终形如：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <RootNamespace>QuickSwitch</RootNamespace>
    <AssemblyName>QuickSwitch</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\QuickSwitch.Core\QuickSwitch.Core.csproj" />
  </ItemGroup>

</Project>
```

`src/QuickSwitch.Core/QuickSwitch.Core.csproj` 最终形如：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  </ItemGroup>

</Project>
```

`tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj` 保持模板生成的内容，只删 TFM 行，并追加项目引用（模板里的 `Microsoft.NET.Test.Sdk` / `xunit` / `xunit.runner.visualstudio` 行原样保留，版本以模板为准）：

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\QuickSwitch.Core\QuickSwitch.Core.csproj" />
  </ItemGroup>
```

- [ ] **步骤 4：挂进解决方案并构建**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet sln QuickSwitch.sln add src/QuickSwitch.Core/QuickSwitch.Core.csproj src/QuickSwitch/QuickSwitch.csproj tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj
dotnet build QuickSwitch.sln
```

预期：`Build succeeded`，0 error。若 `CommunityToolkit.Mvvm 8.4.0` 还原失败，执行 `dotnet add src/QuickSwitch.Core/QuickSwitch.Core.csproj package CommunityToolkit.Mvvm` 取最新版，并把实际解析到的版本号写回 csproj。

- [ ] **步骤 5：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "chore: 解决方案骨架（Core / WPF / Tests）"
```

---

### 任务 1：领域类型 + 防火墙状态解析器

**文件：**
- 创建：`src/QuickSwitch.Core/Switches/SwitchState.cs`、`SwitchGroup.cs`、`SwitchDescriptor.cs`、`ISwitch.cs`、`FirewallStateParser.cs`
- 测试：`tests/QuickSwitch.Tests/FirewallStateParserTests.cs`

- [ ] **步骤 1：先写类型定义（编译所需，无行为）**

`src/QuickSwitch.Core/Switches/SwitchState.cs`：

```csharp
namespace QuickSwitch.Core.Switches;

public enum SwitchState
{
    Unknown,
    On,
    Off,
    Mixed,
    Blocked,
    PendingRestart,
}
```

`src/QuickSwitch.Core/Switches/SwitchGroup.cs`：

```csharp
namespace QuickSwitch.Core.Switches;

public static class SwitchGroup
{
    public const string Security = "安全";
    public const string Network = "网络";
    public const string Power = "电源";
    public const string System = "系统";
}
```

`src/QuickSwitch.Core/Switches/SwitchDescriptor.cs`：

```csharp
namespace QuickSwitch.Core.Switches;

public sealed record SwitchDescriptor(string Id, string Group, string Title, string Subtitle);
```

`src/QuickSwitch.Core/Switches/ISwitch.cs`：

```csharp
namespace QuickSwitch.Core.Switches;

public sealed record SwitchReadResult(SwitchState State, string? Detail = null);

public sealed record SwitchApplyResult(bool Success, string? Error = null)
{
    public static SwitchApplyResult Ok() => new(true);

    public static SwitchApplyResult Fail(string error) => new(false, error);
}

public interface ISwitch
{
    SwitchDescriptor Descriptor { get; }

    Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken);

    Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken);
}
```

- [ ] **步骤 2：编写失败的测试**

`tests/QuickSwitch.Tests/FirewallStateParserTests.cs`：

```csharp
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class FirewallStateParserTests
{
    [Fact]
    public void Parse_AllProfilesDisabled_ReturnsOff()
    {
        var result = FirewallStateParser.Parse("Domain=False;Private=False;Public=False");

        Assert.Equal(SwitchState.Off, result.State);
        Assert.Equal("域 / 专用 / 公用 三档全部关闭", result.Detail);
    }

    [Fact]
    public void Parse_AllProfilesEnabled_ReturnsOn()
    {
        var result = FirewallStateParser.Parse("Domain=True;Private=True;Public=True");

        Assert.Equal(SwitchState.On, result.State);
        Assert.Equal("域 / 专用 / 公用 三档全部开启", result.Detail);
    }

    [Fact]
    public void Parse_PartiallyEnabled_ReturnsMixedListingEnabledProfiles()
    {
        var result = FirewallStateParser.Parse("Domain=True;Private=False;Public=True");

        Assert.Equal(SwitchState.Mixed, result.State);
        Assert.Equal("已开启：域 / 公用", result.Detail);
    }

    [Fact]
    public void Parse_NewlineSeparatedAndLowercase_IsAccepted()
    {
        var result = FirewallStateParser.Parse("domain=true\r\nprivate=true\npublic=true");

        Assert.Equal(SwitchState.On, result.State);
    }

    [Fact]
    public void Parse_Empty_ReturnsUnknown()
    {
        var result = FirewallStateParser.Parse(string.Empty);

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public void Parse_MissingProfile_ReturnsUnknown()
    {
        var result = FirewallStateParser.Parse("Domain=True;Private=True");

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.Contains("Public", result.Detail);
    }

    [Fact]
    public void Parse_Garbage_ReturnsUnknownWithRawOutput()
    {
        var result = FirewallStateParser.Parse("Get-NetFirewallProfile : 无法识别");

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.Contains("无法识别", result.Detail);
    }
}
```

- [ ] **步骤 3：运行测试验证失败**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~FirewallStateParserTests
```

预期：编译失败，报 `CS0103: 当前上下文中不存在名称"FirewallStateParser"`（或 `error CS0246`）。

- [ ] **步骤 4：实现解析器**

`src/QuickSwitch.Core/Switches/FirewallStateParser.cs`：

```csharp
namespace QuickSwitch.Core.Switches;

public static class FirewallStateParser
{
    private static readonly string[] RequiredProfiles = ["Domain", "Private", "Public"];

    private static readonly Dictionary<string, string> DisplayNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = "域",
            ["Private"] = "专用",
            ["Public"] = "公用",
        };

    public static SwitchReadResult Parse(string? standardOutput)
    {
        var raw = (standardOutput ?? string.Empty).Trim();
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in raw.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = chunk.IndexOf('=');
            if (separator <= 0) continue;

            var name = chunk[..separator].Trim();
            var value = chunk[(separator + 1)..].Trim();
            if (name.Length == 0 || !TryParseFlag(value, out var enabled)) continue;

            states[name] = enabled;
        }

        var missing = RequiredProfiles.Where(profile => !states.ContainsKey(profile)).ToArray();
        if (missing.Length > 0)
        {
            var reason = raw.Length == 0
                ? "未读取到任何输出"
                : $"缺少档位 {string.Join(", ", missing)}，原始输出：{raw}";
            return new SwitchReadResult(SwitchState.Unknown, reason);
        }

        var enabledProfiles = RequiredProfiles.Where(profile => states[profile]).ToArray();
        if (enabledProfiles.Length == 0)
            return new SwitchReadResult(SwitchState.Off, "域 / 专用 / 公用 三档全部关闭");

        if (enabledProfiles.Length == RequiredProfiles.Length)
            return new SwitchReadResult(SwitchState.On, "域 / 专用 / 公用 三档全部开启");

        var names = string.Join(" / ", enabledProfiles.Select(DisplayName));
        return new SwitchReadResult(SwitchState.Mixed, $"已开启：{names}");
    }

    private static string DisplayName(string profile) =>
        DisplayNames.TryGetValue(profile, out var display) ? display : profile;

    private static bool TryParseFlag(string value, out bool enabled)
    {
        switch (value.ToLowerInvariant())
        {
            case "true":
            case "1":
            case "on":
            case "yes":
                enabled = true;
                return true;
            case "false":
            case "0":
            case "off":
            case "no":
                enabled = false;
                return true;
            default:
                enabled = false;
                return false;
        }
    }
}
```

- [ ] **步骤 5：运行测试验证通过**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~FirewallStateParserTests
```

预期：`Passed! - Failed: 0, Passed: 7`。

- [ ] **步骤 6：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 领域类型与防火墙状态解析器"
```

---

### 任务 2：唯一进程出口 `ProcessRunner` + `PowerShellRunner`

**文件：**
- 创建：`src/QuickSwitch.Core/Infrastructure/ProcessResult.cs`、`ProcessRunner.cs`、`PowerShellRunner.cs`
- 测试：`tests/QuickSwitch.Tests/PowerShellRunnerTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/QuickSwitch.Tests/PowerShellRunnerTests.cs`：

```csharp
using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests;

public class PowerShellRunnerTests
{
    private static PowerShellRunner CreateRunner() => new(new ProcessRunner());

    [Fact]
    public async Task RunAsync_Echo_ReturnsStdoutAndZeroExit()
    {
        var result = await CreateRunner().RunAsync("Write-Output 'hello'", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("hello", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ChineseOutput_SurvivesRoundTrip()
    {
        var result = await CreateRunner().RunAsync("Write-Output '中文测试·防火墙'", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("中文测试·防火墙", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_TerminatingError_ReturnsNonZeroExitAndErrorText()
    {
        var result = await CreateRunner().RunAsync("throw 'boom'", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("boom", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_WritesToStdErr_KeepsStreamsSeparate()
    {
        var result = await CreateRunner().RunAsync(
            "Write-Output 'out'; [Console]::Error.WriteLine('err')",
            TestContext.Current.CancellationToken);

        Assert.Equal("out", result.StandardOutput);
        Assert.Contains("err", result.StandardError);
    }
}
```

注意：若测试模板使用的是 xUnit v3，`TestContext.Current.CancellationToken` 可用；若是 xUnit v2，把每处改成 `CancellationToken.None`。

- [ ] **步骤 2：运行测试验证失败**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~PowerShellRunnerTests
```

预期：编译失败，`CS0246: 未能找到类型或命名空间名"PowerShellRunner"`。

- [ ] **步骤 3：实现进程出口**

`src/QuickSwitch.Core/Infrastructure/ProcessResult.cs`：

```csharp
namespace QuickSwitch.Core.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
```

`src/QuickSwitch.Core/Infrastructure/ProcessRunner.cs`：

```csharp
using System.Diagnostics;
using System.Text;

namespace QuickSwitch.Core.Infrastructure;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
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
        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(
            process.ExitCode,
            (await stdout.ConfigureAwait(false)).TrimEnd('\r', '\n'),
            (await stderr.ConfigureAwait(false)).TrimEnd('\r', '\n'));
    }
}
```

`src/QuickSwitch.Core/Infrastructure/PowerShellRunner.cs`：

```csharp
namespace QuickSwitch.Core.Infrastructure;

public sealed class PowerShellRunner
{
    /// 子进程若不先改 OutputEncoding，中文系统上会按 GBK 写出，
    /// 而我们按 UTF-8 解码，结果就是乱码。所有脚本前缀这段。
    public const string Preamble = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop';";

    private readonly IProcessRunner _runner;

    public PowerShellRunner(IProcessRunner runner) => _runner = runner;

    public Task<ProcessResult> RunAsync(string script, CancellationToken cancellationToken) =>
        _runner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command", Preamble + script,
            ],
            cancellationToken);
}
```

- [ ] **步骤 4：运行测试验证通过**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~PowerShellRunnerTests
```

预期：`Passed! - Failed: 0, Passed: 4`。若中文用例失败（拿到乱码），说明 preamble 没生效：确认 `Preamble` 在脚本最前、且 `StandardOutputEncoding` 是 UTF-8。

- [ ] **步骤 5：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 集中的进程出口与 UTF-8 管道"
```

---

### 任务 3：`AdminContext` 提权探测

**文件：**
- 创建：`src/QuickSwitch.Core/Infrastructure/AdminContext.cs`
- 测试：`tests/QuickSwitch.Tests/AdminContextTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/QuickSwitch.Tests/AdminContextTests.cs`：

```csharp
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
            TestContext.Current.CancellationToken);

        Assert.True(probe.Succeeded, probe.StandardError);
        Assert.Equal(probe.StandardOutput.Trim().Equals("True", StringComparison.OrdinalIgnoreCase), AdminContext.IsElevated());
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~AdminContextTests
```

预期：编译失败，`CS0103: 名称"AdminContext"不存在`。

- [ ] **步骤 3：实现**

`src/QuickSwitch.Core/Infrastructure/AdminContext.cs`：

```csharp
using System.Security.Principal;

namespace QuickSwitch.Core.Infrastructure;

public static class AdminContext
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~AdminContextTests
```

预期：`Passed! - Failed: 0, Passed: 1`（当前 shell 未提权，两侧都为 `False`，仍然相等）。

- [ ] **步骤 5：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 提权状态探测"
```

---

### 任务 4：`FirewallSwitch`

**文件：**
- 创建：`src/QuickSwitch.Core/Infrastructure/ErrorText.cs`、`src/QuickSwitch.Core/Switches/FirewallSwitch.cs`
- 测试：`tests/QuickSwitch.Tests/FirewallSwitchTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/QuickSwitch.Tests/FirewallSwitchTests.cs`：

```csharp
using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class FirewallSwitchTests
{
    private static FirewallSwitch CreateSwitch() => new(new PowerShellRunner(new ProcessRunner()));

    [Fact]
    public void BuildApplyScript_Enable_UsesBareStringToken()
    {
        var script = FirewallSwitch.BuildApplyScript(SwitchState.On);

        Assert.Equal("Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True", script);
        Assert.DoesNotContain("$true", script);
        Assert.DoesNotContain("$false", script);
    }

    [Fact]
    public void BuildApplyScript_Disable_UsesBareStringToken()
    {
        var script = FirewallSwitch.BuildApplyScript(SwitchState.Off);

        Assert.Equal("Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled False", script);
    }

    [Theory]
    [InlineData(SwitchState.Unknown)]
    [InlineData(SwitchState.Mixed)]
    [InlineData(SwitchState.Blocked)]
    [InlineData(SwitchState.PendingRestart)]
    public void BuildApplyScript_NonBinaryTarget_Throws(SwitchState target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FirewallSwitch.BuildApplyScript(target));
    }

    [Fact]
    public void ReadScript_TargetsAllThreeProfiles()
    {
        Assert.Contains("Get-NetFirewallProfile", FirewallSwitch.ReadScript);
        Assert.Contains("Domain,Private,Public", FirewallSwitch.ReadScript);
    }

    [Fact]
    public async Task ReadAsync_AgainstRealMachine_ReturnsKnownState()
    {
        var result = await CreateSwitch().ReadAsync(TestContext.Current.CancellationToken);

        Assert.Contains(result.State, new[] { SwitchState.On, SwitchState.Off, SwitchState.Mixed });
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task ApplyAsync_CurrentState_IsIdempotentAndReadsBack()
    {
        var firewall = CreateSwitch();
        var before = await firewall.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(SwitchState.Unknown, before.State);

        var apply = await firewall.ApplyAsync(before.State, TestContext.Current.CancellationToken);

        Assert.True(apply.Success, apply.Error);
        var after = await firewall.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before.State, after.State);
    }
}
```

`ApplyAsync_CurrentState_IsIdempotentAndReadsBack` 只写回「当前已经是」的那一档，不改变机器状态，因此未提权也能跑（本机实测 `Set-NetFirewallProfile` 写入相同值时非提权返回成功）。

- [ ] **步骤 2：运行测试验证失败**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~FirewallSwitchTests
```

预期：编译失败，`CS0246: 未能找到类型"FirewallSwitch"`。

- [ ] **步骤 3：实现**

`src/QuickSwitch.Core/Infrastructure/ErrorText.cs`：

```csharp
namespace QuickSwitch.Core.Infrastructure;

public static class ErrorText
{
    public const string Fallback = "命令执行失败，且未返回错误信息。";

    public static string FirstLine(string? text, string fallback = Fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        return text
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? fallback;
    }
}
```

`src/QuickSwitch.Core/Switches/FirewallSwitch.cs`：

```csharp
using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class FirewallSwitch : ISwitch
{
    public const string ReadScript =
        "(Get-NetFirewallProfile -Profile Domain,Private,Public | ForEach-Object { '{0}={1}' -f $_.Name, $_.Enabled }) -join ';'";

    private readonly PowerShellRunner _powerShell;

    public FirewallSwitch(PowerShellRunner powerShell) => _powerShell = powerShell;

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "firewall",
        Group: SwitchGroup.Security,
        Title: "Windows 防火墙",
        Subtitle: "域 / 专用 / 公用 三档统一开关");

    /// 必须传裸字符串标记 True/False：$true 会被 PowerShell 绑成 System.Boolean，
    /// 而 -Enabled 参数类型是 GpoBoolean，会抛
    /// "Invalid cast from 'System.Boolean' to GpoBoolean"（本机已验证）。
    public static string BuildApplyScript(SwitchState target)
    {
        var flag = target switch
        {
            SwitchState.On => "True",
            SwitchState.Off => "False",
            _ => throw new ArgumentOutOfRangeException(
                nameof(target), target, "防火墙只接受 On 或 Off。"),
        };

        return $"Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled {flag}";
    }

    public async Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await _powerShell.RunAsync(ReadScript, cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? FirewallStateParser.Parse(result.StandardOutput)
            : new SwitchReadResult(SwitchState.Unknown, ErrorText.FirstLine(result.StandardError));
    }

    public async Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var script = BuildApplyScript(target);
        var result = await _powerShell.RunAsync(script, cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? SwitchApplyResult.Ok()
            : SwitchApplyResult.Fail(ErrorText.FirstLine(result.StandardError));
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~FirewallSwitchTests
```

预期：`Passed! - Failed: 0, Passed: 9`（4 个 Theory 用例展开算 4 个）。本机防火墙三档均为 OFF，因此 `ReadAsync` 得到 `Off` + `域 / 专用 / 公用 三档全部关闭`。

- [ ] **步骤 5：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 防火墙开关（读、写、权威回读）"
```

---

### 任务 5：`SwitchRegistry`

**文件：**
- 创建：`src/QuickSwitch.Core/Switches/SwitchRegistry.cs`
- 测试：`tests/QuickSwitch.Tests/SwitchRegistryTests.cs`

- [ ] **步骤 1：编写失败的测试**

`tests/QuickSwitch.Tests/SwitchRegistryTests.cs`：

```csharp
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
```

- [ ] **步骤 2：运行测试验证失败**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~SwitchRegistryTests
```

预期：编译失败，`CS0246: 未能找到类型"SwitchRegistry"`。

- [ ] **步骤 3：实现**

`src/QuickSwitch.Core/Switches/SwitchRegistry.cs`：

```csharp
using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class SwitchRegistry
{
    public SwitchRegistry(IEnumerable<ISwitch> switches)
    {
        var items = switches.ToArray();
        var duplicate = items
            .GroupBy(item => item.Descriptor.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"开关 Id 重复：{duplicate.Key}", nameof(switches));

        All = items;
    }

    public IReadOnlyList<ISwitch> All { get; }

    /// 显式 new，不用反射：加开关就是加一行，编译期就能发现名字写错。
    public static SwitchRegistry CreateDefault(PowerShellRunner powerShell) =>
        new([new FirewallSwitch(powerShell)]);
}
```

- [ ] **步骤 4：运行测试验证通过**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~SwitchRegistryTests
```

预期：`Passed! - Failed: 0, Passed: 3`。

- [ ] **步骤 5：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 开关注册表"
```

---

### 任务 6：卡片 ViewModel 与主 ViewModel

**文件：**
- 创建：`src/QuickSwitch.Core/ViewModels/SwitchCardViewModel.cs`、`src/QuickSwitch.Core/ViewModels/MainViewModel.cs`
- 测试：`tests/QuickSwitch.Tests/Fakes/FakeSwitch.cs`、`SwitchCardViewModelTests.cs`、`MainViewModelTests.cs`

- [ ] **步骤 1：编写测试替身与失败的测试**

`tests/QuickSwitch.Tests/Fakes/FakeSwitch.cs`：

```csharp
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests.Fakes;

internal sealed class FakeSwitch : ISwitch
{
    public SwitchDescriptor Descriptor { get; init; } = new("fake", SwitchGroup.Security, "假开关", "原始副标题");

    public SwitchState NextReadState { get; set; } = SwitchState.Off;

    public string? NextReadDetail { get; set; }

    public SwitchApplyResult ApplyResult { get; set; } = SwitchApplyResult.Ok();

    public SwitchState? LastTarget { get; private set; }

    public int ReadCount { get; private set; }

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        ReadCount++;
        return Task.FromResult(new SwitchReadResult(NextReadState, NextReadDetail));
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        LastTarget = target;
        if (ApplyResult.Success) NextReadState = target;
        return Task.FromResult(ApplyResult);
    }
}
```

`tests/QuickSwitch.Tests/SwitchCardViewModelTests.cs`：

```csharp
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class SwitchCardViewModelTests
{
    [Fact]
    public async Task RefreshAsync_AdoptsStateAndDetail()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Mixed, NextReadDetail = "已开启：域 / 公用" };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Mixed, card.State);
        Assert.Equal("已开启：域 / 公用", card.Subtitle);
        Assert.False(card.IsOn);
    }

    [Fact]
    public async Task RefreshAsync_WithoutDetail_FallsBackToDescriptorSubtitle()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.On, NextReadDetail = null };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.True(card.IsOn);
        Assert.Equal("原始副标题", card.Subtitle);
    }

    [Fact]
    public async Task ToggleAsync_WhenOff_TargetsOnAndAdoptsReadBack()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Off };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, fake.LastTarget);
        Assert.Equal(SwitchState.On, card.State);
        Assert.True(card.IsOn);
        Assert.False(card.IsBusy);
    }

    [Fact]
    public async Task ToggleAsync_WhenMixed_TargetsOn()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Mixed };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.On, fake.LastTarget);
    }

    [Fact]
    public async Task ToggleAsync_WhenApplyFails_KeepsStateAndShowsRawError()
    {
        var fake = new FakeSwitch
        {
            NextReadState = SwitchState.Off,
            ApplyResult = SwitchApplyResult.Fail("The requested operation requires elevation (Run as administrator)."),
        };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        await card.ToggleAsync();

        Assert.Equal(SwitchState.Off, card.State);
        Assert.False(card.IsOn);
        Assert.Equal("操作失败：The requested operation requires elevation (Run as administrator).", card.Subtitle);
    }

    [Fact]
    public async Task ToggleAsync_WhenStateUnknown_DoesNothing()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Unknown };
        var card = new SwitchCardViewModel(fake);
        await card.RefreshAsync(CancellationToken.None);

        Assert.False(card.CanToggle);

        await card.ToggleAsync();

        Assert.Null(fake.LastTarget);
    }

    [Fact]
    public async Task RefreshAsync_WhenFailingRead_ShowsErrorAsSubtitle()
    {
        var fake = new FakeSwitch { NextReadState = SwitchState.Unknown, NextReadDetail = "无法解析防火墙状态" };
        var card = new SwitchCardViewModel(fake);

        await card.RefreshAsync(CancellationToken.None);

        Assert.Equal("无法解析防火墙状态", card.Subtitle);
    }
}
```

`tests/QuickSwitch.Tests/MainViewModelTests.cs`：

```csharp
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Constructor_ExposesCardsInOrder()
    {
        var viewModel = new MainViewModel(
            [new SwitchCardViewModel(new FakeSwitch()), new SwitchCardViewModel(new FakeSwitch())],
            isElevated: true);

        Assert.Equal(2, viewModel.Cards.Count);
        Assert.True(viewModel.IsElevated);
    }

    [Fact]
    public void ElevationBadge_ReflectsElevation()
    {
        var elevated = new MainViewModel([], isElevated: true);
        var plain = new MainViewModel([], isElevated: false);

        Assert.Equal("管理员模式", elevated.ElevationBadge);
        Assert.Equal("未提权：部分开关不可用", plain.ElevationBadge);
    }

    [Fact]
    public async Task RefreshAllAsync_RefreshesEveryCard()
    {
        var first = new FakeSwitch { NextReadState = SwitchState.On };
        var second = new FakeSwitch { NextReadState = SwitchState.Off };
        var viewModel = new MainViewModel(
            [new SwitchCardViewModel(first), new SwitchCardViewModel(second)],
            isElevated: true);

        await viewModel.RefreshAllCommand.ExecuteAsync(null);

        Assert.Equal(1, first.ReadCount);
        Assert.Equal(1, second.ReadCount);
        Assert.True(viewModel.Cards[0].IsOn);
        Assert.False(viewModel.Cards[1].IsOn);
        Assert.False(viewModel.IsRefreshing);
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj --filter FullyQualifiedName~ViewModelTests
```

预期：编译失败，`CS0246: 未能找到类型"SwitchCardViewModel"`。

- [ ] **步骤 3：实现**

`src/QuickSwitch.Core/ViewModels/SwitchCardViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Core.ViewModels;

public sealed partial class SwitchCardViewModel : ObservableObject
{
    private readonly ISwitch _switch;
    private SwitchState _state = SwitchState.Unknown;

    public SwitchCardViewModel(ISwitch @switch)
    {
        _switch = @switch;
        title = @switch.Descriptor.Title;
        group = @switch.Descriptor.Group;
        subtitle = @switch.Descriptor.Subtitle;
    }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string group;

    [ObservableProperty]
    private string subtitle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isOn;

    public SwitchDescriptor Descriptor => _switch.Descriptor;

    public SwitchState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value)) ToggleCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanToggle => !IsBusy && State is not (SwitchState.Unknown or SwitchState.Blocked);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var read = await _switch.ReadAsync(cancellationToken).ConfigureAwait(true);
        State = read.State;
        IsOn = read.State == SwitchState.On;
        Subtitle = read.Detail ?? Descriptor.Subtitle;
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    public async Task ToggleAsync()
    {
        // 命令本身受 CanExecute 保护，但测试会直接调用本方法，守卫不能只靠命令层。
        if (!CanToggle) return;

        var target = State == SwitchState.On ? SwitchState.Off : SwitchState.On;

        IsBusy = true;
        try
        {
            var apply = await _switch.ApplyAsync(target, CancellationToken.None).ConfigureAwait(true);
            var read = await _switch.ReadAsync(CancellationToken.None).ConfigureAwait(true);

            State = read.State;
            IsOn = read.State == SwitchState.On;
            Subtitle = apply.Success
                ? read.Detail ?? Descriptor.Subtitle
                : $"操作失败：{apply.Error ?? ErrorTextFallback}";
        }
        finally
        {
            IsBusy = false;
            // ToggleButton 点击时会把 IsChecked 写成局部值。显式重发通知，
            // 让 OneWay 绑定把权威状态重新推回视觉层；操作失败时开关自动弹回。
            OnPropertyChanged(nameof(IsOn));
        }
    }

    private const string ErrorTextFallback = "未知错误";
}
```

`src/QuickSwitch.Core/ViewModels/MainViewModel.cs`：

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuickSwitch.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(IEnumerable<SwitchCardViewModel> cards, bool isElevated)
    {
        Cards = new ObservableCollection<SwitchCardViewModel>(cards);
        IsElevated = isElevated;
    }

    public ObservableCollection<SwitchCardViewModel> Cards { get; }

    public bool IsElevated { get; }

    public string ElevationBadge => IsElevated ? "管理员模式" : "未提权：部分开关不可用";

    [ObservableProperty]
    private bool isRefreshing;

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsRefreshing) return;

        IsRefreshing = true;
        try
        {
            foreach (var card in Cards)
                await card.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj
```

预期：全部用例 `Passed! - Failed: 0`。若源生成器没生成 `ToggleCommand`，检查 `CommunityToolkit.Mvvm` 包是否已还原、类是否为 `partial`。

- [ ] **步骤 5：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 卡片与主视图模型（busy、权威回读、错误呈现）"
```

---

### 任务 7：WPF 宿主、manifest 提权、启动接线

**文件：**
- 修改：`src/QuickSwitch/app.manifest`、`src/QuickSwitch/App.xaml`、`src/QuickSwitch/App.xaml.cs`、`src/QuickSwitch/MainWindow.xaml`、`src/QuickSwitch/MainWindow.xaml.cs`

- [ ] **步骤 1：替换 `app.manifest`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="QuickSwitch.app" />

  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>

  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 / 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **步骤 2：`App.xaml`（去掉 `StartupUri`）**

```xml
<Application x:Class="QuickSwitch.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Styles/ToggleSwitch.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **步骤 3：`App.xaml.cs`**

```csharp
using System.Windows;
using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;

namespace QuickSwitch;

public partial class App : Application
{
    private TrayHost? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var powerShell = new PowerShellRunner(new ProcessRunner());
        var registry = SwitchRegistry.CreateDefault(powerShell);
        var cards = registry.All.Select(item => new SwitchCardViewModel(item));
        var viewModel = new MainViewModel(cards, AdminContext.IsElevated());

        _window = new MainWindow { DataContext = viewModel };
        _tray = new TrayHost(_window);

        MainWindow = _window;
        _window.Show();
        _ = viewModel.RefreshAllCommand.ExecuteAsync(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **步骤 4：临时最简 `MainWindow`（先证明能起、能提权、能读出状态）**

`src/QuickSwitch/MainWindow.xaml`：

```xml
<Window x:Class="QuickSwitch.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Windows 快捷开关" Height="200" Width="420">
    <StackPanel Margin="16">
        <TextBlock Text="{Binding ElevationBadge}" FontSize="14" />
        <TextBlock Text="{Binding Cards.Count, StringFormat=开关数量：{0}}" Margin="0,8,0,0" />
    </StackPanel>
</Window>
```

`src/QuickSwitch/MainWindow.xaml.cs`：

```csharp
using System.ComponentModel;
using System.Windows;

namespace QuickSwitch;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose) return;

        e.Cancel = true;
        Hide();
    }
}
```

- [ ] **步骤 5：临时占位 `TrayHost`（本步骤只为让 App 编译通过，任务 9 补全）**

`src/QuickSwitch/TrayHost.cs`：

```csharp
namespace QuickSwitch;

internal sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;

    public TrayHost(MainWindow window) => _window = window;

    public void Dispose()
    {
        _window.AllowClose = true;
    }
}
```

- [ ] **步骤 6：构建并手动验证提权**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet build src/QuickSwitch/QuickSwitch.csproj
dotnet run --project src/QuickSwitch/QuickSwitch.csproj
```

预期：弹出 UAC 授权框（标题带 `QuickSwitch`）；同意后出现窗口，第一行显示 `管理员模式`，第二行显示 `开关数量：1`。随后关掉窗口。

- [ ] **步骤 7：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: WPF 宿主与 manifest 整体提权"
```

---

### 任务 8：分组卡片 UI + 胶囊开关样式

**文件：**
- 创建：`src/QuickSwitch/Styles/ToggleSwitch.xaml`
- 修改：`src/QuickSwitch/MainWindow.xaml`

- [ ] **步骤 1：写样式字典**

`src/QuickSwitch/Styles/ToggleSwitch.xaml`：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Style x:Key="SwitchToggleStyle" TargetType="ToggleButton">
        <Setter Property="Width" Value="46" />
        <Setter Property="Height" Value="24" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Focusable" Value="False" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Grid>
                        <Border x:Name="Track" CornerRadius="12" Background="#FF3A3A42" />
                        <Border x:Name="Thumb" Width="18" Height="18" CornerRadius="9"
                                Background="#FFEDEDF2" HorizontalAlignment="Left" Margin="3,0,0,0" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Track" Property="Background" Value="#FF3B82F6" />
                            <Setter TargetName="Thumb" Property="HorizontalAlignment" Value="Right" />
                            <Setter TargetName="Thumb" Property="Margin" Value="0,0,3,0" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

</ResourceDictionary>
```

- [ ] **步骤 2：写主窗口 UI**

`src/QuickSwitch/MainWindow.xaml`：

```xml
<Window x:Class="QuickSwitch.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:QuickSwitch.Core.ViewModels;assembly=QuickSwitch.Core"
        xmlns:conv="clr-namespace:QuickSwitch.Converters"
        Title="Windows 快捷开关" Height="560" Width="520"
        Background="#FF17171A" TextOptions.TextFormattingMode="Display"
        UseLayoutRounding="True">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisibility" />

        <CollectionViewSource x:Key="GroupedCards" Source="{Binding Cards}">
            <CollectionViewSource.GroupDescriptions>
                <PropertyGroupDescription PropertyName="Group" />
            </CollectionViewSource.GroupDescriptions>
        </CollectionViewSource>

        <DataTemplate x:Key="CardTemplate" DataType="{x:Type vm:SwitchCardViewModel}">
            <Border Background="#FF24242A" CornerRadius="8" Padding="14,12" Margin="0,0,0,8">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column="0" Margin="0,0,12,0">
                        <TextBlock Text="{Binding Title}" FontSize="15" Foreground="#FFF2F2F5" />
                        <TextBlock Text="{Binding Subtitle}" FontSize="12" Foreground="#FF9A9AA5"
                                   TextWrapping="Wrap" Margin="0,4,0,0" />
                        <TextBlock Text="处理中…" FontSize="12" Foreground="#FF6FA8FF" Margin="0,4,0,0"
                                   Visibility="{Binding IsBusy, Converter={StaticResource BoolToVisibility}}" />
                    </StackPanel>

                    <ToggleButton Grid.Column="1" VerticalAlignment="Center"
                                  Style="{StaticResource SwitchToggleStyle}"
                                  IsChecked="{Binding IsOn, Mode=OneWay}"
                                  Command="{Binding ToggleCommand}" />
                </Grid>
            </Border>
        </DataTemplate>
    </Window.Resources>

    <DockPanel>
        <Border DockPanel.Dock="Top" Background="#FF1F1F24" Padding="16,12">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0">
                    <TextBlock Text="Windows 快捷开关" FontSize="18" Foreground="#FFF7F7FA" />
                    <TextBlock Text="{Binding ElevationBadge}" FontSize="12" Foreground="#FF8A8A95"
                               Margin="0,4,0,0" />
                </StackPanel>
                <Button Grid.Column="1" Content="刷新" Padding="12,6" VerticalAlignment="Center"
                        Command="{Binding RefreshAllCommand}"
                        IsEnabled="{Binding IsRefreshing, Converter={StaticResource InverseBoolConverter}}" />
            </Grid>
        </Border>

        <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="16,12">
            <ItemsControl ItemsSource="{Binding Source={StaticResource GroupedCards}}"
                          ItemTemplate="{StaticResource CardTemplate}">
                <ItemsControl.GroupStyle>
                    <GroupStyle>
                        <GroupStyle.HeaderTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Name}" FontSize="13" FontWeight="SemiBold"
                                           Foreground="#FF9A9AA5" Margin="0,10,0,6" />
                            </DataTemplate>
                        </GroupStyle.HeaderTemplate>
                    </GroupStyle>
                </ItemsControl.GroupStyle>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```

「刷新」按钮用了 `InverseBoolConverter` —— WPF 没有内置的反向布尔转换器，必须自己写一个，放 `src/QuickSwitch/Converters/InverseBoolConverter.cs`（命名空间 `QuickSwitch.Converters`，Core 保持无 WPF 依赖）：

```csharp
using System.Globalization;
using System.Windows.Data;

namespace QuickSwitch.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
```

并在 `MainWindow.xaml` 的 `Window` 开始标签加一个命名空间映射 `xmlns:conv="clr-namespace:QuickSwitch.Converters"`，在 `Window.Resources` 中 `BooleanToVisibilityConverter` 之后插入：

```xml
        <conv:InverseBoolConverter x:Key="InverseBoolConverter" />
```

- [ ] **步骤 3：构建**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet build QuickSwitch.sln
```

预期：`Build succeeded`，0 error。

- [ ] **步骤 4：手动验证 UI 与真实读写**

```powershell
cd 'D:\乱搞\Windows快捷开关'
netsh advfirewall show allprofiles state
dotnet run --project src/QuickSwitch/QuickSwitch.csproj
```

预期（UAC 同意后）：
1. 窗口出现，分组标题 `安全`，卡片标题 `Windows 防火墙`，副标题 `域 / 专用 / 公用 三档全部关闭`（与上面 `netsh` 输出的 `State OFF` 一致），左侧胶囊开关处于关闭位。
2. 点一下胶囊：短暂 `处理中…` → 开关变蓝、副标题变 `域 / 专用 / 公用 三档全部开启`。
3. 另开一个提权 PowerShell 执行 `netsh advfirewall show allprofiles state`，三档均为 `ON`。
4. 再点一下 → 副标题回到 `…三档全部关闭`，`netsh` 三档均 `OFF`。
5. 点 `刷新`：按钮短暂禁用，状态与系统一致。

若分组标题不渲染（`CollectionViewSource` 在资源里没拿到 DataContext），改用代码后置——在 `src/QuickSwitch/MainWindow.xaml.cs` 顶部加 `using QuickSwitch.Core.ViewModels;`，并重写：

```csharp
protected override void OnDataContextChanged(DependencyPropertyChangedEventArgs e)
{
    base.OnDataContextChanged(e);

    if (e.NewValue is MainViewModel viewModel)
        CollectionViewSource.GetDefaultView(viewModel.Cards).GroupDescriptions
            .Add(new System.ComponentModel.PropertyGroupDescription(nameof(SwitchCardViewModel.Group)));
}
```

同时把 `ItemsControl` 的 `ItemsSource` 改成 `{Binding Cards}`，并删掉 `Window.Resources` 里的 `CollectionViewSource`。

- [ ] **步骤 5：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 分组卡片列表与胶囊开关样式"
```

---

### 任务 9：托盘宿主（常驻、关闭即隐藏、退出）

**文件：**
- 修改：`src/QuickSwitch/TrayHost.cs`

- [ ] **步骤 1：实现完整托盘宿主**

`src/QuickSwitch/TrayHost.cs`：

```csharp
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace QuickSwitch;

internal sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;

    public TrayHost(MainWindow window)
    {
        _window = window;

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("显示 / 隐藏", null, (_, _) => ToggleWindow());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Windows 快捷开关",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleWindow();
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        _window.AllowClose = true;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
```

- [ ] **步骤 2：构建**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet build QuickSwitch.sln
```

预期：`Build succeeded`，0 error。若 `Application` 有歧义，确认 `using Forms = System.Windows.Forms;` 是别名形式、`using System.Windows;` 是普通形式。

- [ ] **步骤 3：手动验证托盘行为**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet run --project src/QuickSwitch/QuickSwitch.csproj
```

预期：
1. UAC 同意后窗口出现；任务栏通知区域出现盾牌图标。
2. 点窗口右上角关闭 → 窗口消失，**进程仍在**（`Get-Process QuickSwitch` 有结果），托盘图标仍在。
3. 双击托盘图标 → 窗口回来；右键托盘 → `显示 / 隐藏` 也能切换。
4. 右键托盘 → `退出` → 窗口与托盘图标都消失，`Get-Process QuickSwitch` 报 `找不到进程`。

- [ ] **步骤 4：Commit**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "feat: 托盘常驻、关闭即隐藏、退出清理"
```

---

### 任务 10：单文件发布、README 补充、推送远端

**文件：**
- 修改：`README.md`

- [ ] **步骤 1：发布单文件**

```powershell
cd 'D:\乱搞\Windows快捷开关'
dotnet publish src/QuickSwitch/QuickSwitch.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
Get-ChildItem src/QuickSwitch/bin/Release/net10.0-windows/win-x64/publish/QuickSwitch.exe | Select-Object Name, Length
```

预期：存在 `QuickSwitch.exe`（自包含单文件，约 100–160 MB 属正常）。

- [ ] **步骤 2：手动验证发布产物**

```powershell
cd 'D:\乱搞\Windows快捷开关'
.\src\QuickSwitch\bin\Release\net10.0-windows\win-x64\publish\QuickSwitch.exe
```

预期：UAC 授权框出现（说明 manifest 进了单文件产物），窗口显示 `管理员模式`，防火墙卡片可正常开关。

- [ ] **步骤 3：README 补「构建与运行」**

把 `README.md` 中「## 状态」小节替换为：

```markdown
## 状态

M0 + M1 已实现：托盘常驻、整体提权、分组卡片列表、防火墙三档开关（含权威回读）。
其余八个开关按 M2–M4 逐步接入。

## 构建与运行

```powershell
# 单测（无需管理员权限）
dotnet test QuickSwitch.sln

# 调试运行（会弹 UAC —— manifest 声明 requireAdministrator）
dotnet run --project src/QuickSwitch/QuickSwitch.csproj

# 单文件自包含发布
dotnet publish src/QuickSwitch/QuickSwitch.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

产物：`src/QuickSwitch/bin/Release/net10.0-windows/win-x64/publish/QuickSwitch.exe`

注意：`Set-NetFirewallProfile -Enabled` 必须传裸字符串 `True`/`False`；
传 `$true` 会抛 `Invalid cast from 'System.Boolean' to GpoBoolean`。
```

（README 里嵌套代码块时，最外层用四个反引号。）

- [ ] **步骤 4：Commit 并推送**

```powershell
cd 'D:\乱搞\Windows快捷开关'
git add -A
git commit -m "docs: 补充构建运行说明与已知坑；发布 M0+M1"
$env:GIT_TERMINAL_PROMPT='0'
git push origin main
git --no-pager log --oneline -12
```

预期：`git push` 退出码 0，`origin/main` 指向最新提交。

---

## 验收标准（M0 + M1 完成）

- [ ] `dotnet build QuickSwitch.sln` 0 error。
- [ ] `dotnet test QuickSwitch.sln` 全绿，且不要求管理员权限。
- [ ] 双击发布的 `QuickSwitch.exe`：弹一次 UAC，窗口显示 `管理员模式`。
- [ ] `安全` 分组下只有一张 `Windows 防火墙` 卡片，副标题与 `netsh advfirewall show allprofiles state` 一致。
- [ ] 点胶囊能真实开/关三档防火墙，副标题跟随权威回读更新。
- [ ] 关闭窗口进程不退出；托盘双击可唤回；托盘 `退出` 才真正结束进程。
- [ ] 任何失败都不吞异常：错误原文出现在卡片副标题行。

## 已知不覆盖（留给后续计划）

- 提权归属守卫（比对 explorer.exe 令牌 SID）→ M2。
- `IChoiceSwitch`（电源计划三选一）、`RequiresRestart`/`IsDestructive`/`ConfirmText` → M3/M4。
- 代理、剪贴板、休眠、快速启动、实时防护、功能组件、UAC → M2/M3/M4。
- Get-NetFirewallProfile 不可用时的 netsh 回退路径 → 暂不做，失败直接以原文呈现为 `Unknown`。
