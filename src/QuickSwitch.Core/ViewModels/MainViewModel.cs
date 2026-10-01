using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuickSwitch.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(IEnumerable<ICardViewModel> cards, bool isElevated)
    {
        Cards = new ObservableCollection<ICardViewModel>(cards);
        IsElevated = isElevated;
    }

    public ObservableCollection<ICardViewModel> Cards { get; }

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
            {
                try
                {
                    await card.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // 单卡失败不拖垮其余卡片；卡片内部已把原因写进副标题，这里是最后一道隔离。
                }
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
