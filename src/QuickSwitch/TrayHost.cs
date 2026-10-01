using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace QuickSwitch;

internal sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;

    public TrayHost(MainWindow window)
    {
        _window = window;

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("显示 / 隐藏", null, (_, _) => ToggleWindow());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Windows 快捷开关",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleWindow();
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        _window.AllowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
