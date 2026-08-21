using System.Windows;
using Microsoft.Win32;

namespace DwgConfigurator.AdminApp.Views;

public partial class NewGroupDialog : Window
{
    public string GroupName => txtName.Text.Trim();
    public string GroupDescription { get; private set; } = string.Empty;
    public string CartiglioPath { get; private set; } = string.Empty;

    public NewGroupDialog()
    {
        InitializeComponent();
    }

    private void BrowseCartiglio_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "DWG files (*.dwg)|*.dwg|Tutti i file (*.*)|*.*",
            Title = "Seleziona file Cartiglio DWG"
        };

        if (dlg.ShowDialog() == true)
        {
            CartiglioPath = dlg.FileName;
            txtCartiglio.Text = dlg.FileName;
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            MessageBox.Show("Inserire il nome della Gamma.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
