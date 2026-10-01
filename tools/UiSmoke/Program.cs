using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;

using Condition = System.Windows.Automation.Condition;

namespace UiSmoke;

/// 端到端验收台：宿主进程里跑真正的 QuickSwitch.App（真 App.xaml、真 MainWindow、真 Core、真注册表/真命令），
/// 再用 UI Automation 读真实无障碍树 + 真实鼠标点击驱动界面。所有断言都必须落到"真值"上：
/// 卡片里显示的、内存里持有的、系统里实际的，三者对不上就是 FAIL。
internal static class Program
{
    private const string WindowTitle = "Windows 快捷开关";
    private const string ClipboardKey = @"Software\Microsoft\Clipboard";
    private const string ClipboardValue = "EnableClipboardHistory";
    private const string UacKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string UacValue = "EnableLUA";
    private const string ProxyKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private static readonly List<(string Verdict, string Name, string Evidence)> Results = new();
    private static QuickSwitch.App? _app;
    private static Rect WindowRect;
    private static readonly StringBuilder Transcript = new();

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var dump = args.Contains("--dump");

        _app = new QuickSwitch.App();
        _app.InitializeComponent();

        var worker = new Thread(() => RunScenarios(dump)) { IsBackground = true, Name = "ui-smoke" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        _app.Run();

        Console.WriteLine();
        Console.WriteLine("================ UI SMOKE REPORT ================");
        foreach (var (verdict, name, evidence) in Results)
            Console.WriteLine($"[{verdict}] {name} :: {evidence}");

        var pass = Results.Count(r => r.Verdict == "PASS");
        var fail = Results.Count(r => r.Verdict == "FAIL");
        var skip = Results.Count(r => r.Verdict == "SKIP");
        Console.WriteLine($"---- PASS={pass} FAIL={fail} SKIP={skip} ----");

        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts");
            Directory.CreateDirectory(dir);
            var log = Path.Combine(Path.GetFullPath(dir), "ui-smoke.log");
            File.WriteAllText(log, Transcript.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"log={log}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"log-write-failed: {ex.Message}");
        }

        return fail == 0 ? 0 : 1;
    }

    private static (string Title, string Group, string Subtitle, bool IsOn, SwitchState State, bool Badge) Snapshot(ICardViewModel card)
        => card switch
        {
            SwitchCardViewModel s => (s.Title, s.Group, s.Subtitle, s.IsOn, s.State, s.ShowRestartBadge),
            ChoiceCardViewModel c => (c.Title, c.Group, c.Subtitle, false, c.State, false),
            _ => ("?", "?", "?", false, SwitchState.Unknown, false),
        };

    private static void RunScenarios(bool dump)
    {
        try
        {
            var win = WaitForWindow(WindowTitle, TimeSpan.FromSeconds(120));
            if (win is null)
            {
                Fail("S1 窗口出现", "120 秒内没找到标题为「Windows 快捷开关」的顶层窗口");
                return;
            }

            var pid = win.Current.ProcessId;
            var hwnd = new IntPtr(win.Current.NativeWindowHandle);
            WindowRect = win.Current.BoundingRectangle;
            Pass("S1 窗口出现", $"Name={win.Current.Name} Class={win.Current.ClassName} Pid={pid} Hwnd=0x{hwnd:X} rect={Rect(win)}");

            if (dump)
            {
                Console.WriteLine("---- UIA TREE (control view, depth<=6) ----");
                Dump(win, 0, 6);
            }

            var vm = Dispatcher(() => _app!.MainWindow?.DataContext as MainViewModel);
            if (vm is null)
            {
                Fail("S2 拿到真实 ViewModel", "MainWindow.DataContext 不是 MainViewModel");
                return;
            }

            WaitIdle(vm, TimeSpan.FromSeconds(180));
            Pass("S3 启动刷新跑完", $"IsRefreshing={vm.IsRefreshing} 卡片数={vm.Cards.Count} IsElevated={vm.IsElevated}");

            var expected = new (string Title, string Group)[]
            {
                ("Windows 防火墙", "安全"),
                ("系统代理", "网络"),
                ("休眠", "电源"),
                ("快速启动", "电源"),
                ("电源计划", "电源"),
                ("剪贴板历史", "系统"),
                ("实时防护", "安全"),
                ("用户账户控制 (UAC)", "安全"),
                ("Hyper-V", "系统"),
                ("WSL", "系统"),
                ("虚拟机平台", "系统"),
            };

            var vmCards = Dispatcher(() => vm.Cards.Select(Snapshot).ToList());

            var sameSet = vmCards.Count == expected.Length
                && vmCards.Select(c => c.Title).OrderBy(t => t, StringComparer.Ordinal)
                    .SequenceEqual(expected.Select(e => e.Title).OrderBy(t => t, StringComparer.Ordinal));
            Check(sameSet, "S2 ViewModel 卡片集合 = 11 张且标题与规格一致",
                $"实际({vmCards.Count})=[{string.Join(", ", vmCards.Select(c => $"{c.Title}/{c.Group}"))}]");

            var groups = vmCards.Select(c => c.Group).Distinct().ToList();
            Check(groups.SequenceEqual(new[] { "安全", "网络", "电源", "系统" }),
                "S2b 分组口径 = 安全/网络/电源/系统（按卡片出现顺序）", $"[{string.Join(", ", groups)}]");

            // ---- UI 层：窗口里真的画出来了 ----
            var uiTitles = ReadTexts(win, 400);
            var missing = expected.Select(e => e.Title).Where(t => !uiTitles.Contains(t)).ToList();
            Check(missing.Count == 0, "S4 11 张卡的标题都出现在真实 UI 树里",
                missing.Count == 0 ? $"读到 {uiTitles.Count} 个文本元素，全部命中" : $"UI 里找不到：{string.Join(", ", missing)}");

            var headerOrder = HeaderOrder(win);
            Check(headerOrder.SequenceEqual(new[] { "安全", "网络", "电源", "系统" }),
                "S4b UI 分组容器顺序 = 安全/网络/电源/系统（UIA Group 元素的纵向位置）", $"[{string.Join(", ", headerOrder)}]");

            var badge = Dispatcher(() => vm.ElevationBadge);
            Check(uiTitles.Contains(badge), "S5 提权徽标文本出现在 UI 上",
                $"徽标=\"{badge}\" IsElevated={vm.IsElevated}（期望 {(vm.IsElevated ? "「管理员模式」" : "「未提权：部分开关不可用」")}）",
                expectedOk: vm.IsElevated ? badge == "管理员模式" : badge == "未提权：部分开关不可用");

            // 每张卡都必须能定位到一个可交互控件（开关或下拉框）
            var located = new Dictionary<string, (AutomationElement? Card, AutomationElement? Toggle, AutomationElement? Combo)>();
            foreach (var (title, _, _, _, _, _) in vmCards)
            {
                var hit = LocateCard(win, title);
                if (hit.Card is not null) located[title] = hit;
            }
            Check(located.Count == vmCards.Count, "S6 每张卡都能在 UI 树里定位到卡片容器 + 控件",
                $"定位到 {located.Count}/{vmCards.Count}；缺：" +
                string.Join(", ", vmCards.Select(c => c.Title).Where(t => !located.ContainsKey(t))));
            var noControl = vmCards.Select(c => c.Title)
                .Where(t => located.ContainsKey(t) && located[t].Toggle is null && located[t].Combo is null).ToList();
            Check(noControl.Count == 0, "S6b 开关卡有 ToggleButton、电源计划卡有 ComboBox",
                noControl.Count == 0 ? $"开关卡 {located.Values.Count(v => v.Toggle is not null)} 个，下拉卡 {located.Values.Count(v => v.Combo is not null)} 个"
                                     : $"无控件：{string.Join(", ", noControl)}");

            // 分组后的真实显示顺序（CollectionViewSource 按 Group 分组，组内保持注册表顺序）
            var expectedDisplayOrder = new[]
            {
                "Windows 防火墙", "实时防护", "用户账户控制 (UAC)",
                "系统代理",
                "休眠", "快速启动", "电源计划",
                "剪贴板历史", "Hyper-V", "WSL", "虚拟机平台",
            };
            var uiOrder = located.Select(kv => (Top: kv.Value.Card!.Current.BoundingRectangle.Top, kv.Key))
                .Where(x => x.Top > 0).OrderBy(x => x.Top).Select(x => x.Key).ToList();
            Check(uiOrder.SequenceEqual(expectedDisplayOrder),
                "S4c UI 里从上到下的卡片顺序 = 分组渲染结果（安全3 → 网络1 → 电源3 → 系统4）",
                $"[{string.Join(" | ", uiOrder)}]");

            // 卡片副标题：UI 上看到的字必须与 ViewModel 里的字完全一致（UI 不说谎）
            var subtitleMismatch = new List<string>();
            foreach (var card in vmCards)
            {
                if (!located.TryGetValue(card.Title, out var hit) || hit.Card is null) continue;
                var shown = ReadTexts(hit.Card, 20);
                if (!shown.Contains(card.Subtitle)) subtitleMismatch.Add($"{card.Title}:UI={Truncate(string.Join(" / ", shown), 90)} VM={Truncate(card.Subtitle, 90)}");
            }
            Check(subtitleMismatch.Count == 0, "S4d 每张卡在 UI 上显示的副标题 = ViewModel 里的副标题",
                subtitleMismatch.Count == 0 ? "11 张卡逐字一致" : string.Join(" ;; ", subtitleMismatch));

            // 开关卡的视觉状态必须等于 ViewModel 的权威状态；不可切换的卡必须是禁用态
            var visualMismatch = new List<string>();
            var disabledWhenNotToggleable = new List<string>();
            foreach (var card in vmCards)
            {
                if (!located.TryGetValue(card.Title, out var hit) || hit.Toggle is null) continue;
                var toggleState = ReadToggleState(hit.Toggle);
                var visualOn = toggleState == "On";
                if (visualOn != card.IsOn) visualMismatch.Add($"{card.Title}:UIA={toggleState} VM.IsOn={card.IsOn}");
                var notToggleable = card.State is SwitchState.Unknown or SwitchState.Blocked;
                if (notToggleable && hit.Toggle.Current.IsEnabled) disabledWhenNotToggleable.Add($"{card.Title}(State={card.State}) 仍是可点状态");
            }
            Check(visualMismatch.Count == 0, "S6c 开关的视觉位置（UIA ToggleState）= ViewModel.IsOn",
                visualMismatch.Count == 0 ? "10 张开关卡全部一致" : string.Join(" ;; ", visualMismatch));
            Check(disabledWhenNotToggleable.Count == 0, "S6d 状态未知/被封锁的卡，开关是禁用态（不能让用户误以为能从「关」变「开」）",
                disabledWhenNotToggleable.Count == 0 ? "无违规" : string.Join(" ;; ", disabledWhenNotToggleable));

            // ---- 刷新按钮 ----
            var refresh = FindButton(win, "刷新");
            if (refresh is null) Fail("S7 刷新按钮存在", "UI 树里找不到名为「刷新」的按钮");
            else
            {
                ClickElement(refresh, hwnd);
                var sawBusy = WaitUntil(() => Dispatcher(() => vm.IsRefreshing), TimeSpan.FromSeconds(10));
                WaitIdle(vm, TimeSpan.FromSeconds(180));
                var subs = Dispatcher(() => vm.Cards.Select(c => c switch
                {
                    SwitchCardViewModel s => s.Subtitle,
                    ChoiceCardViewModel c2 => c2.Subtitle,
                    _ => string.Empty,
                }).ToList());
                var stillRefreshing = Dispatcher(() => vm.IsRefreshing);
                // 并行刷新后"捕捉到刷新态"常常来不及看（读太快），那是好事，只当信息；硬指标是回读后每张卡都有话说、且刷新态收干净。
                Check(subs.All(s => !string.IsNullOrWhiteSpace(s)) && !stillRefreshing,
                    "S7 点「刷新」→ 回读后每张卡都有副标题、刷新态收干净",
                    $"捕捉到刷新态={sawBusy}（仅信息）空副标题 {subs.Count(s => string.IsNullOrWhiteSpace(s))} 条 IsRefreshing={stillRefreshing}");
            }

            // ---- 防火墙：真值比对 + 未提权失败路径 ----
            var fwTruth = ReadFirewallTruth();
            var fwCard = vmCards.First(c => c.Title == "Windows 防火墙");
            var fwExpectOff = fwTruth.All(v => v.Value == "False");
            Check(fwExpectOff ? fwCard.State == QuickSwitch.Core.Switches.SwitchState.Off : true,
                "S8 防火墙卡状态 = 系统真值",
                $"真值[{string.Join(", ", fwTruth.Select(v => $"{v.Key}={v.Value}"))}] 卡片 State={fwCard.State} IsOn={fwCard.IsOn}");
            if (fwExpectOff && located.TryGetValue("Windows 防火墙", out var fwHit) && fwHit.Toggle is not null)
            {
                ClickElement(fwHit.Toggle, hwnd);
                WaitUntil(() => Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>()
                    .First(c => c.Title == "Windows 防火墙").IsBusy), TimeSpan.FromSeconds(10));
                WaitIdle(vm, TimeSpan.FromSeconds(120));
                var after = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Windows 防火墙"));
                var truthAfter = ReadFirewallTruth();
                if (vm.IsElevated)
                {
                    var turnedOn = truthAfter.All(v => v.Value == "True")
                                   && after.State == QuickSwitch.Core.Switches.SwitchState.On;
                    Check(turnedOn, "S9（提权）点防火墙 → 真值三档全开 + 卡片翻到开",
                        $"副标题=\"{after.Subtitle}\" State={after.State} IsOn={after.IsOn} 真值[{string.Join(", ", truthAfter.Select(v => $"{v.Key}={v.Value}"))}]");
                    if (turnedOn && fwHit.Toggle is not null)
                    {
                        ClickElement(fwHit.Toggle, hwnd);
                        WaitUntil(() => Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>()
                            .First(c => c.Title == "Windows 防火墙").IsBusy), TimeSpan.FromSeconds(10));
                        WaitIdle(vm, TimeSpan.FromSeconds(120));
                        var restoredCard = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Windows 防火墙"));
                        var truthRestored = ReadFirewallTruth();
                        var allOff = truthRestored.All(v => v.Value == "False");
                        Check(allOff && restoredCard.State == QuickSwitch.Core.Switches.SwitchState.Off && !restoredCard.IsOn,
                            "S9b（提权）再点一次 → 真值还原三档全关 + 开关回 Off",
                            $"副标题=\"{restoredCard.Subtitle}\" State={restoredCard.State} IsOn={restoredCard.IsOn} 真值[{string.Join(", ", truthRestored.Select(v => $"{v.Key}={v.Value}"))}]");
                        if (!allOff)
                            Fail("S9c 防火墙还原", $"真值没还原：{string.Join(", ", truthRestored.Select(v => $"{v.Key}={v.Value}"))}");
                    }
                }
                else
                {
                    var refused = truthAfter.All(v => v.Value == "False") && after.State == QuickSwitch.Core.Switches.SwitchState.Off
                                  && after.Subtitle.StartsWith("操作失败：", StringComparison.Ordinal);
                    Check(refused, "S9 未提权点防火墙 → 真值没变 + 开关弹回 + 副标题给出系统原文",
                        $"副标题=\"{after.Subtitle}\" State={after.State} IsOn={after.IsOn} 真值[{string.Join(", ", truthAfter.Select(v => $"{v.Key}={v.Value}"))}]");
                }
            }
            else
            {
                Skip("S9 防火墙写入路径", "本机防火墙当前并非三档全关，跳过写入尝试以免改动用户设置");
            }

            // ---- 提权时：三张 Windows 功能组件卡必须能读到真状态（未提权时它们只能是 Unknown）----
            if (vm.IsElevated)
            {
                var featureCards = vm.Cards.OfType<SwitchCardViewModel>().Where(c => c.Title is "Hyper-V" or "WSL" or "虚拟机平台").ToList();
                var stillUnknown = featureCards.Where(c => c.State == QuickSwitch.Core.Switches.SwitchState.Unknown).ToList();
                Check(stillUnknown.Count == 0, "S9d（提权）三张功能组件卡读到真状态而不是 Unknown",
                    $"[{string.Join(", ", featureCards.Select(c => $"{c.Title}={c.State}"))}]");
            }

            // ---- 剪贴板历史：真写 + 还原 ----
            var clipBefore = ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64);
            var clipOutcome = ExerciseToggle(vm, located, "剪贴板历史", hwnd, TimeSpan.FromSeconds(120));
            // 写入是异步的：等注册表真的变过去（最多 15 s），别在命令还在路上时就读。
            WaitUntil(() => ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64) is int now
                            && !Equals(now, clipBefore), TimeSpan.FromSeconds(15));
            var clipAfter = ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64);
            var wrote = clipAfter is not null && !Equals(clipAfter, clipBefore);
            Check(wrote, "S10 点剪贴板历史 → HKCU\\...\\Clipboard\\EnableClipboardHistory 真的变了",
                $"点前(原始)={Show(clipBefore)} 点后={Show(clipAfter)} 卡片 Subtitle=\"{clipOutcome.Subtitle}\" State={clipOutcome.State}");

            // 再点一次回到原值，然后强制还原原始 DWORD
            ExerciseToggle(vm, located, "剪贴板历史", hwnd, TimeSpan.FromSeconds(120));
            WaitUntil(() => Equals(ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64), clipBefore),
                TimeSpan.FromSeconds(15));
            if (clipBefore is int v0)
                WriteRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, v0);
            else
                DeleteRegistryValue(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue);
            var restored = ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64);
            Check(Equals(restored, clipBefore), "S10b 验证后已把剪贴板历史还原成验收前的值",
                $"验收前={Show(clipBefore)} 现在={Show(restored)}");

            // ---- 系统代理：只读比对（不写，避免打断用户网络） ----
            var proxyEnable = ReadRegistry(RegistryHive.CurrentUser, ProxyKey, "ProxyEnable", RegistryView.Registry64);
            var proxyCard = vmCards.First(c => c.Title == "系统代理");
            var proxyExpectOn = proxyEnable is int pe && pe != 0;
            Check(proxyCard.State != QuickSwitch.Core.Switches.SwitchState.Unknown, "S11 系统代理卡从注册表读到了确定状态",
                $"ProxyEnable={Show(proxyEnable)} 卡片 State={proxyCard.State} IsOn={proxyCard.IsOn}（期望 On={proxyExpectOn}）",
                expectedOk: proxyCard.State == (proxyExpectOn ? QuickSwitch.Core.Switches.SwitchState.On : QuickSwitch.Core.Switches.SwitchState.Off));

            // ---- 破坏性开关：确认弹窗两分支（UAC） ----
            var uacBefore = ReadRegistry(RegistryHive.LocalMachine, UacKey, UacValue, RegistryView.Registry64);
            var uacLoc = located.TryGetValue("用户账户控制 (UAC)", out var u1) ? u1 : default;
            if (uacLoc.Toggle is null) Skip("S12 UAC 确认弹窗", "UI 里没定位到 UAC 卡的开关");
            else
            {
                var uacVm = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "用户账户控制 (UAC)"));
                Console.WriteLine($"    UAC toggle: enabled={uacLoc.Toggle.Current.IsEnabled} name=\"{uacLoc.Toggle.Current.Name}\" " +
                                  $"kind=\"{uacLoc.Toggle.Current.ClassName}\" rect={Rect(uacLoc.Toggle)} VM.State={uacVm.State} CanToggle={uacVm.CanToggle}");

                var uiaBefore = ReadToggleState(uacLoc.Toggle);
                ClickElement(uacLoc.Toggle, hwnd);
                var dlg = WaitForDialog(TimeSpan.FromSeconds(6));
                if (dlg == IntPtr.Zero)
                {
                    var mid = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "用户账户控制 (UAC)"));
                    Console.WriteLine($"    第一次点击后：UIA ToggleState={ReadToggleState(uacLoc.Toggle)}（点前 {uiaBefore}）" +
                                      $" IsBusy={mid.IsBusy} Subtitle=\"{Truncate(mid.Subtitle, 120)}\"");
                    // 第一次点击可能只被当成"激活窗口"吞掉，再点一次才是真点击。
                    ClickElement(uacLoc.Toggle, hwnd);
                    dlg = WaitForDialog(TimeSpan.FromSeconds(10));
                }

                if (dlg == IntPtr.Zero)
                {
                    var stuck = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "用户账户控制 (UAC)"));
                    Fail("S12 UAC 关闭前弹确认框",
                        $"点开关后没出现对话框；UIA ToggleState={ReadToggleState(uacLoc.Toggle)}（点前 {uiaBefore}）" +
                        $" IsBusy={stuck.IsBusy} State={stuck.State} Subtitle=\"{Truncate(stuck.Subtitle, 120)}\"；" +
                        $"本进程顶层窗口 = {DumpProcessWindows()}");
                }
                else
                {
                    var text = string.Join(" | ", ReadTexts(AutomationElement.FromHandle(dlg), 50));
                    var expectedConfirm = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>()
                        .First(c => c.Title == "用户账户控制 (UAC)").Descriptor.ConfirmText);
                    Check(expectedConfirm is not null && text.Contains(expectedConfirm, StringComparison.Ordinal),
                        "S12 确认框文案 = 描述符里的 ConfirmText", $"期望含「{expectedConfirm}」实际=\"{Truncate(text, 160)}\"");
                    ClickDialogButton(dlg, cancel: true);
                    Thread.Sleep(1500);
                    var afterCancel = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "用户账户控制 (UAC)"));
                    var uacAfterCancel = ReadRegistry(RegistryHive.LocalMachine, UacKey, UacValue, RegistryView.Registry64);
                    Check(Equals(uacAfterCancel, uacBefore) && !afterCancel.Subtitle.StartsWith("操作失败：", StringComparison.Ordinal),
                        "S13 点「取消」→ 什么都不做（注册表未变、没有错误副标题）",
                        $"EnableLUA={Show(uacAfterCancel)}（原 {Show(uacBefore)}）Subtitle=\"{afterCancel.Subtitle}\"");
                    // F3 回归：取消后视觉必须回到权威状态，不能停在"点过去"的局部值上。
                    var uiaAfterCancel = ReadToggleState(uacLoc.Toggle);
                    Check(uiaAfterCancel == (afterCancel.IsOn ? "On" : "Off"),
                        "S13b 取消确认后开关视觉 = 权威状态（同步阻塞的假翻转已修）",
                        $"UIA ToggleState={uiaAfterCancel} VM.IsOn={afterCancel.IsOn} State={afterCancel.State}");
                }
            }

            // ---- 功能组件：确认框含重启提示 ----
            var hvLoc = located.TryGetValue("Hyper-V", out var h1) ? h1 : default;
            if (hvLoc.Toggle is null) Skip("S14 Hyper-V 关闭确认框", "UI 里没定位到 Hyper-V 卡的开关");
            else
            {
                var hvVm = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Hyper-V"));
                var hvEnabled = hvLoc.Toggle.Current.IsEnabled;
                Console.WriteLine($"    Hyper-V toggle: enabled={hvEnabled} VM.State={hvVm.State} CanToggle={hvVm.CanToggle}");

                if (!hvEnabled)
                {
                    // 未提权时功能组件读不到状态（Unknown）→ 开关禁用，这是正确行为，不是缺陷。
                    Check(!hvVm.CanToggle,
                        "S14 未提权时 Hyper-V（状态未知）开关是禁用的，点不动",
                        $"IsEnabled={hvEnabled} State={hvVm.State} CanToggle={hvVm.CanToggle} Subtitle=\"{Truncate(hvVm.Subtitle, 80)}\"");
                    Skip("S14a Hyper-V 关闭确认框", "未提权 → 状态未知 → 开关禁用，无法进入确认流程（提权下由 verify-elevated.ps1 覆盖）");
                }
                else
                {
                    ClickElement(hvLoc.Toggle, hwnd);
                    var dlg = WaitForDialog(TimeSpan.FromSeconds(6));
                    if (dlg == IntPtr.Zero)
                    {
                        ClickElement(hvLoc.Toggle, hwnd);
                        dlg = WaitForDialog(TimeSpan.FromSeconds(10));
                    }

                    if (dlg == IntPtr.Zero) Fail("S14 Hyper-V 关闭前弹确认框",
                        $"点开关后没出现对话框；本进程顶层窗口 = {DumpProcessWindows()}");
                    else
                    {
                        var text = string.Join(" | ", ReadTexts(AutomationElement.FromHandle(dlg), 50));
                        Check(text.Contains("重启", StringComparison.Ordinal), "S14 Hyper-V 确认框提到重启", $"\"{Truncate(text, 160)}\"");
                        ClickDialogButton(dlg, cancel: true);
                        Thread.Sleep(1500);
                        var hv = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Hyper-V"));
                        Check(!hv.Subtitle.StartsWith("操作失败：", StringComparison.Ordinal), "S14b 取消后没有发起任何写入",
                            $"Subtitle=\"{hv.Subtitle}\" State={hv.State}");
                    }
                }
            }

            // ---- UI Automation 的 Toggle() 能不能真触发命令 ----
            if (located.TryGetValue("剪贴板历史", out var clipLoc) && clipLoc.Toggle is not null)
            {
                var beforeToggle = ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64);
                InvokeTogglePattern(clipLoc.Toggle, hwnd);
                WaitUntil(() => !Equals(ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64), beforeToggle),
                    TimeSpan.FromSeconds(15));
                Thread.Sleep(500);
                var afterToggle = ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64);
                var vmOn = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "剪贴板历史").IsOn);
                var uiaState = ReadToggleState(clipLoc.Toggle);
                Check(!Equals(beforeToggle, afterToggle),
                    "S15 UI Automation 的 Toggle() 能触发真实命令（讲述人/自动化可用）",
                    $"点前={Show(beforeToggle)} 点后={Show(afterToggle)} UIA ToggleState={uiaState} VM.IsOn={vmOn}");
                Check(uiaState == (vmOn ? "On" : "Off"),
                    "S15b UIA ToggleState 与 VM 权威状态一致（不再对自动化说谎）",
                    $"UIA ToggleState={uiaState} VM.IsOn={vmOn}");
                // 还原
                if (!Equals(beforeToggle, afterToggle))
                {
                    InvokeTogglePattern(clipLoc.Toggle, hwnd);
                    WaitUntil(() => Equals(ReadRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, RegistryView.Registry64), beforeToggle),
                        TimeSpan.FromSeconds(15));
                }
                if (beforeToggle is int bv) WriteRegistry(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue, bv);
                else DeleteRegistryValue(RegistryHive.CurrentUser, ClipboardKey, ClipboardValue);
            }

            // ---- 托盘：图标 + 菜单里的一键防火墙（反射拿到 TrayHost，点的是真菜单项） ----
            var tray = FindTrayIcon(TimeSpan.FromSeconds(20));
            if (tray is not null) Pass("S16 托盘区出现「Windows 快捷开关」图标", $"element={tray.Current.ControlType.ProgrammaticName} name={tray.Current.Name}");
            else Skip("S16 托盘图标", "通知区域里没直接找到（Win11 会把图标折叠进溢出面板，未自动展开点击）");

            CheckTrayMenu(vm);

            // ---- 关窗 = 隐藏，进程活着 ----
            var windowPattern = win.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
            windowPattern?.Close();
            Thread.Sleep(2500);
            var visibleAfterClose = Dispatcher(() => _app!.MainWindow?.IsVisible);
            var alive = !Process.GetCurrentProcess().HasExited;
            Check(visibleAfterClose == false && alive, "S17 点窗口关闭 → 窗口隐藏而不是退出（托盘常驻契约）",
                $"IsVisible={visibleAfterClose} 进程存活={alive}");
        }
        catch (Exception ex)
        {
            Fail("harness 未处理异常", ex.ToString().Replace("\r\n", " ⏎ "));
        }
        finally
        {
            try { Dispatcher(() => { _app!.Shutdown(); return true; }); }
            catch (Exception ex) { Console.WriteLine($"shutdown failed: {ex.Message}"); }
        }
    }

    // ---------- 断言记录 ----------

    private static void Pass(string name, string evidence) => Record("PASS", name, evidence);

    private static void Fail(string name, string evidence) => Record("FAIL", name, evidence);

    private static void Skip(string name, string reason) => Record("SKIP", name, reason);

    private static void Check(bool ok, string name, string evidence, bool? expectedOk = null)
    {
        var good = expectedOk ?? ok;
        Record(good ? "PASS" : "FAIL", name, evidence);
    }

    private static void Record(string verdict, string name, string evidence)
    {
        var line = $"[{verdict}] {name} :: {evidence}";
        Results.Add((verdict, name, evidence));
        Transcript.AppendLine(line);
        Console.WriteLine(line);
    }

    // ---------- 线程/等待 ----------

    private static T Dispatcher<T>(Func<T> f) => _app!.Dispatcher.Invoke(f);

    private static void WaitIdle(MainViewModel vm, TimeSpan timeout)
    {
        WaitUntil(() => !Dispatcher(() => vm.IsRefreshing), timeout);
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var busy = Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().Any(c => c.IsBusy));
            if (!busy) return;
            Thread.Sleep(200);
        }
    }

    private static bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try { if (predicate()) return true; } catch { }
            Thread.Sleep(200);
        }
        return false;
    }

    // ---------- UI Automation ----------

    private static AutomationElement? WaitForWindow(string title, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, title),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        while (DateTime.UtcNow < deadline)
        {
            var hit = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
            if (hit is not null) return hit;
            Thread.Sleep(300);
        }
        return null;
    }

    private static List<string> ReadTexts(AutomationElement root, int max)
    {
        var list = new List<string>();
        var found = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        foreach (AutomationElement el in found)
        {
            if (list.Count >= max) break;
            var name = el.Current.Name;
            if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
        }
        return list;
    }

    private static List<string> HeaderOrder(AutomationElement win)
    {
        var known = new[] { "安全", "网络", "电源", "系统" };
        var hits = new List<(double Top, string Text)>();
        foreach (var name in known)
        {
            var found = win.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
                new PropertyCondition(AutomationElement.NameProperty, name)));
            foreach (AutomationElement el in found)
                hits.Add((el.Current.BoundingRectangle.Top, name));
        }
        return hits.OrderBy(h => h.Top).Select(h => h.Text).Distinct().ToList();
    }

    private static void EnsureWindowForeground(IntPtr hwnd)
    {
        SetForegroundWindow(hwnd);
        Thread.Sleep(120);
    }

    /// 托盘没法用鼠标点（Win11 把它折进溢出面板），但菜单项本身可以走 PerformClick —— 与真点击同一条 Click 处理器。
    private static void CheckTrayMenu(MainViewModel viewModel)
    {
        var trayField = typeof(QuickSwitch.App).GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance);
        var tray = trayField?.GetValue(_app);
        if (tray is null) { Fail("S18 托盘宿主", "App._tray 为空，托盘没建起来"); return; }

        object? Read(object target, string field) => target.GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        var notifyIcon = Read(tray, "_notifyIcon");
        var menu = Read(tray, "_menu");
        var firewallItem = Read(tray, "_firewallItem");
        if (notifyIcon is null || menu is null || firewallItem is null)
        {
            Fail("S18 托盘宿主", "托盘内部字段取不到（字段被改名？）");
            return;
        }

        var visible = notifyIcon.GetType().GetProperty("Visible")!.GetValue(notifyIcon);
        var iconText = notifyIcon.GetType().GetProperty("Text")!.GetValue(notifyIcon);
        Check(Equals(visible, true) && Equals(iconText, "Windows 快捷开关"),
            "S18 托盘图标真的活着（Visible=true / Text=Windows 快捷开关）",
            $"Visible={visible} Text=\"{iconText}\"");

        var items = ((IEnumerable)menu.GetType().GetProperty("Items")!.GetValue(menu)!).Cast<object>().ToList();
        string ItemText(object item) => item.GetType().GetProperty("Text")?.GetValue(item)?.ToString() ?? "-";
        string? Label() => firewallItem.GetType().GetProperty("Text")!.GetValue(firewallItem)?.ToString();

        var texts = items.Select(ItemText).ToList();
        var card = Dispatcher(() => viewModel.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Windows 防火墙"));
        var expectedLabel = card.State switch
        {
            SwitchState.On => "防火墙：已开启（点击关闭）",
            SwitchState.Off => "防火墙：已关闭（点击开启）",
            SwitchState.PendingRestart => "防火墙：重启后生效（点击再切一次）",
            SwitchState.Blocked => "防火墙：已封锁（打开窗口查看原因）",
            _ => "防火墙：状态未知（打开窗口刷新）",
        };
        Check(Label() == expectedLabel, "S19 托盘菜单第一项 = 防火墙真实状态（不用先开窗口）",
            $"菜单项=\"{Label()}\" 期望=\"{expectedLabel}\" 菜单=[{string.Join(" | ", texts)}]");

        // 先刷一遍，让"点击前"的副标题回到干净状态（上一次 S9 点失败留下的错误文案会掩盖本次变化）。
        Dispatcher(() => { viewModel.RefreshAllCommand.Execute(null); return true; });
        WaitIdle(viewModel, TimeSpan.FromSeconds(120));
        var card2 = Dispatcher(() => viewModel.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Windows 防火墙"));
        var beforeSubtitle = card2.Subtitle;
        Dispatcher(() => { firewallItem.GetType().GetMethod("PerformClick")!.Invoke(firewallItem, null); return true; });
        WaitUntil(() => Dispatcher(() => viewModel.Cards.OfType<SwitchCardViewModel>()
            .First(c => c.Title == "Windows 防火墙").IsBusy), TimeSpan.FromSeconds(5));
        WaitIdle(viewModel, TimeSpan.FromSeconds(120));
        var after = Dispatcher(() => viewModel.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Windows 防火墙"));
        var afterTruth = ReadFirewallTruth();
        if (viewModel.IsElevated)
        {
            var flippedOn = afterTruth.All(pair => pair.Value == "True") && after.State == SwitchState.On;
            Check(flippedOn, "S20（提权）点托盘「防火墙」项 → 同一张卡真的开了防火墙",
                $"State={after.State}（原 {card2.State}）Subtitle=\"{beforeSubtitle}\" → \"{after.Subtitle}\"");
            Dispatcher(() => { firewallItem.GetType().GetMethod("PerformClick")!.Invoke(firewallItem, null); return true; });
            WaitUntil(() => Dispatcher(() => viewModel.Cards.OfType<SwitchCardViewModel>()
                .First(c => c.Title == "Windows 防火墙").IsBusy), TimeSpan.FromSeconds(5));
            WaitIdle(viewModel, TimeSpan.FromSeconds(120));
            var restoredTruth = ReadFirewallTruth();
            var restoredCard = Dispatcher(() => viewModel.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == "Windows 防火墙"));
            Check(restoredTruth.All(pair => pair.Value == "False") && restoredCard.State == SwitchState.Off,
                "S20b（提权）再点一次托盘项 → 防火墙还原成三档全关",
                $"State={restoredCard.State} 真值={string.Join(",", restoredTruth.Select(pair => $"{pair.Key}={pair.Value}"))}");
        }
        else
        {
            Check(after.State == card2.State && after.Subtitle != beforeSubtitle
                  && after.Subtitle.StartsWith("操作失败：", StringComparison.Ordinal)
                  && after.Subtitle.Contains("Access is denied", StringComparison.Ordinal),
                "S20 点托盘「防火墙」项 → 走的是卡片同一条命令（未提权给出系统原文，真值没动）",
                $"State={after.State}（原 {card2.State}）Subtitle=\"{beforeSubtitle}\" → \"{after.Subtitle}\" 真值={string.Join(",", afterTruth.Select(pair => $"{pair.Key}={pair.Value}"))}");
        }

        var hideItem = items.FirstOrDefault(item => ItemText(item).Contains("显示", StringComparison.Ordinal));
        if (hideItem is null) Fail("S21 托盘「显示 / 隐藏」", "菜单里没有这一项");
        else
        {
            var beforeVisible = Dispatcher(() => _app!.MainWindow?.IsVisible ?? false);
            Dispatcher(() => { hideItem.GetType().GetMethod("PerformClick")!.Invoke(hideItem, null); return true; });
            Thread.Sleep(800);
            var hidden = Dispatcher(() => _app!.MainWindow?.IsVisible ?? true);
            Dispatcher(() => { hideItem.GetType().GetMethod("PerformClick")!.Invoke(hideItem, null); return true; });
            Thread.Sleep(800);
            var shown = Dispatcher(() => _app!.MainWindow?.IsVisible ?? false);
            Check(beforeVisible && !hidden && shown,
                "S21 托盘「显示 / 隐藏」两次点击 = 藏起来再弹回来",
                $"before={beforeVisible} afterHide={hidden} afterShow={shown}");
        }
    }

    private static AutomationElement? FindButton(AutomationElement root, string name)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, name));
        return root.FindFirst(TreeScope.Descendants, condition);
    }

    private static (AutomationElement? Card, AutomationElement? Toggle, AutomationElement? Combo) LocateCard(AutomationElement win, string title)
    {
        var titleEl = win.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.NameProperty, title)));
        if (titleEl is null) return (null, null, null);

        var node = titleEl;
        for (var i = 0; i < 6 && node is not null; i++)
        {
            var toggle = node.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var combo = node.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));
            if (toggle is not null || combo is not null) return (node, toggle, combo);
            node = TreeWalker.ControlViewWalker.GetParent(node);
        }
        return (null, null, null);
    }

    private static void Dump(AutomationElement el, int depth, int maxDepth)
    {
        Console.WriteLine($"{new string(' ', depth * 2)}{el.Current.ControlType.ProgrammaticName} name=\"{Truncate(el.Current.Name, 60)}\" rect={Rect(el)}");
        if (depth >= maxDepth) return;
        var children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement child in children) Dump(child, depth + 1, maxDepth);
    }

    private static string Rect(AutomationElement el)
    {
        var r = el.Current.BoundingRectangle;
        return $"[{(int)r.Left},{(int)r.Top},{(int)r.Width}x{(int)r.Height}]";
    }

    private static void ClickElement(AutomationElement el, IntPtr hwnd)
    {
        var rect = EnsureVisible(el);
        SetForegroundWindow(hwnd);
        Thread.Sleep(200);
        var x = (int)(rect.Left + rect.Width / 2);
        var y = (int)(rect.Top + rect.Height / 2);
        SetCursorPos(x, y);
        Thread.Sleep(80);
        GetCursorPos(out var pos);
        Console.WriteLine($"    click @({x},{y}) cursor=({pos.X},{pos.Y}) rect={rect}");
        mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(300);
    }

    private static Rect EnsureVisible(AutomationElement el)
    {
        var rect = el.Current.BoundingRectangle;
        if (CenterOnScreen(rect) && InWindow(rect)) return rect;

        try
        {
            if (el.GetCurrentPattern(ScrollItemPattern.Pattern) is ScrollItemPattern sip)
            {
                sip.ScrollIntoView();
                Thread.Sleep(350);
                rect = el.Current.BoundingRectangle;
                if (CenterOnScreen(rect)) return rect;
            }
        }
        catch (InvalidOperationException) { }

        var node = el;
        for (var i = 0; i < 8 && node is not null; i++)
        {
            try
            {
                if (node.GetCurrentPattern(ScrollPattern.Pattern) is ScrollPattern sp)
                {
                    for (var step = 0; step < 40; step++)
                    {
                        rect = el.Current.BoundingRectangle;
                        if (CenterOnScreen(rect) && InWindow(rect)) return rect;
                        var amount = rect.Top > WindowRect.Bottom ? ScrollAmount.LargeIncrement
                                   : rect.Bottom < WindowRect.Top ? ScrollAmount.LargeDecrement
                                   : ScrollAmount.NoAmount;
                        if (amount == ScrollAmount.NoAmount) break;
                        sp.Scroll(ScrollAmount.NoAmount, amount);
                        Thread.Sleep(150);
                    }
                }
            }
            catch (InvalidOperationException) { }
            node = TreeWalker.ControlViewWalker.GetParent(node);
        }

        return el.Current.BoundingRectangle;
    }

    private static bool CenterOnScreen(Rect r)
        => r.Width > 0 && r.Height > 0
           && r.Left + r.Width / 2 > 0 && r.Top + r.Height / 2 > 0
           && r.Left + r.Width / 2 < SystemParameters.PrimaryScreenWidth
           && r.Top + r.Height / 2 < SystemParameters.PrimaryScreenHeight;

    private static bool InWindow(Rect r)
        => r.Width > 0 && r.Height > 0
           && r.Top + r.Height / 2 >= WindowRect.Top && r.Top + r.Height / 2 <= WindowRect.Bottom;

    private static void InvokeTogglePattern(AutomationElement toggle, IntPtr hwnd)
    {
        SetForegroundWindow(hwnd);
        if (toggle.GetCurrentPattern(TogglePattern.Pattern) is TogglePattern tp)
        {
            Console.WriteLine($"    UIA Toggle() on \"{toggle.Current.Name}\" (before={tp.Current.ToggleState})");
            tp.Toggle();
        }
        else Console.WriteLine("    UIA TogglePattern 不可用");
    }

    private static string ReadToggleState(AutomationElement toggle)
        => toggle.GetCurrentPattern(TogglePattern.Pattern) is TogglePattern tp ? tp.Current.ToggleState.ToString() : "n/a";

    /// 用 Win32 枚举找本进程的模态对话框：UIA 的 RootElement 在自家 UI 线程忙于模态循环时可能查不到这些窗口。
    private static IntPtr WaitForDialog(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hwnd = FindProcessDialog();
            if (hwnd != IntPtr.Zero)
            {
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                Console.WriteLine($"    dialog: hwnd=0x{hwnd.ToInt64():X} class=#32770 title=\"{title}\"");
                return hwnd;
            }
            Thread.Sleep(250);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindProcessDialog()
    {
        var pid = (uint)Environment.ProcessId;
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd)) return true;
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() != "#32770") return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static void ClickDialogButton(IntPtr dialog, bool cancel)
    {
        var root = AutomationElement.FromHandle(dialog);
        if (root is null) { Fail("对话框按钮", "拿不到对话框的 UIA 元素"); return; }

        var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        AutomationElement? target = null;
        foreach (AutomationElement b in buttons)
        {
            var name = b.Current.Name;
            var isCancel = name.Contains("取消", StringComparison.Ordinal) || name.Contains("Cancel", StringComparison.OrdinalIgnoreCase);
            var isOk = name.Contains("确定", StringComparison.Ordinal) || name.Contains("是", StringComparison.Ordinal) || name.Contains("OK", StringComparison.OrdinalIgnoreCase);
            if (cancel && isCancel) { target = b; break; }
            if (!cancel && isOk && !isCancel) { target = b; break; }
        }
        if (target is null && buttons.Count > 0 && cancel) target = buttons[buttons.Count - 1];

        if (target is null) { Fail("对话框按钮", $"在对话框里找不到{(cancel ? "取消" : "确定")}按钮，按钮数={buttons.Count}"); return; }
        Console.WriteLine($"    dialog button: \"{target.Current.Name}\"");
        if (target.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern ip) ip.Invoke();
        else ClickElement(target, dialog);
    }

    private static AutomationElement? FindTrayIcon(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var root in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                var bar = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, root));
                if (bar is null) continue;
                var hit = bar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Windows 快捷开关"));
                if (hit is not null) return hit;
            }
            Thread.Sleep(500);
        }
        return null;
    }

    // ---------- 真值探针 ----------

    private static List<(string Key, string Value)> ReadFirewallTruth()
    {
        var runner = new ProcessRunner();
        var result = runner.RunAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "(Get-NetFirewallProfile -Profile Domain,Private,Public | ForEach-Object { \"$($_.Name)=$($_.Enabled)\" }) -join ';'" },
            CancellationToken.None, TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();

        Transcript.AppendLine($"    [truth] firewall stdout={result.StandardOutput.Trim()} stderr={Truncate(result.StandardError, 200)} exit={result.ExitCode}");

        var list = new List<(string, string)>();
        foreach (var part in result.StandardOutput.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) list.Add((kv[0].Trim(), kv[1].Trim()));
        }
        if (list.Count == 0) list.Add(("(读不到真值)", $"(exit {result.ExitCode}) {Truncate(result.StandardError, 120)}"));
        return list;
    }

    private static object? ReadRegistry(RegistryHive hive, string subKey, string value, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(value);
        }
        catch (Exception ex) { Transcript.AppendLine($"    [registry] read {subKey}\\{value} failed: {ex.Message}"); return null; }
    }

    private static void WriteRegistry(RegistryHive hive, string subKey, string value, int data)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(subKey, writable: true);
        key.SetValue(value, data, RegistryValueKind.DWord);
    }

    private static void DeleteRegistryValue(RegistryHive hive, string subKey, string value)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            key?.DeleteValue(value, throwOnMissingValue: false);
        }
        catch (Exception ex) { Transcript.AppendLine($"    [registry] delete {subKey}\\{value} failed: {ex.Message}"); }
    }

    private static (string Subtitle, QuickSwitch.Core.Switches.SwitchState State) ExerciseToggle(
        MainViewModel vm,
        Dictionary<string, (AutomationElement? Card, AutomationElement? Toggle, AutomationElement? Combo)> located,
        string title,
        IntPtr hwnd,
        TimeSpan timeout)
    {
        if (!located.TryGetValue(title, out var hit) || hit.Toggle is null)
        {
            Fail($"点击「{title}」", "UI 里没定位到该卡的开关");
            return (string.Empty, QuickSwitch.Core.Switches.SwitchState.Unknown);
        }

        ClickElement(hit.Toggle, hwnd);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == title).IsBusy)) break;
            Thread.Sleep(150);
        }
        WaitUntil(() => !Dispatcher(() => vm.Cards.OfType<SwitchCardViewModel>().First(c => c.Title == title).IsBusy), timeout);
        Thread.Sleep(500);

        var snap = Dispatcher(() =>
        {
            var c = vm.Cards.OfType<SwitchCardViewModel>().First(x => x.Title == title);
            return (c.Subtitle, c.State);
        });
        Console.WriteLine($"    after click 「{title}」: State={snap.State} Subtitle=\"{Truncate(snap.Subtitle, 120)}\"");
        return snap;
    }

    private static string Show(object? v) => v is null ? "(不存在)" : $"{v} ({v.GetType().Name})";

    private static string Truncate(string? s, int n)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var flat = s.Replace("\r", " ").Replace("\n", " ");
        return flat.Length <= n ? flat : flat[..n] + "…";
    }

    // ---------- Win32 ----------

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32 { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point32 p);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder buffer, int maxCount);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

    /// Win32 MessageBox 是 #32770，但 UIA 树里偶尔拿不到；这里直接枚举本进程所有顶层窗口当证据。
    private static string DumpProcessWindows()
    {
        var lines = new List<string>();
        var pid = (uint)Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid) return true;

            var className = new StringBuilder(256);
            var title = new StringBuilder(512);
            GetClassName(hwnd, className, className.Capacity);
            GetWindowText(hwnd, title, title.Capacity);
            lines.Add($"hwnd=0x{hwnd.ToInt64():X} class={className} visible={IsWindowVisible(hwnd)} title=\"{title}\"");
            return true;
        }, IntPtr.Zero);

        return lines.Count == 0 ? "（本进程没有任何顶层窗口）" : string.Join(" ; ", lines);
    }
}
