using QuickSwitch.Core.ViewModels;

namespace QuickSwitch.Tests.Fakes;

internal sealed class FakeConfirmationPrompt : IConfirmationPrompt
{
    public bool Answer { get; set; } = true;

    public List<string> Questions { get; } = [];

    public bool Confirm(string message)
    {
        Questions.Add(message);
        return Answer;
    }
}

