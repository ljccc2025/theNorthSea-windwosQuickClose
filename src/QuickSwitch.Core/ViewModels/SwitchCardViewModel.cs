using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Core.ViewModels;

public sealed partial class SwitchCardViewModel : ObservableObject, ICardViewModel
{
    private readonly ISwitch _switch;
    private readonly IConfirmationPrompt? _confirmationPrompt;

    /// 同一张卡上「刷新」与「切换」共用的一把闸门：两者一旦交错，
    /// 先启动的旧读会在新写之后落地，界面就会显示与真值相反的状态。
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SwitchState _state = SwitchState.Unknown;

    public SwitchCardViewModel(ISwitch @switch, IConfirmationPrompt? confirmationPrompt = null)
    {
        _switch = @switch;
        _confirmationPrompt = confirmationPrompt;
        title = @switch.Descriptor.Title;
        group = @switch.Descriptor.Group;
        subtitle = @switch.Descriptor.Subtitle;
    }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string group;

    [ObservableProperty]
    private string subtitle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isOn;

    /// 「重启后生效」徽标：只在权威回读说 PendingRestart，或刚写完一个需重启的开关时亮。
    [ObservableProperty]
    private bool showRestartBadge;

    public SwitchDescriptor Descriptor => _switch.Descriptor;

    public SwitchState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value)) ToggleCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanToggle => !IsBusy && State is not (SwitchState.Unknown or SwitchState.Blocked);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var read = await _switch.ReadAsync(cancellationToken).ConfigureAwait(true);
            State = read.State;
            IsOn = read.State == SwitchState.On;
            Subtitle = read.Detail ?? Descriptor.Subtitle;
            ShowRestartBadge = read.State == SwitchState.PendingRestart;
        }
        catch (Exception ex)
        {
            // 读失败只影响这一张卡：状态退回"未知"，原因留在副标题，进程继续活着。
            State = SwitchState.Unknown;
            IsOn = false;
            Subtitle = $"读取失败：{ex.Message}";
        }
        finally
        {
            _gate.Release();
            OnPropertyChanged(nameof(IsOn));
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    public async Task ToggleAsync()
    {
        // 命令本身受 CanExecute 保护，但测试会直接调用本方法，守卫不能只靠命令层。
        if (!CanToggle) return;

        var target = State == SwitchState.On ? SwitchState.Off : SwitchState.On;

        // 关闭破坏性开关（UAC、功能组件）先过确认；用户点取消就当没点过。
        if (target == SwitchState.Off && Descriptor.IsDestructive && !ConfirmDestructive())
            return;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            IsBusy = true;

            var apply = await _switch.ApplyAsync(target, CancellationToken.None).ConfigureAwait(true);
            var read = await _switch.ReadAsync(CancellationToken.None).ConfigureAwait(true);

            State = read.State;
            IsOn = read.State == SwitchState.On;
            Subtitle = apply.Success
                ? read.Detail ?? Descriptor.Subtitle
                : $"操作失败：{apply.Error ?? ErrorTextFallback}";
            ShowRestartBadge = apply.Success && Descriptor.RequiresRestart;
        }
        catch (Exception ex)
        {
            // 命令里逃出去的异常会被 AsyncRelayCommand 抛到 UI 线程击毙整个托盘进程。
            // 这里就地收成一条副标题，界面留在旧状态（既不知道真值，就不假装知道）。
            Subtitle = $"操作失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
            // ToggleButton 点击时会把 IsChecked 写成局部值。显式重发通知，
            // 让 OneWay 绑定把权威状态重新推回视觉层；操作失败时开关自动弹回。
            OnPropertyChanged(nameof(IsOn));
        }
    }

    private bool ConfirmDestructive()
    {
        var message = Descriptor.ConfirmText ?? $"关闭「{Descriptor.Title}」需要重启或会影响现有配置，确定吗？";

        return _confirmationPrompt?.Confirm(message) ?? true;
    }

    private const string ErrorTextFallback = "未知错误";
}
