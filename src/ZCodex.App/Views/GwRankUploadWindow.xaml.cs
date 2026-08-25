using System.Windows;
using System.Windows.Controls;

namespace ZCodex.App.Views;

/// <summary>Ce qu'on envoie : tout le teambuild, ou l'une de ses compositions cadenassées.
/// <paramref name="LockIndex"/> null = le teambuild entier.</summary>
public sealed record GwRankUploadScope(string Label, int? LockIndex);

/// <summary>
/// Confirmation d'envoi vers GWRank : ce qu'on envoie, et si on le partage.
///
/// Les deux se décident ICI, envoi par envoi, et non dans un réglage global : partager un build
/// avec tout le monde ne doit pas pouvoir arriver par report d'une préférence posée des semaines
/// plus tôt.
/// </summary>
public partial class GwRankUploadWindow : Window
{
    private static string T(string key) => LanguageManager.T(key);

    public const string Private = "private";
    public const string Public  = "public";

    private readonly Func<GwRankUploadScope, string?> _visibilityOf;
    private readonly Func<GwRankUploadScope, string> _nameOf;
    private readonly bool _defaultPublic;

    /// <summary>Visibilité retenue, si <c>DialogResult</c> vaut true. Nommée ainsi et non
    /// « Visibility » : ce nom-là appartient déjà à <see cref="Window"/>.</summary>
    public string SelectedVisibility { get; private set; } = Private;

    /// <summary>Portée retenue, si <c>DialogResult</c> vaut true.</summary>
    public GwRankUploadScope SelectedScope { get; private set; }

    /// <summary>Nom sous lequel déposer, tel que saisi.</summary>
    public string SelectedName { get; private set; } = string.Empty;

    /// <param name="scopes">Portées proposées ; la première est la sélection initiale. Une seule
    /// entrée = rien à choisir, le panneau reste replié.</param>
    /// <param name="nameOf">Nom à proposer pour une portée : celui du dernier dépôt s'il existe,
    /// sinon celui du fichier.</param>
    /// <param name="visibilityOf">Visibilité du dernier dépôt de CETTE portée, ou null si elle n'a
    /// jamais été envoyée : chaque cadenas est un teambuild distinct côté serveur, avec son propre
    /// état de partage.</param>
    /// <param name="defaultPublic">Présélection quand la portée n'a jamais été envoyée.</param>
    public GwRankUploadWindow(IReadOnlyList<GwRankUploadScope> scopes,
                              Func<GwRankUploadScope, string> nameOf,
                              Func<GwRankUploadScope, string?> visibilityOf,
                              bool defaultPublic)
    {
        InitializeComponent();

        _visibilityOf  = visibilityOf;
        _nameOf        = nameOf;
        _defaultPublic = defaultPublic;
        SelectedScope  = scopes[0];

        if (scopes.Count > 1)
        {
            ScopePanel.Visibility = System.Windows.Visibility.Visible;
            foreach (var scope in scopes)
            {
                var radio = new RadioButton
                {
                    Content = scope.Label,
                    GroupName = "Scope",
                    Tag = scope,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                // Handler posé AVANT la sélection : c'est lui qui aligne l'état affiché sur la
                // portée retenue, y compris pour la toute première.
                radio.Checked += Scope_Checked;
                ScopeOptions.Children.Add(radio);
            }
            ((RadioButton)ScopeOptions.Children[0]).IsChecked = true;
        }
        else
        {
            ApplyScope(scopes[0]);
        }
    }

    private void Scope_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: GwRankUploadScope scope }) ApplyScope(scope);
    }

    /// <summary>Aligne le rappel d'état et la case de partage sur la portée choisie : chaque
    /// cadenas a sa propre existence sur GWRank, donc son propre partage.</summary>
    private void ApplyScope(GwRankUploadScope scope)
    {
        SelectedScope = scope;
        // Chaque portée est un build distinct sur GWRank : son nom comme son partage lui
        // appartiennent. Changer de portée remplace donc le nom proposé.
        NameBox.Text = _nameOf(scope);
        var current = _visibilityOf(scope);

        // Re-déposer quelque chose de déjà partagé doit rester coché, sinon un simple
        // aller-retour le dépublierait sans que personne ne l'ait demandé.
        PublicCheck.IsChecked = current is { } v
            ? string.Equals(v, Public, StringComparison.OrdinalIgnoreCase)
            : _defaultPublic;

        if (current is { Length: > 0 })
        {
            CurrentPanel.Visibility = System.Windows.Visibility.Visible;
            CurrentText.Text = T(
                string.Equals(current, Public, StringComparison.OrdinalIgnoreCase)
                    ? "S.GwRank.CurrentlyPublic" : "S.GwRank.CurrentlyPrivate");
        }
        else
        {
            CurrentPanel.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    /// <summary>Un nom vide ne partirait pas : le serveur accepterait un build sans titre, que
    /// personne ne saurait retrouver. On bloque l'envoi plutôt que de déposer ça.</summary>
    private void Name_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (NameWarning is null || SendButton is null) return;   // peut précéder la fin de l'init
        bool empty = NameBox.Text.Trim().Length == 0;
        NameWarning.Visibility = empty ? System.Windows.Visibility.Visible
                                       : System.Windows.Visibility.Collapsed;
        SendButton.IsEnabled = !empty;
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0) return;
        SelectedName = name;
        SelectedVisibility = PublicCheck.IsChecked == true ? Public : Private;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
