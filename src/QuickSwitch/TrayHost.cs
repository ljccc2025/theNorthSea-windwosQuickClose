namespace QuickSwitch;

internal sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;

    public TrayHost(MainWindow window) => _window = window;

    public void Dispose()
    {
        _window.AllowClose = true;
    }
}
