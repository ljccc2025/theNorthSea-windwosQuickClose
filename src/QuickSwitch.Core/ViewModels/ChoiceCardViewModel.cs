using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Core.ViewModels;

/// 三选一开关（电源计划）的卡片：下拉框选方案，不是开关。
public sealed partial class ChoiceCardViewModel : ObservableObject, ICardViewModel
{
    private const string ErrorTextFallback = "未知错误";

    private readonly IChoiceSwitch _switch;
    private SwitchState _state = SwitchState.Unknown;
    private bool _suppressSelectionCallback;

    public ChoiceCardViewModel(IChoiceSwitch @switch)
    {
        _switch = @switch;
        title = @switch.Descriptor.Title;
        group = @switch.Descriptor.Group;
        subtitle = @switch.Descriptor.Subtitle;
        options = new ObservableCollection<string>(@switch.Options);
    }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string group;

    [ObservableProperty]
    private string subtitle;

    [ObservableProperty]
    private ObservableCollection<string> options;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private string? selectedOption;

    public SwitchDescriptor Descriptor => _switch.Descriptor;

    public SwitchState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value)) SelectCommand.NotifyCanExecuteChanged();
        }
    }

    /// 认不出的自建方案（Unknown）依然允许切回三选一；只有被归属守卫封锁时才真禁用。
    public bool CanSelect => !IsBusy && State is not SwitchState.Blocked;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var read = await _switch.ReadAsync(cancellationToken).ConfigureAwait(true);
        var selected = await _switch.ReadSelectedAsync(cancellationToken).ConfigureAwait(true);

        Apply(read, selected);
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    public async Task SelectAsync(string? option)
    {
        // 命令本身受 CanExecute 保护，但测试会直接调用本方法，守卫不能只靠命令层。
        if (!CanSelect || string.IsNullOrEmpty(option)) return;

        IsBusy = true;
        try
        {
            var apply = await _switch.SelectAsync(option, CancellationToken.None).ConfigureAwait(true);
            var read = await _switch.ReadAsync(CancellationToken.None).ConfigureAwait(true);
            var selected = await _switch.ReadSelectedAsync(CancellationToken.None).ConfigureAwait(true);

            Apply(read, selected, apply.Success ? null : $"操作失败：{apply.Error ?? ErrorTextFallback}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// 下拉框改动即切方案。刷新期间同步回权威值时要压住这个回调，否则会自己触发一次无效切换。
    partial void OnSelectedOptionChanged(string? value)
    {
        if (_suppressSelectionCallback || string.IsNullOrEmpty(value)) return;

        SelectCommand.Execute(value);
    }

    private void Apply(SwitchReadResult read, string? selected, string? error = null)
    {
        State = read.State;
        Subtitle = error ?? read.Detail ?? Descriptor.Subtitle;

        _suppressSelectionCallback = true;
        try
        {
            SelectedOption = selected;
        }
        finally
        {
            _suppressSelectionCallback = false;
        }
    }
}
