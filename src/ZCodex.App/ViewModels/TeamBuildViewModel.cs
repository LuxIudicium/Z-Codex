using ZCodex.Core.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;

namespace ZCodex.App.ViewModels;

public class TeamBuildViewModel : ViewModelBase, IRenamableTab
{
    private string _name = DefaultName(1);

    // Nom d'onglet par défaut localisé (« Nouveau Teambuild N » / « New Teambuild N »), baké à la
    // création dans la langue courante (comme un titre de document — pas de hot-swap voulu).
    public static string DefaultName(int n) =>
        string.Format(ZCodex.App.LanguageManager.T("S.Tab.DefaultName"), n);
    private bool _isDirty;
    private bool _isActive;
    private bool _showAttributes;
    private bool _showInheritedAsBarred = true;
    private bool _isRenaming;
    private string _editName = "";

    // Flux simulé sur ce build, persisté en .pn3 v7 (cf. ActiveFlux). Partagé avec l'icône.
    public FluxIndicatorViewModel FluxIndicator { get; }

    // Environnement « rituels de la nature » simulé sur ce build, persisté .pn3 v13 (global : un
    // rituel actif modifie les infobulles de tous les persos).
    public NatureRitualEnvironment NatureRituals { get; } = new();

    public TeamBuildViewModel()
    {
        FluxIndicator = new FluxIndicatorViewModel();
        // ActiveFlux n'est PAS dans la liste d'exclusion de BeginTracking : re-notifier suffit à
        // déclencher MarkDirty (dirty flag + Mutated pour le snapshot d'undo).
        // Un flux « attributs » (Hidden Talent / Meek) modifie les infobulles de tout l'arbre →
        // on rafraîchit chaque perso (les slots relisent ActiveFlux via OwnerBuild).
        FluxIndicator.Changed += () =>
        {
            OnPropertyChanged(nameof(ActiveFlux));
            foreach (var n in EnumerateTree()) n.RefreshTooltips();
        };
        // Même patron : re-notifier une prop tracked déclenche MarkDirty (dirty + Mutated/undo),
        // et chaque perso relit l'environnement via OwnerBuild.
        NatureRituals.Changed += () =>
        {
            OnPropertyChanged(nameof(NatureRitualsSignature));
            // Rituels = skills seulement (pas les attributs) → refresh léger sur tout l'arbre.
            foreach (var n in EnumerateTree()) n.RefreshSkillTooltips();
        };
        // Heroic Refrain (Lot D) : seule diffusion INTER-PERSO — Mutated couvre l'équipement/retrait
        // n'importe où dans l'arbre ET un changement de rang (Leadership) du porteur. Garde
        // signature (cf. reference_wpf_mutated_storm) : ne redémarre le débounce que si la diffusion
        // a réellement changé. Recalcul LOURD (RefreshTooltips, pas la version légère — il faut
        // recalculer SkillBoost sur les receveurs + l'overlay, pas seulement les tooltips) DÉBOUNCÉ
        // 500 ms (retour Philippe 19/07 : une rafale de molette sur le Leadership du lanceur ne doit
        // provoquer qu'UN SEUL recalcul, pas un par cran — même patron que le rang Roaring
        // Winds/Tranquility).
        _heroicRefrainTimer.Tick += (_, _) =>
        {
            _heroicRefrainTimer.Stop();
            foreach (var n in EnumerateTree())
            {
                n.RefreshAttributeBoostBand();
                n.RefreshTooltips();
            }
        };
        Mutated += () =>
        {
            var (skill, bonus) = HeroicRefrain;
            string sig = $"{skill?.Id}|{bonus}";
            if (sig == _heroicRefrainSig) return;
            _heroicRefrainSig = sig;
            _heroicRefrainTimer.Stop();
            _heroicRefrainTimer.Start();
        };
    }

    private string _heroicRefrainSig = "";
    private readonly DispatcherTimer _heroicRefrainTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    // Pass-through vers l'indicateur (source de vérité) : lu par le pont VM↔modèle .pn3.
    public Flux? ActiveFlux
    {
        get => FluxIndicator.ActiveFlux;
        set => FluxIndicator.ActiveFlux = value;
    }

    // Signature de l'environnement de rituels : re-notifiée à chaque changement → MarkDirty (comme
    // ActiveFlux). Non exclue de BeginTracking. L'état lui-même transite par la persistance .pn3.
    public string NatureRitualsSignature => string.Join(",", NatureRituals.ToSkillIds().OrderBy(x => x));

    // Surcoût Roaring Winds (rang du porteur, ou rang de simulation si non équipé), global au build.
    public int RoaringWindsBonus =>
        CharacterSlotViewModel.RoaringWindsBonusFor(NatureRituals.Active, EnumerateTree(), NatureRituals.RoaringWindsRank);

    // % Tranquility (durée d'enchantement) : rang du porteur, ou rang de simulation si non équipé.
    public int TranquilityPercent =>
        CharacterSlotViewModel.TranquilityPercentFor(NatureRituals.Active, EnumerateTree(), NatureRituals.TranquilityRank);

    // Heroic Refrain (Lot D) : compétence trouvée + bonus résolu au rang du porteur le plus fort,
    // sur TOUT l'arbre (racines + variantes). (null, 0) si personne ne l'équipe.
    public (Skill? Skill, int Bonus) HeroicRefrain => CharacterSlotViewModel.HeroicRefrainFor(EnumerateTree());

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Cible native d'enregistrement (.pn3). Null tant que le build n'a pas été sauvegardé.
    public string? FilePath { get; set; }

    // Fichier d'origine de l'onglet (tout format : .pn3/.pwnd/.txt), tracé pour le renommage
    // depuis l'onglet. Null pour un build neuf non encore sauvegardé.
    public string? SourcePath { get; set; }

    public string Name
    {
        get => _name;
        set { SetField(ref _name, value); OnPropertyChanged(nameof(DisplayName)); }
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public bool ShowAttributes
    {
        get => _showAttributes;
        set
        {
            if (SetField(ref _showAttributes, value))
                foreach (var c in EnumerateTree())
                    c.ShowAttributeEditor = value;
        }
    }

    // Toggle par build : afficher les slots identiques au parent en diagonale "hérité" (true)
    // ou en icône réelle (false). UI seule, non persisté, hors dirty tracking.
    public bool ShowInheritedAsBarred
    {
        get => _showInheritedAsBarred;
        set
        {
            if (SetField(ref _showInheritedAsBarred, value))
                foreach (var n in EnumerateTree())
                    foreach (var s in n.SkillSlots) s.RaiseBarredChanged();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set { if (SetField(ref _isDirty, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    public string DisplayName => IsDirty ? $"*{Name}" : Name;

    // ── Renommage inline de l'onglet (transitoire, hors dirty tracking) ────────
    public string RenameSeed => _name;

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

    public ObservableCollection<string> Tags { get; } = new();

    private static readonly Profession[] _allProfessions =
        Enum.GetValues<Profession>().Where(p => p != Profession.None).ToArray();

    private static CharacterSlotViewModel CreateWithRandomProfession() => new()
    {
        PrimaryProfession = _allProfessions[Random.Shared.Next(_allProfessions.Length)]
    };

    // Joueurs racine (niveau 0 de l'arbre). Les variantes vivent dans CharacterSlotViewModel.Variants.
    public ObservableCollection<CharacterSlotViewModel> Characters { get; } = new(
        Enumerable.Range(0, 8).Select(_ => CreateWithRandomProfession())
    );

    // Lignes affichées = arbre aplati en pré-ordre (racines + variantes), sous-arbres repliés exclus.
    // C'est ce que lie l'ItemsControl des personnages.
    public ObservableCollection<CharacterSlotViewModel> VisibleRows { get; } = new();

    private readonly HashSet<CharacterSlotViewModel> _subscribed = new();

    public void AddCharacterSlot()
    {
        // Cap GW1 : 12 joueurs racine max ; les variantes ne comptent pas.
        if (Characters.Count < 12)
            Characters.Add(CreateWithRandomProfession());
    }

    // ── Arbre : énumération, profondeurs, lignes visibles ─────────────────────

    public IEnumerable<CharacterSlotViewModel> EnumerateTree()
    {
        static IEnumerable<CharacterSlotViewModel> Walk(CharacterSlotViewModel n)
        {
            yield return n;
            foreach (var c in n.Variants)
                foreach (var d in Walk(c)) yield return d;
        }
        return Characters.SelectMany(Walk);
    }

    // (Re)câble abonnements + Parent/Depth/OwnerBuild de tout l'arbre, puis reconstruit VisibleRows.
    // À appeler après toute mutation structurelle.
    public void RefreshTree()
    {
        var all = EnumerateTree().ToList();
        var allSet = new HashSet<CharacterSlotViewModel>(all);

        foreach (var gone in _subscribed.Where(n => !allSet.Contains(n)).ToList())
        {
            UnsubscribeNode(gone);
            _subscribed.Remove(gone);
        }
        foreach (var n in all)
        {
            n.OwnerBuild = this;
            if (_subscribed.Add(n)) SubscribeNode(n);
        }
        // Roster spike : purge des lignes qui ont quitté l'arbre (suppression, restauration).
        for (int i = SpikeMembers.Count - 1; i >= 0; i--)
            if (!allSet.Contains(SpikeMembers[i])) SpikeMembers.RemoveAt(i);
        AssignParentsAndDepths();
        RebuildLockCells();
        RebuildVisibleRows();
    }

    private void AssignParentsAndDepths()
    {
        void Walk(CharacterSlotViewModel n, int depth, CharacterSlotViewModel? parent)
        {
            n.Depth = depth;
            n.Parent = parent;
            foreach (var c in n.Variants) Walk(c, depth + 1, n);
        }
        foreach (var root in Characters) Walk(root, 0, null);
    }

    private void RebuildVisibleRows()
    {
        VisibleRows.Clear();
        // Vue filtrée par cadenas : seules les lignes membres du cadenas actif, en ordre d'arbre.
        if (_activeLockFilter is { } f)
        {
            foreach (var n in EnumerateTree())
                if (f.Members.Contains(n)) VisibleRows.Add(n);
            return;
        }
        void Walk(CharacterSlotViewModel n)
        {
            VisibleRows.Add(n);
            if (!n.IsExpanded) return;
            foreach (var c in n.Variants) Walk(c);
        }
        foreach (var root in Characters) Walk(root);
    }

    // ── Filtre d'affichage par cadenas ────────────────────────────────────────
    private VariantLockViewModel? _activeLockFilter;
    public VariantLockViewModel? ActiveLockFilter
    {
        get => _activeLockFilter;
        private set
        {
            if (SetField(ref _activeLockFilter, value))
            {
                RebuildVisibleRows();
                // Vue cadenas N → décoché par défaut (on veut voir l'icône réelle des slots
                // homologues du cadenas) ; vue complète → recoché. Reste librement modifiable
                // par l'utilisateur ensuite, ce n'est qu'une valeur par défaut à chaque bascule.
                ShowInheritedAsBarred = value == null;
            }
        }
    }
    public void SetLockFilter(VariantLockViewModel? lk) => ActiveLockFilter = lk;
    public void ClearLockFilter() => ActiveLockFilter = null;
    public bool HasLocks => Locks.Count > 0;

    // ── Opérations sur les variantes ──────────────────────────────────────────

    // Crée une variante = copie totale et indépendante de `parent`, ajoutée sous lui.
    public CharacterSlotViewModel CreateVariant(CharacterSlotViewModel parent)
    {
        var v = CloneCharacter(parent);
        v.Name = parent.Name;          // copie totale (l'utilisateur renommera s'il veut)
        parent.Variants.Add(v);
        parent.IsExpanded = true;
        MarkDirty();
        RefreshTree();
        return v;
    }

    // Supprime un nœud ; s'il a des variantes, sa 1ʳᵉ variante est promue à sa place,
    // les autres deviennent enfants de la promue (après les enfants existants de celle-ci).
    public void DeleteRow(CharacterSlotViewModel node)
    {
        var container = node.Parent is { } p ? p.Variants : Characters;
        int idx = container.IndexOf(node);
        if (idx < 0) return;
        if (container == Characters && Characters.Count <= 1 && node.Variants.Count == 0) return;

        if (node.Variants.Count == 0)
        {
            container.RemoveAt(idx);
        }
        else
        {
            var promoted = node.Variants[0];
            node.Variants.RemoveAt(0);
            foreach (var rest in node.Variants.ToList())
            {
                node.Variants.Remove(rest);
                promoted.Variants.Add(rest);
            }
            container[idx] = promoted;   // remplace au même index (Parent recâblé par RefreshTree)
        }
        MarkDirty();
        RefreshTree();
    }

    public void MoveVariantUp(CharacterSlotViewModel node)
    {
        if (node.Parent is not { } p) return;
        int i = p.Variants.IndexOf(node);
        if (i <= 0) return;
        p.Variants.Move(i, i - 1);
        MarkDirty();
        RefreshTree();
    }

    public void MoveVariantDown(CharacterSlotViewModel node)
    {
        if (node.Parent is not { } p) return;
        int i = p.Variants.IndexOf(node);
        if (i < 0 || i >= p.Variants.Count - 1) return;
        p.Variants.Move(i, i + 1);
        MarkDirty();
        RefreshTree();
    }

    // Échange positionnel node↔parent : chacun adopte la place (parent, index, liste d'enfants)
    // de l'autre. Les enfants appartiennent à la PLACE, pas au nœud.
    // Ex. R(racine)=[V,V2], V=[Va] → après swap sur V : V(racine)=[R,V2], R=[Va].
    public void SwapWithParent(CharacterSlotViewModel node)
    {
        if (node.Parent is not { } p) return;
        var container = p.Parent is { } gp ? gp.Variants : Characters;
        int i = container.IndexOf(p);
        int j = p.Variants.IndexOf(node);
        if (i < 0 || j < 0) return;

        var pChildren = p.Variants.ToList();       // enfants attachés à la place de p
        var nodeChildren = node.Variants.ToList();  // enfants attachés à la place de node
        pChildren[j] = p;                           // node cède sa place (en j) à p

        node.Variants.Clear();
        foreach (var c in pChildren) node.Variants.Add(c);
        p.Variants.Clear();
        foreach (var c in nodeChildren) p.Variants.Add(c);

        container[i] = node;                        // node monte à la place de p
        MarkDirty();
        RefreshTree();                              // recâble tous les Parent/Depth
    }

    public void ToggleExpanded(CharacterSlotViewModel node)
    {
        node.IsExpanded = !node.IsExpanded;
        RebuildVisibleRows();
    }

    // ── Cadenas (tuples de variantes) ──────────────────────────────────────────

    public ObservableCollection<VariantLockViewModel> Locks { get; } = new();

    private bool _isLockSelectionMode;
    // Mode verrouillage : cases à cocher visibles + bandeau Valider/Annuler. Transient, hors dirty.
    public bool IsLockSelectionMode { get => _isLockSelectionMode; set => SetField(ref _isLockSelectionMode, value); }

    // Cadenas en cours d'édition (null = création d'un nouveau cadenas).
    private VariantLockViewModel? _editingLock;

    public string LockSelectionPrompt => _selectingSpike
        ? ZCodex.App.LanguageManager.T("S.Tb2.SpikePickRows") + _spikeSelectionError
        : _editingLock is { } lk
            ? string.Format(ZCodex.App.LanguageManager.T("S.Tb2.LockEditRows"), lk.Index)
            : ZCodex.App.LanguageManager.T("S.Tb2.LockPickRows");

    private static readonly string[] _lockPalette =
        { "#E53935", "#1E88E5", "#43A047", "#FB8C00", "#8E24AA", "#00ACC1", "#C0CA33", "#6D4C41" };

    public void EnterLockSelectionMode()
    {
        _editingLock = null;
        _selectingSpike = false;
        ActiveLockFilter = null;   // voir toutes les lignes pour pouvoir sélectionner librement
        foreach (var n in EnumerateTree()) n.IsSelectedForLock = false;
        OnPropertyChanged(nameof(LockSelectionPrompt));
        IsLockSelectionMode = true;
    }

    // Rouvre les cases avec les membres du cadenas pré-cochés, pour réassocier les lignes.
    public void EditLock(VariantLockViewModel lk)
    {
        _editingLock = lk;
        ActiveLockFilter = null;   // voir toutes les lignes pour réassocier librement
        foreach (var n in EnumerateTree()) n.IsSelectedForLock = lk.Members.Contains(n);
        OnPropertyChanged(nameof(LockSelectionPrompt));
        IsLockSelectionMode = true;
    }

    public void CancelLockSelection()
    {
        foreach (var n in EnumerateTree()) n.IsSelectedForLock = false;
        _editingLock = null;
        _selectingSpike = false;
        _spikeSelectionError = string.Empty;
        IsLockSelectionMode = false;
    }

    // Valide la sélection (≥2 lignes) : met à jour le cadenas en édition, ou en crée un nouveau.
    // En mode spike, route vers la validation du roster (cap 8).
    public void ConfirmLockSelection()
    {
        if (_selectingSpike) { ConfirmSpikeSelection(); return; }
        var members = EnumerateTree().Where(n => n.IsSelectedForLock).ToList();
        if (members.Count >= 2)
        {
            if (_editingLock is { } lk)
            {
                lk.Members.Clear();
                foreach (var m in members) lk.Members.Add(m);
            }
            else
            {
                int index = Locks.Count == 0 ? 1 : Locks.Max(l => l.Index) + 1;
                var nlk = new VariantLockViewModel(index, _lockPalette[(index - 1) % _lockPalette.Length]);
                foreach (var m in members) nlk.Members.Add(m);
                Locks.Add(nlk);
                OnPropertyChanged(nameof(HasLocks));
            }
            RebuildLockCells();
            RebuildVisibleRows();
            MarkDirty();
        }
        CancelLockSelection();
    }

    public void RemoveLock(VariantLockViewModel lk)
    {
        if (!Locks.Remove(lk)) return;
        if (_activeLockFilter == lk) ActiveLockFilter = null;
        RebuildLockCells();
        RebuildVisibleRows();
        OnPropertyChanged(nameof(HasLocks));
        MarkDirty();
    }

    // Une cellule par cadenas du build sur CHAQUE ligne (membre ou non) → barres de même indice
    // alignées verticalement. Cellules vides quand le build n'a aucun cadenas (largeur 0).
    public void RebuildLockCells()
    {
        foreach (var n in EnumerateTree())
        {
            n.LockCells.Clear();
            foreach (var lk in Locks)
                n.LockCells.Add(new LockCellViewModel(lk, lk.Members.Contains(n)));
        }
    }

    // ── Spike damage calculus : roster (8 lignes max) + skills cochées par slot ──

    public const int MaxSpikeMembers = 8;

    // Lignes de l'arbre qui alimentent la fenêtre Spike, en ordre d'arbre (racine ou variante,
    // chacune compte 1). Persisté (.pn3 v6) avec les IsSpikeSelected de leurs slots ; purgé par
    // RefreshTree quand une ligne quitte l'arbre.
    public ObservableCollection<CharacterSlotViewModel> SpikeMembers { get; } = new();

    // Attaques vampiriques comptées dans le spike, par valeur de vol de vie (3 = armes à une main,
    // 5 = à deux mains). GLOBAL au build et non par perso : découper donnerait jusqu'à 16 lignes
    // pour un total identique (décision Philippe). Alimente les lignes ARTIFICIELLES de vol de vie
    // affichées en fin de liste dès qu'un mod « vampirique » est déclaré quelque part dans le spike ;
    // 0 = ligne affichée mais non comptée. Persisté (.zcx v18).
    private int _vampiricHits3 = 1;
    private int _vampiricHits5 = 1;

    public int VampiricHits3 { get => _vampiricHits3; set => SetField(ref _vampiricHits3, value); }
    public int VampiricHits5 { get => _vampiricHits5; set => SetField(ref _vampiricHits5, value); }

    /// <summary>Compteur d'attaques de la ligne de vol de vie <paramref name="steal"/> (3 ou 5).</summary>
    public int VampiricHits(int steal) => steal == Core.Data.SpikeWeaponMods.VampiricStealTwoHanded
        ? VampiricHits5 : VampiricHits3;

    /// <summary>Écrit le compteur de la ligne de vol de vie <paramref name="steal"/> (3 ou 5).</summary>
    public void SetVampiricHits(int steal, int value)
    {
        if (steal == Core.Data.SpikeWeaponMods.VampiricStealTwoHanded) VampiricHits5 = value;
        else VampiricHits3 = value;
    }

    private bool _selectingSpike;
    private string _spikeSelectionError = string.Empty;

    // Lu par MainWindow pour rouvrir la fenêtre Spike après une validation de roster.
    public bool IsSelectingSpike => _selectingSpike;

    // Mode sélection du roster spike : réutilise le bandeau et les cases des cadenas
    // (IsSelectedForLock), membres actuels pré-cochés, cap 8 à la validation.
    public void EnterSpikeSelectionMode()
    {
        _editingLock = null;
        _selectingSpike = true;
        _spikeSelectionError = string.Empty;
        ActiveLockFilter = null;
        foreach (var n in EnumerateTree()) n.IsSelectedForLock = SpikeMembers.Contains(n);
        OnPropertyChanged(nameof(LockSelectionPrompt));
        IsLockSelectionMode = true;
    }

    private void ConfirmSpikeSelection()
    {
        var members = EnumerateTree().Where(n => n.IsSelectedForLock).ToList();  // ordre d'arbre
        if (members.Count > MaxSpikeMembers)
        {
            _spikeSelectionError = string.Format(ZCodex.App.LanguageManager.T("S.Tb2.SpikeTooMany"),
                members.Count, members.Count - MaxSpikeMembers);
            OnPropertyChanged(nameof(LockSelectionPrompt));
            return;   // le mode reste ouvert
        }
        SpikeMembers.Clear();
        foreach (var m in members) SpikeMembers.Add(m);
        // Les cadres verts (et leur type d'arme, nettoyé par le setter) ne survivent pas à la
        // sortie du roster : seuls les membres sont persistés.
        foreach (var n in EnumerateTree())
            if (!members.Contains(n))
                foreach (var s in n.SkillSlots) s.IsSpikeSelected = false;  // le setter remet SpikeOrder à 0
        CompactSpikeOrder();   // ré-indexe l'ordre de cast des slots restants
        MarkDirty();
        CancelLockSelection();
    }

    // ── Ordre de cast du Spike (Chain Combo) ──────────────────────────────────

    // Clic sur une skill du roster spike : sélection indicée à la suite (1, 2, 3…), re-clic = retrait
    // avec ré-indexation contiguë des autres (UX Philippe). Appelé par SpikeWindow.
    public void ToggleSpikeSlot(SkillSlotViewModel slot)
    {
        if (slot.Skill is null) return;
        if (slot.IsSpikeSelected)
            slot.IsSpikeSelected = false;        // le setter remet SpikeOrder à 0 ; Mutated → recalcul
        else
        {
            slot.SpikeOrder = MaxSpikeOrder() + 1;
            slot.IsSpikeSelected = true;         // Mutated → recalcul (nouvelle skill en fin de chaîne)
        }
        CompactSpikeOrder();                     // indices 1..N contigus (cosmétique, hors dirty)
    }

    private int MaxSpikeOrder()
    {
        int max = 0;
        foreach (var s in SpikeMembers.SelectMany(m => m.SkillSlots))
            if (s.IsSpikeSelected && s.SpikeOrder > max) max = s.SpikeOrder;
        return max;
    }

    // Réassigne des indices de cast contigus 1..N aux slots sélectionnés, dans leur ordre courant.
    // Public : appelé aussi après le chargement d'un .pn3 (normalise les anciens fichiers sans ordre).
    public void CompactSpikeOrder()
    {
        var selected = SpikeMembers.SelectMany(m => m.SkillSlots)
            .Where(s => s.IsSpikeSelected)
            .OrderBy(s => s.SpikeOrder)
            .ToList();
        for (int i = 0; i < selected.Count; i++)
            selected[i].SpikeOrder = i + 1;
    }

    // ── Violations ───────────────────────────────────────────────────────────

    public void RefreshViolationsForAll()
    {
        foreach (var c in EnumerateTree())
            c.RefreshViolations();
    }

    // ── Sélection « molette » ─────────────────────────────────────────────────
    // Le personnage sur lequel la molette a le droit de régler les caractéristiques quand le toggle
    // Affichage ▸ « Molette : sélectionner le personnage d'abord » est coché. Un clic n'importe où
    // dans la carte le désigne (MainWindow.CharacterCard_MouseLeftButtonDown).

    private CharacterSlotViewModel? _wheelSelection;
    public CharacterSlotViewModel? WheelSelection => _wheelSelection;

    public void SelectForWheel(CharacterSlotViewModel? character)
    {
        if (ReferenceEquals(_wheelSelection, character)) return;
        if (_wheelSelection != null) _wheelSelection.IsWheelSelected = false;
        _wheelSelection = character;
        if (_wheelSelection != null) _wheelSelection.IsWheelSelected = true;
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    public void BeginTracking()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(IsDirty)
                               and not nameof(DisplayName)
                               and not nameof(IsActive)
                               and not nameof(ShowAttributes)
                               and not nameof(ShowInheritedAsBarred)
                               and not nameof(IsLockSelectionMode)
                               and not nameof(ActiveLockFilter)
                               and not nameof(HasLocks)
                               and not nameof(LockSelectionPrompt)
                               and not nameof(IsRenaming)
                               and not nameof(EditName))
                MarkDirty();
        };
        Tags.CollectionChanged += (_, _) => MarkDirty();

        Characters.CollectionChanged += OnCharactersChanged;
        RefreshTree();   // abonne tous les nœuds initiaux + peuple VisibleRows
    }

    public void MarkClean() => IsDirty = false;

    // Undo/redo par snapshot : levé à CHAQUE vraie mutation (mêmes filtres que le dirty flag),
    // y compris quand le build est déjà dirty. Consommé par UndoManager.
    public event Action? Mutated;

    // Gestionnaire d'undo de CE build. Attaché par MainWindow quand l'onglet rejoint
    // OpenTeamBuilds (seul MainWindow détient le pont VM ↔ modèle .pn3).
    public Undo.UndoManager? Undo { get; set; }

    private bool _suppressTracking;

    /// <summary>
    /// Exécute une mutation qui ne doit ni salir le build ni entrer dans l'historique d'undo.
    /// Réservé aux changements DÉRIVÉS d'un réglage d'affichage : la bascule PvE/PvP substitue
    /// « Heal Party » et « Heal Party (PvP) » dans les slots, mais la variante affichée découle du
    /// mode courant — elle est re-résolue au chargement, donc ce n'est pas une édition du build.
    /// </summary>
    public void RunWithoutTracking(Action action)
    {
        bool previous = _suppressTracking;
        _suppressTracking = true;
        try { action(); }
        finally { _suppressTracking = previous; }
    }

    private void MarkDirty()
    {
        if (_suppressTracking) return;
        Mutated?.Invoke();
        if (_isDirty) return;
        IsDirty = true;
    }

    // ── Restauration d'un snapshot d'undo ─────────────────────────────────────
    // L'appelant (MainWindow) repeuple Name/Tags/Characters/Locks entre les deux appels ;
    // l'instance du VM (et donc l'onglet, FilePath, IsActive) survit à la restauration.

    public void BeginRestore()
    {
        _suppressTracking = true;
        // Les cadenas vont être reconstruits : purge des états qui référencent les instances mortes.
        if (IsLockSelectionMode) CancelLockSelection();
        ClearLockFilter();
    }

    public void EndRestore()
    {
        RefreshTree();
        OnPropertyChanged(nameof(HasLocks));
        // Le setter de ShowAttributes ne propage que sur changement : les nœuds recréés
        // (défaut false) doivent être réalignés explicitement.
        foreach (var c in EnumerateTree()) c.ShowAttributeEditor = ShowAttributes;
        RefreshViolationsForAll();
        _suppressTracking = false;
        IsDirty = true;
    }

    private void OnCharactersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkDirty();
        RefreshTree();   // (dés)abonne les racines ajoutées/retirées + leurs sous-arbres
    }

    private void SubscribeNode(CharacterSlotViewModel c)
    {
        c.PropertyChanged += OnChildChanged;
        foreach (var s in c.SkillSlots) s.PropertyChanged += OnChildChanged;
        c.SkillSlots.CollectionChanged += OnSkillSlotsChanged;
    }

    private void UnsubscribeNode(CharacterSlotViewModel c)
    {
        c.PropertyChanged -= OnChildChanged;
        foreach (var s in c.SkillSlots) s.PropertyChanged -= OnChildChanged;
        c.SkillSlots.CollectionChanged -= OnSkillSlotsChanged;
    }

    private void OnSkillSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkDirty();

    // ── Character slot operations ─────────────────────────────────────────────

    public void SwapCharacters(CharacterSlotViewModel a, CharacterSlotViewModel b)
    {
        int ia = Characters.IndexOf(a);
        int ib = Characters.IndexOf(b);
        if (ia < 0 || ib < 0 || ia == ib) return;
        if (ia > ib) (ia, ib) = (ib, ia);
        Characters.Move(ia, ib);      // a → ib; old ib is now at ib-1
        Characters.Move(ib - 1, ia); // old ib → ia
    }

    public void MoveCharacter(CharacterSlotViewModel who, int toIndex)
    {
        int from = Characters.IndexOf(who);
        if (from < 0 || from == toIndex || (uint)toIndex >= (uint)Characters.Count) return;
        Characters.Move(from, toIndex);
    }

    public void InsertCopyAt(CharacterSlotViewModel source, int afterIndex)
    {
        if (Characters.Count >= 12) return;
        int insertAt = Math.Clamp(afterIndex + 1, 0, Characters.Count);
        Characters.Insert(insertAt, CloneCharacter(source));
    }

    private static CharacterSlotViewModel CloneCharacter(CharacterSlotViewModel src)
    {
        var copy = new CharacterSlotViewModel
        {
            Name                = src.Name + ZCodex.App.LanguageManager.T("S.Tb2.CopySuffix"),
            Notes               = src.Notes,
            PrimaryProfession   = src.PrimaryProfession,
            SecondaryProfession = src.SecondaryProfession,
            IsFavorite          = src.IsFavorite,
            Assignment          = src.Assignment,
            Gender              = src.Gender,
            Equipment           = CloneEquipment(src.Equipment),
            DurationBoostersEnabled = src.DurationBoostersEnabled,
            Attributes          = src.Attributes is null ? null : new AttributesBuild
            {
                Allocations = src.Attributes.Allocations
                    .Select(a => new AttributeAllocation(a.AttributeId, a.Points))
                    .ToList(),
                TitleRanks = new Dictionary<string, int>(src.Attributes.TitleRanks),
            },
        };
        for (int i = 0; i < 8; i++)
            copy.SkillSlots[i].Skill = src.SkillSlots[i].Skill;
        foreach (var id in src.ActiveAttributeBoosts)
            copy.SetAttributeBoost(id, true);
        return copy;
    }

    // Copie profonde de l'équipement : indispensable pour une variante (ou une copie) indépendante —
    // sinon variante et parent partageraient la même instance EquipmentBuild.
    private static EquipmentItem CloneItem(EquipmentItem i) => new()
    {
        Slot = i.Slot,
        ItemId = i.ItemId,
        Dye = i.Dye,
        ModifierIds = new List<int>(i.ModifierIds),
    };

    private static EquipmentBuild? CloneEquipment(EquipmentBuild? eq) =>
        eq is null ? null : new EquipmentBuild
        {
            Armor = eq.Armor.Select(CloneItem).ToList(),
            WeaponSets = eq.WeaponSets
                .Select(s => new WeaponSet { Items = s.Items.Select(CloneItem).ToList() })
                .ToList(),
            ActiveSet = eq.ActiveSet,
        };

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterSlotViewModel.ShowAttributeEditor)
                           or nameof(CharacterSlotViewModel.TotalAttributePoints)
                           or nameof(CharacterSlotViewModel.IsOverAttributeBudget)
                           or nameof(CharacterSlotViewModel.Depth)
                           or nameof(CharacterSlotViewModel.Parent)
                           or nameof(CharacterSlotViewModel.IsVariant)
                           or nameof(CharacterSlotViewModel.HasVariants)
                           or nameof(CharacterSlotViewModel.IsExpanded)
                           or nameof(CharacterSlotViewModel.IsSelectedForLock)
                           or nameof(CharacterSlotViewModel.IsWheelSelected)
                           // UI-only, levé DEPUIS SpikeViewModel.Recalculate (sync de la rangée
                           // d'icônes buffs) → doit être hors dirty, sinon boucle Mutated/recalcul.
                           or nameof(CharacterSlotViewModel.HasSpikeBuffToggles)
                           or nameof(SkillSlotViewModel.ShowBarred)
                           or nameof(SkillSlotViewModel.IsSameAsParent)
                           or nameof(SkillSlotViewModel.SpikeOrder))
            return;
        MarkDirty();
        if (sender is SkillSlotViewModel && e.PropertyName == nameof(SkillSlotViewModel.Skill))
            RefreshViolationsForAll();
        else if (sender is CharacterSlotViewModel ch &&
                 e.PropertyName is nameof(CharacterSlotViewModel.PrimaryProfession)
                                or nameof(CharacterSlotViewModel.SecondaryProfession))
            ch.RefreshViolations();
    }
}
