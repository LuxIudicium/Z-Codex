using ZCodex.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace ZCodex.App.ViewModels;

// Vue principale affichée dans la zone de contenu (exclusives).
public enum MainView { Browser, SearchBuilder, SearchResults, BuildEditor, TeamBuild, ArmorCalc }

// Trois crans de taille d'icône (menu View → Taille des icônes).
// Grande = taille native 64px (1:1, net comme le wiki) ; Moyenne/Petite rétrécissent pour tenir sur un petit écran.
public enum IconSizeMode { Large, Medium, Small }

public class MainViewModel : ViewModelBase
{
    private TeamBuildViewModel? _activeTeamBuild;
    private SearchResultsViewModel? _activeSearchResults;
    private BuildEditorViewModel? _activeBuild;
    private int _searchResultsCounter;
    private int _buildCounter;
    private IconSizeMode _iconSize = IconSizeMode.Large;
    private MainView _activeView = MainView.Browser;
    private bool _hasSearchBuilder;
    private bool _hasArmorCalc;

    // Cran de taille des icônes (lignes de build + liste de skills). Grande = taille native 64px.
    public IconSizeMode IconSize
    {
        get => _iconSize;
        set
        {
            if (SetField(ref _iconSize, value))
            {
                OnPropertyChanged(nameof(SlotSize));
                OnPropertyChanged(nameof(SlotImageSize));
                OnPropertyChanged(nameof(ListIconSize));
                OnPropertyChanged(nameof(SlotRowMinHeight));
            }
        }
    }

    // Slot conteneur = image + 12 (marges + bordure). Image native 64px en Grande → 1:1, net comme le wiki.
    public double SlotSize      => _iconSize switch { IconSizeMode.Large => 76, IconSizeMode.Medium => 64, _ => 52 };  // conteneur des lignes de build
    public double SlotImageSize => _iconSize switch { IconSizeMode.Large => 64, IconSizeMode.Medium => 52, _ => 40 };  // image dans le slot
    public double ListIconSize  => _iconSize switch { IconSizeMode.Large => 64, IconSizeMode.Medium => 52, _ => 40 };  // icône de la liste de droite

    // Hauteur MINI d'une ligne de perso (teambuild). MinHeight, pas Height : le cas courant (non assigné,
    // panneau gauche ~69px) se cale au plancher, mais une ligne assignée (~83px) grandit d'elle-même sans
    // rognage. Suit la taille d'icône pour récupérer l'espace mort en Moyenne/Petite (slots 64/52 dans une
    // ligne qui était figée à 88). Grande inchangée. Racine et variante = même hauteur depuis la refonte 2×2.
    public double SlotRowMinHeight => _iconSize switch { IconSizeMode.Large => 88, IconSizeMode.Medium => 74, _ => 72 };

    public BrowserViewModel Browser { get; } = new();
    // Onglets « Résultats N » : un browser en mode résultats par lancement de recherche.
    public ObservableCollection<SearchResultsViewModel> SearchResultsTabs { get; } = new();
    // Onglet « Search » : constructeur de requête (perso + catalogue).
    public SearchBuilderViewModel SearchBuilder { get; } = new();
    // Onglet « Armure » : calculateur d'armure (chantier 14), vue unique paresseuse comme Search.
    public ArmorCalcViewModel ArmorCalc { get; } = new();
    // Onglets « Build » : éditeur d'un build simple (un seul perso) par build ouvert/créé.
    public ObservableCollection<BuildEditorViewModel> OpenBuilds { get; } = new();
    public ObservableCollection<TeamBuildViewModel> OpenTeamBuilds { get; } = new();
    public SkillPanelViewModel SkillPanel { get; } = new();

    // ── Bandeaux de conditions ────────────────────────────────────────────────

    // Toggle du menu View (persisté dans settings.json) : masque/affiche les bandeaux
    // de conditions de l'éditeur de build ET du teambuild.
    private bool _showConditions = true;
    public bool ShowConditions
    {
        get => _showConditions;
        set => SetField(ref _showConditions, value);
    }

    // Toggle du menu View (persisté settings.json) : dans le teambuild, la molette ne règle les
    // caractéristiques que sur le personnage cliqué. Bindé aussi par le XAML pour n'allumer le
    // liseré de « carte armée » que lorsque la sélection compte vraiment.
    private bool _wheelNeedsSelection;
    public bool WheelNeedsSelection
    {
        get => _wheelNeedsSelection;
        set => SetField(ref _wheelNeedsSelection, value);
    }

    // Toggle du menu View (persisté settings.json) : afficher/masquer la BARRE des rituels de la
    // nature (teambuild + éditeur). Défaut affiché. Gate la visibilité, indépendant de HasItems.
    private bool _showNatureRituals = true;
    public bool ShowNatureRituals
    {
        get => _showNatureRituals;
        set => SetField(ref _showNatureRituals, value);
    }

    // Toggle du menu View (persisté settings.json) : le bandeau des rituels de la nature affiche
    // TOUS les 8 rituels (cliquables même non équipés, pour la simulation) au lieu des seuls équipés.
    private bool _showAllNatureRituals;
    public bool ShowAllNatureRituals
    {
        get => _showAllNatureRituals;
        set
        {
            NatureRitualBandViewModel.ShowAll = value;   // état global des bandeaux, toujours synchronisé
            if (!SetField(ref _showAllNatureRituals, value)) return;
            RefreshTeamNatureRitualBand();
            ActiveBuild?.RefreshNatureRitualBand();
        }
    }

    // Ligne unique du teambuild : agrégat des persos racine du build actif.
    public ConditionBandViewModel TeamConditionBand { get; } = new();

    // Bandeau des rituels de la nature équipés sur le teambuild actif (togglables).
    public NatureRitualBandViewModel TeamNatureRitualBand { get; } = new();

    public MainViewModel()
    {
        // Le grisé du bandeau suit le mode de jeu PvE/PvP du catalogue du teambuild.
        SkillPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SkillPanelViewModel.SelectedGameMode))
            {
                // AVANT le bandeau : sa signature dépend des ids des compétences équipées.
                ApplyGameModeToEquippedSkills();
                RefreshTeamConditionBand();
                ApplyPvpAttributeRule();
                PushGameModeToBuildCatalogs();
            }
        };
        ApplyPvpAttributeRule();
    }

    // ── Mode de jeu : bouton global ⇄ combo des catalogues Build ──────────────
    //
    // Le teambuild partage l'INSTANCE de catalogue avec le bouton de la barre d'outils (SkillPanel),
    // les deux sont donc liés par construction. Chaque onglet Build a en revanche son PROPRE
    // SkillPanelViewModel (catalogue dédié) : d'où ce miroir explicite, dans les deux sens.
    //
    // Pas de garde de réentrance : le setter ne notifie que sur changement RÉEL, la propagation
    // s'arrête donc d'elle-même dès que les deux côtés sont d'accord. Le test d'égalité explicite
    // n'est là que pour ne pas refiltrer (RefreshSkills) un catalogue déjà au bon mode.

    /// <summary>Bouton global → catalogues de TOUS les onglets Build ouverts.</summary>
    private void PushGameModeToBuildCatalogs()
    {
        foreach (var b in OpenBuilds)
            if (b.Catalog.SelectedGameMode != SkillPanel.SelectedGameMode)
                b.Catalog.SelectedGameMode = SkillPanel.SelectedGameMode;
    }

    /// <summary>
    /// Aligne le catalogue d'un onglet Build sur le mode courant à son ouverture, puis le rend
    /// maître à son tour : bouger son combo repositionne le bouton global (et par ricochet le
    /// teambuild et les autres onglets Build).
    /// </summary>
    private void LinkBuildCatalogGameMode(BuildEditorViewModel tab)
    {
        if (tab.Catalog.SelectedGameMode != SkillPanel.SelectedGameMode)
            tab.Catalog.SelectedGameMode = SkillPanel.SelectedGameMode;

        tab.Catalog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SkillPanelViewModel.SelectedGameMode)
                && SkillPanel.SelectedGameMode != tab.Catalog.SelectedGameMode)
                SkillPanel.SelectedGameMode = tab.Catalog.SelectedGameMode;
        };
    }

    // Filtre PvP du catalogue → aucun niveau bonus JOUEUR (rune/casque/conso) sur les
    // caractéristiques de la profession SECONDAIRE (décision Philippe). Le blocage lui-même vit
    // dans AttributeRowViewModel (EffectiveMaxBonus → 0), donc il couvre aussi les persos créés
    // APRÈS le passage en PvP. Ici on ne fait que (1) poser le drapeau global et (2) PURGER les
    // bonus déjà posés — purge + blocage, sans restauration si on repasse en PvE/All.
    //
    // /!\ Les bonus de FLUX (Hidden Talent +2 SEC, Meek Shall Inherit +2 PR/SEC) ne sont PAS
    // concernés : ils n'existent qu'en PvP justement, le joueur ne les pose pas, et ils passent
    // par FluxBonus / CharacterSlotViewModel.AttributeLevel — pas par BonusPoints. On ne les purge
    // donc surtout pas ici.
    private void ApplyPvpAttributeRule()
    {
        bool pvp = SkillPanel.SelectedGameMode == SkillGameMode.PvP;
        AttributeRowViewModel.PvpBlocksSecondaryBonus = pvp;
        if (!pvp) return; // retour en PvE/All : on lève le blocage, rien à purger

        foreach (var c in AllCharacters())
            foreach (var row in c.SecondaryAttributeRows)
                row.BonusPoints = 0; // borné par EffectiveMaxBonus (0) → notifie l'affichage
    }

    // Bascule PvE/PvP du catalogue : les compétences DÉJÀ ÉQUIPÉES suivent le mode
    // (« Heal Party » ⇄ « Heal Party (PvP) »), pour que les infobulles et tous les calculs qui en
    // dépendent affichent les valeurs du contexte choisi — c'est tout l'intérêt d'avoir les deux
    // entrées au catalogue. En mode « All » on ne touche à rien : aucune des deux variantes n'y est
    // plus légitime que l'autre.
    //
    // Sans dirty flag ni entrée d'undo (RunWithoutTracking) : la variante affichée est DÉRIVÉE du
    // mode courant, elle n'est pas une édition du build. Le code de template exporté, lui, ne
    // change pas — il porte toujours l'id de la version de base (cf. SkillVariants.TemplateIdsByName).
    private void ApplyGameModeToEquippedSkills()
    {
        foreach (var tb in OpenTeamBuilds) ApplyGameModeTo(tb);
        foreach (var b in OpenBuilds) ApplyGameModeTo(b.Character);
        ApplyGameModeTo(SearchBuilder.QueryCharacter);
    }

    /// <summary>
    /// Aligne les compétences équipées d'un team build sur le mode de jeu courant. Appelé à la
    /// bascule du filtre ET à l'ouverture d'un fichier : la variante affichée dépend TOUJOURS du
    /// mode dans lequel on se trouve, jamais de celle qui se trouvait enregistrée dans le fichier.
    /// </summary>
    public void ApplyGameModeTo(TeamBuildViewModel tb)
    {
        if (SkillPanel.SelectedGameMode == SkillGameMode.All) return;
        tb.RunWithoutTracking(() =>
        {
            foreach (var c in tb.EnumerateTree()) ResolveSlots(c);
        });
    }

    /// <summary>Idem pour un personnage isolé (éditeur de build simple, perso de requête).</summary>
    public void ApplyGameModeTo(CharacterSlotViewModel character)
    {
        if (SkillPanel.SelectedGameMode == SkillGameMode.All) return;
        ResolveSlots(character);
    }

    private void ResolveSlots(CharacterSlotViewModel c)
    {
        foreach (var slot in c.SkillSlots)
            if (slot.Skill is { } s && SkillPanel.ResolveForGameMode(s) is var v && !ReferenceEquals(v, s))
                slot.Skill = v;
    }

    // Tous les persos éditables de l'app : arbres des teambuilds ouverts (variantes comprises),
    // persos des éditeurs de build simple, et le perso de requête du Search.
    private IEnumerable<CharacterSlotViewModel> AllCharacters()
    {
        foreach (var tb in OpenTeamBuilds)
            foreach (var c in tb.EnumerateTree())
                yield return c;
        foreach (var b in OpenBuilds) yield return b.Character;
        yield return SearchBuilder.QueryCharacter;
    }

    // Bascule de langue : re-notifie l'affichage dépendant de la langue sur TOUS les persos éditables
    // (nom placeholder, résumés/lignes d'attributs, noms de compétences) + les infobulles de flux des
    // teambuilds et éditeurs. Sans reconstruire (les points investis sont préservés). Appelé par
    // MainWindow.SwitchLanguage.
    public void RefreshLanguage()
    {
        foreach (var c in AllCharacters()) c.RefreshLanguage();
        foreach (var tb in OpenTeamBuilds) tb.FluxIndicator.RaiseLanguageChanged();
        foreach (var b in OpenBuilds) b.FluxIndicator.RaiseLanguageChanged();

        // Bandeaux de conditions : InflictedBy est une chaîne bakée avec le DisplayName des skills
        // fauteurs → rejouer le calcul dans la langue courante. La garde « signature inchangée » du
        // teambuild sauterait (mêmes skills) → on la réinitialise pour forcer le recalcul.
        _lastTeamCondSig = "";
        RefreshTeamConditionBand();
        foreach (var b in OpenBuilds) b.RefreshConditionBand();

        // Bandeaux de rituels de la nature : ClickNote et la description résolue sont bakées à la
        // construction des indicateurs, et leur garde de signature sauterait (rien de « visible »
        // n'a changé) → invalider puis reconstruire dans la langue courante.
        TeamNatureRitualBand.InvalidateLanguage();
        RefreshTeamNatureRitualBand();
        foreach (var b in OpenBuilds) { b.NatureRitualBand.InvalidateLanguage(); b.RefreshNatureRitualBand(); }

        // Onglets de catalogue (éditeur de build + recherche) : Label calculé → re-notifier.
        SearchBuilder.RefreshCatalogTabsLanguage();
        foreach (var b in OpenBuilds) { b.RefreshCatalogTabsLanguage(); b.Catalog.RefreshLanguage(); }

        // Navigateur de fichiers : format, professions, caractéristique et élite sont affichés
        // dans la langue courante (les clés EN restent en place) + re-tri.
        Browser.RefreshLanguage();
    }

    // Recalcule la ligne de conditions du teambuild actif : agrégat des persos RACINE
    // (les variantes sont des alternatives, pas des membres supplémentaires ; les filtres
    // cadenas n'influent pas). Couleur = un perso l'inflige ; grisée = un combo le pourrait.
    private string _lastTeamCondSig = "";

    public void RefreshTeamConditionBand()
    {
        var chars = (_activeTeamBuild?.Characters ?? Enumerable.Empty<CharacterSlotViewModel>()).ToList();
        // Garde « inchangé » : le bandeau ne dépend QUE des compétences équipées, des professions et
        // du mode de jeu — PAS des niveaux d'attributs. Un changement d'attribut (molette) déclenche
        // pourtant Mutated en rafale → on saute le recalcul (coûteux : scan du catalogue) si rien de
        // pertinent n'a changé.
        string sig = SkillPanel.SelectedGameMode + "|" + string.Join(";", chars.Select(c =>
            $"{(int)c.PrimaryProfession},{(int)c.SecondaryProfession}:"
            + string.Join(",", c.SkillSlots.Select(s => s.Skill?.Id ?? -1))));
        if (sig == _lastTeamCondSig) return;
        _lastTeamCondSig = sig;

        var scopes = new List<ConditionScope>();
        foreach (var c in chars)
        {
            var equipped = c.SkillSlots.Where(s => s.Skill != null).Select(s => s.Skill!).ToList();
            // Perso vierge (?/? sans compétence) : ne contribue pas — sinon les compétences
            // cross-profession griseraient des conditions pour un slot vide.
            if (c.PrimaryProfession == Profession.None
                && c.SecondaryProfession == Profession.None && equipped.Count == 0)
                continue;
            scopes.Add(new ConditionScope(
                equipped,
                SkillPanel.ScopeSkillsFor(c.PrimaryProfession, c.SecondaryProfession).ToList()));
        }
        TeamConditionBand.Refresh(scopes);
    }

    // Recalcule le bandeau des rituels de la nature équipés du teambuild actif (persos racine).
    // Mutated couvre l'équipement/retrait d'un rituel ET le toggle d'activation (via la signature).
    public void RefreshTeamNatureRitualBand()
    {
        if (_activeTeamBuild is not { } tb) { TeamNatureRitualBand.Clear(); return; }
        var equipped = tb.Characters.SelectMany(c => c.SkillSlots)
            .Where(s => s.Skill != null).Select(s => s.Skill!).ToList();
        tb.NatureRituals.SyncEquipped(NatureRitualBandViewModel.EquippedRituals(equipped)); // option B : extinction au retrait
        TeamNatureRitualBand.Refresh(equipped, SkillPanel.AllSkills, tb.NatureRituals);
    }

    // Vue active. Le tab d'un build n'est visuellement actif que dans la vue TeamBuild.
    public MainView ActiveView
    {
        get => _activeView;
        private set
        {
            if (!SetField(ref _activeView, value)) return;
            if (_activeTeamBuild != null)
                _activeTeamBuild.IsActive = value == MainView.TeamBuild;
            if (_activeSearchResults != null)
                _activeSearchResults.IsActive = value == MainView.SearchResults;
            if (_activeBuild != null)
                _activeBuild.IsActive = value == MainView.BuildEditor;
            OnPropertyChanged(nameof(IsBrowserActive));
            OnPropertyChanged(nameof(IsSearchBuilderActive));
            OnPropertyChanged(nameof(IsSearchResultsActive));
            OnPropertyChanged(nameof(IsBuildEditorActive));
            OnPropertyChanged(nameof(IsTeamBuildActive));
            OnPropertyChanged(nameof(IsArmorCalcActive));
        }
    }

    public bool IsBrowserActive       => _activeView == MainView.Browser;
    public bool IsSearchBuilderActive => _activeView == MainView.SearchBuilder;
    public bool IsSearchResultsActive => _activeView == MainView.SearchResults;
    public bool IsBuildEditorActive   => _activeView == MainView.BuildEditor;
    public bool IsTeamBuildActive     => _activeView == MainView.TeamBuild;
    public bool IsArmorCalcActive     => _activeView == MainView.ArmorCalc;

    // Onglet de résultats actuellement affiché (null si aucun / autre vue active).
    public SearchResultsViewModel? ActiveSearchResults => _activeSearchResults;

    // Onglet Build actuellement affiché (null si aucun / autre vue active). Utilisé par les handlers
    // du catalogue et du bouton « Enregistrer » : seul l'onglet actif est visible → clics sûrs.
    public BuildEditorViewModel? ActiveBuild => _activeBuild;

    // L'onglet Search n'apparaît qu'après un premier clic sur 🔍.
    public bool HasSearchBuilder
    {
        get => _hasSearchBuilder;
        set => SetField(ref _hasSearchBuilder, value);
    }

    // L'onglet Armure n'apparaît qu'après un premier clic dans Extras → Calculateur d'armure.
    public bool HasArmorCalc
    {
        get => _hasArmorCalc;
        set => SetField(ref _hasArmorCalc, value);
    }

    public TeamBuildViewModel? ActiveTeamBuild
    {
        get => _activeTeamBuild;
        set
        {
            var old = _activeTeamBuild;
            if (SetField(ref _activeTeamBuild, value))
            {
                if (old != null) old.IsActive = false;
                if (value != null)
                {
                    value.IsActive = true;
                    ActiveView = MainView.TeamBuild;
                }
                else if (_activeView == MainView.TeamBuild)
                {
                    ActiveView = MainView.Browser;
                }

                // Ligne de conditions : suit l'onglet actif ; Mutated couvre toute mutation
                // du build (skills, professions, persos ajoutés/retirés, variantes).
                if (old != null) old.Mutated -= RefreshTeamConditionBand;
                if (value != null) value.Mutated += RefreshTeamConditionBand;
                RefreshTeamConditionBand();

                if (old != null) old.Mutated -= RefreshTeamNatureRitualBand;
                if (value != null) value.Mutated += RefreshTeamNatureRitualBand;
                RefreshTeamNatureRitualBand();

                // Aperçu léger du rang (molette) : met à jour le badge sans le recalcul lourd débouncé.
                if (old != null) old.NatureRituals.RankPreview -= RefreshTeamNatureRitualBand;
                if (value != null) value.NatureRituals.RankPreview += RefreshTeamNatureRitualBand;
            }
        }
    }

    public void ActivateBrowser()
    {
        ActiveTeamBuild = null;
        ActiveView = MainView.Browser;
    }

    // Crée un nouvel onglet « Résultats N » (numérotation monotone), l'ajoute et l'active.
    // Le câblage (skills provider, ouverture de fichier) et le chargement des résultats
    // sont faits par l'appelant sur le VM retourné.
    public SearchResultsViewModel NewSearchResults()
    {
        var tab = new SearchResultsViewModel(++_searchResultsCounter);
        SearchResultsTabs.Add(tab);
        ActivateSearchResults(tab);
        return tab;
    }

    public void ActivateSearchResults(SearchResultsViewModel tab)
    {
        ActiveTeamBuild = null;
        if (_activeSearchResults != null && _activeSearchResults != tab)
            _activeSearchResults.IsActive = false;
        _activeSearchResults = tab;
        OnPropertyChanged(nameof(ActiveSearchResults));
        tab.IsActive = true;
        ActiveView = MainView.SearchResults;
    }

    // Ferme un onglet de résultats. Si c'était l'onglet actif, bascule sur le voisin
    // restant, sinon retombe sur le browser.
    public void CloseSearchResults(SearchResultsViewModel tab)
    {
        int idx = SearchResultsTabs.IndexOf(tab);
        if (idx < 0) return;
        bool wasActive = _activeSearchResults == tab;
        SearchResultsTabs.Remove(tab);

        if (!wasActive) return;

        _activeSearchResults = null;
        OnPropertyChanged(nameof(ActiveSearchResults));
        if (SearchResultsTabs.Count > 0)
            ActivateSearchResults(SearchResultsTabs[Math.Min(idx, SearchResultsTabs.Count - 1)]);
        else
            ActivateBrowser();
    }

    public void ActivateSearchBuilder()
    {
        ActiveTeamBuild = null;
        HasSearchBuilder = true;
        ActiveView = MainView.SearchBuilder;
    }

    // Extras → Calculateur d'armure : ouvre/active l'onglet unique (clone d'ActivateSearchBuilder).
    public void ActivateArmorCalc()
    {
        ActiveTeamBuild = null;
        HasArmorCalc = true;
        ActiveView = MainView.ArmorCalc;
    }

    // Ferme l'onglet Armure (rework 17/07). Le VM du calculateur reste en vie : rouvrir depuis
    // Extras → Calculateur d'armure retrouve l'état courant.
    public void CloseArmorCalc()
    {
        HasArmorCalc = false;
        if (_activeView == MainView.ArmorCalc) ActivateBrowser();
    }

    // ── Onglets Build (éditeur d'un build simple) ─────────────────────────────

    // Crée un onglet « Nouveau build N » vierge (numérotation monotone), l'ajoute et l'active.
    public BuildEditorViewModel NewBuild()
    {
        var tab = new BuildEditorViewModel(BuildEditorViewModel.DefaultName(++_buildCounter));
        LinkBuildCatalogGameMode(tab);   // le combo du catalogue et le bouton global se suivent
        OpenBuilds.Add(tab);
        ActivateBuild(tab);
        return tab;
    }

    // Ouvre un onglet Build depuis un perso préchargé (import .txt). L'appelant câble ensuite le
    // catalogue et l'ouverture de fichier.
    public BuildEditorViewModel OpenBuild(string title, CharacterSlotViewModel character, string? sourcePath)
    {
        ApplyGameModeTo(character);   // le mode courant prime sur la variante enregistrée
        var tab = new BuildEditorViewModel(title, character) { SourcePath = sourcePath };
        LinkBuildCatalogGameMode(tab);   // le combo du catalogue et le bouton global se suivent
        OpenBuilds.Add(tab);
        ActivateBuild(tab);
        return tab;
    }

    public void ActivateBuild(BuildEditorViewModel tab)
    {
        ActiveTeamBuild = null;
        if (_activeBuild != null && _activeBuild != tab)
            _activeBuild.IsActive = false;
        _activeBuild = tab;
        OnPropertyChanged(nameof(ActiveBuild));
        tab.IsActive = true;
        ActiveView = MainView.BuildEditor;
    }

    // Ferme un onglet Build. Si c'était l'onglet actif, bascule sur le voisin, sinon le browser.
    public void CloseBuild(BuildEditorViewModel tab)
    {
        int idx = OpenBuilds.IndexOf(tab);
        if (idx < 0) return;
        bool wasActive = _activeBuild == tab;
        OpenBuilds.Remove(tab);

        if (!wasActive) return;

        _activeBuild = null;
        OnPropertyChanged(nameof(ActiveBuild));
        if (OpenBuilds.Count > 0)
            ActivateBuild(OpenBuilds[Math.Min(idx, OpenBuilds.Count - 1)]);
        else
            ActivateBrowser();
    }

    public void NewTeamBuild()
    {
        var tb = new TeamBuildViewModel { Name = TeamBuildViewModel.DefaultName(OpenTeamBuilds.Count + 1) };
        tb.BeginTracking();
        OpenTeamBuilds.Add(tb);
        ActiveTeamBuild = tb;
    }
}
