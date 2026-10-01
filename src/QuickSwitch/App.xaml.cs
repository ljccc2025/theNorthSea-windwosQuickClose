using System.Windows;
using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;

namespace QuickSwitch;

public partial class App : System.Windows.Application
{
    private TrayHost? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        _ = viewModel.RefreshAllCommand.ExecuteAsync(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
