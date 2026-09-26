using System.Windows;
using MagicCar.SiteDoctor.Recovery;

namespace MagicCar.SiteDoctor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0)
        {
            // Any arguments are interpreted only by the strict repair parser; unknown modes never open a command shell.
            Shutdown(ElevatedRepair.Run(e.Args));
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
