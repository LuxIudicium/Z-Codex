using System.Windows;
using System.Windows.Media;
using ZCodex.Core.Sync;

namespace ZCodex.App.Views;

/// <summary>
/// « Extras ▸ Réglages GWRank ». Saisie de la clé d'API et du serveur, avec un test de connexion
/// qui ne dépose RIEN (il ne fait que lister, cf. <see cref="GwRankClient.TestConnectionAsync"/>).
/// </summary>
public partial class GwRankSettingsWindow : Window
{
    private static string T(string key) => LanguageManager.T(key);

    private readonly CancellationTokenSource _cts = new();

    /// <summary>Clé telle qu'elle était à l'ouverture, pour reconnaître une saisie NEUVE à la
    /// fermeture (cf. <see cref="OnClosing"/>).</summary>
    private readonly string _initialToken;

    /// <summary>
    /// L'utilisateur a validé : bouton « Enregistrer », ou « Oui » à l'avertissement de fermeture.
    /// C'est CE drapeau que l'appelant lit, et non <c>DialogResult</c> : le bouton Annuler est
    /// <c>IsCancel</c>, et WPF y repose <c>DialogResult</c> à false autour de notre gestionnaire de
    /// clic — un « Oui, enregistrer » donné dans l'avertissement pourrait être annulé en silence,
    /// c'est-à-dire perdre exactement la clé qu'on venait de promettre de garder.
    /// </summary>
    public bool Accepted { get; private set; }

    /// <summary>Valeurs retenues, à persister par l'appelant si <see cref="Accepted"/> vaut true.</summary>
    public string? Token { get; private set; }
    public string? BaseUrl { get; private set; }
    public bool PublicByDefault { get; private set; }

    /// <summary>Site à ouvrir depuis le bandeau d'accueil. L'accueil et non une URL profonde :
    /// GWRank est une application monopage, et le chemin exact de la page des clés lui appartient —
    /// une URL en dur y deviendrait un lien mort sans que Z-Codex puisse le savoir.</summary>
    private const string GwRankSiteUrl = "https://gwrank.com";

    public GwRankSettingsWindow(string? token, string? baseUrl, bool publicByDefault)
    {
        InitializeComponent();
        _initialToken = token?.Trim() ?? string.Empty;
        // Après InitializeComponent : les champs posés en XAML déclencheraient Input_Changed
        // alors que les autres ne sont pas encore construits.
        TokenBox.Text  = token ?? string.Empty;
        ServerBox.Text = baseUrl ?? string.Empty;
        PublicBox.IsChecked = publicByDefault;

        // Pas encore de clé = premier contact : on explique où la prendre. Une fois configurée,
        // le bandeau disparaît (il ne réapparaît pas si l'utilisateur efface son champ en séance :
        // il sait alors déjà de quoi il s'agit).
        if (string.IsNullOrWhiteSpace(token))
        {
            OnboardPanel.Visibility = Visibility.Visible;
            TokenBox.Focus();
        }
    }

    private void OpenSite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(GwRankSiteUrl) { UseShellExecute = true });
        }
        // Aucun navigateur associé, ou stratégie de poste qui le bloque : l'utilisateur peut
        // toujours taper l'adresse à la main, ce n'est pas une raison de faire tomber la fenêtre.
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GwRank] ouverture du site : {ex.Message}"); }
    }

    /// <summary>Toute modification invalide le résultat du test affiché : il porte sur l'ancienne
    /// clé, et le laisser à l'écran ferait croire que la nouvelle est validée.</summary>
    private void Input_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TestResult is null) return;   // TextChanged peut précéder la fin de InitializeComponent
        TestResult.Text = string.Empty;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Text.Trim();
        if (token.Length == 0)
        {
            Show(T("S.GwRank.TestNoToken"), ok: false);
            return;
        }

        TestButton.IsEnabled = false;
        Show(T("S.GwRank.Testing"), ok: null);
        try
        {
            var baseUrl = ServerBox.Text.Trim();
            using var client = new GwRankClient(token, baseUrl.Length == 0 ? null : baseUrl);
            var r = await client.TestConnectionAsync(_cts.Token);

            Show(r.Status switch
            {
                GwRankStatus.Ok           => string.Format(T("S.GwRank.TestOk"),
                                                 r.Value?.Pagination?.TotalCount ?? 0),
                GwRankStatus.Unauthorized => T("S.GwRank.TestUnauthorized"),
                GwRankStatus.Offline      => T("S.GwRank.TestOffline"),
                GwRankStatus.NoToken      => T("S.GwRank.TestNoToken"),
                _                         => string.Format(T("S.GwRank.TestError"), r.Message ?? "?"),
            }, ok: r.IsOk);
        }
        catch (OperationCanceledException) { /* fenêtre fermée pendant le test */ }
        finally
        {
            // La fenêtre peut avoir été fermée pendant l'appel : ne pas toucher un contrôle mort.
            if (IsLoaded) TestButton.IsEnabled = true;
        }
    }

    private void Show(string message, bool? ok)
    {
        TestResult.Text = message;
        TestResult.Foreground = ok switch
        {
            true  => (Brush)FindResource("TextPrimaryBrush"),
            false => Brushes.OrangeRed,
            null  => (Brush)FindResource("TextSecondaryBrush"),
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Capture();
        DialogResult = true;
        Close();
    }

    private void Capture()
    {
        var token   = TokenBox.Text.Trim();
        var baseUrl = ServerBox.Text.Trim();
        Token           = token.Length == 0 ? null : token;
        BaseUrl         = baseUrl.Length == 0 ? null : baseUrl;
        PublicByDefault = PublicBox.IsChecked == true;
        Accepted        = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Filet de sécurité de la clé d'API : fermer sans enregistrer (Annuler, Échap, la croix) la
    /// jetterait en silence, et il faudrait retourner la chercher sur GWRank. Le cas est loin
    /// d'être théorique — « Tester la connexion » répond « Connexion OK », ce qui ressemble
    /// beaucoup à « c'est enregistré », et la fenêtre s'ouvre souvent AU MILIEU d'un envoi, geste
    /// qu'on termine par Échap par réflexe.
    ///
    /// On ne demande rien pour le serveur ou la case de visibilité : ces deux-là se retapent en
    /// trois secondes. La clé, non.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || Accepted) return;   // déjà annulé ailleurs, ou « Enregistrer »

        var typed = TokenBox.Text.Trim();
        if (typed.Length == 0 || typed == _initialToken) return;   // rien de neuf à perdre

        var answer = MessageBox.Show(this, T("S.GwRank.UnsavedBody"), T("S.GwRank.UnsavedTitle"),
                                     MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (answer != MessageBoxResult.Yes) return;

        Capture();   // et rien d'autre : on ne touche pas à DialogResult ici (cf. Accepted)
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
