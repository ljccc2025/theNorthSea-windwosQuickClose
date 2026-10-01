using System.Drawing;
using System.Windows;
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using Forms = System.Windows.Forms;

namespace QuickSwitch;

internal sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _firewallItem;
    private readonly SwitchCardViewModel? _firewallCard;

    public TrayHost(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        _firewallCard = viewModel.Cards
            .OfType<SwitchCardViewModel>()
            .FirstOrDefault(card => card.Descriptor.Id == FirewallSwitch.Id);

        _firewallItem = new Forms.ToolStripMenuItem("防火墙", null, (_, _) => ToggleFirewall());

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add(_firewallItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("显示 / 隐藏", null, (_, _) => ToggleWindow());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Exit());

        // 菜单一掀开就报当前真值：这是"一个按钮开关防火墙"的原始需求，不该先弹窗口再翻卡片。
        _menu.Opening += (_, _) => UpdateFirewallLabel();

        // 卡片状态在任何线程上都可能变（读写跑在后台），监听后回发到 UI 线程，
        // 否则不掀菜单时文案会一直停在构造那一刻的"状态未知"。
        if (_firewallCard is not null)
        {
            _firewallCard.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SwitchCardViewModel.State) or nameof(SwitchCardViewModel.Subtitle))
                    _window.Dispatcher.BeginInvoke(new Action(UpdateFirewallLabel));
            };
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Windows 快捷开关",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleWindow();

        UpdateFirewallLabel();
    }

    private async void ToggleFirewall()
    {
        if (_firewallCard is null) return;

        try
        {
            // 走的是和卡片同一个命令：确认、闸门、权威回读、失败弹回全都一样。
            await _firewallCard.ToggleAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            UpdateFirewallLabel();
        }
    }

    private void UpdateFirewallLabel()
    {
        if (_firewallCard is null)
        {
            _firewallItem.Text = "防火墙：不可用";
            return;
        }

        _firewallItem.Text = _firewallCard.State switch
        {
            SwitchState.On => "防火墙：已开启（点击关闭）",
            SwitchState.Off => "防火墙：已关闭（点击开启）",
            SwitchState.PendingRestart => "防火墙：重启后生效（点击再切一次）",
            SwitchState.Blocked => "防火墙：已封锁（打开窗口查看原因）",
            _ => "防火墙：状态未知（打开窗口刷新）",
        };
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
