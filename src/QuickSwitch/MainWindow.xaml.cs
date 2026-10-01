using System.ComponentModel;
using System.Windows;

namespace QuickSwitch;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose) return;

        e.Cancel = true;
        Hide();
    }
}
