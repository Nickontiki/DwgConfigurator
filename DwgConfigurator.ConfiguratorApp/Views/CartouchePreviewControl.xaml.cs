using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;

namespace DwgConfigurator.ConfiguratorApp.Views;

public partial class CartouchePreviewControl : UserControl
{
    public CartouchePreviewControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;

        Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.CartiglioPreviewAttributes))
            Refresh();
    }

    private void Refresh()
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            // Converte il dizionario in una lista di KeyValuePair per il binding
            AttributeList.ItemsSource = vm.CartiglioPreviewAttributes;
        }
    }
}
