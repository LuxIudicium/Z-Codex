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
        _selectedProfessionOption = Professions.First(p => p.Value == Profession.Warrior);
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
    private bool InScope(Skill s)
    {
        if (_professionScope is { Count: > 0 })
        {
            // Portée PR/SEC posée. Un onglet « toute la profession » choisit une option concrète
            // (forcément dans la portée) pour restreindre à cette SEULE profession ; sinon (All) on
            // montre toute la portée. Cross-profession (None) toujours incluse dans les deux cas.
            var only = _selectedProfessionOption.Value;
            if (only is { } concrete && _professionScope.Contains(concrete))
                return s.Profession == concrete || s.Profession == Profession.None;
            return _professionScope.Contains(s.Profession) || s.Profession == Profession.None;
        }
        var p = _selectedProfessionOption.Value;
        return p is null || s.Profession == p || s.Profession == Profession.None;
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

        // Recherche par nom active : on cherche dans tout le catalogue, en ignorant les
        // filtres profession et caractéristique (le filtre PvE/PvP reste appliqué).
        bool searching = !string.IsNullOrEmpty(_searchText);

        var filtered = AllSkills
            .Where(s => !CelestialSkillNames.Contains(s.Name))   // filtre caché : jamais de skills célestes
            .Where(s => searching || InScope(s))
            .Where(s => searching || MatchesAttributeFilter(s))
            .Where(MatchesGameMode)
            .Where(s => _extraFilter?.Invoke(s) ?? true)
            .Where(s => !searching
                        || s.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

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
            var req = GwAllegianceData.RequiredProfession(s);
            var prof = req != Profession.None ? req : s.Profession;
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
        OnPropertyChanged(nameof(GameModeLabel));   // libellé du bouton de la barre d'outils
        RefreshAttributes();
        RefreshSkills();   // re-rend la liste (DisplayName des skills dans la langue courante)
    }
}
