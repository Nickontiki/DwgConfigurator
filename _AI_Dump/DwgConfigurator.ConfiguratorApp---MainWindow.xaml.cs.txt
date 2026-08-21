using System.Windows;
using DwgConfigurator.ConfiguratorApp.ViewModels;

namespace DwgConfigurator.ConfiguratorApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
