# theNorthSea-windwosQuickClose

Windows 快捷开关 — 常驻托盘的 Windows 11 系统设置开关面板。

一个开关控制一类系统设置，状态来自权威回读，失败原样呈现，不崩。

## 状态

M0 + M1 已实现：托盘常驻、整体提权、分组卡片列表、防火墙三档开关（含权威回读）。
其余八个开关按 M2–M4 逐步接入。

## 构建与运行

```powershell
# 单测（无需管理员权限）
dotnet test QuickSwitch.slnx

# 调试运行（会弹 UAC —— manifest 声明 requireAdministrator）
dotnet run --project src/QuickSwitch/QuickSwitch.csproj

# 单文件自包含发布
dotnet publish src/QuickSwitch/QuickSwitch.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

产物：`src/QuickSwitch/bin/Release/net10.0-windows/win-x64/publish/QuickSwitch.exe`

注意：`Set-NetFirewallProfile -Enabled` 必须传裸字符串 `True`/`False`；
传 `$true` 会抛 `Invalid cast from 'System.Boolean' to GpoBoolean`。

## 文档

- [设计规格](docs/superpowers/specs/2026-10-01-windows-quickswitch-design.md)

## 计划中的开关

| 分组 | 开关 | 管理员 | 需重启 |
|---|---|---|---|
| 安全 | 防火墙（域/专用/公用 三档统一） | 是 | 否 |
| 安全 | 实时防护（Defender） | 是 | 否 |
| 安全 | UAC | 是 | 是 |
| 网络 | 系统代理 | 否 | 否 |
| 电源 | 休眠 | 是 | 否 |
| 电源 | 快速启动 | 是 | 否 |
| 电源 | 电源计划（高性能/平衡/节能） | 是 | 否 |
| 系统 | 剪贴板历史 | 否 | 否 |
| 系统 | Windows 功能组件（Hyper-V / WSL / VirtualMachinePlatform） | 是 | 是 |

## 技术栈

- .NET 10 + WPF（`net10.0-windows`）
- WinForms `NotifyIcon`（托盘）
- `CommunityToolkit.Mvvm`
- 启动即整体提权（`app.manifest` 的 `requireAdministrator`）

## 里程碑

| # | 内容 |
|---|---|
| M0 | 骨架 + manifest 提权 + 托盘宿主 + 假卡片 |
| M1 | `ISwitch` + 注册表 + 卡片列表 UI + 防火墙全链路 |
| M2 | 代理 + 剪贴板 + 提权归属守卫 |
| M3 | 休眠 + 快速启动 + 电源计划 |
| M4 | 实时防护 + 功能组件 + UAC |
