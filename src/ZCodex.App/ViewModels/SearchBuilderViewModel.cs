using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Search;
using System.Collections.ObjectModel;

namespace ZCodex.App.ViewModels;

// Onglet « Search » : construit une requête (perso + catalogue, façon PawNed²) puis lance
// BuildSearchEngine.SearchRoot. Réutilise CharacterSlotViewModel (perso-requête) et
// SkillPanelViewModel (catalogue dédié, instance séparée de l'éditeur).
public class SearchBuilderViewModel : ViewModelBase
{
    private SkillVariantMode _variantMode = SkillVariantMode.AllVersions;
    private bool _searchInTemplates = true;
    private bool _searchInTeamBuilds = true;
    private bool _pveTabShown = true;

    // PR/SEC précédents, pour détecter le retrait d'une profession (→ None) et vider ses skills des slots.
    private Profession _prevPrimary = Profession.None;
    private Profession _prevSecondary = Profession.None;

    // L'onglet "PvE only" (et les skills PvE-only) n'ont aucun sens en mode "PvP only" du catalogue.
    private bool ShowPveTab => Catalog.SelectedGameMode != SkillGameMode.PvP;

    // Le perso-requête (0–8 compétences, niveaux de carac, PR/SEC). Attributs toujours visibles.
    public CharacterSlotViewModel QueryCharacter { get; } = new() { ShowAttributeEditor = true };

    // Catalogue dédié à la recherche.
    public SkillPanelViewModel Catalog { get; } = new();

    public SearchBuilderViewModel()
    {
        // Les onglets du catalogue sont pilotés par PR/SEC du perso-requête (≡ filtre profession
        // du catalogue, cf. [[feedback_grill_before_coding]]). On les reconstruit à chaque changement.
        // Retirer une profession (PR ou SEC → None) vide aussi ses compétences des slots.
        QueryCharacter.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(CharacterSlotViewModel.PrimaryProfession):
                    var pr = QueryCharacter.PrimaryProfession;
                    if (_prevPrimary != Profession.None && pr == Profession.None)
                        ClearSkillsOfProfession(_prevPrimary);
                    _prevPrimary = pr;
                    RebuildCatalogTabs();
                    RefreshQueryViolations();
                    break;
                case nameof(CharacterSlotViewModel.SecondaryProfession):
                    var sec = QueryCharacter.SecondaryProfession;
                    if (_prevSecondary != Profession.None && sec == Profession.None)
                        ClearSkillsOfProfession(_prevSecondary);
                    _prevSecondary = sec;
                    RebuildCatalogTabs();
                    RefreshQueryViolations();
                    break;
            }
        };
        foreach (var slot in QueryCharacter.SkillSlots)
            slot.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SkillSlotViewModel.Skill))
                    RefreshQueryViolations();
            };
        // "PvP only" masque l'onglet "PvE only" → on reconstruit la barre quand cette visibilité bascule.
        Catalog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SkillPanelViewModel.SelectedGameMode) && ShowPveTab != _pveTabShown)
                RebuildCatalogTabs();
        };
        BuildCategoryTabs();   // lignes Skill Types / Mechanics : indépendantes de PR/SEC
        RebuildCatalogTabs();
        RefreshQueryViolations();
    }

    // Onglets du catalogue (ligne 1) + sous-onglets du groupe déplié (ligne 2, cas None/None ou PvE).
    public ObservableCollection<CatalogTab> CatalogTabs    { get; } = new();
    public ObservableCollection<CatalogTab> CatalogSubTabs { get; } = new();
    public bool ShowSubTabs => CatalogSubTabs.Count > 0;

    public SkillVariantMode VariantMode
    {
        get => _variantMode;
        set
        {
            if (SetField(ref _variantMode, value))
            {
                OnPropertyChanged(nameof(VariantAll));
                OnPropertyChanged(nameof(VariantPve));
                OnPropertyChanged(nameof(VariantPvp));
            }
        }
    }

    // Radios (exclusifs) — un seul actif à la fois.
    public bool VariantAll { get => VariantMode == SkillVariantMode.AllVersions; set { if (value) VariantMode = SkillVariantMode.AllVersions; } }
    public bool VariantPve { get => VariantMode == SkillVariantMode.PveOnly;     set { if (value) VariantMode = SkillVariantMode.PveOnly; } }
    public bool VariantPvp { get => VariantMode == SkillVariantMode.PvpOnly;     set { if (value) VariantMode = SkillVariantMode.PvpOnly; } }

    public bool SearchInTemplates  { get => _searchInTemplates;  set { if (SetField(ref _searchInTemplates, value))  NotifyScopeChanged(); } }
    public bool SearchInTeamBuilds { get => _searchInTeamBuilds; set { if (SetField(ref _searchInTeamBuilds, value)) NotifyScopeChanged(); } }

    public SearchScope Scope =>
        (SearchInTemplates  ? SearchScope.SkillTemplates : SearchScope.None) |
        (SearchInTeamBuilds ? SearchScope.TeamBuilds     : SearchScope.None);

    // Cocher/décocher un périmètre change Scope, donc l'activation du bouton Rechercher.
    private void NotifyScopeChanged()
    {
        OnPropertyChanged(nameof(Scope));
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(SearchDisabledReason));
    }

    // Reconstruit les onglets du catalogue selon PR/SEC. None/None → groupes par profession (browse
    // découplé) ; PR/SEC posés → onglets d'attributs à plat + portée `ProfessionScope` sur le catalogue.
    // Public : appelé aussi après le chargement des skills (MainWindow) pour recâbler le filtre initial.
    public void RebuildCatalogTabs()
    {
        var pr  = QueryCharacter.PrimaryProfession;
        var sec = QueryCharacter.SecondaryProfession;

        var scope = new HashSet<Profession>();
        if (pr  != Profession.None) scope.Add(pr);
        if (sec != Profession.None) scope.Add(sec);

        CatalogTabs.Clear();
        CatalogSubTabs.Clear();

        if (scope.Count == 0)
        {
            // Browse toutes professions : un groupe dépliable par profession.
            Catalog.ProfessionScope = null;
            foreach (var p in Enum.GetValues<Profession>().Where(p => p != Profession.None))
            {
                var group = new CatalogTab(p.ToString(), CatalogTabKind.Profession) { Profession = p };
                foreach (var a in GwAttributeData.ForProfession(p).OrderBy(a => a.IsPrimary ? 0 : 1).ThenBy(a => a.Name))
                    group.Children.Add(new CatalogTab(a.Name, CatalogTabKind.Attribute) { Attribute = a.Name });
                CatalogTabs.Add(group);
            }
            Catalog.SelectedProfessionOption    = ProfessionOption.All;
            Catalog.SelectedProfessionAttribute = GwAttributeData.AllAttributesLabel;
        }
        else
        {
            // Catalogue limité à PR/SEC (l'équivalence). Pour CHAQUE profession de la portée : un onglet
            // « toute la profession » (browse complet, comme un groupe None/None mais sans sous-onglets)
            // suivi de ses caractéristiques à plat (y compris la primaire de la SEC, browsable pour
            // équiper ses skills, mais hors des seuils — cf. #6).
            Catalog.ProfessionScope = scope;
            if (pr  != Profession.None) AddProfessionTabs(pr);
            if (sec != Profession.None && sec != pr) AddProfessionTabs(sec);
            Catalog.SelectedProfessionOption    = ProfessionOption.All;
            Catalog.SelectedProfessionAttribute = GwAttributeData.AllAttributesLabel;
        }

        // Onglets communs à toutes les professions. L'onglet "PvE only" est masqué en mode "PvP only"
        // (les compétences PvE-only y sont filtrées → l'onglet ne servirait à rien).
        CatalogTabs.Add(new CatalogTab("No Attribute", CatalogTabKind.NoAttribute) { Attribute = "No Attribute" });
        if (ShowPveTab)
        {
            // Cliquer "PvE only" montre TOUTES les compétences DÉDIÉES au PvE (rang de titre / allégeance
            // / catégorie PvE + élites anniversaire) — jamais les skills de profession ni la version PvE
            // des skills splittées (contrairement à la combobox "PvE only" = tout ce qui est jouable en
            // PvE). Se déplie en sous-catégories par piste.
            var allPve = new PveCategoryItem(new GwAttributeData.PveCategoryDef("PvE only", 0, GwAttributeData.IsPveOnlySkill));
            var pve = new CatalogTab("PvE only", CatalogTabKind.PveGroup) { PveCategory = allPve };
            foreach (var def in GwAttributeData.PveCategoryDefs)
                pve.Children.Add(new CatalogTab(def.Label, CatalogTabKind.PveCategory) { PveCategory = new PveCategoryItem(def) });
            CatalogTabs.Add(pve);
        }
        _pveTabShown = ShowPveTab;

        OnPropertyChanged(nameof(ShowSubTabs));
    }

    // Bascule de langue : les onglets ont un Label calculé (FR/EN) → re-notifier sans reconstruire
    // (préserve la sélection). Le catalogue (skills/filtres) est rafraîchi par ailleurs.
    public void RefreshCatalogTabsLanguage()
    {
        foreach (var t in CatalogTabs)
        {
            t.RaiseLanguageChanged();
            foreach (var c in t.Children) c.RaiseLanguageChanged();
        }
        foreach (var s in CatalogSubTabs) s.RaiseLanguageChanged();
        foreach (var t in CatalogTypeTabs) t.RaiseLanguageChanged();
        // L'ordre alphabétique dépend de la langue : SkillPanelViewModel vient de réordonner les
        // deux colonnes, les puces s'alignent dessus (elles n'en sont qu'un reflet).
        ReorderCategoryTabs();
    }

    // Ajoute pour une profession un onglet « toute la profession » (browse complet) puis TOUTES ses
    // caractéristiques à plat (primaire d'abord). La caractéristique primaire de la SEC est incluse ICI
    // (onglet browsable, pour équiper ses skills) mais reste HORS de la grille de seuils (exclue par
    // CharacterSlotViewModel.RefreshAttributeRows) — cf. règle #6.
    private void AddProfessionTabs(Profession p)
    {
        CatalogTabs.Add(new CatalogTab(p.ToString(), CatalogTabKind.Profession) { Profession = p });
        foreach (var a in GwAttributeData.ForProfession(p)
                     .OrderBy(a => a.IsPrimary ? 0 : 1).ThenBy(a => a.Name))
            CatalogTabs.Add(new CatalogTab(a.Name, CatalogTabKind.Attribute) { Attribute = a.Name });
    }

    // ── Lignes « Skill Types » et « Mechanics » ───────────────────────────────
    //
    // Chacune sa ligne, sous les onglets de profession (choix Philippe 19/08/2026). Construites UNE
    // fois : elles ne dépendent pas de PR/SEC, contrairement aux onglets. Toutes les entrées sont
    // toujours là, à la même place — celles qui ne rendraient rien sont GRISÉES (SkillCategoryItem
    // .IsEmpty), jamais retirées : les puces ne bougent pas sous le curseur quand on change de
    // profession. Le surlignage suit la sélection du catalogue, d'où qu'elle vienne.
    // ⚠ SEULS les types sont des puces ici. Les MÉCANIQUES sont un rail de droite pleine hauteur,
    // lié directement à Catalog.MechanicCategories : pas de collection miroir, donc pas de
    // réordonnancement ni de surlignage à tenir de ce côté. Identique à la vue Build.
    public ObservableCollection<CatalogTab> CatalogTypeTabs { get; } = new();

    private void BuildCategoryTabs()
    {
        foreach (var item in Catalog.TypeCategories)
            CatalogTypeTabs.Add(new CatalogTab(item.Label, CatalogTabKind.TypeCategory) { Category = item });

        Catalog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SkillPanelViewModel.SelectedTypeItem))
                SyncCategoryTabSelection();
        };
        SyncCategoryTabSelection();
    }


    // Les puces sont construites dans l'ordre des colonnes du catalogue et doivent y rester : à la
    // bascule de langue, l'ordre alphabétique change des deux côtés.
    private void ReorderCategoryTabs()
    {
        SkillPanelViewModel.ReorderLike(CatalogTypeTabs,
            SkillCategoryData.SortForDisplay(SkillCategoryData.TypeDefs), t => t.Category?.Def);
    }

    // Le surlignage est un REFLET de la sélection du catalogue, pas une seconde source de vérité :
    // masquer les colonnes (qui remet les deux filtres sur « All ») met donc les puces à jour aussi.
    private void SyncCategoryTabSelection()
    {
        foreach (var t in CatalogTypeTabs)
            t.IsSelected = ReferenceEquals(t.Category, Catalog.SelectedTypeItem);
    }

    /// <summary>Clic sur une puce « Skill Types ». N'affecte NI la profession NI la caractéristique :
    /// c'est le croisement des trois filtres qui est recherché.</summary>
    public void SelectCatalogTypeTab(CatalogTab tab)
    {
        if (tab.Category is { } item) Catalog.SelectedTypeItem = item;
    }

    // Clic sur un onglet de la ligne 1 : déplie ses enfants (ligne 2) et applique son filtre.
    public void SelectCatalogTab(CatalogTab tab)
    {
        foreach (var t in CatalogTabs) t.IsSelected = ReferenceEquals(t, tab);
        CatalogSubTabs.Clear();
        foreach (var c in tab.Children) CatalogSubTabs.Add(c);
        OnPropertyChanged(nameof(ShowSubTabs));
        ApplyTabFilter(tab);
    }

    // Clic sur un sous-onglet (ligne 2) : applique son filtre (ne touche pas à la ligne 1).
    public void SelectCatalogSubTab(CatalogTab sub)
    {
        foreach (var t in CatalogSubTabs) t.IsSelected = ReferenceEquals(t, sub);
        ApplyTabFilter(sub);
    }

    private void ApplyTabFilter(CatalogTab tab)
    {
        switch (tab.Kind)
        {
            case CatalogTabKind.Profession:   // browse d'une profession entière (None/None)
                Catalog.SelectedProfessionOption =
                    Catalog.Professions.FirstOrDefault(o => o.Value == tab.Profession) ?? ProfessionOption.All;
                Catalog.SelectedProfessionAttribute = GwAttributeData.AllAttributesLabel;
                break;
            case CatalogTabKind.Attribute:    // l'attribut implique déjà sa profession
            case CatalogTabKind.NoAttribute:
                Catalog.SelectedProfessionOption    = ProfessionOption.All;
                Catalog.SelectedProfessionAttribute = tab.Attribute!;
                break;
            case CatalogTabKind.PveGroup:     // "PvE only" (parent) = toutes les compétences dédiées PvE
            case CatalogTabKind.PveCategory:  // une piste précise (Sunspear, Kurzick…)
                Catalog.SelectedProfessionOption = ProfessionOption.All;
                Catalog.SelectedPveItem          = tab.PveCategory;
                break;
        }
    }

    // ── Incompatibilité skill/profession (cadre rouge + blocage du bouton Rechercher) ──────────
    // Règle éditeur généralisée aux jokers : PR/SEC à None = « n'importe quelle profession ». Un
    // build a 2 emplacements (primaire/secondaire) ; les posés sont fixés, les libres = joker. Une
    // skill est en rouge si l'ensemble des professions requises déborde la capacité disponible.
    private bool _hasProfessionConflict;
    public bool HasProfessionConflict
    {
        get => _hasProfessionConflict;
        private set
        {
            if (SetField(ref _hasProfessionConflict, value))
            {
                OnPropertyChanged(nameof(CanSearch));
                OnPropertyChanged(nameof(SearchDisabledReason));
            }
        }
    }

    // Recherche possible si aucune incompatibilité de profession ET au moins un périmètre coché.
    // (La requête vide est gérée au lancement par un message, pas par une désactivation réactive :
    // observer chaque spinner de seuil serait disproportionné.)
    public bool CanSearch => !HasProfessionConflict && Scope != SearchScope.None;
    public string? SearchDisabledReason =>
        HasProfessionConflict
            ? ZCodex.App.LanguageManager.T("S.Search.FixProfConflict")
            : Scope == SearchScope.None
                ? ZCodex.App.LanguageManager.T("S.Search.PickScope")
                : null;

    public void RefreshQueryViolations()
    {
        var pr  = QueryCharacter.PrimaryProfession;
        var sec = QueryCharacter.SecondaryProfession;

        var fixedProfs = new HashSet<Profession>();
        if (pr  != Profession.None) fixedProfs.Add(pr);
        if (sec != Profession.None) fixedProfs.Add(sec);
        int freeSlots = (pr == Profession.None ? 1 : 0) + (sec == Profession.None ? 1 : 0);

        // Professions requises (hors cross-prof None) au-delà des professions fixées → à couvrir par
        // les emplacements libres. Si elles sont plus nombreuses que les libres, l'ensemble déborde.
        var extra = QueryCharacter.SkillSlots
            .Select(s => s.Skill).Where(s => s != null).Select(s => ProfOf(s!))
            .Where(p => p != Profession.None && !fixedProfs.Contains(p))
            .Distinct().ToList();
        bool overflow = extra.Count > freeSlots;

        foreach (var slot in QueryCharacter.SkillSlots)
        {
            var skill = slot.Skill;
            if (skill == null) { slot.HasViolation = false; continue; }
            var p = ProfOf(skill);
            slot.HasViolation = p != Profession.None && !fixedProfs.Contains(p) && overflow;
        }

        HasProfessionConflict = QueryCharacter.SkillSlots.Any(s => s.HasViolation);
    }

    // Profession requise d'une skill : allégeance verrouillée (Kurzick/Luxon…) si présente, sinon
    // la profession de la skill (None = cross-profession, jouable par tous).
    private static Profession ProfOf(Skill s)
    {
        var req = GwAllegianceData.RequiredProfession(s);
        return req != Profession.None ? req : s.Profession;
    }

    // Vide des slots les compétences de la profession qu'on vient de retirer (PR ou SEC → None).
    // Les compétences cross-profession (None) et celles de la profession restante sont conservées.
    private void ClearSkillsOfProfession(Profession removed)
    {
        if (removed == Profession.None) return;
        // Ne pas vider si la profession est encore présente ailleurs : un swap (None/W → W/None)
        // déplace W de la SEC vers la PR, ses compétences restent valides.
        if (removed == QueryCharacter.PrimaryProfession || removed == QueryCharacter.SecondaryProfession) return;
        foreach (var slot in QueryCharacter.SkillSlots)
            if (slot.Skill is { } sk && ProfOf(sk) == removed)
                slot.Skill = null;
    }

    // Construit la requête depuis le perso + le mode variante (variantes résolues via le catalogue).
    public BuildSearchQuery BuildQuery()
    {
        var q = new BuildSearchQuery
        {
            Primary   = QueryCharacter.PrimaryProfession   == Profession.None ? null : QueryCharacter.PrimaryProfession,
            Secondary = QueryCharacter.SecondaryProfession == Profession.None ? null : QueryCharacter.SecondaryProfession,
        };

        foreach (var slot in QueryCharacter.SkillSlots)
            if (slot.Skill is { } sk)
                q.RequiredSkillGroups.Add(SkillVariants.ResolveGroup(sk, VariantMode, Catalog.AllSkills));

        foreach (var row in QueryCharacter.PrimaryAttributeRows.Concat(QueryCharacter.SecondaryAttributeRows))
            if (row.Points > 0)
                q.AttributeThresholds.Add(new AttributeThreshold(
                    row.AttributeId, row.Points,
                    row.AtMost ? AttributeComparison.AtMost : AttributeComparison.AtLeast));

        return q;
    }
}

public enum CatalogTabKind
{
    Profession, Attribute, NoAttribute, PveGroup, PveCategory,
    // Skill Types et Mechanics ont chacun leur PROPRE ligne de puces sous les onglets de
    // profession (choix Philippe 19/08/2026) : chaque ligne gère sa sélection, donc les trois
    // filtres — profession, type, mécanique — sont surlignés en même temps sans se mentir.
    TypeCategory, MechanicCategory,
}

// Un onglet du catalogue de recherche : soit un groupe-profession dépliable (None/None), soit une
// caractéristique, soit un onglet commun (No Attribute / PvE only + ses catégories).
public sealed class CatalogTab : ViewModelBase
{
    public CatalogTab(string labelEn, CatalogTabKind kind) { LabelEn = labelEn; Kind = kind; }

    // Libellé passé à la construction (EN) : clé de filtrage/repli. Le filtrage utilise Kind/
    // Profession/Attribute/PveCategory, jamais Label → Label est purement d'affichage.
    public string LabelEn { get; }

    // Libellé AFFICHÉ (langue courante), dérivé du type + des données typées de l'onglet.
    // « PvE only » reste identique en FR (choix Philippe).
    public string Label => Kind switch
    {
        CatalogTabKind.Profession                        => Profession.DisplayName(),
        CatalogTabKind.Attribute or CatalogTabKind.NoAttribute => GwAttributeData.DisplayName(Attribute ?? LabelEn),
        CatalogTabKind.PveGroup                          => "PvE only",
        CatalogTabKind.PveCategory                       => PveCategory?.DisplayLabel ?? LabelEn,
        CatalogTabKind.TypeCategory or CatalogTabKind.MechanicCategory
                                                         => Category?.DisplayLabel ?? LabelEn,
        _                                                => LabelEn,
    };
    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(Label));

    public CatalogTabKind Kind { get; }
    public Profession Profession { get; init; }
    public string? Attribute { get; init; }
    public PveCategoryItem? PveCategory { get; init; }
    // Entrée d'une des deux lignes Skill Types / Mechanics. Le grisé (« ne rendrait rien ») vit
    // sur l'item partagé, pas sur la puce : une seule vérité pour les 4 catalogues.
    public SkillCategoryItem? Category { get; init; }
    public List<CatalogTab> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
}
