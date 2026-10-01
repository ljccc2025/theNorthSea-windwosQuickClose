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
            // 并行读：11 张卡串行跑 PowerShell 要十几秒，托盘弹出来还是旧状态。
            // 每张卡各有自己的闸门，卡与卡之间没有共享状态，并发是安全的。
            var reads = new Task[Cards.Count];
            for (var index = 0; index < Cards.Count; index++)
                reads[index] = RefreshOneAsync(Cards[index]);

            await Task.WhenAll(reads).ConfigureAwait(true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static async Task RefreshOneAsync(ICardViewModel card)
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
