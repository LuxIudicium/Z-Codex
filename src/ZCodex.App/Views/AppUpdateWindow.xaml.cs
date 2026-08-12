using System.Windows;

namespace ZCodex.App.Views;

// « Une nouvelle version de Z-Codex est disponible ». Calquée sur GwUpdateWindow (même mécanique
// oui/non + case « ne plus demander »), avec deux différences : les boutons annoncent ce qu'ils
// font (« Télécharger » plutôt que « Oui »), et la réponse positive ouvre le navigateur au lieu
// de lancer un traitement — l'installation reste à la main de l'utilisateur.
public partial class AppUpdateWindow : Window
{
    public bool ShouldDownload { get; private set; }
    public bool IgnoreChecked => IgnoreBox.IsChecked == true;

    public AppUpdateWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        ShouldDownload = true;
        DialogResult = true;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        ShouldDownload = false;
        DialogResult = false;
    }
}
