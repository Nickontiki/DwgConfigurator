using System.Windows;
using System.Windows.Controls;
using DwgConfigurator.AdminApp.ViewModels;

namespace DwgConfigurator.AdminApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl) return;
        if (MainTabControl.SelectedItem is TabItem tab && tab.Content is UserControl uc)
        {
            if (uc.DataContext is FixedAttributeViewModel faVm)
                faVm.RefreshProductTypes();
        }
    }

    private void ProductTypesView_Loaded(object sender, RoutedEventArgs e)
    {

    }
}
