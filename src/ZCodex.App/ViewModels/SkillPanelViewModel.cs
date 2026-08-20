using ZCodex.Core.Models;
using ZCodex.Core.Search;
using System.Collections.ObjectModel;
using System.Windows;

namespace ZCodex.App.ViewModels;

// Entrée affichable du panneau PvE-only : libellé indenté + accès au prédicat de la définition.
public class PveCategoryItem
{
    public PveCategoryItem(GwAttributeData.PveCategoryDef def) => Def = def;
    public GwAttributeData.PveCategoryDef Def { get; }
    // Label = clé EN (logique/restauration de sélection) ; DisplayLabel = libellé affiché (langue courante).
    public string Label => Def.Label;
    public string DisplayLabel => GwAttributeData.DisplayName(Def.Label);
    public int Indent => Def.Indent;
    public Thickness Margin => new(Def.Indent * 14, 0, 0, 0);
}

// Entrée affichable des colonnes « Skill Types » et « Mechanics ». Même patron que PveCategoryItem,
// à une différence près : la liste est STATIQUE (jamais reconstruite → on notifie le libellé à la
// bascule de langue au lieu de vider la collection, ce qui préserve la sélection).
public class SkillCategoryItem : ViewModelBase
{
    public SkillCategoryItem(SkillCategoryData.SkillCategoryDef def) => Def = def;
    public SkillCategoryData.SkillCategoryDef Def { get; }
    // Label = clé EN (logique/restauration) ; DisplayLabel = libellé affiché (langue courante).
    public string Label => Def.Label;
    public string DisplayLabel => SkillCategoryData.DisplayName(Def.Label);
    public int Indent => Def.Indent;
    public Thickness Margin => new(Def.Indent * 12, 0, 0, 0);
    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(DisplayLabel));

    // Vrai quand SÉLECTIONNER cette entrée ne rendrait aucune compétence, compte tenu de tous les
    // autres filtres actifs (profession, caractéristique, PvE/PvP, recherche, et l'AUTRE colonne).
    // L'entrée reste affichée à sa place et reste cliquable : elle est seulement grisée — c'est ce
    // qui permet de garder les listes stables (idée Philippe 19/08/2026) tout en montrant d'un coup
    // d'œil ce qui est vide. Cliquer quand même donne une liste vide + la ligne « N masqués par … ».
    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetField(ref _isEmpty, value); }

    // ── Repli des enfants (20/08/2026) ────────────────────────────────────
    //
    // La colonne des mécaniques est passée de 30 à 87 lignes : replier les enfants la ramène à 40.
    // L'arbre est POSÉ SUR la liste plate, il ne la remplace pas — la collection reste linéaire (une
    // seule source pour les 4 catalogues et pour les puces), et c'est la VISIBILITÉ de chaque ligne
    // qui change. Rien à réordonner, la sélection ne bouge pas.
    public SkillCategoryItem? Parent { get; set; }
    public List<SkillCategoryItem> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetField(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ExpanderGlyph));
            NotifyVisibilityDown();
        }
    }

    /// <summary>Une ligne n'est visible que si TOUS ses ancêtres sont dépliés.</summary>
    public bool IsVisible => Parent is null || (Parent.IsExpanded && Parent.IsVisible);

    /// <summary>Chevron de la ligne. Chaîne vide (et non masquage) quand il n'y a pas d'enfant :
    /// la gouttière reste réservée, donc les libellés de même niveau restent alignés.</summary>
    public string ExpanderGlyph => !HasChildren ? string.Empty : _isExpanded ? "\u25be" : "\u25b8";

    private void NotifyVisibilityDown()
    {
        foreach (var c in Children)
        {
            c.OnPropertyChanged(nameof(IsVisible));
            c.NotifyVisibilityDown();
        }
    }

    /// <summary>Déplie tout ce qu'il faut pour que cette ligne soit visible — appelé quand une
    /// sélection arrive d'ailleurs (recherche, restauration, clic sur une puce) et tomberait dans
    /// une branche repliée.</summary>
    public void ExpandAncestors()
    {
        for (var p = Parent; p is not null; p = p.Parent) p.IsExpanded = true;
    }
}

// Filtre PvE/PvP du catalogue (façon paw·ned²) :
//   All → tout ; PvE → tout sauf les versions " (PvP)" ; PvP → tout sauf la version
//   PvE des skills splittées PvE/PvP (les non-splittées et les " (PvP)" restent).
public enum SkillGameMode { All, PvE, PvP }

// Le filtre se fait sur Mode (enum) → Label est purement d'affichage. Classe NOTIFIANTE (pas record) :
// au switch de langue on lève PropertyChanged(Label) SANS reconstruire la collection — un ComboBox
// lié perd sa sélection au Clear+Add (cf. régression 21/07), contrairement au ListBox. Les vues
// bindent Label via un ItemTemplate (le template ComboBox custom honore SelectionBoxItemTemplate).
// « PvE only »/« PvP only » restent identiques en FR (choix Philippe) ; seul « All skills » est traduit.
public class SkillGameModeOption : ViewModelBase
{
    public SkillGameMode Mode { get; }
    public SkillGameModeOption(SkillGameMode mode) => Mode = mode;
    public string Label => Mode switch
    {
        SkillGameMode.All => T("S.Filter.AllSkills"),
        SkillGameMode.PvE => T("S.Filter.PvEOnly"),
        SkillGameMode.PvP => T("S.Filter.PvPOnly"),
        _ => Mode.ToString(),
    };
    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(Label));
    public override string ToString() => Label;
    private static string T(string key) => ZCodex.App.LanguageManager.T(key);
}

// Entrée du panneau professions. Value == null → "All professions" (aucune restriction).
// Le FILTRE se fait sur Value (enum) : Label est purement d'affichage. Classe NOTIFIANTE (pas record,
// cf. SkillGameModeOption ci-dessus) : au switch on lève PropertyChanged(Label) sans reconstruire la
// collection (préserve la sélection des ComboBox). Les vues bindent Label (ListBox : Text=Label ;
// ComboBox : ItemTemplate → Label).
public class ProfessionOption : ViewModelBase
{
    public Profession? Value { get; }
    public ProfessionOption(Profession? value) => Value = value;
    public static readonly ProfessionOption All = new((Profession?)null);
    public string Label => Value.HasValue
        ? Value.Value.DisplayName()
        : ZCodex.App.LanguageManager.T("S.Filter.AllProfessions");
    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(Label));
    public override string ToString() => Label;
}

public class SkillPanelViewModel : ViewModelBase
{
    private ProfessionOption _selectedProfessionOption;

    // Deux filtres d'attribut mutuellement exclusifs : caractéristique de profession (panneau 2)
    // OU catégorie PvE-only (panneau 3). Sélectionner dans l'un vide l'autre.
    private string _selectedProfessionAttribute = string.Empty;
    private PveCategoryItem? _selectedPveItem;
    private bool _suppressSelectionWriteback;

    // Colonnes Types / Mechanics : deux filtres INDÉPENDANTS (croisement type × mécanique), chacun
    // en mono-sélection. Jamais null en pratique — l'entrée méta « All … » tient le rôle de « aucun
    // filtre » (décision 4).
    private SkillCategoryItem? _selectedTypeItem;
    private SkillCategoryItem? _selectedMechanicItem;

    // Ligne de retour sous la barre de recherche (décision 14) : catégories reconnues + décompte de
    // ce que les filtres profession/caractéristique cachent. Vide = ligne masquée.
    private string _searchNotice = string.Empty;
    private bool _canLiftFilters;

    private string _searchText = string.Empty;
    private SkillGameMode _selectedGameMode = SkillGameMode.All;
    private Skill? _selectedSkill;

    // Noms (sans suffixe) des skills ayant une version " (PvP)" distincte.
    private readonly HashSet<string> _pvpSplitBaseNames = new(StringComparer.OrdinalIgnoreCase);

    // Index nom → compétence : résolution de variante à la bascule PvE/PvP, sans rebalayer
    // AllSkills pour chacun des slots équipés de tous les persos ouverts.
    private readonly Dictionary<string, Skill> _byName = new(StringComparer.Ordinal);

    // Skills célestes : acquis temporairement en jeu (dragon Kuunavang), jamais draftables.
    // Filtre caché → ils n'apparaissent jamais dans le catalogue.
    // https://wiki.guildwars.com/wiki/Celestial_skill
    private static readonly HashSet<string> CelestialSkillNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Celestial Haste", "Celestial Stance", "Celestial Storm", "Celestial Summoning",
        "Star Servant", "Star Shine", "Star Strike", "Storm of Swords",
    };

    public SkillPanelViewModel()
    {
        Professions = new ObservableCollection<ProfessionOption>(
            new[] { ProfessionOption.All }
            .Concat(Enum.GetValues<Profession>()
                .Where(p => p != Profession.None)
                .Select(p => new ProfessionOption(p))));
        // « All professions » par défaut (et non Guerrier) : depuis que la recherche respecte TOUS
        // les filtres actifs (décision 2), un défaut de profession transformerait chaque recherche
        // en fausse absence — « heal » ne rendait plus que 1 résultat sur 31.
        _selectedProfessionOption = Professions.First(p => p.Value is null);

        // Ordre ALPHABÉTIQUE dans la langue courante, hiérarchie préservée (SortForDisplay) :
        // avec une trentaine de mécaniques, l'ordre thématique ne se retrouvait plus à l'œil.
        foreach (var def in SkillCategoryData.SortForDisplay(SkillCategoryData.TypeDefs))
            TypeCategories.Add(new SkillCategoryItem(def));
        foreach (var def in SkillCategoryData.SortForDisplay(SkillCategoryData.MechanicDefs))
            MechanicCategories.Add(new SkillCategoryItem(def));
        // ⚠ SEULES les mécaniques sont repliables. La colonne des types est une liste PLATE dont
        // les 33 entrées pendent toutes de « Tous les types » : les replier n'y laissait qu'une
        // seule ligne visible (constaté in-app par Philippe le 20/08/2026). Sans parent, IsVisible
        // est toujours vrai et le chevron reste vide.
        LinkParents(MechanicCategories);
        _selectedTypeItem = TypeCategories[0];        // All types
        _selectedMechanicItem = MechanicCategories[0]; // All mechanics
    }

    /// <summary>Réordonne une collection observable pour qu'elle suive <paramref name="order"/>,
    /// par Move successifs — aucun item n'est recréé. Les entrées sans définition correspondante
    /// (il n'y en a pas aujourd'hui) sont laissées en fin de liste.</summary>
    public static void ReorderLike<T>(ObservableCollection<T> items,
                                      IReadOnlyList<SkillCategoryData.SkillCategoryDef> order,
                                      Func<T, SkillCategoryData.SkillCategoryDef?> keyOf)
    {
        for (int target = 0; target < order.Count; target++)
        {
            int cur = -1;
            for (int j = target; j < items.Count; j++)
                if (ReferenceEquals(keyOf(items[j]), order[target])) { cur = j; break; }
            if (cur >= 0 && cur != target) items.Move(cur, target);
        }
    }

    /// <summary>Relie chaque ligne à son parent d'après l'indentation, sur la liste DÉJÀ triée.
    /// Une pile suffit : les enfants suivent immédiatement leur parent (cf. SortForDisplay).</summary>
    private static void LinkParents(IList<SkillCategoryItem> items)
    {
        var stack = new List<SkillCategoryItem>();
        foreach (var it in items)
        {
            it.Parent = null;
            it.Children.Clear();
            while (stack.Count > it.Indent) stack.RemoveAt(stack.Count - 1);
            if (stack.Count > 0)
            {
                it.Parent = stack[^1];
                stack[^1].Children.Add(it);
            }
            stack.Add(it);
        }
    }

    public string SortColumn { get; private set; } = "Name";
    public bool SortAscending { get; private set; } = true;

    // Largeur des colonnes de stats du catalogue : 0 quand aucune compétence filtrée n'a
    // de valeur pour cette stat (colonne masquée), 48 sinon. Recalculée à chaque RefreshSkills.
    private const double StatColumnWidth = 48;
    private double _energyColumnWidth = StatColumnWidth;
    private double _adrenalineColumnWidth = StatColumnWidth;
    private double _sacrificeColumnWidth = StatColumnWidth;
    private double _overcastColumnWidth = StatColumnWidth;
    private double _upkeepColumnWidth = StatColumnWidth;
    private double _castColumnWidth = StatColumnWidth;
    private double _rechargeColumnWidth = StatColumnWidth;

    public double EnergyColumnWidth { get => _energyColumnWidth; private set => SetField(ref _energyColumnWidth, value); }
    public double AdrenalineColumnWidth { get => _adrenalineColumnWidth; private set => SetField(ref _adrenalineColumnWidth, value); }
    public double SacrificeColumnWidth { get => _sacrificeColumnWidth; private set => SetField(ref _sacrificeColumnWidth, value); }
    public double OvercastColumnWidth { get => _overcastColumnWidth; private set => SetField(ref _overcastColumnWidth, value); }
    public double UpkeepColumnWidth { get => _upkeepColumnWidth; private set => SetField(ref _upkeepColumnWidth, value); }
    public double CastColumnWidth { get => _castColumnWidth; private set => SetField(ref _castColumnWidth, value); }
    public double RechargeColumnWidth { get => _rechargeColumnWidth; private set => SetField(ref _rechargeColumnWidth, value); }

    // Largeur d'une cellule du catalogue multi-colonnes (vue Liste explorateur). Calculée UNE FOIS
    // par la vue au chargement (mesure du nom le plus long) pour afficher les noms EN ENTIER sans
    // ellipsis, colonnes de largeur uniforme. Défaut = repli si non calculé.
    private double _catalogItemWidth = 340;
    public double CatalogItemWidth { get => _catalogItemWidth; set => SetField(ref _catalogItemWidth, value); }

    public void SetSort(string column, bool ascending)
    {
        SortColumn = column;
        SortAscending = ascending;
        RefreshSkills();
    }

    public ObservableCollection<ProfessionOption> Professions { get; }
    public ObservableCollection<string> ProfessionAttributes { get; } = new();
    public ObservableCollection<PveCategoryItem> PveCategories { get; } = new();
    // Colonnes Types / Mechanics : listes STATIQUES (jamais filtrées sur la portée courante,
    // contrairement à PveCategories) — une entrée qui ne rend rien s'explique par la ligne
    // « N masqués par … » sous la barre de recherche, elle ne doit pas disparaître sous le curseur.
    public ObservableCollection<SkillCategoryItem> TypeCategories { get; } = new();
    public ObservableCollection<SkillCategoryItem> MechanicCategories { get; } = new();
    public ObservableCollection<Skill> FilteredSkills { get; } = new();
    public ObservableCollection<Skill> AllSkills { get; } = new();

    public ObservableCollection<SkillGameModeOption> GameModes { get; } = new()
    {
        new SkillGameModeOption(SkillGameMode.All),
        new SkillGameModeOption(SkillGameMode.PvE),
        new SkillGameModeOption(SkillGameMode.PvP),
    };

    public SkillGameMode SelectedGameMode
    {
        get => _selectedGameMode;
        set
        {
            SetField(ref _selectedGameMode, value);
            OnPropertyChanged(nameof(GameModeLabel));
            RefreshSkills();
        }
    }

    // Libellé court du mode courant, pour le bouton de la barre d'outils. Le filtre étant global à
    // la session (et modifiable indirectement : ouvrir un .pn3 v17 le repositionne), ce bouton sert
    // d'INDICATEUR permanent autant que de commande.
    public string GameModeLabel => _selectedGameMode switch
    {
        SkillGameMode.PvE => ZCodex.App.LanguageManager.T("S.Tb.ModePvE"),
        SkillGameMode.PvP => ZCodex.App.LanguageManager.T("S.Tb.ModePvP"),
        _                 => ZCodex.App.LanguageManager.T("S.Tb.ModeAll"),
    };

    public ProfessionOption SelectedProfessionOption
    {
        get => _selectedProfessionOption;
        set
        {
            if (value is null) return;
            if (SetField(ref _selectedProfessionOption, value))
            {
                RefreshAttributes();
                RefreshSkills();
            }
        }
    }

    // Portée multi-profession (catalogue de recherche = PR/SEC du perso-requête). Quand non-null
    // et non vide, prime sur SelectedProfessionOption dans InScope (une seule option ne peut pas
    // représenter PR ET SEC). Null = comportement éditeur inchangé. Cross-profession (None) toujours
    // incluse. Cf. [[feedback_grill_before_coding]] : PR/SEC ≡ filtre profession du catalogue.
    private HashSet<Profession>? _professionScope;
    public HashSet<Profession>? ProfessionScope
    {
        get => _professionScope;
        set
        {
            _professionScope = value;
            RefreshAttributes();
            RefreshSkills();
        }
    }

    public string SelectedProfessionAttribute
    {
        get => _selectedProfessionAttribute;
        set
        {
            var v = value ?? string.Empty;
            if (_suppressSelectionWriteback) return;
            if (_selectedProfessionAttribute == v && _selectedPveItem is null) return;
            SetSelection(v, null);
            RefreshSkills();
        }
    }

    public PveCategoryItem? SelectedPveItem
    {
        get => _selectedPveItem;
        set
        {
            if (_suppressSelectionWriteback) return;
            if (ReferenceEquals(_selectedPveItem, value) && _selectedProfessionAttribute.Length == 0) return;
            SetSelection(string.Empty, value);
            RefreshSkills();
        }
    }

    public SkillCategoryItem? SelectedTypeItem
    {
        get => _selectedTypeItem;
        set
        {
            // Un write-back null (désélection WPF) ne doit pas effacer le filtre : pour revenir à
            // tout, on clique « All types ».
            if (value is null) return;
            if (SetField(ref _selectedTypeItem, value)) RefreshSkills();
        }
    }

    public SkillCategoryItem? SelectedMechanicItem
    {
        get => _selectedMechanicItem;
        set
        {
            if (value is null) return;
            // La sélection peut venir d'ailleurs que d'un clic dans la liste (saisie reconnue,
            // puce d'un autre catalogue, restauration) et tomber dans une branche repliée : on la
            // rend visible plutôt que de laisser un filtre actif hors de vue.
            value.ExpandAncestors();
            if (SetField(ref _selectedMechanicItem, value)) RefreshSkills();
        }
    }

    /// <summary>Remet le filtre TYPES sur « All ». Appelé quand on masque la colonne : jamais de
    /// filtre actif invisible. Les deux colonnes se masquent séparément (demande Philippe
    /// 20/08/2026), d'où un reset par colonne et non plus un seul pour les deux.</summary>
    public void ResetTypeFilter()
    {
        if (ReferenceEquals(_selectedTypeItem, TypeCategories[0])) return;
        _selectedTypeItem = TypeCategories[0];
        OnPropertyChanged(nameof(SelectedTypeItem));
        RefreshSkills();
    }

    /// <summary>Remet le filtre MÉCANIQUES sur « All ». Cf. <see cref="ResetTypeFilter"/>.</summary>
    public void ResetMechanicFilter()
    {
        if (ReferenceEquals(_selectedMechanicItem, MechanicCategories[0])) return;
        _selectedMechanicItem = MechanicCategories[0];
        OnPropertyChanged(nameof(SelectedMechanicItem));
        RefreshSkills();
    }

    public string SearchNotice
    {
        get => _searchNotice;
        private set
        {
            if (SetField(ref _searchNotice, value)) OnPropertyChanged(nameof(HasSearchNotice));
        }
    }

    public bool HasSearchNotice => _searchNotice.Length > 0;

    /// <summary>Vrai quand la ligne de retour annonce des compétences masquées par des filtres que
    /// le clic peut lever (profession / caractéristique).</summary>
    public bool CanLiftFilters { get => _canLiftFilters; private set => SetField(ref _canLiftFilters, value); }

    /// <summary>Le clic sur la ligne de retour : lève les filtres NOMMÉS, et eux seuls — ni le mode
    /// PvE/PvP, ni les colonnes Types/Mechanics (décision 14).</summary>
    public void LiftBlockingFilters()
    {
        if (!_canLiftFilters) return;
        if (SetField(ref _selectedProfessionOption, Professions.First(p => p.Value is null),
                     nameof(SelectedProfessionOption)))
            RefreshAttributes();
        SetSelection(GwAttributeData.AllAttributesLabel, null);
        RefreshSkills();
    }

    // Pose les deux sélections de façon atomique (exclusivité). Le garde empêche la
    // désélection WPF de la ListBox vidée (write-back null) d'effacer celle qu'on vient de poser.
    private void SetSelection(string profAttr, PveCategoryItem? pveItem)
    {
        _suppressSelectionWriteback = true;
        try
        {
            _selectedProfessionAttribute = profAttr;
            _selectedPveItem = pveItem;
            OnPropertyChanged(nameof(SelectedProfessionAttribute));
            OnPropertyChanged(nameof(SelectedPveItem));
        }
        finally { _suppressSelectionWriteback = false; }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            SetField(ref _searchText, value);
            RefreshSkills();
        }
    }

    public Skill? SelectedSkill
    {
        get => _selectedSkill;
        set => SetField(ref _selectedSkill, value);
    }

    // Filtre supplémentaire optionnel posé par l'HÔTE du catalogue (ex. calculateur d'armure :
    // « n'inflige que des dégâts »). Appliqué TOUJOURS, y compris pendant la recherche par nom
    // (contrairement aux filtres profession/attribut). null = aucun filtre.
    private Func<Skill, bool>? _extraFilter;
    public Func<Skill, bool>? ExtraFilter
    {
        get => _extraFilter;
        set { _extraFilter = value; RefreshSkills(); }
    }

    // Portée : "All professions" → aucune restriction ; sinon profession courante + cross-profession (None).
    // Si ProfessionScope est posée (catalogue de recherche), elle prime : portée = PR/SEC + cross-prof.
    // Profession réellement exigée par une compétence. Les 20 compétences d'allégeance
    // (Kurzick/Luxon) sont stockées avec Profession.None et l'attribut « Allegiance rank » alors
    // qu'elles sont verrouillées à une profession précise : sans cette résolution, elles passent
    // pour de l'inter-profession et « Triple Shot (Luxon) » (Rôdeur) s'affiche chez un Guerrier.
    // Ce sont les SEULES dans ce cas — les autres compétences sans profession (Deldrimor, Norn,
    // Asura, Avant-garde d'Ebon, Sceau de résurrection…) sont réellement inter-professions.
    private static Profession EffectiveProfession(Skill s)
    {
        var required = GwAllegianceData.RequiredProfession(s);
        return required != Profession.None ? required : s.Profession;
    }

    private bool InScope(Skill s)
    {
        var prof = EffectiveProfession(s);
        if (_professionScope is { Count: > 0 })
        {
            // Portée PR/SEC posée. Un onglet « toute la profession » choisit une option concrète
            // (forcément dans la portée) pour restreindre à cette SEULE profession ; sinon (All) on
            // montre toute la portée. Cross-profession (None) toujours incluse dans les deux cas.
            var only = _selectedProfessionOption.Value;
            if (only is { } concrete && _professionScope.Contains(concrete))
                return prof == concrete || prof == Profession.None;
            return _professionScope.Contains(prof) || prof == Profession.None;
        }
        var p = _selectedProfessionOption.Value;
        return p is null || prof == p || prof == Profession.None;
    }

    private void RefreshAttributes()
    {
        var prevProfAttr = _selectedProfessionAttribute;
        var prevPveLabel = _selectedPveItem?.Label;

        ProfessionAttributes.Clear();
        PveCategories.Clear();

        var inScope = AllSkills.Where(InScope).ToList();

        // Panneau 2 : entrée méta "All attributes" + caractéristiques de profession (hors PvE-only).
        ProfessionAttributes.Add(GwAttributeData.AllAttributesLabel);
        foreach (var a in inScope
                     .Select(s => s.Attribute)
                     .Where(a => !string.IsNullOrWhiteSpace(a) && !GwAttributeData.IsPveOnlyAttribute(a))
                     .Distinct()
                     .OrderBy(a => a))
            ProfessionAttributes.Add(a);

        // Panneau 3 : umbrellas campagne + sous-catégories présentes dans la portée (ordre des défs).
        foreach (var def in GwAttributeData.PveCategoryDefs)
            if (inScope.Any(def.Matches))
                PveCategories.Add(new PveCategoryItem(def));

        // Restaure la sélection en respectant l'exclusivité.
        var restoredPve = prevPveLabel is null
            ? null
            : PveCategories.FirstOrDefault(i => i.Label == prevPveLabel);
        if (restoredPve is not null)
            SetSelection(string.Empty, restoredPve);
        else
        {
            var profAttr = ProfessionAttributes.Contains(prevProfAttr)
                ? prevProfAttr
                : ProfessionAttributes.FirstOrDefault() ?? string.Empty;
            SetSelection(profAttr, null);
        }
    }

    private void RefreshSkills()
    {
        FilteredSkills.Clear();

        // Recherche UNIFORME (décision 2) : elle ne désactive plus les filtres profession et
        // caractéristique. Un mot est cherché dans les noms ET reconnu comme catégorie (« flash
        // ench » → les enchantements flash) ; le résultat est l'UNION des deux (décision 13).
        bool searching = !string.IsNullOrEmpty(_searchText);
        var recognized = searching ? SkillCategoryData.Recognize(_searchText) : [];

        bool MatchesSearch(Skill s) =>
            !searching
            || s.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || recognized.Any(d => d.Test(s));

        // Socle = tous les filtres SAUF profession et caractéristique. Il sert deux fois : à la
        // liste affichée, et au décompte « N masqués par … » qui nomme les responsables.
        var basis = AllSkills
            .Where(s => !CelestialSkillNames.Contains(s.Name))   // filtre caché : jamais de skills célestes
            .Where(MatchesGameMode)
            .Where(s => _extraFilter?.Invoke(s) ?? true)
            .Where(s => _selectedTypeItem?.Def.Test(s) ?? true)
            .Where(s => _selectedMechanicItem?.Def.Test(s) ?? true)
            .Where(MatchesSearch)
            .ToList();

        var filtered = basis.Where(s => InScope(s) && MatchesAttributeFilter(s)).ToList();
        UpdateSearchNotice(basis, filtered.Count, recognized);

        // Grisé des deux colonnes : on repart de tout ce qui passe les filtres SAUF les deux
        // colonnes elles-mêmes, puis on croise chaque entrée avec la sélection de l'autre colonne.
        var pool = AllSkills
            .Where(s => !CelestialSkillNames.Contains(s.Name))
            .Where(MatchesGameMode)
            .Where(s => _extraFilter?.Invoke(s) ?? true)
            .Where(MatchesSearch)
            .Where(s => InScope(s) && MatchesAttributeFilter(s))
            .ToList();
        foreach (var item in TypeCategories)
            item.IsEmpty = !pool.Any(s => (_selectedMechanicItem?.Def.Test(s) ?? true) && item.Def.Test(s));
        foreach (var item in MechanicCategories)
            item.IsEmpty = !pool.Any(s => (_selectedTypeItem?.Def.Test(s) ?? true) && item.Def.Test(s));

        var skills = (SortColumn, SortAscending) switch
        {
            ("E", true)  => filtered.OrderBy(s => s.EnergyCost),
            ("E", false) => filtered.OrderByDescending(s => s.EnergyCost),
            ("A", true)  => filtered.OrderBy(s => s.Adrenaline),
            ("A", false) => filtered.OrderByDescending(s => s.Adrenaline),
            ("S", true)  => filtered.OrderBy(s => s.Sacrifice),
            ("S", false) => filtered.OrderByDescending(s => s.Sacrifice),
            ("O", true)  => filtered.OrderBy(s => s.Overcast),
            ("O", false) => filtered.OrderByDescending(s => s.Overcast),
            ("U", true)  => filtered.OrderBy(s => s.Upkeep),
            ("U", false) => filtered.OrderByDescending(s => s.Upkeep),
            ("C", true)  => filtered.OrderBy(s => s.CastTime),
            ("C", false) => filtered.OrderByDescending(s => s.CastTime),
            ("R", true)  => filtered.OrderBy(s => s.Recharge),
            ("R", false) => filtered.OrderByDescending(s => s.Recharge),
            (_, true)    => filtered.OrderBy(s => s.DisplayName),
            (_, false)   => filtered.OrderByDescending(s => s.DisplayName),
        };

        foreach (var s in skills)
            FilteredSkills.Add(s);

        EnergyColumnWidth = FilteredSkills.Any(s => s.EnergyCost > 0) ? StatColumnWidth : 0;
        AdrenalineColumnWidth = FilteredSkills.Any(s => s.Adrenaline > 0) ? StatColumnWidth : 0;
        SacrificeColumnWidth = FilteredSkills.Any(s => s.Sacrifice > 0) ? StatColumnWidth : 0;
        OvercastColumnWidth = FilteredSkills.Any(s => s.Overcast > 0) ? StatColumnWidth : 0;
        UpkeepColumnWidth = FilteredSkills.Any(s => s.Upkeep > 0) ? StatColumnWidth : 0;
        CastColumnWidth = FilteredSkills.Any(s => s.CastTime > 0) ? StatColumnWidth : 0;
        RechargeColumnWidth = FilteredSkills.Any(s => s.Recharge > 0) ? StatColumnWidth : 0;
    }

    // Ligne de retour sous la barre de recherche (décision 14) : ce qu'on a compris de la saisie,
    // et ce que les filtres cachent. Elle n'apparaît que si elle a quelque chose à dire, et ne
    // nomme QUE des filtres que le clic peut lever — si c'est la portée PR/SEC du catalogue de
    // recherche qui bloque, aucun clic n'y peut rien, donc on se tait.
    private void UpdateSearchNotice(List<Skill> basis, int shownCount,
                                    IReadOnlyList<SkillCategoryData.SkillCategoryDef> recognized)
    {
        int hidden = basis.Count(InScopeIgnoringProfessionOption) - shownCount;

        bool profBlocks = _selectedProfessionOption.Value is not null && basis.Any(s => !InScope(s));
        bool attrBlocks = AttributeFilterActive && basis.Any(s => !MatchesAttributeFilter(s));
        CanLiftFilters = hidden > 0 && (profBlocks || attrBlocks);

        var parts = new List<string>();
        if (recognized.Count > 0)
            parts.Add(string.Format(T("S.Cat.Recognized"),
                string.Join(", ", recognized.Select(d => SkillCategoryData.DisplayName(d.Label))
                                            .Distinct(StringComparer.CurrentCultureIgnoreCase))));
        if (CanLiftFilters)
        {
            var culprits = new List<string>();
            if (profBlocks) culprits.Add(T("S.Cat.Professions"));
            if (attrBlocks) culprits.Add(_selectedPveItem is not null ? "PvE only" : T("S.Cat.Attributes"));
            parts.Add(string.Format(T("S.Cat.Hidden"), hidden, string.Join(", ", culprits)));
        }

        SearchNotice = string.Join("   ", parts);
    }

    // Portée hors option de profession : ce que la liste montrerait si on levait le filtre
    // profession ET le filtre caractéristique. Sert de référence au décompte « N masqués ».
    private bool InScopeIgnoringProfessionOption(Skill s) =>
        _professionScope is not { Count: > 0 }
        || _professionScope.Contains(EffectiveProfession(s))
        || EffectiveProfession(s) == Profession.None;

    private bool AttributeFilterActive =>
        _selectedPveItem is not null
        || (_selectedProfessionAttribute.Length > 0
            && _selectedProfessionAttribute != GwAttributeData.AllAttributesLabel);

    private static string T(string key) => ZCodex.App.LanguageManager.T(key);

    // Filtre d'attribut effectif : catégorie PvE-only si l'une est sélectionnée, sinon
    // caractéristique de profession (les deux sont mutuellement exclusives).
    private bool MatchesAttributeFilter(Skill s)
    {
        if (_selectedPveItem is not null)
            return _selectedPveItem.Def.Matches(s);

        return _selectedProfessionAttribute.Length == 0
               || _selectedProfessionAttribute == GwAttributeData.AllAttributesLabel
               || s.Attribute == _selectedProfessionAttribute;
    }

    // Compétences « accessibles au combo » PR/SEC pour le bandeau de conditions : profession
    // requise (allégeance verrouillée incluse) ∈ {PR, SEC, cross-profession}, filtre PvE/PvP
    // COURANT appliqué (choix produit : le grisé suit le mode de jeu du catalogue), célestes
    // exclues — SANS les filtres d'attribut/recherche propres à l'affichage du catalogue.
    public IEnumerable<Skill> ScopeSkillsFor(Profession pr, Profession sec)
    {
        foreach (var s in AllSkills)
        {
            if (CelestialSkillNames.Contains(s.Name)) continue;
            if (!MatchesGameMode(s)) continue;
            var prof = EffectiveProfession(s);
            if (prof != Profession.None && prof != pr && prof != sec) continue;
            yield return s;
        }
    }

    private bool MatchesGameMode(Skill s) => _selectedGameMode switch
    {
        SkillGameMode.PvE => !IsPvpVariant(s.Name),
        // PvP = seulement les compétences utilisables en PvP. On exclut donc les 3 cas "PvE-only" :
        //   (3) la version PvE d'une compétence splittée (on garde son pendant "(PvP)")  → _pvpSplitBaseNames
        //   (1) attribut rang de titre / catégorie PvE   ┐
        //   (2) élite anniversaire                        ┘→ GwAttributeData.IsPveOnlySkill
        SkillGameMode.PvP => !_pvpSplitBaseNames.Contains(s.Name) && !GwAttributeData.IsPveOnlySkill(s),
        _                 => true,
    };

    private static bool IsPvpVariant(string name)
        => name.EndsWith(" (PvP)", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Variante d'une compétence correspondant au mode de jeu COURANT : « Heal Party » en PvE,
    /// « Heal Party (PvP) » en PvP — les deux entrées portant des valeurs différentes (recharge,
    /// incantation…), c'est ce qui fait que les infobulles et les calculs reflètent le contexte.
    /// Renvoie la compétence inchangée en mode « All », si elle n'est pas splittée, ou si la
    /// variante visée est absente du catalogue.
    /// </summary>
    public Skill ResolveForGameMode(Skill skill)
    {
        string target = _selectedGameMode switch
        {
            SkillGameMode.PvP => SkillVariants.PvpName(skill.Name),
            SkillGameMode.PvE => SkillVariants.BaseName(skill.Name),
            _                 => skill.Name,
        };
        return target != skill.Name && _byName.TryGetValue(target, out var variant) ? variant : skill;
    }

    public void LoadSkills(IEnumerable<Skill> skills)
    {
        AllSkills.Clear();
        foreach (var s in skills)
            AllSkills.Add(s);

        _pvpSplitBaseNames.Clear();
        foreach (var s in AllSkills)
            if (IsPvpVariant(s.Name))
                _pvpSplitBaseNames.Add(s.Name[..^6]); // retire " (PvP)"

        _byName.Clear();
        foreach (var s in AllSkills)
            _byName[s.Name] = s;

        RefreshAttributes();
        RefreshSkills();
    }

    // Bascule de langue : les libellés Profession/game-mode sont des propriétés Label calculées sur
    // des VM NOTIFIANTES → on lève PropertyChanged(Label) sans toucher aux collections (sélection des
    // ComboBox préservée). Les listes attribut/PvE (chaînes/DisplayLabel) sont reconstruites par
    // RefreshAttributes (ListBox + converter, sélection restaurée par clé EN).
    public void RefreshLanguage()
    {
        foreach (var p in Professions) p.RaiseLanguageChanged();
        foreach (var m in GameModes)   m.RaiseLanguageChanged();
        // Colonnes Types/Mechanics : listes statiques → on notifie le libellé, on ne reconstruit pas
        // (un Clear+Add ferait perdre la sélection, cf. la régression ComboBox du 21/07).
        foreach (var t in TypeCategories)     t.RaiseLanguageChanged();
        foreach (var c in MechanicCategories) c.RaiseLanguageChanged();
        // ...mais l'ordre alphabétique, lui, change avec la langue. Move() plutôt que Clear+Add :
        // les items gardent leur identité, donc la sélection ET le surlignage des puces des autres
        // catalogues (qui la reflètent par ReferenceEquals) survivent au réordonnancement.
        ReorderLike(TypeCategories, SkillCategoryData.SortForDisplay(SkillCategoryData.TypeDefs),
                    x => x.Def);
        ReorderLike(MechanicCategories, SkillCategoryData.SortForDisplay(SkillCategoryData.MechanicDefs),
                    x => x.Def);
        OnPropertyChanged(nameof(GameModeLabel));   // libellé du bouton de la barre d'outils
        RefreshAttributes();
        RefreshSkills();   // re-rend la liste (DisplayName des skills dans la langue courante)
    }
}
