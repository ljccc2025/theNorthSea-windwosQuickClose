# theNorthSea-windwosQuickClose

Windows 快捷开关 — 常驻托盘的 Windows 11 系统设置开关面板。

一个开关控制一类系统设置，状态来自权威回读，失败原样呈现，不崩。

## 状态

M0–M4 全部实现，11 张开关卡片，245 个单测全绿：

- 托盘常驻、启动即整体提权（`app.manifest` 的 `requireAdministrator`，全程只弹一次 UAC）
- 分组卡片列表（安全 / 网络 / 电源 / 系统），写操作乐观按钮 + 权威回读 + 失败弹回
- 防火墙三档统一开关、系统代理、剪贴板历史、休眠、快速启动、电源计划（三选一）
- 实时防护（篡改防护开启时显示"被阻止"并说明原因）、UAC、Windows 功能组件（Hyper-V / WSL / 虚拟机平台）
- 破坏性开关（UAC、功能组件）关闭前弹确认框；需重启的开关成功后亮"重启后生效"徽标
- 提权归属守卫：以其他管理员账户运行时，封锁写 HKCU 的两张用户级卡片

已实现但**尚未在提权环境下做人工验收**：见 [人工验收清单](docs/manual-verification.md)。

## 构建与运行

```powershell
# 单测（无需管理员权限；含真机读路径用例）
dotnet test QuickSwitch.slnx

# 调试运行（会弹 UAC —— manifest 声明 requireAdministrator）
dotnet run --project src/QuickSwitch/QuickSwitch.csproj

# 单文件自包含发布
dotnet publish src/QuickSwitch/QuickSwitch.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

产物：`src/QuickSwitch/bin/Release/net10.0-windows/win-x64/publish/QuickSwitch.exe`

换成 v2 基础设施时要记的坑：

- `Set-NetFirewallProfile -Enabled` 必须传裸字符串 `True`/`False`；传 `$true` 会抛 `Invalid cast from 'System.Boolean' to GpoBoolean`。
- 所有 PowerShell 出口统一带 `[Console]::OutputEncoding=[Text.Encoding]::UTF8` 前缀，否则中文系统上 powercfg / dism 输出乱码。
- PowerShell 出口统一以 `exit $LASTEXITCODE` 结尾（换行起头），否则脚本尾部注释会把真实退出码吞成 0/1。

## 文档

- [设计规格](docs/superpowers/specs/2026-10-01-windows-quickswitch-design.md)
- [M0 + M1 实现计划](docs/superpowers/plans/2026-10-01-windows-quickswitch-m0-m1.md)
- [M2 实现计划](docs/superpowers/plans/2026-10-01-windows-quickswitch-m2.md)
- [人工验收清单](docs/manual-verification.md)

## 开关

| 分组 | 开关 | 管理员 | 需重启 | 破坏性 |
|---|---|---|---|---|
| 安全 | 防火墙（域/专用/公用 三档统一） | 是 | 否 | 否 |
| 安全 | 实时防护（Defender） | 是 | 否 | 否 |
| 安全 | UAC | 是 | 是 | 是 |
| 网络 | 系统代理 | 否 | 否 | 否 |
| 电源 | 休眠 | 是 | 否 | 否 |
| 电源 | 快速启动 | 是 | 否 | 否 |
| 电源 | 电源计划（高性能/平衡/节能） | 是 | 否 | 否 |
| 系统 | 剪贴板历史 | 否 | 否 | 否 |
| 系统 | Windows 功能组件（Hyper-V / WSL / VirtualMachinePlatform） | 是 | 是 | 是 |

## 技术栈

- .NET 10 + WPF（`net10.0-windows`），`QuickSwitch.Core` 不含 WPF 依赖
- WinForms `NotifyIcon`（托盘）
- `CommunityToolkit.Mvvm`
- 启动即整体提权（`app.manifest` 的 `requireAdministrator`）

## 里程碑

| # | 内容 | 状态 |
|---|---|---|
| M0 | 骨架 + manifest 提权 + 托盘宿主 + 假卡片 | 已完成 |
| M1 | `ISwitch` + 注册表 + 卡片列表 UI + 防火墙全链路 | 已完成 |
| M2 | 代理 + 剪贴板 + 提权归属守卫 | 已完成 |
| M3 | 休眠 + 快速启动 + 电源计划（选择卡） | 已完成 |
| M4 | 实时防护 + 功能组件 + UAC + 确认弹窗 + 重启徽标 | 已完成 |
