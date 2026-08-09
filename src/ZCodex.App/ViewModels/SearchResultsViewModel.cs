namespace ZCodex.App.ViewModels;

// Onglet « Résultats N » : un browser en mode liste plate (IsResultsMode) portant le
// titre (renommable, en mémoire seulement) et l'état actif du tab. Chaque lancement de
// recherche en crée un nouveau.
public class SearchResultsViewModel : BrowserViewModel, IRenamableTab
{
    private bool _isActive;
    private string _title;
    private bool _isRenaming;
    private string _editName = "";

    public SearchResultsViewModel(int number)
    {
        Number = number;
        _title = DefaultName(number);
        IsResultsMode = true;
    }

    // Nom d'onglet par défaut localisé (« Résultats N » / « Results N »), baké à la création dans
    // la langue courante — même patron que TeamBuildViewModel.DefaultName : c'est un titre de
    // document renommable, pas un libellé d'UI, donc pas de hot-swap à la bascule de langue.
    public static string DefaultName(int n) =>
        string.Format(ZCodex.App.LanguageManager.T("S.Tab.ResultsName"), n);

    // Numéro de série monotone (ne se réutilise pas après fermeture d'un onglet).
    public int Number { get; }

    // Libellé de l'onglet (renommable ; sans correspondance fichier).
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    // Onglet visuellement actif (surbrillance du tab + visibilité de la vue).
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    // ── Renommage inline ──────────────────────────────────────────────────────
    public string RenameSeed => _title;

    public string EditName
    {
        get => _editName;
        set => SetField(ref _editName, value);
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetField(ref _isRenaming, value);
    }
}
