# theNorthSea-windwosQuickClose

Windows 快捷开关 — 常驻托盘的 Windows 11 系统设置开关面板。

一个开关控制一类系统设置，状态来自权威回读，失败原样呈现，不崩。

## 状态

设计阶段完成，尚未实现。里程碑计划见设计规格。

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

## 构建

```
dotnet build QuickSwitch.sln
dotnet publish src/QuickSwitch -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
