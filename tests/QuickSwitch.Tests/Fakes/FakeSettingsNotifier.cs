using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Tests.Fakes;

internal sealed class FakeSettingsNotifier : ISettingsNotifier
{
    public Exception? Failure { get; set; }

    public int InternetSettingsNotifications { get; private set; }

    public int ClipboardNotifications { get; private set; }

    public void NotifyInternetSettingsChanged()
    {
        if (Failure is not null) throw Failure;
        InternetSettingsNotifications++;
    }

    public void NotifyClipboardSettingChanged()
    {
        if (Failure is not null) throw Failure;
        ClipboardNotifications++;
    }
}
