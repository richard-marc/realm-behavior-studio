using System.Windows;
namespace Studio.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) => { MessageBox.Show(a.Exception.GetBaseException().Message, "Realm Behavior Studio"); a.Handled = true; };
        var window = new MainWindow(e.Args); MainWindow = window; window.Show();
    }
}
