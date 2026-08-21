using System.Windows;
using DwgConfigurator.Shared.Data;

namespace DwgConfigurator.ConfiguratorApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Inizializza il database al primo avvio (crea tabelle se non esistono)
        DatabaseInitializer.Initialize();
    }
}
