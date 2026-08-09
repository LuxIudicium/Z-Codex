using System.Windows;

namespace ZCodex.App.Views;

// Fenêtre d'aide : référence des menus, actions et raccourcis. Le contenu vit dans
// HelpContent (une ligne = ses deux langues) ; la fenêtre ne fait que le rendre.
// Ouverte non modale depuis Help > Aide et raccourcis (F1).
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        RefreshLanguage();
    }

    /// <summary>Pose (ou repose) les textes dans la langue courante. HelpContent résout la
    /// langue A LA LECTURE : rebinder la liste suffit à tout retraduire. Appelé par
    /// MainWindow.SwitchLanguage quand la fenêtre est déjà ouverte (bascule à chaud).</summary>
    public void RefreshLanguage()
    {
        Title = HelpContent.WindowTitle;
        TitleText.Text = HelpContent.Title;
        IntroText.Text = HelpContent.Intro;
        VersionText.Text = HelpContent.Version;
        SectionsHost.ItemsSource = null;
        SectionsHost.ItemsSource = HelpContent.Sections;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
