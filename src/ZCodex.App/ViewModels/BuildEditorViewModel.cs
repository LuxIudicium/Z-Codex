using ZCodex.Core.Data;
using ZCodex.Core.Models;
using System.Collections.ObjectModel;

namespace ZCodex.App.ViewModels;

// Onglet « Build » : éditeur d'un build SIMPLE (un seul personnage — skills + caracs + PR/SEC),
// enregistrable en template de compétences .txt (code O GW1). Cloné de l'onglet Search façon
// PawNed² : réutilise CharacterSlotViewModel EN MODE ÉDITEUR (attributs réels, PR obligatoire —
// isQuery=false car ce n'est PAS le perso-requête de la recherche) + SkillPanelViewModel (catalogue
// dédié, instance séparée de l'éditeur et de la recherche).
//
// TODO(build-editor): la machinerie catalogue-onglets (RebuildCatalogTabs / AddProfessionTabs /
//   SelectCatalogTab*) est DUPLIQUÉE depuis SearchBuilderViewModel — choix assumé (« dupliquer puis
//   adapter » + pragmatisme) pour ne pas risquer la Recherche qui marche. À extraire dans un socle
//   commun quand ça se stabilise.
// TODO(build-editor): équipement HORS PÉRIMÈTRE (décidé : code O pur = skills + caracs + PR/SEC).
public class BuildEditorViewModel : ViewModelBase
{
    private bool _pveTabShown = true;
    private bool _isActive;
    private string _title;
    private string _savedSignature = string.Empty;
    private bool _tracking;
    private bool _isDirty;

    // Nom d'onglet par défaut localisé (« Nouveau build N » / « New Build N »), baké à la création
    // dans la langue courante — même patron que TeamBuildViewModel.DefaultName : c'est un titre de
    // document renommable, pas un libellé d'UI, donc pas de hot-swap à la bascule de langue.
    public static string DefaultName(int n) =>
        string.Format(ZCodex.App.LanguageManager.T("S.Tab.BuildName"), n);

    // Le perso édité : mode éditeur (attributs réels toujours visibles). Fourni préchargé à
    // l'ouverture d'un .txt, ou vierge à la création d'un nouveau build.
    public CharacterSlotViewModel Character { get; }

    // Catalogue dédié à cet onglet.
    public SkillPanelViewModel Catalog { get; } = new();

    // Bandeau des 10 conditions sous la barre de compétences (couleur = infligée par une
    // skill équipée, grisée = accessible au combo, quasi-éteinte = inaccessible).
    public ConditionBandViewModel ConditionBand { get; } = new();

    // Flux simulé, VOLATIL : un .txt (code O natif) ne peut pas le porter → non persisté,
    // comme les BonusPoints (simulation éditeur). Remis à zéro à chaque ouverture.
    public FluxIndicatorViewModel FluxIndicator { get; } = new();

    // Rituels de la nature, VOLATILS (même raison que le flux : le .txt ne les porte pas).
    public NatureRitualEnvironment NatureRituals { get; } = new();

    // Bandeau des rituels de la nature équipés (togglables).
    public NatureRitualBandViewModel NatureRitualBand { get; } = new();

    // Fichier .txt d'origine si le build a été ouvert depuis le browser (réenregistrement en place).
    public string? SourcePath { get; set; }

    public BuildEditorViewModel(string title, CharacterSlotViewModel? character = null)
    {
        _title = title;
        bool isNewBlank = character == null;
        Character = character ?? new CharacterSlotViewModel();
        Character.ShowAttributeEditor = true;

        // Flux « attributs » (Chantier 13, Lot C) : le perso édité lit le flux VOLATIL de l'onglet
        // (le teambuild passe par OwnerBuild ; ici pas d'OwnerBuild → provider explicite), et ses
        // infobulles se rafraîchissent quand on active/désactive le flux.
        Character.ActiveFluxProvider = () => FluxIndicator.ActiveFlux;
        FluxIndicator.Changed += () => Character.RefreshTooltips();

        // Rituels de la nature : le perso édité lit l'environnement volatil de l'onglet (pas
        // d'OwnerBuild → providers explicites), et ses infobulles se rafraîchissent au toggle.
        Character.ActiveNatureRitualsProvider = () => NatureRituals.Active;
        Character.RoaringWindsBonusProvider =
            () => CharacterSlotViewModel.RoaringWindsBonusFor(NatureRituals.Active, new[] { Character }, NatureRituals.RoaringWindsRank);
        Character.TranquilityPercentProvider =
            () => CharacterSlotViewModel.TranquilityPercentFor(NatureRituals.Active, new[] { Character }, NatureRituals.TranquilityRank);
        NatureRituals.Changed += () => { Character.RefreshSkillTooltips(); RefreshNatureRitualBand(); };
        NatureRituals.RankPreview += RefreshNatureRitualBand;   // badge immédiat, recalcul débouncé via Changed

        // Heroic Refrain (Lot D) : build simple = un seul perso, donc "diffusion d'équipe" se
        // réduit à lui-même (auto-ciblage). Pas d'OwnerBuild → provider explicite, patron identique.
        Character.HeroicRefrainProvider = () => CharacterSlotViewModel.HeroicRefrainFor(new[] { Character });

        // Nouveau build vierge : la PR est obligatoire dans l'éditeur (isQuery=false → le picker
        // n'offre pas « None » sur la PR). Pour qu'un code Any/SEC ne puisse JAMAIS être produit,
        // on tire une profession primaire au hasard dès l'initialisation (un .txt importé garde la
        // sienne). Posée avant le câblage/RebuildCatalogTabs → catalogue déjà filtré sur cette prof.
        if (isNewBlank)
            Character.PrimaryProfession = RandomPrimaryProfession();

        // Les onglets du catalogue sont pilotés par PR/SEC du perso (≡ filtre profession du catalogue,
        // cf. [[feedback_grill_before_coding]]). Reconstruits à chaque changement. Tout changement de
        // profession vide aussi les compétences dont la profession n'est plus ni PR ni SEC.
        Character.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CharacterSlotViewModel.PrimaryProfession)
                               or nameof(CharacterSlotViewModel.SecondaryProfession))
            {
                SyncSkillsToProfessions();
                RebuildCatalogTabs();
            }
        };
        // "PvP only" masque l'onglet "PvE only" → reconstruire la barre quand cette visibilité bascule.
        // Le bandeau de conditions suit aussi le mode de jeu (le grisé = accessible dans ce mode).
        Catalog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SkillPanelViewModel.SelectedGameMode))
            {
                if (ShowPveTab != _pveTabShown) RebuildCatalogTabs();
                RefreshConditionBand();
            }
        };
        // Toute pose/retrait de compétence recalcule le bandeau de conditions.
        foreach (var slot in Character.SkillSlots)
            slot.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SkillSlotViewModel.Skill))
                {
                    RefreshConditionBand();
                    RefreshNatureRitualBand();
                }
            };
        RebuildCatalogTabs();
    }

    // Recalcule le bandeau de conditions : équipées = slots du perso, accessibles = catalogue
    // du combo PR/SEC au mode de jeu courant. Public : rejoué au switch de langue (InflictedBy est
    // une chaîne bakée avec le DisplayName des skills fauteurs).
    public void RefreshConditionBand()
    {
        var equipped = Character.SkillSlots
            .Where(s => s.Skill != null).Select(s => s.Skill!).ToList();
        var available = Catalog
            .ScopeSkillsFor(Character.PrimaryProfession, Character.SecondaryProfession).ToList();
        ConditionBand.Refresh([new ConditionScope(equipped, available)]);
    }

    // Recalcule le bandeau des rituels de la nature du perso édité. Public : MainViewModel le
    // rappelle quand la préférence « afficher tous les rituels » change.
    public void RefreshNatureRitualBand()
    {
        var equipped = Character.SkillSlots.Where(s => s.Skill != null).Select(s => s.Skill!).ToList();
        NatureRituals.SyncEquipped(NatureRitualBandViewModel.EquippedRituals(equipped)); // option B
        NatureRitualBand.Refresh(equipped, Catalog.AllSkills, NatureRituals);
    }

    public string Title
    {
        get => _title;
        set { if (SetField(ref _title, value)) OnPropertyChanged(nameof(DisplayName)); }
    }
    public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }

    // ── Dirty tracking ────────────────────────────────────────────────────────
    //
    // Contrairement à TeamBuildViewModel, qui liste les propriétés à NE PAS surveiller, on compare
    // ici une SIGNATURE de ce qu'un .txt sait porter. La raison est que cet onglet héberge beaucoup
    // d'état délibérément volatil — flux, rituels de la nature, rangs de titre, points bonus,
    // onglet de catalogue sélectionné — qu'un code O ne peut pas transporter (cf. les commentaires
    // de FluxIndicator et NatureRituals plus haut). Avec une liste d'exclusions, en oublier une
    // seule ferait demander « enregistrer ? » à la fermeture pour un réglage qui n'aurait de toute
    // façon jamais été écrit dans le fichier — et l'oubli serait silencieux.
    //
    // Avec une signature, l'abonnement peut au contraire être LARGE sans risque : seul un écart de
    // signature salit l'onglet. Ajouter demain un nouvel état volatil ne demande aucune précaution.
    //
    // Le contenu ci-dessous doit rester le miroir exact de ce qu'encode
    // MainWindow.EncodeCharacterCode : profession primaire, secondaire, les 8 compétences et les
    // points d'attribut. Les TitleRanks d'AttributesBuild en sont volontairement absents — ce sont
    // les rangs de titre simulés, que le code O ne porte pas.
    public string ContentSignature
    {
        get
        {
            var skills = string.Join(",", Character.SkillSlots.Select(s => s.Skill?.Id ?? 0));
            var attrs = Character.Attributes is { } a
                ? string.Join(",", a.Allocations.Where(x => x.Points > 0)
                                                .OrderBy(x => x.AttributeId)
                                                .Select(x => $"{x.AttributeId}:{x.Points}"))
                : string.Empty;
            return $"{(int)Character.PrimaryProfession}/{(int)Character.SecondaryProfession}|{skills}|{attrs}";
        }
    }

    public bool IsDirty => _isDirty;

    // Astérisque devant le titre de l'onglet, comme les team builds.
    public string DisplayName => _isDirty ? $"*{Title}" : Title;

    // ── Undo/redo ─────────────────────────────────────────────────────────────
    //
    // Même patron que TeamBuildViewModel : un événement à chaque mutation, un UndoManager attaché
    // par MainWindow. Le snapshot est la ContentSignature ci-dessus — donc EXACTEMENT le périmètre
    // du dirty flag et du .txt : annuler ne peut pas défaire un réglage volatil (flux, rituels,
    // rangs de titre, onglet de catalogue) qui n'a jamais eu vocation à entrer dans l'historique.
    public event Action? Mutated;

    public Undo.UndoManager? Undo { get; set; }

    // Recharge un snapshot produit par ContentSignature. Format : "pr/sec|id×8|attr:pts,...".
    // Tolérant : un snapshot malformé est ignoré plutôt que d'écraser le build à moitié.
    public void RestoreContent(string snapshot, IReadOnlyDictionary<int, Skill> skillsById)
    {
        var parts = snapshot.Split('|');
        if (parts.Length != 3) return;

        var profs = parts[0].Split('/');
        if (profs.Length != 2
            || !int.TryParse(profs[0], out int pr) || !int.TryParse(profs[1], out int sec)) return;

        var ids = parts[1].Split(',');
        if (ids.Length != Character.SkillSlots.Count) return;

        Character.PrimaryProfession   = (Profession)pr;
        Character.SecondaryProfession = (Profession)sec;

        // Les attributs AVANT les skills : changer PR/SEC purge les compétences hors profession
        // (SyncSkillsToProfessions), donc les slots se posent en dernier.
        var allocations = new List<AttributeAllocation>();
        foreach (var entry in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = entry.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[0], out int id) && int.TryParse(kv[1], out int pts))
                allocations.Add(new AttributeAllocation(id, pts));
        }
        // TitleRanks est HORS snapshot (rangs de titre simulés, absents du .txt) : le reconduire
        // tel quel, sinon annuler une pose de compétence réinitialiserait aussi les rangs.
        Character.Attributes = new AttributesBuild
        {
            Allocations = allocations,
            TitleRanks  = Character.Attributes?.TitleRanks ?? new(),
        };

        for (int i = 0; i < ids.Length; i++)
            Character.SkillSlots[i].Skill =
                int.TryParse(ids[i], out int sid) && skillsById.TryGetValue(sid, out var sk) ? sk : null;
    }

    // Appelé une fois l'onglet entièrement câblé (catalogue compris) : la signature de départ doit
    // être celle du build tel que l'utilisateur le voit, pas celle d'un état intermédiaire.
    public void BeginTracking()
    {
        _savedSignature = ContentSignature;
        _tracking = true;
        Character.PropertyChanged += (_, _) => OnChanged();
        Character.SkillSlots.CollectionChanged += (_, _) => OnChanged();
        foreach (var slot in Character.SkillSlots)
            slot.PropertyChanged += (_, _) => OnChanged();
    }

    // Abonnements volontairement larges (cf. le commentaire du dirty tracking) : Mutated est donc
    // bavard, mais l'UndoManager ne fait qu'y relancer sa fenêtre de coalescence et compare les
    // signatures avant d'empiler — une notification qui ne change rien ne crée aucun pas d'undo.
    private void OnChanged()
    {
        RefreshDirty();
        Mutated?.Invoke();
    }

    // Après un enregistrement réussi : le fichier reflète désormais l'état courant.
    public void MarkClean()
    {
        _savedSignature = ContentSignature;
        RefreshDirty();
    }

    // Les abonnements sont volontairement larges (toute notification du perso ou d'un slot), donc
    // très bavards : on ne notifie que sur BASCULE du booléen, pour ne pas inonder la liaison du
    // titre d'onglet à chaque frappe dans un champ d'attribut.
    private void RefreshDirty()
    {
        if (!_tracking) return;
        bool now = ContentSignature != _savedSignature;
        if (now == _isDirty) return;
        _isDirty = now;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayName));
    }

    // L'onglet "PvE only" (et les skills PvE-only) n'ont aucun sens en mode "PvP only" du catalogue.
    private bool ShowPveTab => Catalog.SelectedGameMode != SkillGameMode.PvP;

    // Onglets du catalogue (ligne 1) + sous-onglets du groupe déplié (ligne 2).
    public ObservableCollection<CatalogTab> CatalogTabs    { get; } = new();
    public ObservableCollection<CatalogTab> CatalogSubTabs { get; } = new();
    public bool ShowSubTabs => CatalogSubTabs.Count > 0;

    // Reconstruit les onglets du catalogue selon PR/SEC. None/None → groupes par profession (browse
    // découplé) ; PR/SEC posés → onglets d'attributs à plat + portée `ProfessionScope` sur le catalogue.
    // Public : appelé aussi après le chargement des skills (MainWindow) pour recâbler le filtre initial.
    // Bascule de langue : onglets à Label calculé (FR/EN) → re-notifier sans reconstruire.
    public void RefreshCatalogTabsLanguage()
    {
        foreach (var t in CatalogTabs)
        {
            t.RaiseLanguageChanged();
            foreach (var c in t.Children) c.RaiseLanguageChanged();
        }
        foreach (var s in CatalogSubTabs) s.RaiseLanguageChanged();
    }

    public void RebuildCatalogTabs()
    {
        var pr  = Character.PrimaryProfession;
        var sec = Character.SecondaryProfession;

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
            // Catalogue limité à PR/SEC. Pour CHAQUE profession de la portée : un onglet « toute la
            // profession » (browse complet) suivi de ses caractéristiques à plat.
            Catalog.ProfessionScope = scope;
            if (pr  != Profession.None) AddProfessionTabs(pr);
            if (sec != Profession.None && sec != pr) AddProfessionTabs(sec);
            Catalog.SelectedProfessionOption    = ProfessionOption.All;
            Catalog.SelectedProfessionAttribute = GwAttributeData.AllAttributesLabel;
        }

        // Onglets communs. "PvE only" masqué en mode "PvP only" du catalogue.
        CatalogTabs.Add(new CatalogTab("No Attribute", CatalogTabKind.NoAttribute) { Attribute = "No Attribute" });
        if (ShowPveTab)
        {
            var allPve = new PveCategoryItem(new GwAttributeData.PveCategoryDef("PvE only", 0, GwAttributeData.IsPveOnlySkill));
            var pve = new CatalogTab("PvE only", CatalogTabKind.PveGroup) { PveCategory = allPve };
            foreach (var def in GwAttributeData.PveCategoryDefs)
                pve.Children.Add(new CatalogTab(def.Label, CatalogTabKind.PveCategory) { PveCategory = new PveCategoryItem(def) });
            CatalogTabs.Add(pve);
        }
        _pveTabShown = ShowPveTab;

        OnPropertyChanged(nameof(ShowSubTabs));

        // Appelé sur changement de PR/SEC ET après le chargement des skills (MainWindow) →
        // couvre les deux déclencheurs des bandeaux (conditions + rituels).
        RefreshConditionBand();
        RefreshNatureRitualBand();
    }

    private void AddProfessionTabs(Profession p)
    {
        CatalogTabs.Add(new CatalogTab(p.ToString(), CatalogTabKind.Profession) { Profession = p });
        foreach (var a in GwAttributeData.ForProfession(p)
                     .OrderBy(a => a.IsPrimary ? 0 : 1).ThenBy(a => a.Name))
            CatalogTabs.Add(new CatalogTab(a.Name, CatalogTabKind.Attribute) { Attribute = a.Name });
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
            case CatalogTabKind.Profession:
                Catalog.SelectedProfessionOption =
                    Catalog.Professions.FirstOrDefault(o => o.Value == tab.Profession) ?? ProfessionOption.All;
                Catalog.SelectedProfessionAttribute = GwAttributeData.AllAttributesLabel;
                break;
            case CatalogTabKind.Attribute:
            case CatalogTabKind.NoAttribute:
                Catalog.SelectedProfessionOption    = ProfessionOption.All;
                Catalog.SelectedProfessionAttribute = tab.Attribute!;
                break;
            case CatalogTabKind.PveGroup:
            case CatalogTabKind.PveCategory:
                Catalog.SelectedProfessionOption = ProfessionOption.All;
                Catalog.SelectedPveItem          = tab.PveCategory;
                break;
        }
    }

    // Vide des slots toute compétence dont la profession requise n'est plus ni PR ni SEC (changement OU
    // retrait de profession). Les compétences cross-profession (None) sont toujours conservées.
    private void SyncSkillsToProfessions()
    {
        var kept = new HashSet<Profession>();
        if (Character.PrimaryProfession   != Profession.None) kept.Add(Character.PrimaryProfession);
        if (Character.SecondaryProfession != Profession.None) kept.Add(Character.SecondaryProfession);
        foreach (var slot in Character.SkillSlots)
            if (slot.Skill is { } sk)
            {
                var p = ProfOf(sk);
                if (p != Profession.None && !kept.Contains(p))
                    slot.Skill = null;
            }
    }

    // Tire une profession primaire au hasard parmi les 10 professions réelles (jamais None).
    // Enum contigu : None=0 puis 1..N → borne haute = Length (Next exclusif) couvre 1..N.
    private static Profession RandomPrimaryProfession()
        => (Profession)Random.Shared.Next(1, Enum.GetValues<Profession>().Length);

    // Profession requise d'une skill : allégeance verrouillée (Kurzick/Luxon…) si présente, sinon
    // la profession de la skill (None = cross-profession).
    private static Profession ProfOf(Skill s)
    {
        var req = GwAllegianceData.RequiredProfession(s);
        return req != Profession.None ? req : s.Profession;
    }
}
