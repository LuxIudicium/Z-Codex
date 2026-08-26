using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
    private readonly Func<GwRankUploadScope, IReadOnlyList<string>> _tagsOf;
    private readonly bool _defaultPublic;

    /// <summary>Visibilité retenue, si <c>DialogResult</c> vaut true. Nommée ainsi et non
    /// « Visibility » : ce nom-là appartient déjà à <see cref="Window"/>.</summary>
    public string SelectedVisibility { get; private set; } = Private;

    /// <summary>Portée retenue, si <c>DialogResult</c> vaut true.</summary>
    public GwRankUploadScope SelectedScope { get; private set; }

    /// <summary>Nom sous lequel déposer, tel que saisi.</summary>
    public string SelectedName { get; private set; } = string.Empty;

    /// <summary>
    /// Étiquettes cochées, dans l'ordre de la liste du serveur.
    ///
    /// ⚠ Renvoyées même quand le build part en privé : décocher le partage ne doit pas jeter des
    /// étiquettes déjà posées, sinon un simple aller-retour privé/public les perdrait. Elles ne
    /// coûtent rien sur un build privé — le serveur les accepte, personne d'autre ne le voit.
    /// </summary>
    public IReadOnlyList<string> SelectedTags { get; private set; } = [];

    /// <param name="scopes">Portées proposées ; la première est la sélection initiale. Une seule
    /// entrée = rien à choisir, le panneau reste replié.</param>
    /// <param name="nameOf">Nom à proposer pour une portée : celui du dernier dépôt s'il existe,
    /// sinon celui du fichier.</param>
    /// <param name="visibilityOf">Visibilité du dernier dépôt de CETTE portée, ou null si elle n'a
    /// jamais été envoyée : chaque cadenas est un teambuild distinct côté serveur, avec son propre
    /// état de partage.</param>
    /// <param name="tagsOf">Étiquettes du dernier dépôt de CETTE portée, pour les représenter
    /// cochées. Chaque cadenas est un build distinct côté serveur : il a les siennes.</param>
    /// <param name="availableTags">La liste FERMÉE rendue par GWRank. Vide (liste jamais lue et
    /// serveur injoignable) = on ne demande rien et on ne bloque rien : mieux vaut un build
    /// partagé sans étiquette qu'un envoi rendu impossible par une liste qu'on n'a pas.</param>
    /// <param name="defaultPublic">Présélection quand la portée n'a jamais été envoyée.</param>
    public GwRankUploadWindow(IReadOnlyList<GwRankUploadScope> scopes,
                              Func<GwRankUploadScope, string> nameOf,
                              Func<GwRankUploadScope, string?> visibilityOf,
                              Func<GwRankUploadScope, IReadOnlyList<string>> tagsOf,
                              IReadOnlyList<string> availableTags,
                              bool defaultPublic)
    {
        InitializeComponent();

        _visibilityOf  = visibilityOf;
        _nameOf        = nameOf;
        _tagsOf        = tagsOf;
        _defaultPublic = defaultPublic;
        SelectedScope  = scopes[0];

        // Les puces AVANT le choix de portée : c'est lui qui les coche, elles doivent exister.
        foreach (var tag in availableTags)
        {
            var pill = new ToggleButton
            {
                Content = tag,
                Tag     = tag,
                Style   = (Style)FindResource("TagPillStyle"),
            };
            pill.Click += TagPill_Click;
            TagPills.Children.Add(pill);
        }

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

        // ⚠ Une étiquette retenue qu'Arka aurait retirée de la liste n'a plus de puce : elle
        // disparaît d'elle-même de la sélection, et c'est bien ce qu'il faut — le serveur la
        // refuserait aujourd'hui.
        var previous = _tagsOf(scope);
        foreach (var pill in Pills())
            pill.IsChecked = previous.Contains((string)pill.Tag!, StringComparer.OrdinalIgnoreCase);

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

        UpdateSendState();
    }

    private IEnumerable<ToggleButton> Pills() => TagPills.Children.OfType<ToggleButton>();

    private List<string> CheckedTags()
        => [.. Pills().Where(p => p.IsChecked == true).Select(p => (string)p.Tag!)];

    /// <summary>Les étiquettes ne se demandent qu'au partage : un build qui reste dans sa propre
    /// collection n'a personne à qui se rendre trouvable.</summary>
    private void Public_Changed(object sender, RoutedEventArgs e) => UpdateSendState();

    /// <summary>Clic et non Checked/Unchecked : <see cref="ApplyScope"/> coche les puces par
    /// programme, et ne doit pas déclencher la logique de saisie en le faisant.</summary>
    private void TagPill_Click(object sender, RoutedEventArgs e) => UpdateSendState();

    private void Name_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateSendState();

    /// <summary>
    /// Les deux conditions d'un envoi utile, au même endroit.
    ///
    /// Un nom vide : le serveur accepterait un build sans titre, que personne ne saurait
    /// retrouver. Un partage sans étiquette : le build existerait sur GWRank sans ressortir
    /// d'aucune recherche filtrée, ce qui vide le partage de son intérêt.
    /// </summary>
    private void UpdateSendState()
    {
        // Peut précéder la fin de l'initialisation : NameBox.TextChanged part dès le premier
        // texte posé, alors que le reste de l'arbre XAML n'existe pas encore.
        if (NameWarning is null || SendButton is null || TagWarning is null || TagPanel is null)
            return;

        bool shared    = PublicCheck.IsChecked == true;
        bool hasPills  = Pills().Any();
        bool nameEmpty = NameBox.Text.Trim().Length == 0;
        bool noTag     = shared && hasPills && CheckedTags().Count == 0;

        TagPanel.Visibility    = shared && hasPills ? System.Windows.Visibility.Visible
                                                    : System.Windows.Visibility.Collapsed;
        NameWarning.Visibility = nameEmpty ? System.Windows.Visibility.Visible
                                           : System.Windows.Visibility.Collapsed;
        TagWarning.Visibility  = noTag ? System.Windows.Visibility.Visible
                                       : System.Windows.Visibility.Collapsed;
        SendButton.IsEnabled   = !nameEmpty && !noTag;
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        // Le bouton porte DÉJÀ les deux conditions (nom, étiquettes) : les redire ici les ferait
        // diverger un jour. Un bouton désactivé ne répond ni au clic ni à la touche Entrée.
        if (!SendButton.IsEnabled) return;
        SelectedName = NameBox.Text.Trim();
        SelectedTags = CheckedTags();
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
