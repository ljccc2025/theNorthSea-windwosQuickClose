using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuickSwitch.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(IEnumerable<SwitchCardViewModel> cards, bool isElevated)
    {
        Cards = new ObservableCollection<SwitchCardViewModel>(cards);
        IsElevated = isElevated;
    }

    public ObservableCollection<SwitchCardViewModel> Cards { get; }

    public bool IsElevated { get; }

    public string ElevationBadge => IsElevated ? "管理员模式" : "未提权：部分开关不可用";

    [ObservableProperty]
    private bool isRefreshing;

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (IsRefreshing) return;

        IsRefreshing = true;
        try
        {
            foreach (var card in Cards)
                await card.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
