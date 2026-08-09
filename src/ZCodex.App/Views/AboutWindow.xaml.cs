using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace ZCodex.App.Views;

// Boîte « À propos » : identité + version + crédits. Ouverte modale depuis Help > À propos.
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // Version affichée = <Version> du .csproj (Major.Minor.Patch), lue sur l'assembly.
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "Version 1.0" : $"Version {v.Major}.{v.Minor}.{v.Build}";
    }

    // Ouvre le lien du crédit wiki dans le navigateur par défaut.
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
