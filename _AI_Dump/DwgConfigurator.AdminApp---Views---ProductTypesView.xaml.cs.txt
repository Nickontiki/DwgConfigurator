using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using DwgConfigurator.AdminApp.ViewModels;

namespace DwgConfigurator.AdminApp.Views;

public partial class ProductTypesView : UserControl
{
    public ProductTypesView()
    {
        InitializeComponent();
    }

    private void LayoutPath_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProductTypeViewModel vm && vm.SelectedProduct != null)
        {
            var dlg = new OpenFileDialog { Filter = "File DWG|*.dwg" };
            if (dlg.ShowDialog() == true)
            {
                vm.UpdateProductLayout(vm.SelectedProduct, dlg.FileName);
            }
        }
    }
}
