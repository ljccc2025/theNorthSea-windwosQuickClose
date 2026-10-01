using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Core.ViewModels;

public sealed partial class SwitchCardViewModel : ObservableObject, ICardViewModel
{
    private readonly ISwitch _switch;
    private SwitchState _state = SwitchState.Unknown;

    public SwitchCardViewModel(ISwitch @switch)
    {
        _switch = @switch;
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
        var read = await _switch.ReadAsync(cancellationToken).ConfigureAwait(true);
        State = read.State;
        IsOn = read.State == SwitchState.On;
        Subtitle = read.Detail ?? Descriptor.Subtitle;
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    public async Task ToggleAsync()
    {
        // 命令本身受 CanExecute 保护，但测试会直接调用本方法，守卫不能只靠命令层。
        if (!CanToggle) return;

        var target = State == SwitchState.On ? SwitchState.Off : SwitchState.On;

        IsBusy = true;
        try
        {
            var apply = await _switch.ApplyAsync(target, CancellationToken.None).ConfigureAwait(true);
            var read = await _switch.ReadAsync(CancellationToken.None).ConfigureAwait(true);

            State = read.State;
            IsOn = read.State == SwitchState.On;
            Subtitle = apply.Success
                ? read.Detail ?? Descriptor.Subtitle
                : $"操作失败：{apply.Error ?? ErrorTextFallback}";
        }
        finally
        {
            IsBusy = false;
            // ToggleButton 点击时会把 IsChecked 写成局部值。显式重发通知，
            // 让 OneWay 绑定把权威状态重新推回视觉层；操作失败时开关自动弹回。
            OnPropertyChanged(nameof(IsOn));
        }
    }

    private const string ErrorTextFallback = "未知错误";
}
