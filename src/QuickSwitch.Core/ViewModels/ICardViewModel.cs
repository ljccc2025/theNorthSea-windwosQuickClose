namespace QuickSwitch.Core.ViewModels;

/// 卡片的最小契约：分组用来自动分组，刷新由主视图统一驱动。两种卡片（开关 / 三选一）都走它。
public interface ICardViewModel
{
    string Group { get; }

    Task RefreshAsync(CancellationToken cancellationToken);
}
