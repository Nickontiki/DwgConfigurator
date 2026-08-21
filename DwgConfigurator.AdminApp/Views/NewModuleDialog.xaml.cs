using System.Windows;

namespace DwgConfigurator.AdminApp.Views;

public partial class NewModuleDialog : Window
{
    public string ModuleName => txtModuleName.Text.Trim();
    public string ModuleSigla => txtSigla.Text.Trim().ToUpperInvariant();

    public NewModuleDialog()
    {
        InitializeComponent();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ModuleName))
        {
            MessageBox.Show("Inserire il nome del modulo.", "Attenzione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(ModuleSigla))
        {
            MessageBox.Show("Inserire la sigla.", "Attenzione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
