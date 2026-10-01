using System.Windows;
using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;

namespace QuickSwitch;

public partial class App : System.Windows.Application
{
    private TrayHost? _tray;
    private MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 没有任何 UI 兜底时，一个漏网的异常就会以非 0 退出码击毙托盘进程。
        DispatcherUnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine(args.Exception);
            args.Handled = true;
        };

        var powerShell = new PowerShellRunner(new ProcessRunner());
        // powercfg 也必须走 PowerShellRunner：直启会让中文方案名按 CP936 出来被 UTF-8 误解码。
        var powerCfg = new PowerCfg(powerShell);
        var sessionOwner = new SessionOwnerGuard(new WindowsUserSidSource()).Evaluate();
        var registry = SwitchRegistry.CreateDefault(
            powerShell, new WindowsRegistryStore(), new Win32SettingsNotifier(), powerCfg, sessionOwner);
        var cards = registry.All.Select(item => CardViewModelFactory.Create(item, new MessageBoxConfirmationPrompt()));
        var viewModel = new MainViewModel(cards, AdminContext.IsElevated());

        _window = new MainWindow { DataContext = viewModel };
        _tray = new TrayHost(_window, viewModel);

        // 规格 §8：窗口显示时就重读。托盘里藏了几分钟后弹出来，卡片必须不是旧状态。
        _window.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true)
                _ = RefreshQuietly(viewModel);
        };

        MainWindow = _window;
        _window.Show();

        // 启动刷新必须被观察：丢弃 Task 会让刷新失败彻底静默（卡片永远停在"未知"）。
        await RefreshQuietly(viewModel);
    }

    private static async Task RefreshQuietly(MainViewModel viewModel)
    {
        try
        {
            await viewModel.RefreshAllCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
