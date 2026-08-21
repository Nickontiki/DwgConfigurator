using System.Windows;
using DwgConfigurator.Shared.Data;

namespace DwgConfigurator.AdminApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DatabaseInitializer.Initialize();
    }
}
