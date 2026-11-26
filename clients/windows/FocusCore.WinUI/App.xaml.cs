using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FocusCore.WinUI;

public partial class App : Application
{
    public new static App Current => (App)Application.Current;

    public MainWindow? MainWindow { get; set; }

    public DispatcherQueue? DispatcherQueue { get; set; }

    public App()
    {
        this.InitializeComponent();
    }
}
