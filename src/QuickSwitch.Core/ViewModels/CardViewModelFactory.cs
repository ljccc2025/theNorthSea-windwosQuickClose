using QuickSwitch.Core.Switches;

namespace QuickSwitch.Core.ViewModels;

/// 按开关类型挑卡片壳子。UI 只认卡片接口，加新类型只改这一处。
public static class CardViewModelFactory
{
    public static ICardViewModel Create(ISwitch item, IConfirmationPrompt? confirmationPrompt = null) => item switch
    {
        IChoiceSwitch choice => new ChoiceCardViewModel(choice),
        _ => new SwitchCardViewModel(item, confirmationPrompt),
    };
}
