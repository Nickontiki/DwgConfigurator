using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DwgConfigurator.AdminApp.ViewModels;

/// <summary>
/// ViewModel principale dell'AdminApp. Attualmente minimale:
/// la navigazione avviene tramite TabControl in MainWindow.xaml,
/// ogni tab ha il proprio DataContext/ViewModel.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
