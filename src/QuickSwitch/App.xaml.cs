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
        var powerCfg = new PowerCfg(new ProcessRunner());
        var sessionOwner = new SessionOwnerGuard(new WindowsUserSidSource()).Evaluate();
        var registry = SwitchRegistry.CreateDefault(
            powerShell, new WindowsRegistryStore(), new Win32SettingsNotifier(), powerCfg, sessionOwner);
        var cards = registry.All.Select(CardViewModelFactory.Create);
        var viewModel = new MainViewModel(cards, AdminContext.IsElevated());

        _window = new MainWindow { DataContext = viewModel };
        _tray = new TrayHost(_window);

        MainWindow = _window;
        _window.Show();

        // 启动刷新必须被观察：丢弃 Task 会让刷新失败彻底静默（卡片永远停在"未知"）。
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
