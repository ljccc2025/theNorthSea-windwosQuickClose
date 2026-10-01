# 人工验收清单

自动化单测（`dotnet test QuickSwitch.slnx`，245 个）能覆盖解析、注册表读写、进程出口、竞态与异常兜底，
但**提权后的真实系统改动、托盘交互、UAC 弹窗、确认框**只能在有桌面的真机上人工验收。

前置：

```powershell
dotnet publish src/QuickSwitch/QuickSwitch.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

产物 `src/QuickSwitch/bin/Release/net10.0-windows/win-x64/publish/QuickSwitch.exe`。

验收前记下当前真值，验收后逐项还原：

```powershell
Get-NetFirewallProfile -Profile Domain,Private,Public | Format-Table Name,Enabled
Get-MpComputerStatus | Select-Object RealTimeProtectionEnabled,IsTamperProtected
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name EnableLUA
powercfg /getactivescheme
```

## 1. 启动与提权

- [ ] 双击 exe，只弹**一次** UAC；标题栏出现提权徽标
- [ ] 窗口关闭（X）后进程仍在托盘；托盘图标右键有菜单
- [ ] 托盘菜单"显示/隐藏"能来回切换窗口（最小化后点一次也能回来）
- [ ] 托盘菜单"退出"能真正结束进程（任务管理器里无残留）

## 2. 卡片列表

- [ ] 11 张卡片分四组（安全 / 网络 / 电源 / 系统），顺序与 README 表一致
- [ ] 每张卡片的副标题是权威回读来的真实状态，不是写死的说明文字
- [ ] "刷新"按钮点击后短暂禁用，刷新完自动恢复

## 3. 防火墙（M1 核心链路）

- [ ] 三档（域/专用/公用）当前为关时点开关 → 系统设置里三档全部变为**启用**
- [ ] 再点一次 → 三档全部关闭
- [ ] 中途三档被人为改成不一致（`Set-NetFirewallProfile -Profile Domain -Enabled True`）后刷新：卡片显示不一致状态且开关不呈现"开"
- [ ] 验收后恢复原来三档的状态

## 4. 用户级卡片（M2）

- [ ] 系统代理：开 → `HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings` 的 `ProxyEnable` 变 1，系统设置里代理开关同步变化
- [ ] 剪贴板历史：开 → 设置里剪贴板历史同步打开（快捷键 Win+V 可用）
- [ ] 两张卡片都不需要额外弹窗（已在提权进程内完成）

## 5. 电源与开机（M3）

- [ ] 休眠：关 → `powercfg /a` 里"休眠"变为不可用
- [ ] 快速启动：开 → `HiberbootEnabled` 为 1；休眠关闭时副标题提示"休眠已关闭，实际不生效"
- [ ] 电源计划：下拉是三选一（高性能/平衡/节能），切换后 `powercfg /getactivescheme` 跟着变
- [ ] 电源计划卡**没有**右侧开关（三选一用下拉，这是设计；若出现开关即为缺陷）

## 6. 破坏性开关与徽标（M4）

- [ ] 实时防护：点关 → Defender 实时防护真的关闭（`Get-MpComputerStatus` 里 `RealTimeProtectionEnabled` 为 False）
- [ ] 打开篡改防护（Windows 安全中心）后刷新实时防护卡片 → 卡片显示"被阻止"并说明"篡改防护已开启…"，开关不可点
- [ ] UAC：点关 → **先弹确认框**，点"取消"系统无任何改变
- [ ] UAC：点关确认后 → `EnableLUA` 为 0，卡片亮"重启后生效"
- [ ] 功能组件（Hyper-V / WSL / 虚拟机平台）：关闭时先弹确认框（确认文案含该组件名）
- [ ] 关闭一个功能组件后 → `Get-WindowsOptionalFeature -Online -FeatureName <名字>` 显示 `DisablePending`，卡片亮"重启后生效"
- [ ] 验收后把 UAC 与功能组件改回原值（UAC 改回 1 同样需要重启）

## 7. 提权归属守卫（M2，需要另一个管理员账户）

- [ ] 在另一个管理员账户的会话里以"以管理员身份运行"启动本程序 → 系统代理与剪贴板历史两张卡显示"当前以其他管理员账户运行，用户级设置不可用"且不可点
- [ ] 同一台机器上其余卡片仍可正常使用

## 8. 失败路径

- [ ] 断网/无 Defender 的机器上刷新实时防护 → 卡片显示"未知 + 原因"，程序不崩
- [ ] 任何一张卡读取失败时，其他卡片照常工作；程序不退出
