# Windows 快捷开关 —— M2 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 在 M0 + M1 骨架之上接入 M2 三个交付物：系统代理开关、剪贴板历史开关、提权归属守卫（`SessionOwnerGuard` + 卡片封锁）。

**架构：** 新增三个基础设施接缝，全部面向接口、可单测：`IRegistryStore`（唯一 HKCU 读写口）、`ISettingsNotifier`（唯一 Win32 生效通知口）、`IUserSidSource`（SID 探测口）。开关实现只依赖接缝，不在内部碰 `Registry` 或 P/Invoke。守卫命中时用装饰器 `BlockedSwitch` 包住用户级开关，注册顺序不变、卡片照常占位。`SwitchRegistry.CreateDefault` 由 `(powerShell)` 扩为 `(powerShell, registry, notifier, sessionOwner)`——破坏性签名变更，调用点只有 `src/QuickSwitch/App.xaml.cs`。

**技术栈：** .NET 10（`net10.0-windows`）、xUnit、`Microsoft.Win32.Registry`、P/Invoke（advapi32 / kernel32 / wininet / user32）。

---

## 范围说明（先读这一条）

设计规格 `docs/superpowers/specs/2026-10-01-windows-quickswitch-design.md` 覆盖九个开关（M0–M4）。本计划**只覆盖 M2**：两张 HKCU 卡片 + 提权归属守卫。理由：M2 完成后九个开关里的三个可用，且守卫是与账户模型相关的横切关注点，越早在骨架里落地越省事；M3（休眠 + 快速启动 + 电源计划）、M4（实时防护 + 功能组件 + UAC）各自出独立计划。

因此本计划**不定义** `IChoiceSwitch`、`RequiresRestart`、`IsDestructive`、`ConfirmText`（它们在 M3/M4 随首个使用者落地），也**不改** `ISwitch` 契约——M2 的三个开关都是纯布尔开关，`ReadAsync` / `ApplyAsync` 足够。

## 已在本机验证过的事实（写代码前必须知道）

1. 本机存在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ProxyEnable`（DWORD，实测 `Int32` = `0`）与 `ProxyServer`（SZ，实测 `127.0.0.1:7897`）；`HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory`（DWORD = `1`）。
2. **坑：`RegistryValueKind` 的枚举值是 `DWord`（大写 W）**，写成 `RegistryValueKind.Dword` 编译报 `error CS0117: “RegistryValueKind”未包含“Dword”的定义`。
3. 真机 `WindowsUserSidSource`：`processSid == shellSid == S-1-5-21-229725293-1237267458-2383575522-1004`，`SessionOwnerGuard.Evaluate()` → `IsForeignAdmin = False`；即 `OpenProcess` + `OpenProcessToken` + `GetTokenInformation(TokenUser)` 取 explorer 令牌 SID 的整条路径真机可用，`shellSid` 不为 null。
4. 真机 `Win32SettingsNotifier` 两个入口（`InternetSetOption` ×2 = SETTINGS_CHANGED 39 再 REFRESH 37；`SendMessageTimeout` 广播 `WM_SETTINGCHANGE`）均无异常返回。
5. 非提权（实测 `AdminContext.IsElevated() = False`）下以上全部可读写——与规格 §6「系统代理 / 剪贴板历史：不需管理员、不需重启」一致。
6. `Microsoft.Win32.Registry` 与 `System.Security.Principal.Windows` 已在 Core 的 `obj/project.assets.json` 里（随 `Microsoft.WindowsDesktop.App.Runtime.win-x64` 带入），**不需要加包引用**。
7. 用文件式 app（`dotnet run probe.cs`）做真机探针时，必须加 `#:property TargetFramework=net10.0-windows`，否则 NU1201（Core 是 `net10.0-windows7.0`，文件式 app 默认 `net10.0`）。

## 文件结构

创建：

| 路径 | 职责 |
|---|---|
| `src/QuickSwitch.Core/Infrastructure/NativeMethods.cs` | internal static P/Invoke 集中地（常量 + 5 个 DllImport） |
| `src/QuickSwitch.Core/Infrastructure/RegistryStore.cs` | `IRegistryStore` + `WindowsRegistryStore`（HKCU） |
| `src/QuickSwitch.Core/Infrastructure/SettingsNotifier.cs` | `ISettingsNotifier` + `Win32SettingsNotifier` |
| `src/QuickSwitch.Core/Infrastructure/SessionOwnerGuard.cs` | `SessionOwnerState` / `IUserSidSource` / `SessionOwnerGuard` / `WindowsUserSidSource` |
| `src/QuickSwitch.Core/Switches/BlockedSwitch.cs` | 装饰器：读恒 `Blocked`、写恒失败 |
| `src/QuickSwitch.Core/Switches/SystemProxySwitch.cs` | `system-proxy` 开关 |
| `src/QuickSwitch.Core/Switches/ClipboardHistorySwitch.cs` | `clipboard-history` 开关 |
| `tests/QuickSwitch.Tests/Fakes/FakeRegistryStore.cs` | 内存注册表 + `RecordedWrite` 记录 |
| `tests/QuickSwitch.Tests/Fakes/FakeSettingsNotifier.cs` | 计数 + 可注入失败 |
| `tests/QuickSwitch.Tests/SessionOwnerGuardTests.cs` | SID 比对分支 + 真机 SID 形状 |
| `tests/QuickSwitch.Tests/SystemProxySwitchTests.cs` | 代理读写全分支 |
| `tests/QuickSwitch.Tests/ClipboardHistorySwitchTests.cs` | 剪贴板读写全分支 |
| `tests/QuickSwitch.Tests/BlockedSwitchTests.cs` | 封锁语义 |

修改：

| 路径 | 改动 |
|---|---|
| `src/QuickSwitch.Core/Switches/SwitchRegistry.cs` | `CreateDefault` 扩参 + 用户级开关包守卫 |
| `src/QuickSwitch/App.xaml.cs` | 组装 `WindowsRegistryStore` / `Win32SettingsNotifier` / `WindowsUserSidSource` |
| `tests/QuickSwitch.Tests/SwitchRegistryTests.cs` | 适配新签名 + 同账户/异账户两条注册断言 |
| `tests/QuickSwitch.Tests/SwitchCardViewModelTests.cs` | 新增「封锁卡片显示原因且不可切」用例 |
| `README.md` | 状态、文档索引、里程碑表 |

## 任务

- [x] **1. 注册表接缝** —— `IRegistryStore { int? ReadDword; string? ReadString; void WriteDword; }`；`WindowsRegistryStore` 用 `Registry.CurrentUser`，`CreateSubKey(path, writable: true)` 返回 null 时抛 `InvalidOperationException($"无法打开注册表键 HKCU\\{subKeyPath}。")`，写入用 `RegistryValueKind.DWord`。**验收：** 编译通过 + 真机写回同值后回读一致。
- [x] **2. Win32 通知接缝** —— `ISettingsNotifier { NotifyInternetSettingsChanged(); NotifyClipboardSettingChanged(); }`；代理走 `InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0)` 再 `37`；剪贴板走 `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "Software\\Microsoft\\Clipboard", SMTO_ABORTIFHUNG, 1000, out _)`。通知是尽力而为，**不抛**。**验收：** 真机探针无异常。
- [x] **3. 提权归属守卫** —— 进程 SID 走 `WindowsIdentity.GetCurrent().User?.Value`；桌面 SID 遍历 `Process.GetProcessesByName("explorer")` 过滤同 `SessionId`，`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `OpenProcessToken(TOKEN_QUERY)` → `GetTokenInformation(TokenUser)` 两段式（先取 size 再 `AllocHGlobal` 缓冲），`new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value`（TOKEN_USER 首字段即 SID 指针）。**任一侧为 null/空白 → 按同账户**（探测失败不锁死开关，规格 §8）；SID 用 `OrdinalIgnoreCase` 比对。原因文案常量：`SessionOwnerGuard.ForeignAdminDetail = "当前以其他管理员账户运行，用户级设置不可用"`。**验收：** 真机 `IsForeignAdmin = False`；单测覆盖同/异/缺失三分支。
- [x] **4. `BlockedSwitch` 装饰器** —— `Descriptor` 透传；`ReadAsync` 恒 `SwitchReadResult(SwitchState.Blocked, reason)`；`ApplyAsync` 恒 `SwitchApplyResult.Fail(reason)`；reason 空白抛 `ArgumentException`。**验收：** 单测断言内层 `ReadCount == 0` / `LastTarget == null`。
- [x] **5. 系统代理开关** —— 常量 `KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"`、`EnableValueName = "ProxyEnable"`、`ServerValueName = "ProxyServer"`。读：缺失 → `Unknown`（文案含 `ProxyEnable`）；`0` → `Off`；非 0 → `On`，有 `ProxyServer` 时副标题 `代理服务器：{server}`，否则 `系统代理已开启，但未配置代理服务器地址`；异常 → `Unknown` + 系统原文。写：`On` → 1、`Off` → 0，其它目标抛 `ArgumentOutOfRangeException`；写失败 → `Fail("写入系统代理失败：…")` 且**不发通知**；写成功后通知并吞掉通知异常（写入已生效，不该谎报失败）。**验收：** 全分支单测。
- [x] **6. 剪贴板历史开关** —— `KeyPath = @"Software\Microsoft\Clipboard"`、`ValueName = "EnableClipboardHistory"`，读写语义同 5，通知走 `NotifyClipboardSettingChanged()`。**验收：** 全分支单测。
- [x] **7. 接线** —— `SwitchRegistry.CreateDefault(powerShell, registry, notifier, sessionOwner)`；`GuardUserLevel` 只包用户级开关（HKCU），防火墙走 HKLM 不包；`App.xaml.cs` 启动时 `new SessionOwnerGuard(new WindowsUserSidSource()).Evaluate()`。**验收：** `dotnet build QuickSwitch.slnx -c Release` 0 错误；单测断言异账户下前两张是 `BlockedSwitch`、第三张仍是 `FirewallSwitch`。
- [x] **8. 测试与文档** —— 单测、README 状态/里程碑、本计划文档。**验收：** `dotnet test -c Release` 全绿。

## 验证记录

- `dotnet test tests/QuickSwitch.Tests/QuickSwitch.Tests.csproj -c Release`：**86 通过，0 失败**（M1 基线 40，M2 新增 46）。
- `dotnet build QuickSwitch.slnx -c Release`：**0 错误**，2 个既有 `WFO0003` 高 DPI 警告（`app.manifest` 历史遗留，与 M2 无关）。
- 真机探针（临时文件式 app，跑完即删；只写回当前值，不改动机器设置）：

```text
session=1 elevated=False
processSid=S-1-5-21-229725293-1237267458-2383575522-1004
shellSid=S-1-5-21-229725293-1237267458-2383575522-1004
state=SessionOwnerState { IsForeignAdmin = False, Detail =  }
notifier ok
proxy before: Off / 系统代理已关闭        apply(Off): success=True
clipboard before: On / 剪贴板历史已开启  apply(On): success=True
```

- 探针后回读注册表：`ProxyEnable=0`、`EnableClipboardHistory=1`，与探针前一致。

## 与规格的偏差（有意为之）

- 规格 §8 只写了「取 explorer 令牌失败 → 按同账户」；实现把两侧任一为空的判断收敛到纯函数 `SessionOwnerGuard.Compare`，因此**进程侧取不到 SID 时也按同账户**。理由：判定逻辑必须可单测，而单测里塞不进真实的令牌失败。
- 规格说封锁卡片副标题显示原因；实现不改 XAML——`SwitchCardViewModel.RefreshAsync` 本来就用 `ReadResult.Detail` 覆盖副标题，`BlockedSwitch` 把原因放进 `Detail` 即达成。
- `SameAccount` 静态实例挂在 `SessionOwnerState` 记录上（`SessionOwnerState.SameAccount`），不在 `SessionOwnerGuard` 上。
- **不做真机切换**（代理/剪贴板写入非同值）：规格与既有测试约定只允许对这两个开关做真机读取。真机探针因此只写回当前值。
