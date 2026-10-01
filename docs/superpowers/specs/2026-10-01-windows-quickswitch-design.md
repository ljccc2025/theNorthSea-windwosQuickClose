# Windows 快捷开关 — 设计规格

- 日期：2026-10-01
- 状态：已批准，待实现
- 仓库：https://github.com/ljccc2025/theNorthSea-windwosQuickClose.git
- 工作目录：`D:\乱搞\Windows快捷开关`

## 1. 目标与范围

在 Windows 11 上提供一个常驻托盘的控制面板，用一个开关控制一类系统设置的开/关。

**v1 覆盖的九个开关，界面上归入四个分组（安全 / 网络 / 电源 / 系统）：**

1. 防火墙（域 / 专用 / 公用 三档统一）
2. 实时防护（Microsoft Defender 实时监控）
3. UAC（用户账户控制）
4. 休眠
5. 快速启动
6. 电源计划（高性能 / 平衡 / 节能）
7. 系统代理
8. 剪贴板历史
9. Windows 功能组件（Hyper-V / WSL / VirtualMachinePlatform）

**明确排除（YAGNI）：**

- 网卡启用/禁用（断网风险，未选入）
- DNS 切换、系统还原点（未选入）
- 遥测等级、Windows Update 服务（官方不推荐硬碰，未选入）
- 开机自启（v1 不做；将来用任务计划程序 + "以最高权限运行" 实现零弹窗自启）
- 定时轮询状态刷新

**成功标准：**

- 双击 exe → 一次 UAC → 托盘图标常驻，窗口可开可收。
- 九个开关均可在 UI 上切换，状态来自权威回读，不是本地缓存。
- 任何写失败都把系统原文错误呈现给用户，程序不崩溃。
- 增加第十个开关只需新增一个类 + 一行注册，UI 与进程层零改动。

## 2. 技术选型（已决策）

| 维度 | 决定 | 理由 |
|---|---|---|
| 运行时 | .NET 10 + WPF（`net10.0-windows`） | 本机已装 SDK 10.0.300；COM / 注册表 / 进程调用无隔层；`app.manifest` 一行搞定提权 |
| 托盘 | WinForms `NotifyIcon`（`<UseWindowsForms>true</UseWindowsForms>`） | WPF 无原生托盘 API，混 WinForms 是该组合的标准做法 |
| 依赖 | 仅 `CommunityToolkit.Mvvm` | 源生成器提供 `[ObservableProperty]` / `[RelayCommand]`，消除全部样板。可替换为手写 `ObservableObject`（约 20 行），若需零依赖 |
| 提权 | `app.manifest` 的 `requireAdministrator` | 启动一次 UAC，九个开关全程零弹窗 |
| 发布 | `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true` | 单文件 exe，目标机免装运行时 |
| 测试 | xunit，`ISwitch` 实现全部按接口注入 | 单测覆盖委派逻辑与解析，不碰真机 |

**被否决的方案及原因：**

- **WinUI 3**：Fluent 观感最好，但拖入 Windows App SDK 依赖、打包流程变复杂、AOT 不友好；对接九个开关属于过度工程。
- **Python + PySide6**：写得快，但提权要走 `ctypes.windll.shell32.ShellExecuteW(..., "runas", ...)`，弹窗控制与错误回传难看；成品需 PyInstaller；启动慢，不适合常驻托盘。
- **C++ / Win32 + 直连 COM**：零依赖、体积最小，但代码量约三倍，"以后加开关"的边际成本是负资产。
- **托盘常驻 + 按需提权 helper**：启动零弹窗，但每次写操作要拉提权子进程 + 命名管道 IPC，多一套进程模型与错误通道；用三分复杂度换一次弹窗，不划算。
- **单窗口用完即走**：代码最少，但每次切换都要开程序，与"快捷开关"的产品定位冲突。

## 3. 形态与权限模型（已决策）

- 托盘常驻 + 启动即整体提权（`requireAdministrator`）。
- 窗口关闭 = 收进托盘（拦截 `Closing`，`e.Cancel = true` + `Hide()`）；真正退出走托盘右键菜单。
- 托盘左键：显示/激活窗口。托盘右键：`常用开关`（v1 只放防火墙）+ `打开面板` + `退出`。

**已知代价（必须处理）：** 若用户是标准账户，用**另一个管理员账户的凭据**提升，则 `HKCU` 指向那个管理员的 hive，代理与剪贴板两个用户级开关会写错位置。见 §8 提权归属守卫。

## 4. 目录结构

```
D:\乱搞\Windows快捷开关\
  QuickSwitch.sln
  README.md
  .gitignore
  docs\
    superpowers\specs\2026-10-01-windows-quickswitch-design.md
    manual-verification.md            # 真机验收清单（M4 产出）
  src\QuickSwitch\
    QuickSwitch.csproj
    app.manifest
    App.xaml / App.xaml.cs            # 启动、托盘引导、全局异常兜底
    Views\MainWindow.xaml(.cs)        # 分组卡片列表
    Views\SwitchCard.xaml(.cs)        # UserControl：布尔开关卡片
    Views\ChoiceCard.xaml(.cs)        # UserControl：多选开关卡片
    ViewModels\MainViewModel.cs
    ViewModels\SwitchCardViewModel.cs
    Model\ISwitch.cs                  # ISwitch / IToggleSwitch / IChoiceSwitch
    Model\SwitchState.cs
    Model\SwitchDescriptor.cs
    Model\SwitchRegistry.cs           # 声明式注册，显式构造，不用反射
    Switches\FirewallSwitch.cs
    Switches\HibernateSwitch.cs
    Switches\FastStartupSwitch.cs
    Switches\PowerPlanSwitch.cs
    Switches\ProxySwitch.cs
    Switches\ClipboardHistorySwitch.cs
    Switches\DefenderRealtimeSwitch.cs
    Switches\WindowsFeatureSwitch.cs  # 一类多实例
    Switches\UacSwitch.cs
    Services\ProcessRunner.cs         # 唯一的进程出口
    Services\RegistryService.cs
    Services\FirewallCom.cs           # INetFwPolicy2 late-bound 封装
    Services\ElevationGuard.cs
    Services\TrayService.cs
  tests\QuickSwitch.Tests\
```

程序集名与命名空间用 `QuickSwitch`（英文），仓库目录保留中文名。

## 5. 核心抽象

```csharp
public enum SwitchState { Unknown, On, Off, Mixed, Blocked, PendingRestart }

public sealed record SwitchDescriptor(
    string Id,
    string Group,            // 安全 / 网络 / 电源 / 系统
    string Title,
    string Subtitle,         // 静态提示文案
    bool RequiresRestart,
    bool IsDestructive,
    string? ConfirmText);    // 非 null 时点击需二次确认

public sealed record SwitchReadResult(SwitchState State, string? Detail);

public sealed record SwitchWriteResult(bool Ok, string? Detail, bool NeedsRestart);

public interface ISwitch
{
    SwitchDescriptor Descriptor { get; }
    Task<SwitchReadResult> ReadAsync(CancellationToken ct);
    Task<SwitchWriteResult> WriteAsync(bool target, CancellationToken ct);
}

public interface IToggleSwitch : ISwitch { }

public interface IChoiceSwitch : ISwitch
{
    IReadOnlyList<string> Options { get; }
    Task<string> ReadSelectedAsync(CancellationToken ct);
    Task<SwitchWriteResult> SelectAsync(string option, CancellationToken ct);
}
```

**为什么现在就需要 `IChoiceSwitch`：** 电源计划不是布尔量，是三选一。硬塞进布尔接口就要写一个假的"开 = 高性能 / 关 = 平衡"映射，那是谎。代价是一个接口 + 一个 `DataTemplateSelector`，收益是把这个坑立刻填掉。

**注册表显式化：** `SwitchRegistry.CreateAll()` 里显式 `new` 每个开关。不用反射扫描程序集——反射会挡住裁剪/AOT，且编译器不再检查开关类是否实现完整。

**注册表自检：** 构造时校验 `Id` 唯一、`Group` 非空、每个 `IsDestructive` 开关必须带 `ConfirmText`；违反直接抛异常（快速失败，别让 UI 带着坏数据起来）。

## 6. 九个开关的实现路径

| 开关 | 读取 | 写入 | 管理员 | 重启 |
|---|---|---|---|---|
| 防火墙 | late-bound COM `HNetCfg.FwPolicy2`，读 `FirewallEnabled[DOMAIN\|PRIVATE\|PUBLIC]` 三档聚合为 On / Off / **Mixed** | 同一 COM 属性写三档 | 是 | 否 |
| 休眠 | 注册表 `HKLM\SYSTEM\CurrentControlSet\Control\Power\HibernateEnabled` (DWORD) | `powercfg /hibernate on\|off` | 是 | 否 |
| 快速启动 | 注册表 `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled` (DWORD) | 同键写 DWORD | 是 | 否 |
| 电源计划 | `powercfg /getactivescheme` 解析 GUID | `powercfg /list` 动态枚举 → `powercfg /setactive <guid>` | 是 | 否 |
| 系统代理 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings` 的 `ProxyEnable` (DWORD) / `ProxyServer` (SZ) | 写键 + P/Invoke `InternetSetOption` 双发（`INTERNET_OPTION_SETTINGS_CHANGED` 与 `INTERNET_OPTION_REFRESH`） | 否 | 否 |
| 剪贴板历史 | `HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory` (DWORD) | 写键 + 广播 `WM_SETTINGCHANGE` | 否 | 否 |
| 实时防护 | PowerShell `(Get-MpComputerStatus).RealTimeProtectionEnabled` | `Set-MpPreference -DisableRealtimeMonitoring`；**篡改防护开启时会被拒绝** → 捕获为 `Blocked` 并回显系统原文 | 是 | 否 |
| Windows 功能组件 | PowerShell `Get-WindowsOptionalFeature -Online -FeatureName <name>`（对象输出，避开 `dism` 的中文代码页问题） | `Enable/Disable-WindowsOptionalFeature -Online -FeatureName <name> -NoRestart` | 是 | **是** |
| UAC | 注册表 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\EnableLUA` (DWORD) | 同键写 DWORD | 是 | **是** |

**电源计划 GUID 不硬编码为唯一来源：** 优先按 `powercfg /list` 的名字匹配（需同时容忍中文与英文资源名），GUID 作为兜底常量：高性能 `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`、平衡 `381b4222-f694-41f0-9685-ff5bb260df2e`、节能 `a1841308-3541-4fab-bc81-f71556f20b4a`。

**Windows 功能组件做成"一类多实例"：** 注册表里一条配置 = 一个功能实例，v1 默认 `Microsoft-Hyper-V-All`、`Microsoft-Windows-Subsystem-Linux`、`VirtualMachinePlatform`。加功能只改配置，不改代码。

**依赖关系提示（仅展示，不强制）：** 关闭休眠会删除 `hiberfil.sys` 并使快速启动失效；快速启动卡在休眠关闭时应显示提示文案。

**GPO / 策略托管检测：** 相应策略键存在（如 `HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall`、`HKLM\SOFTWARE\Policies\Microsoft\Windows\System`）→ 该卡置 `Blocked`，副标题写"由组策略管理"。

## 7. 进程出口集中化

所有 `powercfg` / `powershell.exe` 调用只经过 `ProcessRunner`：

- `ProcessStartInfo`：`UseShellExecute = false`、`CreateNoWindow = true`、`RedirectStandardOutput = true`、`RedirectStandardError = true`。
- **编码（中文系统上最容易炸的一环）**：PowerShell 命令前缀注入 `[Console]::OutputEncoding=[Text.Encoding]::UTF8`，stdout/stderr 按 UTF-8 读取。不统一编码时 `powercfg` 的中文输出会变乱码。
- 超时：默认 60 秒；`dism` / Windows 功能组件类 600 秒。超时后杀进程树。
- 统一返回 `(exitCode, stdout, stderr)`。**非零退出码不是异常，是结果**——错误文本原样进入卡片副标题。
- `ProcessRunner` 是唯一允许 `Process.Start` 的位置；其余代码不得直接起进程。

## 8. 状态、错误、刷新

- **六态**：`Unknown / On / Off / Mixed / Blocked / PendingRestart`。`Mixed` 专用于防火墙三档不一致等场景。
- **写入是悲观的**：点击 → 卡片进入 busy 态（开关禁用 + 进度指示）→ 执行 → **重新读取权威状态** → 更新 UI。不做乐观翻转：撒谎比慢两百毫秒糟糕得多。
- **失败不吞**：`SwitchWriteResult.Detail` 携带系统原文（篡改防护报错、GPO 拒绝），显示在副标题行，卡片描边变红。
- **刷新策略**：窗口显示时并行读取九张卡，各自独立从 `Unknown` 变为实值；提供手动刷新按钮。**不做定时轮询**——每 5 秒拉一次 PowerShell 是纯浪费。

### 提权归属守卫

启动时执行：

1. 取当前进程令牌的用户 SID（即被提升的账户）。
2. 定位当前会话的 `explorer.exe`，`OpenProcessToken` + `GetTokenInformation(TokenUser)` 取得**桌面用户的 SID**。
3. 两者不一致 → 判定为"以其他管理员账户提升"。

处理：代理与剪贴板两张卡置 `Blocked`，副标题写"当前以其他管理员账户运行，用户级设置不可用"。若步骤 2 失败（拿不到 explorer 令牌），按同账户处理并记录日志。

## 9. UI 设计

### 布局（已决策：分组卡片列表）

```
┌─ Windows 快捷开关 ───────────────────── □ ✕ ┐
│                                              │
│  安全                                        │
│  ┌────────────────────────────────────────┐  │
│  │ 防火墙          已开启 ●      [ ●━ ]  │  │
│  │ 域 / 专用 / 公用 三档统一              │  │
│  └────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────┐  │
│  │ 实时防护        已开启 ●      [ ●━ ]  │  │
│  │ 受篡改防护保护，可能拒绝修改            │  │
│  └────────────────────────────────────────┘  │
│                                              │
│  电源                                        │
│  ┌────────────────────────────────────────┐  │
│  │ 休眠            已关闭 ○      [ ━● ]  │  │
│  │ 关闭后将删除 hiberfil.sys，快速启动失效│  │
│  └────────────────────────────────────────┘  │
│  ...                                         │
└──────────────────────────────────────────────┘
```

- `MainWindow` = `ScrollViewer` + `ItemsControl`，按 `Group` 分组（安全 / 网络 / 电源 / 系统），组标题用 `Expander` 包裹。
- `SwitchCard`：左侧 `Title` + 副标题行（动态 `Detail` 或静态 `Subtitle`）；右上角状态点（绿 = On / 灰 = Off / 黄 = Mixed / 红 = Blocked）；右侧 `ToggleSwitch`。
- `RequiresRestart` 的卡在副标题行挂 `重启后生效` 徽标。
- `IsDestructive` 的卡点击时弹确认对话框，文案取自 `ConfirmText`。UAC 那条必须写明：「关闭后需重启才能改回，期间商店应用不可用」。
- 窗口尺寸 420×640 起，可缩放，最小 380×480。

**为什么选卡片列表而非磁贴网格 / 紧凑列表：** 这个产品的失败模式不是"开关按不动"，而是"按不动却不知道为什么"——GPO 托管、篡改防护、需要重启、需要先关休眠。副标题行就是给这些解释留的位置。磁贴网格与紧凑列表都砍掉了这个位置。

## 10. 测试

**单元测试（xunit，全部走接口注入，不碰真机）：**

- `powercfg` 输出解析（中英文双语样本，含多计划场景）
- 防火墙三档聚合：全开 → On；全关 → Off；一开两关 → Mixed
- `EnableLUA` / `HiberbootEnabled` / `HibernateEnabled` 的键路径与 DWORD 类型
- Windows 功能组件状态映射（`Enabled` / `Disabled` / `EnablePending`）
- `SwitchRegistry` 自检规则（Id 唯一、破坏性开关必须带确认文案）
- `ElevationGuard` 的同账户 / 异账户判定分支
- `ProcessRunner` 的超时与编码处理（可用 `cmd.exe` 这类确定性命令做端到端小测）

**真机验收：** `docs/manual-verification.md` 清单，九个开关逐个人工切换并记录观察结果；必须覆盖 GPO 场景与篡改防护场景的"优雅失败"。

## 11. 里程碑

| # | 内容 | 验收标准 |
|---|---|---|
| M0 | sln + WPF 骨架 + `app.manifest` 提权 + 托盘宿主 + 一张假卡片 | 双击弹 UAC；托盘图标存在；窗口能收能放 |
| M1 | `ISwitch` + 注册表 + 卡片列表 UI + **防火墙**全链路 | 托盘右键切防火墙；面板状态同步；GPO 场景给出正确提示 |
| M2 | 代理 + 剪贴板 + 提权归属守卫 | 换账户提升时两张卡正确置 `Blocked` |
| M3 | 休眠 + 快速启动 + 电源计划（引入 `IChoiceSwitch` 与 `ChoiceCard`） | 电源计划三选一在 UI 上可用 |
| M4 | 实时防护 + 功能组件 + UAC（二次确认 + 重启徽标） | 篡改防护开启时优雅失败，程序不崩 |

每个里程碑产出一个可运行的 exe。

## 12. 已知风险

| # | 风险 | 处理 |
|---|---|---|
| 1 | 中文系统上外部命令输出编码导致乱码 | §7 统一 UTF-8 管道 |
| 2 | COM late binding 无编译期检查，属性名拼错只在运行时暴露 | 全部包 try/catch，异常归入 `Blocked` 状态并回显 |
| 3 | 篡改防护拒绝 Defender 写入 | 捕获原文，标记 `Blocked`，不禁用其它卡片 |
| 4 | 关闭 UAC 后需重启才能改回 | 确认文案强制写明；卡片挂 `重启后生效` 徽标 |
| 5 | 换账户提升导致 `HKCU` 错位 | §8 提权归属守卫 |
| 6 | ~~项目目录当前无 git 仓库~~ 已解决 | 已 `git init -b main` 并关联远端，首个提交已推送 |
| 7 | 远端仓库名含拼写 `windwos` | 按用户给定 URL 原样使用，不擅自更正 |

## 13. 远端仓库

- 地址：`https://github.com/ljccc2025/theNorthSea-windwosQuickClose.git`
- 约定：所有文档与代码均推送到该仓库；每个里程碑完成后推送一次。
- 本地身份：`root <3349789097@qq.com>`，凭据由 Git Credential Manager 管理。
- 当前状态：仓库已初始化（`main` 分支），设计规格提交 `0a54fe6` 已推送。
