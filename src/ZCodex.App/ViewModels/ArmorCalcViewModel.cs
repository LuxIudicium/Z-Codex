using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZCodex.App.Settings;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;
using ZCodex.Scraper;

namespace ZCodex.App.ViewModels;

/// <summary>
/// VM de l'onglet « Calculateur d'armure » (chantier 14, Lot C). Trois zones :
/// (1) perso + équipement (profession, AL de base, insignes par pièce, bouclier, contributions
///     personnalisées), (2) sources externes cochables (table du Lot B, rang par skill),
/// (3) grille de résultats localisation × type de dégâts + espérance, avec détail dépliable.
/// Le moteur est <see cref="ArmorCalculator"/> (Lot A) ; les valeurs des effets viennent de
/// <see cref="ArmorEffectsData"/> (Lot B). Persistance : profils nommés (AppSettings.ArmorProfiles).
/// </summary>
public class ArmorCalcViewModel : ViewModelBase
{
    // Raccourci de localisation (onglet non modal : sur switch de langue, les libellés déjà
    // calculés se rafraîchissent au prochain Recompute / RefreshBreakdown).
    private static string T(string key) => LanguageManager.T(key);

    // Libellé bilingue baké (les libellés du calculateur sont construits en chaîne).
    private static string L(string fr, string en) => ZCodex.Core.Models.AppLanguage.IsFr ? fr : en;

    // ── Colonnes de la grille (type de dégâts) ────────────────────────────────────────────────
    // Label = propriété calculée (langue courante) ; les instances restent stables (identité pour
    // la sélection de cellule), seul le libellé change → re-lire ColumnList au switch.
    public sealed record DamageColumn(string Key, string LabelFr, string LabelEn, string Group)
    {
        public string Label => ZCodex.Core.Models.AppLanguage.IsFr ? LabelFr : LabelEn;
    }

    public static readonly IReadOnlyList<DamageColumn> Columns =
    [
        new("slashing",  "Tranchant",  "Slashing",     "Physical"),
        new("piercing",  "Perforant",  "Piercing",     "Physical"),
        new("blunt",     "Contondant", "Blunt",        "Physical"),
        new("fire",      "Feu",        "Fire",         "Elemental"),
        new("cold",      "Froid",      "Cold",         "Elemental"),
        new("earth",     "Terre",      "Earth",        "Elemental"),
        new("lightning", "Foudre",     "Lightning",    "Elemental"),
        new("dark",      "Tén./Chaos", "Dark/Chaos",   "Other"),
    ];

    // Accès d'instance pour le binding XAML (Columns est statique → invisible au binding d'instance).
    public IReadOnlyList<DamageColumn> ColumnList => Columns;

    // Portée d'une contribution (superset des scopes du Lot B + types physiques précis des insignes).
    public enum ArmorScope { All, Physical, Elemental, Slashing, Piercing, Blunt, Fire, Cold, Earth, Lightning, Projectile }

    // includeProjectile : les sources Scope.Projectile (« Shields Up! ») ne comptent QUE contre
    // les attaques à projectile (rework 17/07) — jamais dans la grille, seulement dans l'AL
    // recalculée pour une attaque de référence arc/lance.
    private static bool ScopeApplies(ArmorScope scope, DamageColumn col, bool includeProjectile = false)
        => scope switch
    {
        ArmorScope.All        => true,
        ArmorScope.Projectile => includeProjectile,
        ArmorScope.Physical   => col.Group == "Physical",
        ArmorScope.Elemental  => col.Group == "Elemental",
        ArmorScope.Slashing   => col.Key == "slashing",
        ArmorScope.Piercing   => col.Key == "piercing",
        ArmorScope.Blunt      => col.Key == "blunt",
        ArmorScope.Fire       => col.Key == "fire",
        ArmorScope.Cold       => col.Key == "cold",
        ArmorScope.Earth      => col.Key == "earth",
        ArmorScope.Lightning  => col.Key == "lightning",
        _ => false,
    };

    private static ArmorScope FromEffectScope(ArmorEffectsData.Scope s) => s switch
    {
        ArmorEffectsData.Scope.Physical   => ArmorScope.Physical,
        ArmorEffectsData.Scope.Elemental  => ArmorScope.Elemental,
        ArmorEffectsData.Scope.Slashing   => ArmorScope.Slashing,
        ArmorEffectsData.Scope.Projectile => ArmorScope.Projectile,
        _ => ArmorScope.All,
    };

    public IReadOnlyList<Profession> Professions { get; } =
    [
        Profession.Warrior, Profession.Ranger, Profession.Monk, Profession.Necromancer,
        Profession.Mesmer, Profession.Elementalist, Profession.Assassin, Profession.Ritualist,
        Profession.Paragon, Profession.Dervish,
    ];

    // ── Zone 1 : perso / équipement ───────────────────────────────────────────────────────────
    private Profession _profession = Profession.Warrior;
    public Profession Profession
    {
        get => _profession;
        set
        {
            if (!SetField(ref _profession, value)) return;
            BaseArmor = ArmorCalculator.BaseArmor(value);   // reset à l'AL max de la profession
            RebuildInsigniaOptions();
            OnPropertyChanged(nameof(InherentDescription));
            Recompute();
        }
    }

    private int _baseArmor = 80;
    public int BaseArmor
    {
        get => _baseArmor;
        set { if (SetField(ref _baseArmor, value)) Recompute(); }
    }

    // Inhérents du Basic armor (Core, filtrés par type) — auto, affichés en clair.
    public string InherentDescription => _profession switch
    {
        Profession.Warrior => L("+20 armure vs physique (inhérent Guerrier)", "+20 armor vs physical (Warrior inherent)"),
        Profession.Ranger  => L("+30 armure vs élémentaire (inhérent Rôdeur)", "+30 armor vs elemental (Ranger inherent)"),
        _ => L("Aucun inhérent de type", "No type inherent"),
    };

    public ObservableCollection<ArmorPieceVM> Pieces { get; } = new();
    public ObservableCollection<InsigniaOption> InsigniaOptions { get; } = new();

    // Bouclier. L'équiper occupe la main secondaire : les mods d'arme excédentaires (règle
    // 17/07 : 1 seul mod d'arme avec bouclier) sont retirés, avec message.
    private bool _shieldEquipped;
    public bool ShieldEquipped
    {
        get => _shieldEquipped;
        set
        {
            if (!SetField(ref _shieldEquipped, value)) return;
            if (value)
            {
                var excess = AddedMods.Where(m => m.Kind == EquipModKind.WeaponMod).Skip(1).ToList();
                if (excess.Count > 0)
                {
                    foreach (var m in excess) AddedMods.Remove(m);
                    ModLimitMessage = T("S.ArmorVM.ModLimitShield");
                }
            }
            Recompute();
        }
    }
    private int _shieldBaseArmor = 16;
    // Cap à 16 (armure de base max d'un bouclier) et plancher 0.
    public int ShieldBaseArmor { get => _shieldBaseArmor; set { if (SetField(ref _shieldBaseArmor, Math.Clamp(value, 0, 16))) Recompute(); } }
    private bool _shieldRequirementMet = true;
    public bool ShieldRequirementMet { get => _shieldRequirementMet; set { if (SetField(ref _shieldRequirementMet, value)) Recompute(); } }
    private bool _shieldIsStrength;
    public bool ShieldIsStrength { get => _shieldIsStrength; set { if (SetField(ref _shieldIsStrength, value)) Recompute(); } }

    // Mods d'équipement RÉELS (runes, mods d'armes, inscriptions) sélectionnables + ajoutés.
    public IReadOnlyList<EquipmentModOption> AvailableMods { get; } = BuildAvailableMods();
    public ObservableCollection<EquipmentModOption> AddedMods { get; } = new();
    private EquipmentModOption? _selectedModOption;
    public EquipmentModOption? SelectedModOption
    {
        get => _selectedModOption;
        set => SetField(ref _selectedModOption, value);
    }

    // Ajout manuel (cas non couverts par le catalogue : mods inhérents de bouclier…).
    public ObservableCollection<CustomContribVM> Customs { get; } = new();

    // Récap des réductions de durée d'altération cumulées (sous la grille).
    public ObservableCollection<DurationSummaryVM> DurationSummaries { get; } = new();

    // ── État du perso : active les clauses conditionnelles des insignes (chantier 14 bis) et
    // les inscriptions de réduction plate conditionnelles (Lot D). Bools = situations ; ints =
    // comptes/seuils (Undertaker's, Windwalker, Minion Master's, Shaman's, Anchorite's/Prodigy's,
    // Artificer's/Mantra of Signets, Illusionary Weaponry).
    private bool _isEnchanted;
    public bool IsEnchanted { get => _isEnchanted; set { if (SetField(ref _isEnchanted, value)) Recompute(); } }
    private bool _isHexed;
    public bool IsHexed { get => _isHexed; set { if (SetField(ref _isHexed, value)) Recompute(); } }
    private bool _isInStance;
    public bool IsInStance { get => _isInStance; set { if (SetField(ref _isInStance, value)) Recompute(); } }
    private bool _isAttacking;
    public bool IsAttacking { get => _isAttacking; set { if (SetField(ref _isAttacking, value)) Recompute(); } }
    private bool _isHoldingItem;
    public bool IsHoldingItem { get => _isHoldingItem; set { if (SetField(ref _isHoldingItem, value)) Recompute(); } }
    private bool _isUsingPreparation;
    public bool IsUsingPreparation { get => _isUsingPreparation; set { if (SetField(ref _isUsingPreparation, value)) Recompute(); } }
    private bool _isPetAlive;
    public bool IsPetAlive { get => _isPetAlive; set { if (SetField(ref _isPetAlive, value)) Recompute(); } }
    private bool _hasCondition;
    public bool HasCondition { get => _hasCondition; set { if (SetField(ref _hasCondition, value)) Recompute(); } }
    private bool _isActivatingSkill;
    public bool IsActivatingSkill { get => _isActivatingSkill; set { if (SetField(ref _isActivatingSkill, value)) Recompute(); } }
    private bool _hasWeaponSpell;
    public bool HasWeaponSpell { get => _hasWeaponSpell; set { if (SetField(ref _hasWeaponSpell, value)) Recompute(); } }
    private bool _hasShoutChant;
    public bool HasShoutChant { get => _hasShoutChant; set { if (SetField(ref _hasShoutChant, value)) Recompute(); } }

    private int _healthPercent = 100;
    public int HealthPercent { get => _healthPercent; set { if (SetField(ref _healthPercent, Math.Clamp(value, 0, 100))) Recompute(); } }
    private int _enchantmentCount;
    public int EnchantmentCount { get => _enchantmentCount; set { if (SetField(ref _enchantmentCount, Math.Clamp(value, 0, 30))) Recompute(); } }
    private int _rechargingSkillCount;
    public int RechargingSkillCount { get => _rechargingSkillCount; set { if (SetField(ref _rechargingSkillCount, Math.Clamp(value, 0, 8))) Recompute(); } }
    private int _minionCount;
    public int MinionCount { get => _minionCount; set { if (SetField(ref _minionCount, Math.Clamp(value, 0, 30))) Recompute(); } }
    private int _spiritCount;
    public int SpiritCount { get => _spiritCount; set { if (SetField(ref _spiritCount, Math.Clamp(value, 0, 30))) Recompute(); } }
    // Défauts = continuité Lot B : Mantra of Signets valait +3 (1 signet), IW +25 (5 comp. Illusion,
    // IW comprise — exemple wiki n°2).
    private int _signetCount = 1;
    public int SignetCount
    {
        get => _signetCount;
        set { if (SetField(ref _signetCount, Math.Clamp(value, 0, 8))) { RefreshPerUnitRows(); Recompute(); } }
    }
    private int _illusionSkillCount = 5;
    public int IllusionSkillCount
    {
        get => _illusionSkillCount;
        set { if (SetField(ref _illusionSkillCount, Math.Clamp(value, 0, 8))) { RefreshPerUnitRows(); Recompute(); } }
    }

    // Enchanté « effectif » : la case OU un compte d'enchantements > 0 (Windwalker) — cohérence
    // automatique entre les deux saisies.
    private bool EffectivelyEnchanted => IsEnchanted || EnchantmentCount > 0;

    // Multiplicateur per-unit d'un effet de la zone 2 (IW ×comp. Illusion, Mantra ×signets).
    private int UnitCountFor(int skillId) => skillId switch
    {
        33 => IllusionSkillCount,   // Illusionary Weaponry : +5/comp. Illusion équipée
        18 => SignetCount,          // Mantra of Signets : +3/signet équipé
        _ => 1,
    };

    private void RefreshPerUnitRows()
    {
        foreach (var row in Effects)
            if (row.UsesUnitCount) row.RaiseResolvedChanged();
    }

    private bool FlatActive(EquipmentModOption m) => m.FlatCondition switch
    {
        FlatCondition.None      => true,
        FlatCondition.Chance    => true,        // probabiliste → compté en espérance
        FlatCondition.Enchanted => EffectivelyEnchanted,
        FlatCondition.Hexed     => IsHexed,
        FlatCondition.Stance    => IsInStance,
        _ => false,
    };

    // Une clause d'insigne est-elle active dans l'état courant ? Requires (Sentinel's/Prismatic) =
    // supposé rempli (v1, signalé dans le libellé) ; PerSignet toujours active (valeur ×signets).
    private bool ClauseActive(InsigniaClause c) => c.Cond switch
    {
        InsigniaCond.None or InsigniaCond.Requires or InsigniaCond.PerSignet => true,
        InsigniaCond.Attacking          => IsAttacking,
        InsigniaCond.Enchanted          => EffectivelyEnchanted,
        InsigniaCond.NotEnchanted       => !EffectivelyEnchanted,
        InsigniaCond.Hexed              => IsHexed,
        InsigniaCond.Stance             => IsInStance,
        InsigniaCond.HoldingItem        => IsHoldingItem,
        InsigniaCond.Preparation        => IsUsingPreparation,
        InsigniaCond.PetAlive           => IsPetAlive,
        InsigniaCond.HasCondition       => HasCondition,
        InsigniaCond.ActivatingSkill    => IsActivatingSkill,
        InsigniaCond.WeaponSpell        => HasWeaponSpell,
        InsigniaCond.ShoutChant         => HasShoutChant,
        InsigniaCond.HealthBelow        => HealthPercent < c.Threshold,
        InsigniaCond.MinionsAtLeast     => MinionCount >= c.Threshold,
        InsigniaCond.SpiritsAtLeast     => SpiritCount >= c.Threshold,
        InsigniaCond.RechargingAtLeast  => RechargingSkillCount >= c.Threshold,
        InsigniaCond.EnchantmentsAtLeast=> EnchantmentCount >= c.Threshold,
        _ => false,
    };

    private int ClauseValue(InsigniaClause c)
        => c.Cond == InsigniaCond.PerSignet ? c.Value * SignetCount : c.Value;

    /// <summary>Vulnérabilité sacrée GLOBALE (Tormentor's : cumul des pièces, valeur selon le slot
    /// de chaque insigne). Ajoutée aux paquets sacrés des attaques de référence, côté build.</summary>
    public int HolyVulnTotal
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Pieces.Count; i++)
                total += Pieces[i].Selected?.HolyVulnAt(i) ?? 0;
            return total;
        }
    }

    /// <summary>Réduction PLATE de dégâts physiques applicable aux coups reçus sur une localisation,
    /// en points (≥ 0). Règle validée Philippe : sources DIFFÉRENTES additives ; l'Absorption est
    /// Non-stacking avec elle-même (seule la meilleure rune compte) ; Knight's est un insigne donc
    /// ne compte que sur SA pièce ; « Luck of the Draw » compté en espérance. Appliquée APRÈS le
    /// calcul d'AL, sur les dégâts, avec plancher 0 (cf. <see cref="ReferenceAttack"/>).</summary>
    public int FlatPhysicalAt(int pieceIndex)
    {
        int absorption = AddedMods.Where(m => m.FlatPhysical > 0 && m.FlatNonStacking)
                                  .Select(m => m.FlatValue).DefaultIfEmpty(0).Max();
        int others = AddedMods.Where(m => m.FlatPhysical > 0 && !m.FlatNonStacking && FlatActive(m))
                              .Sum(m => m.FlatValue);
        int knight = pieceIndex >= 0 && pieceIndex < Pieces.Count
            ? Pieces[pieceIndex].Selected?.FlatPhysical ?? 0 : 0;
        return absorption + others + knight;
    }

    private static IReadOnlyList<EquipmentModOption> BuildAvailableMods()
    {
        var list = new List<EquipmentModOption>();
        foreach (var (id, d) in GwEquipmentModDetails.ByModId)
        {
            if (d.Description.EndsWith("Insignia", StringComparison.Ordinal)) continue; // insignes = zone par-pièce
            var name = GwEquipmentData.Modifiers.TryGetValue(id, out var n) ? n : d.WikiPath;
            if (name.EndsWith(" Insignia", StringComparison.Ordinal)) continue;
            var opt = EquipmentModOption.TryParse(id, name, d.Description);
            if (opt is not null) list.Add(opt);
        }
        // Dédupliqué par (nom, effet) — les mods identiques sur types d'armes différents (of Defense…)
        // n'apparaissent qu'une fois dans le picker.
        return list
            .GroupBy(o => $"{o.Name}|{o.EffectText}")
            .Select(g => g.First())
            .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ── Zone 2 : effets externes + pénétration ────────────────────────────────────────────────
    public ObservableCollection<ArmorEffectRowVM> Effects { get; } = new();
    // Vue groupée (Compétences alliées / Malus subis / Consommables & effets) pour la liste zone 2.
    public ICollectionView EffectsView { get; private set; } = null!;

    private int _armorPenetrationPercent;
    public int ArmorPenetrationPercent
    {
        get => _armorPenetrationPercent;
        set { if (SetField(ref _armorPenetrationPercent, Math.Clamp(value, 0, 100))) Recompute(); }
    }

    // ── Zone 3 : résultats + détail ───────────────────────────────────────────────────────────
    public ObservableCollection<ResultRowVM> ResultRows { get; } = new();

    private ResultCellVM? _selectedCell;
    public ResultCellVM? SelectedCell
    {
        get => _selectedCell;
        set
        {
            var old = _selectedCell;
            if (!SetField(ref _selectedCell, value)) return;
            old?.RaiseSelectedChanged();
            value?.RaiseSelectedChanged();
            RefreshBreakdown();
            RefreshAttacks();   // la cellule choisit la localisation de référence des attaques
        }
    }

    public ObservableCollection<BreakdownLineVM> BreakdownLines { get; } = new();
    private string _breakdownHeader = T("S.ArmorVM.SelectCell");
    public string BreakdownHeader { get => _breakdownHeader; set => SetField(ref _breakdownHeader, value); }

    // ── Zone 4 : attaques de référence (Lot D) ────────────────────────────────────────────────
    // Dégâts subis à AL 60 (catalogue, cible nue) vs à l'AL du build, sur la localisation de la
    // cellule sélectionnée (défaut : espérance). Le TYPE vient de l'ATTAQUE, pas de la colonne
    // cliquée — la cellule ne choisit que la localisation.
    public const int DefaultAttackRank = 15;   // rang par défaut (décision Philippe), éditable par ligne

    // Liste par défaut : une attaque par type de dégâts (décision Philippe).
    private static readonly int[] DefaultAttackIds =
    [
        338,  // Eviscerate         — tranchant (hache)
        398,  // Penetrating Attack — perforant (arc, pénétration 10 %)
        331,  // Hammer Bash        — contondant (marteau)
        186,  // Fireball           — feu
        214,  // Ice Spear          — froid
        171,  // Stoning            — terre
        229,  // Lightning Orb      — foudre (pénétration 25 %)
    ];

    public ObservableCollection<ReferenceAttackVM> ReferenceAttacks { get; } = new();
    private IReadOnlyList<Skill> _catalog = [];

    // Catalogue STANDARD (même contrôle que le teambuilder/Search : profession, attribut, PvE/PvP,
    // recherche) pour choisir l'attaque à ajouter — demande Philippe 17/07, remplace la recherche
    // maison. Filtre spécifique « dégâts seulement » via ExtraFilter, résultat DealsDamage caché
    // par skill (l'analyse regex sur ~1500 skills ne se paie qu'une fois).
    public SkillPanelViewModel AttackCatalog { get; } = new();

    private readonly Dictionary<int, bool> _dealsDamageCache = new();
    private bool DealsDamageCached(Skill s)
    {
        if (_dealsDamageCache.TryGetValue(s.Id, out bool ok)) return ok;
        return _dealsDamageCache[s.Id] = ReferenceAttack.DealsDamage(s, DefaultAttackRank);
    }

    private bool _damageOnlyFilter = true;
    public bool DamageOnlyFilter
    {
        get => _damageOnlyFilter;
        set { if (SetField(ref _damageOnlyFilter, value)) ApplyDamageFilter(); }
    }

    private void ApplyDamageFilter()
        => AttackCatalog.ExtraFilter = _damageOnlyFilter ? DealsDamageCached : null;

    // Libellé de la localisation de référence des attaques (suit la cellule sélectionnée).
    public string AttackContextText
    {
        get
        {
            int loc = AttackLocation;
            string where = loc < 0 ? L("Espérance (Moyenne pondérée)", "Expectancy (Weighted Mean)") : PieceNames[loc];
            // En espérance la réduction plate varie par pièce (Knight's) : on n'affiche que la part
            // GLOBALE (FlatPhysicalAt(-1)) — le calcul, lui, applique bien la valeur de chaque pièce.
            int flat = FlatPhysicalAt(loc);
            string flatTxt = flat > 0
                ? $" · −{flat} {L("dégâts physiques", "physical damage")} ({L("plat", "flat")}{(loc < 0 ? L(", part globale", ", global part") : "")})"
                : "";
            string holyTxt = HolyVulnTotal > 0
                ? $" · +{HolyVulnTotal} {L("dégâts sacrés reçus (Tormentor's, global)", "holy damage received (Tormentor's, global)")}"
                : "";
            return $"{L("Référence", "Reference")} : {where}{flatTxt}{holyTxt}";
        }
    }

    // Localisation de référence : celle de la cellule sélectionnée, −1 = espérance.
    private int AttackLocation => SelectedCell is null || SelectedCell.IsExpectancy ? -1 : SelectedCell.Location;

    // Appelé une fois depuis MainWindow quand le catalogue est chargé.
    public void LoadSkills(IReadOnlyList<Skill> skills)
    {
        _catalog = skills;
        if (ReferenceAttacks.Count == 0)
            foreach (int id in DefaultAttackIds)
                if (skills.FirstOrDefault(s => s.Id == id) is { } sk)
                    ReferenceAttacks.Add(new ReferenceAttackVM(sk, DefaultAttackRank, RefreshAttacks));
        AttackCatalog.LoadSkills(skills);
        // Défaut : toutes professions (le filtre pertinent ici est « dégâts », pas la profession).
        AttackCatalog.SelectedProfessionOption = AttackCatalog.Professions[0];
        ApplyDamageFilter();
        RefreshAttacks();
    }

    public void AddAttack(Skill? skill)
    {
        if (skill is null) return;
        ReferenceAttacks.Add(new ReferenceAttackVM(skill, DefaultAttackRank, RefreshAttacks));
        RefreshAttacks();
    }

    public void RemoveAttack(ReferenceAttackVM a)
    {
        ReferenceAttacks.Remove(a);
        RefreshAttacks();
    }

    /// <summary>AL du build pour un type de dégâts à une localisation (clé de colonne
    /// <see cref="ReferenceAttack.ColumnKey"/>). <paramref name="vsProjectile"/> : AL recalculée en
    /// incluant les sources Scope.Projectile (« Shields Up! ») — utilisée pour les attaques de
    /// référence à projectile, jamais pour la grille.</summary>
    private int AlAt(int loc, string columnKey, bool vsProjectile = false)
    {
        if (loc < 0) return 60;
        int c = 0;
        for (int i = 0; i < Columns.Count; i++) if (Columns[i].Key == columnKey) { c = i; break; }
        if (vsProjectile)
            return ArmorCalculator.Compute(Assemble(loc, Columns[c], includeProjectile: true),
                                           ArmorPenetrationPercent).Effective;
        // ResultRows : Espérance à l'index 0, la localisation loc est à l'index loc+1.
        return ResultRows.Count > loc + 1 ? ResultRows[loc + 1].Cells[c].EffectiveAl : 60;
    }

    // Une source « projectiles » est-elle active (effet coché ou contribution manuelle) ? Sinon,
    // inutile de recalculer une AL spécifique pour les attaques à projectile.
    private bool HasProjectileSources
        => Effects.Any(r => r.IsChecked && r.HasProjectileClause)
           || Customs.Any(c => c.Scope == ArmorScope.Projectile);

    // Recalcule toutes les lignes d'attaque sur la localisation de référence courante.
    private void RefreshAttacks()
    {
        if (ResultRows.Count < 6) return;
        int loc = AttackLocation;
        int holy = HolyVulnTotal;
        bool projSources = HasProjectileSources;
        foreach (var a in ReferenceAttacks)
        {
            bool proj = projSources && ReferenceAttack.IsProjectile(a.Skill);
            string? extra = proj ? T("S.ArmorVM.ProjectilesIncluded") : null;
            if (loc >= 0)
                a.Apply(ReferenceAttack.Compute(a.Skill, a.Rank, k => AlAt(loc, k, proj),
                                                FlatPhysicalAt(loc), holy), extra);
            else
                a.Apply(ExpectedOver(a, proj, holy), extra);   // ligne Espérance : pondération sur les 5 localisations
            RefreshAttackConditions(a);
        }
        OnPropertyChanged(nameof(AttackContextText));
    }

    // Conditions infligées par une attaque de référence (durée de base au rang) + réduction
    // d'équipement (récap des durées) → pilules affichées à droite du nom. Indépendant de la
    // localisation/AL, mais dépend du rang de la ligne et des mods ajoutés.
    private void RefreshAttackConditions(ReferenceAttackVM a)
    {
        var pills = new List<ConditionPillVM>();
        foreach (var inf in ConditionInfliction.For(a.Skill, a.Rank))
            pills.Add(new ConditionPillVM(inf.Condition, inf.BaseSeconds,
                                          DurationReductionPercentFor(inf.Condition)));
        a.ApplyConditions(pills);
    }

    /// <summary>Réduction totale de durée (%) pour une condition (nom canonique anglais) d'après le
    /// récap cumulé — 0 si aucune source. Matching par nom FR partagé (<see cref="GwConditionData"/>).</summary>
    public int DurationReductionPercentFor(string conditionEn)
    {
        string fr = GwConditionData.DisplayName(conditionEn);
        return DurationSummaries.FirstOrDefault(d => d.ConditionFr == fr)?.TotalPercent ?? 0;
    }

    // Espérance des dégâts : Σ p_loc × dégâts(loc) — on somme les DÉGÂTS par localisation, jamais
    // l'AL équivalente (la pénétration par attaque rendrait le raccourci log2 inexact).
    private ReferenceAttack.Result ExpectedOver(ReferenceAttackVM a, bool vsProjectile, int holy)
    {
        double lo = 0, hi = 0;
        ReferenceAttack.Result? representative = null;
        for (int loc = 0; loc < 5; loc++)
        {
            double p = ArmorCalculator.HitProbability((ArmorCalculator.HitLocation)loc);
            var r = ReferenceAttack.Compute(a.Skill, a.Rank, k => AlAt(loc, k, vsProjectile),
                                            FlatPhysicalAt(loc), holy);
            lo += p * r.LoCalc; hi += p * r.HiCalc;
            // Champs non chiffrés (type, notes dont le critique) : pris sur le TORSE, la
            // localisation la plus probable (3/8) — les dégâts @AL 60 sont eux indépendants du lieu.
            if (loc == 1) representative = r;
        }
        return representative! with
        {
            LoCalc = (int)Math.Round(lo, MidpointRounding.AwayFromZero),
            HiCalc = (int)Math.Round(hi, MidpointRounding.AwayFromZero),
        };
    }

    // ── Profils ───────────────────────────────────────────────────────────────────────────────
    public ObservableCollection<string> ProfileNames { get; } = new();
    private string? _selectedProfileName;
    public string? SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (SetField(ref _selectedProfileName, value) && value is not null)
                LoadProfile(value);
        }
    }

    private AppSettings? _settings;

    public ArmorCalcViewModel()
    {
        BuildPieces();
        RebuildInsigniaOptions();
        BuildEffects();
        Recompute();
    }

    // Appelé une fois depuis MainWindow : donne accès aux settings (profils persistés).
    public void LoadSettings(AppSettings settings)
    {
        _settings = settings;
        RefreshProfileNames();
    }

    // ── Construction des sous-VM ──────────────────────────────────────────────────────────────
    // Noms de pièces dans la langue courante (recalculés à l'accès).
    private static string[] PieceNames => AppLanguage.IsFr
        ? ["Tête", "Torse", "Bras", "Jambes", "Pieds"]
        : ["Head", "Chest", "Arms", "Legs", "Feet"];

    /// <summary>Nom de la pièce d'index i dans la langue courante (utilisé par ArmorPieceVM).</summary>
    public static string PieceName(int i) => PieceNames[i];

    private void BuildPieces()
    {
        Pieces.Clear();
        for (int i = 0; i < 5; i++)
            Pieces.Add(new ArmorPieceVM(i, InsigniaOptions, Recompute));
    }

    // Insignes proposables : ceux de la profession + communs, dont la description confère de l'armure.
    private void RebuildInsigniaOptions()
    {
        var previous = Pieces.Select(p => p.Selected?.ModId ?? 0).ToArray();
        InsigniaOptions.Clear();
        InsigniaOptions.Add(InsigniaOption.None);
        foreach (var mod in GwEquipmentInfo.ModsFor(GwEquipmentInfo.SlotChest, WeaponKind.None,
                                                    ModCategory.Insignia, _profession))
        {
            var opt = InsigniaOption.FromMod(mod.ModId, mod.Name);
            if (opt is not null) InsigniaOptions.Add(opt);
        }
        // Réappliquer la sélection précédente si l'insigne existe encore.
        for (int i = 0; i < Pieces.Count; i++)
            Pieces[i].Selected = InsigniaOptions.FirstOrDefault(o => o.ModId == previous[i]) ?? InsigniaOption.None;
    }

    // Une ligne par SKILL : les entrées dédoublées d'un même skill (Elemental/Physical Resistance
    // bonus+malus, Ward Against Harm base+supplément élémentaire) sont FUSIONNÉES en une seule case
    // qui applique toutes les clauses à la fois (rework 17/07 — plus de « demi-effet » cochable).
    private void BuildEffects()
    {
        Effects.Clear();
        foreach (var g in ArmorEffectsData.All.GroupBy(e => (e.SkillId, e.Name)))
            Effects.Add(new ArmorEffectRowVM(g.ToList(), Recompute, UnitCountFor, SkillDisplayNameById)
            { OnCheckedTrue = ExcludeVariants });
        EffectsView = CollectionViewSource.GetDefaultView(Effects);
        EffectsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ArmorEffectRowVM.Group)));
    }

    // DisplayName (langue courante) de la compétence d'ID donné, ou null si absente du catalogue.
    // Lit _catalog en direct → un switch de langue change le résultat sans reconstruction.
    private string? SkillDisplayNameById(int id)
        => _catalog.FirstOrDefault(s => s.Id == id)?.DisplayName;

    // Une compétence cochée décoche ses variantes PvE/PvP/faction (même nom de base, suffixe
    // différent) : mutuellement exclusives.
    private static string StripVariant(string name)
    {
        foreach (var suffix in new[] { " (PvP)", " (Kurzick)", " (Luxon)" })
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return name[..^suffix.Length];
        return name;
    }

    private void ExcludeVariants(ArmorEffectRowVM justChecked)
    {
        var baseName = StripVariant(justChecked.Name);
        foreach (var row in Effects)
            if (!ReferenceEquals(row, justChecked) && StripVariant(row.Name) == baseName)
                row.UncheckSilently();
    }

    // ── Assemblage des contributions pour (localisation, colonne) ─────────────────────────────
    // includeProjectile : inclut les sources Scope.Projectile (AL spécifique aux attaques à
    // projectile des attaques de référence) — false pour la grille.
    private List<ArmorCalculator.Contribution> Assemble(int pieceIndex, DamageColumn col,
                                                        bool includeProjectile = false)
    {
        var list = new List<ArmorCalculator.Contribution>();
        var C = ArmorCalculator.Category.Core;

        // Core PAR PIÈCE : armure de base + inhérent typé + insigne de CETTE pièce.
        list.Add(new(T("S.ArmorVM.BaseArmor"), C, BaseArmor));
        if (_profession == Profession.Warrior && col.Group == "Physical")
            list.Add(new(T("S.ArmorVM.WarriorInherent"), C, 20));
        else if (_profession == Profession.Ranger && col.Group == "Elemental")
            list.Add(new(T("S.ArmorVM.RangerInherent"), C, 30));

        // Insigne de la pièce : TOUTES ses clauses actives dans l'état courant (seuils de PV,
        // comptes, situations…), chacune avec sa propre portée (Aeromancer : +10 élém ET +10
        // foudre cumulés vs foudre).
        var insig = Pieces[pieceIndex].Selected;
        if (insig is { ModId: not 0 })
            foreach (var cl in insig.Clauses)
                if (ScopeApplies(cl.Scope, col, includeProjectile) && ClauseActive(cl))
                {
                    int v = ClauseValue(cl);
                    if (v != 0)
                        list.Add(new($"{L("Insigne", "Insignia")} {insig.ShortName}{(cl.DisplayCond is null ? "" : $" [{cl.DisplayCond}]")} ({PieceNames[pieceIndex]})", C, v));
                }

        // Core GLOBAL : bouclier (base) + contributions personnalisées Core.
        if (ShieldEquipped)
        {
            int shieldVal = ShieldRequirementMet
                ? Math.Min(ShieldBaseArmor, 16)
                : (ShieldIsStrength ? 9 : 8);   // bug +9 du bouclier de Force (requis non rempli)
            list.Add(new(L("Bouclier (base)", "Shield (base)"), C, shieldVal));
        }

        // Mods d'équipement réels ajoutés (of Defense, inscriptions « Armor +N vs X »…).
        foreach (var mod in AddedMods)
            if (mod.HasArmor && ScopeApplies(mod.ArmorScope, col, includeProjectile))
                list.Add(new(GwEquipmentModsFr.DisplayName(mod.ModId, mod.Name), mod.ArmorCategory, mod.ArmorValue));

        // Ajouts manuels (cas non couverts par le catalogue).
        foreach (var cu in Customs)
            if (ScopeApplies(cu.Scope, col, includeProjectile))
                list.Add(new(string.IsNullOrWhiteSpace(cu.Label) ? L("(perso)", "(custom)") : cu.Label, cu.Category, cu.Value));

        // Effets externes cochés (globaux) — Bonus/Special. Une case = TOUTES les clauses du skill
        // (Résistances : +40 d'un côté ET malus de l'autre, en un seul clic — rework 17/07).
        foreach (var row in Effects)
            if (row.IsChecked)
                foreach (var (clause, value) in row.ResolvedClauses())
                    if (value != 0 && ScopeApplies(FromEffectScope(clause.Scope), col, includeProjectile))
                        list.Add(new(row.LabelFor(clause), clause.Category, value));

        return list;
    }

    // ── Recalcul complet de la grille ─────────────────────────────────────────────────────────
    /// <summary>Rebâtit l'affichage dans la langue courante après un switch : lignes d'effets
    /// (libellés calculés), re-groupement, noms des attaques de référence, et grille recalculée.</summary>
    public void RefreshLanguage()
    {
        RebuildInsigniaOptions();                             // dropdowns d'insignes (DisplayName baké), sélection préservée par ModId
        foreach (var p in Pieces) p.RaiseLanguageChanged();   // noms de pièces (Tête/Head…)
        foreach (var e in Effects) e.RaiseLanguageChanged();  // libellés d'effets calculés
        foreach (var a in ReferenceAttacks) a.RaiseLanguageChanged();  // noms des attaques (DisplayName du skill)
        EffectsView?.Refresh();                               // re-groupe (Group change de langue)
        OnPropertyChanged(nameof(ColumnList));                // en-têtes de colonnes (DamageColumn.Label)
        OnPropertyChanged(nameof(InherentDescription));
        Recompute();                                          // grille + breakdown (re-sélection de cellule)
    }

    public void Recompute()
    {
        if (Pieces.Count < 5) return;
        ResultRows.Clear();

        // Ordre d'AFFICHAGE (demande Philippe) : Espérance EN PREMIER, puis les 5 localisations.
        // L'indexation par localisation reste 0..4 (Location), mais dans ResultRows la localisation
        // loc est à l'index loc+1 (Espérance occupe l'index 0) — cf. RowForLocation/AlAt.
        var perLocation = new List<ResultRowVM>();
        for (int loc = 0; loc < 5; loc++)
        {
            var row = new ResultRowVM(PieceNames[loc], isExpectancy: false);
            foreach (var col in Columns)
            {
                var res = ArmorCalculator.Compute(Assemble(loc, col), ArmorPenetrationPercent);
                row.Cells.Add(new ResultCellVM(loc, col, res, this));
            }
            perLocation.Add(row);
        }

        // Ligne Espérance : par colonne, espérance du multiplicateur de dégâts sur les 5 pièces.
        var esp = new ResultRowVM(L("Moyenne", "Mean"), isExpectancy: true);
        for (int c = 0; c < Columns.Count; c++)
        {
            var col = Columns[c];
            double expected = 0;
            for (int loc = 0; loc < 5; loc++)
            {
                double p = ArmorCalculator.HitProbability((ArmorCalculator.HitLocation)loc);
                expected += p * ArmorCalculator.DamageMultiplier(perLocation[loc].Cells[c].EffectiveAl);
            }
            int alEq = (int)Math.Round(ArmorCalculator.EquivalentArmor(expected));
            esp.Cells.Add(new ResultCellVM(-1, col, expected, alEq, this));
        }
        ResultRows.Add(esp);                          // index 0 = Espérance
        foreach (var row in perLocation) ResultRows.Add(row);   // index 1..5 = localisations 0..4

        // Les cellules viennent d'être recréées : re-pointer la sélection sur la NOUVELLE cellule
        // aux mêmes coordonnées — sinon le détail dépliable resterait figé sur l'ancien Result et
        // la surbrillance disparaîtrait (l'ancienne instance n'est plus affichée).
        if (_selectedCell is { } prev)
        {
            int r = prev.IsExpectancy ? 0 : prev.Location + 1;
            int c = 0;
            for (int i = 0; i < Columns.Count; i++) if (Columns[i].Key == prev.Column.Key) { c = i; break; }
            _selectedCell = ResultRows[r].Cells[c];
            _selectedCell.RaiseSelectedChanged();
        }

        // Les réductions AVANT les attaques : les pilules de conditions des attaques lisent le
        // récap (durée effective = base × réduction).
        RefreshDurationSummaries();
        RefreshAttacks();

        // Réactualise le détail si une cellule était sélectionnée (mêmes coords).
        RefreshBreakdown();
    }

    // Réductions de durée d'altération cumulées depuis les mods ajoutés.
    // Règle wiki (Effect_stacking, tag Stacking/Non-stacking porté par la description du jeu) :
    // « Runes and inscriptions that provide condition reduction stack with each other. Runes that
    // provide the same condition reduction do not stack. » Dans les données : inscriptions = Stacking
    // (s'additionnent), runes = Non-stacking (la meilleure seule). Total = somme(Stacking) +
    // max(Non-stacking), plancher à 0 durée donc plafond 100 % (jamais atteint : 20 %/source, max 40 % par condition).
    private void RefreshDurationSummaries()
    {
        DurationSummaries.Clear();
        // Mods ajoutés + insignes sélectionnés (Lieutenant's : −20 % hex, Non-stacking).
        var byCond = AddedMods.SelectMany(m => m.Durations.Select(d => (m.Name, d)))
            .Concat(Pieces.Where(p => p.Selected is { ModId: not 0 })
                          .SelectMany(p => p.Selected!.Durations
                              .Select(d => (Name: $"Insigne {p.Selected!.ShortName}", d))))
                              .GroupBy(x => x.d.ConditionFr);
        foreach (var g in byCond.OrderBy(g => g.Key))
        {
            int stackingSum = g.Where(x => x.d.Stacking).Sum(x => x.d.Percent);
            int nonStackMax = g.Where(x => !x.d.Stacking).Select(x => x.d.Percent).DefaultIfEmpty(0).Max();
            int total = Math.Min(stackingSum + nonStackMax, 100);
            string sources = string.Join(", ", g.Select(x => $"{x.Name} (−{x.d.Percent}%)"));
            DurationSummaries.Add(new DurationSummaryVM
            {
                ConditionFr = g.Key, TotalPercent = total, SourcesText = sources,
            });
        }
    }

    private void RefreshBreakdown()
    {
        BreakdownLines.Clear();
        if (SelectedCell is null || SelectedCell.IsExpectancy)
        {
            BreakdownHeader = SelectedCell?.IsExpectancy == true
                ? string.Format(T("S.ArmorVM.ExpectancyHeader"), SelectedCell.Column.Label, SelectedCell.EffectiveAl)
                : T("S.ArmorVM.SelectCell");
            return;
        }

        var res = SelectedCell.Result!;
        BreakdownHeader = $"{PieceNames[SelectedCell.Location]} — {SelectedCell.Column.Label}";
        foreach (var grp in new[] { ArmorCalculator.Category.Core, ArmorCalculator.Category.Bonus, ArmorCalculator.Category.Special })
        {
            var items = res.Contributions.Where(c => c.Category == grp).ToList();
            if (items.Count == 0) continue;
            BreakdownLines.Add(BreakdownLineVM.Header(grp.ToString()));
            foreach (var c in items)
                BreakdownLines.Add(BreakdownLineVM.Item(c.Label, c.Value));
        }
        BreakdownLines.Add(BreakdownLineVM.Header(T("S.ArmorVM.Steps")));
        BreakdownLines.Add(BreakdownLineVM.Item("Core", res.Core));
        BreakdownLines.Add(BreakdownLineVM.Item(T("S.ArmorVM.BonusNet"), res.BonusNet));
        BreakdownLines.Add(BreakdownLineVM.Item(T("S.ArmorVM.BonusApplied"), res.BonusApplied));
        BreakdownLines.Add(BreakdownLineVM.Item(T("S.ArmorVM.BeforePen"), res.BeforePenetration));
        BreakdownLines.Add(BreakdownLineVM.Item(string.Format(T("S.ArmorVM.AfterPen"), ArmorPenetrationPercent), res.AfterPenetration));
        BreakdownLines.Add(BreakdownLineVM.Item(T("S.ArmorVM.EffectiveAL"), res.Effective));
    }

    // ── Import code P (équipement) ────────────────────────────────────────────────────────────
    // Pré-remplit insignes des 5 pièces + bouclier depuis un code d'équipement. Tout reste éditable.
    public string ImportPCode(string code)
    {
        var build = GwEquipmentCodec.Decode(code?.Trim() ?? "");
        if (build is null) return T("S.ArmorVM.CodeUnreadable");

        int mapped = 0;
        foreach (var item in build.Armor)
        {
            int pieceIdx = SlotToPiece(item.Slot);
            if (pieceIdx < 0) continue;
            foreach (var modId in item.ModifierIds)
            {
                var opt = InsigniaOptions.FirstOrDefault(o => o.ModId == modId);
                if (opt is not null) { Pieces[pieceIdx].Selected = opt; mapped++; }
            }
        }
        // Bouclier : présent dans un set d'armes (off-hand de type Shield).
        bool shield = build.WeaponSets.SelectMany(s => s.Items)
            .Any(i => GwEquipmentInfo.Items.TryGetValue(i.ItemId, out var info) && info.Weapon == WeaponKind.Shield);
        if (shield) { ShieldEquipped = true; ShieldRequirementMet = true; }

        Recompute();
        return mapped > 0 || shield
            ? string.Format(T("S.ArmorVM.Imported"), mapped, shield ? T("S.ArmorVM.PlusShield") : "")
            : T("S.ArmorVM.NoInsigniaRecognized");
    }

    private static int SlotToPiece(int slot) => slot switch
    {
        GwEquipmentInfo.SlotHead  => 0,
        GwEquipmentInfo.SlotChest => 1,
        GwEquipmentInfo.SlotHands => 2,
        GwEquipmentInfo.SlotLegs  => 3,
        GwEquipmentInfo.SlotFeet  => 4,
        _ => -1,
    };

    // ── Mods d'équipement réels ───────────────────────────────────────────────────────────────
    // Limites réalistes (règle Philippe 17/07) : 5 runes max (une par pièce d'armure), 1
    // inscription + 1 mod d'arme par main — soit 2 inscriptions (la 2e étant celle de l'arme
    // secondaire OU du bouclier) et 2 mods d'arme, réduits à 1 si un bouclier est équipé.
    private string? _modLimitMessage;
    public string? ModLimitMessage { get => _modLimitMessage; set => SetField(ref _modLimitMessage, value); }

    private string? ModLimitViolation(EquipmentModOption m)
    {
        int count = AddedMods.Count(x => x.Kind == m.Kind);
        return m.Kind switch
        {
            EquipModKind.Rune when count >= 5 =>
                T("S.ArmorVM.LimitRunes"),
            EquipModKind.Inscription when count >= 2 =>
                T("S.ArmorVM.LimitInscriptions"),
            EquipModKind.WeaponMod when ShieldEquipped && count >= 1 =>
                T("S.ArmorVM.LimitWeaponModShield"),
            EquipModKind.WeaponMod when count >= 2 =>
                T("S.ArmorVM.LimitWeaponMods"),
            _ => null,
        };
    }

    public void AddSelectedMod()
    {
        if (SelectedModOption is null) return;
        if (ModLimitViolation(SelectedModOption) is { } msg) { ModLimitMessage = msg; return; }
        ModLimitMessage = null;
        AddedMods.Add(SelectedModOption);
        Recompute();
    }

    public void RemoveMod(EquipmentModOption mod)
    {
        AddedMods.Remove(mod);
        ModLimitMessage = null;
        Recompute();
    }

    // ── Contributions personnalisées ──────────────────────────────────────────────────────────
    public void AddCustom()
    {
        Customs.Add(new CustomContribVM(Recompute) { Label = T("S.ArmorVM.NewMod"), Value = 5 });
        Recompute();
    }

    public void RemoveCustom(CustomContribVM c)
    {
        Customs.Remove(c);
        Recompute();
    }

    // ── Profils nommés ────────────────────────────────────────────────────────────────────────
    private void RefreshProfileNames()
    {
        ProfileNames.Clear();
        if (_settings is null) return;
        foreach (var p in _settings.ArmorProfiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            ProfileNames.Add(p.Name);
    }

    public string SaveProfile(string name)
    {
        if (_settings is null) return T("S.ArmorVM.SettingsUnavailable");
        if (string.IsNullOrWhiteSpace(name)) return T("S.ArmorVM.EmptyProfileName");
        name = name.Trim();

        var dto = ToDto(name);
        int idx = _settings.ArmorProfiles.FindIndex(p =>
            string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (idx >= 0) _settings.ArmorProfiles[idx] = dto;
        else _settings.ArmorProfiles.Add(dto);
        _settings.Save();
        RefreshProfileNames();
        _selectedProfileName = name;
        OnPropertyChanged(nameof(SelectedProfileName));
        return string.Format(T("S.ArmorVM.ProfileSaved"), name);
    }

    public string DeleteProfile(string? name)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(name)) return T("S.ArmorVM.NoProfileSelected");
        int removed = _settings.ArmorProfiles.RemoveAll(p =>
            string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (removed == 0) return T("S.ArmorVM.ProfileNotFound");
        _settings.Save();
        RefreshProfileNames();
        return string.Format(T("S.ArmorVM.ProfileDeleted"), name);
    }

    private ArmorCalcProfile ToDto(string name) => new()
    {
        Name = name,
        Profession = _profession,
        BaseArmor = BaseArmor,
        PieceInsigniaModIds = Pieces.Select(p => p.Selected?.ModId ?? 0).ToList(),
        ShieldEquipped = ShieldEquipped,
        ShieldBaseArmor = ShieldBaseArmor,
        ShieldRequirementMet = ShieldRequirementMet,
        ShieldIsStrength = ShieldIsStrength,
        AddedModIds = AddedMods.Select(m => m.ModId).ToList(),
        Customs = Customs.Select(c => new CustomContribDto
        {
            Label = c.Label, Value = c.Value, Category = (int)c.Category, Scope = c.Scope.ToString(),
        }).ToList(),
        Effects = Effects.Where(e => e.IsChecked)
            .Select(e => new EffectStateDto { Key = e.Key, Checked = true, Rank = e.Rank }).ToList(),
        ArmorPenetrationPercent = ArmorPenetrationPercent,
        IsEnchanted = IsEnchanted,
        IsHexed = IsHexed,
        IsInStance = IsInStance,
        IsAttacking = IsAttacking,
        IsHoldingItem = IsHoldingItem,
        IsUsingPreparation = IsUsingPreparation,
        IsPetAlive = IsPetAlive,
        HasCondition = HasCondition,
        IsActivatingSkill = IsActivatingSkill,
        HasWeaponSpell = HasWeaponSpell,
        HasShoutChant = HasShoutChant,
        HealthPercent = HealthPercent,
        EnchantmentCount = EnchantmentCount,
        RechargingSkillCount = RechargingSkillCount,
        MinionCount = MinionCount,
        SpiritCount = SpiritCount,
        SignetCount = SignetCount,
        IllusionSkillCount = IllusionSkillCount,
        ReferenceAttacks = ReferenceAttacks
            .Select(a => new ReferenceAttackDto { SkillId = a.Skill.Id, Rank = a.Rank }).ToList(),
    };

    private void LoadProfile(string name)
    {
        var dto = _settings?.ArmorProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (dto is null) return;

        _profession = dto.Profession;
        OnPropertyChanged(nameof(Profession));
        OnPropertyChanged(nameof(InherentDescription));
        _baseArmor = dto.BaseArmor;
        OnPropertyChanged(nameof(BaseArmor));
        RebuildInsigniaOptions();
        for (int i = 0; i < Pieces.Count && i < dto.PieceInsigniaModIds.Count; i++)
            Pieces[i].Selected = InsigniaOptions.FirstOrDefault(o => o.ModId == dto.PieceInsigniaModIds[i]) ?? InsigniaOption.None;

        _shieldEquipped = dto.ShieldEquipped; OnPropertyChanged(nameof(ShieldEquipped));
        _shieldBaseArmor = dto.ShieldBaseArmor; OnPropertyChanged(nameof(ShieldBaseArmor));
        _shieldRequirementMet = dto.ShieldRequirementMet; OnPropertyChanged(nameof(ShieldRequirementMet));
        _shieldIsStrength = dto.ShieldIsStrength; OnPropertyChanged(nameof(ShieldIsStrength));

        AddedMods.Clear();
        foreach (var id in dto.AddedModIds)
        {
            var opt = AvailableMods.FirstOrDefault(m => m.ModId == id);
            if (opt is not null) AddedMods.Add(opt);
        }

        Customs.Clear();
        foreach (var c in dto.Customs)
            Customs.Add(new CustomContribVM(Recompute)
            {
                Label = c.Label, Value = c.Value,
                Category = (ArmorCalculator.Category)Math.Clamp(c.Category, 0, 2),
                Scope = Enum.TryParse<ArmorScope>(c.Scope, out var s) ? s : ArmorScope.All,
            });

        // Clés d'effets : nouvelles = « Name » seul ; anciens profils (pré-17/07) = « Name|Scope »
        // par demi-effet → une clé ancienne matche la ligne fusionnée par son préfixe.
        foreach (var row in Effects)
        {
            var st = dto.Effects.FirstOrDefault(e =>
                e.Key == row.Key || e.Key.StartsWith(row.Key + "|", StringComparison.Ordinal));
            if (st is not null) row.SetState(st.Checked, st.Rank);
            else row.SetState(false, row.Rank);
        }

        _armorPenetrationPercent = dto.ArmorPenetrationPercent;
        OnPropertyChanged(nameof(ArmorPenetrationPercent));

        _isEnchanted = dto.IsEnchanted; OnPropertyChanged(nameof(IsEnchanted));
        _isHexed = dto.IsHexed; OnPropertyChanged(nameof(IsHexed));
        _isInStance = dto.IsInStance; OnPropertyChanged(nameof(IsInStance));
        _isAttacking = dto.IsAttacking; OnPropertyChanged(nameof(IsAttacking));
        _isHoldingItem = dto.IsHoldingItem; OnPropertyChanged(nameof(IsHoldingItem));
        _isUsingPreparation = dto.IsUsingPreparation; OnPropertyChanged(nameof(IsUsingPreparation));
        _isPetAlive = dto.IsPetAlive; OnPropertyChanged(nameof(IsPetAlive));
        _hasCondition = dto.HasCondition; OnPropertyChanged(nameof(HasCondition));
        _isActivatingSkill = dto.IsActivatingSkill; OnPropertyChanged(nameof(IsActivatingSkill));
        _hasWeaponSpell = dto.HasWeaponSpell; OnPropertyChanged(nameof(HasWeaponSpell));
        _hasShoutChant = dto.HasShoutChant; OnPropertyChanged(nameof(HasShoutChant));
        _healthPercent = Math.Clamp(dto.HealthPercent, 0, 100); OnPropertyChanged(nameof(HealthPercent));
        _enchantmentCount = Math.Clamp(dto.EnchantmentCount, 0, 30); OnPropertyChanged(nameof(EnchantmentCount));
        _rechargingSkillCount = Math.Clamp(dto.RechargingSkillCount, 0, 8); OnPropertyChanged(nameof(RechargingSkillCount));
        _minionCount = Math.Clamp(dto.MinionCount, 0, 30); OnPropertyChanged(nameof(MinionCount));
        _spiritCount = Math.Clamp(dto.SpiritCount, 0, 30); OnPropertyChanged(nameof(SpiritCount));
        _signetCount = Math.Clamp(dto.SignetCount, 0, 8); OnPropertyChanged(nameof(SignetCount));
        _illusionSkillCount = Math.Clamp(dto.IllusionSkillCount, 0, 8); OnPropertyChanged(nameof(IllusionSkillCount));
        RefreshPerUnitRows();

        // Liste vide = profil antérieur au Lot D → on garde les attaques par défaut en place.
        if (dto.ReferenceAttacks.Count > 0 && _catalog.Count > 0)
        {
            ReferenceAttacks.Clear();
            foreach (var a in dto.ReferenceAttacks)
                if (_catalog.FirstOrDefault(s => s.Id == a.SkillId) is { } sk)
                    ReferenceAttacks.Add(new ReferenceAttackVM(sk, a.Rank, RefreshAttacks));
        }

        Recompute();
    }
}

// ── Sous-VM ───────────────────────────────────────────────────────────────────────────────────

// Une pièce d'armure : nom + insigne sélectionné + liste partagée d'options (bindée directement
// pour éviter un RelativeSource fragile depuis le DataTemplate de la pièce).
public class ArmorPieceVM : ViewModelBase
{
    private readonly Action _onChanged;
    private readonly int _index;
    // Nom de pièce (Tête/Head…) dans la langue courante, sans rebâtir la pièce (sélection préservée).
    public string Name => ArmorCalcViewModel.PieceName(_index);
    public ObservableCollection<InsigniaOption> Options { get; }

    public ArmorPieceVM(int index, ObservableCollection<InsigniaOption> options, Action onChanged)
    { _index = index; Options = options; _onChanged = onChanged; }

    /// <summary>Rafraîchit le nom de pièce après un changement de langue.</summary>
    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(Name));

    private InsigniaOption? _selected = InsigniaOption.None;
    public InsigniaOption? Selected
    {
        get => _selected;
        set { if (SetField(ref _selected, value)) _onChanged(); }
    }
}

/// <summary>Condition d'activation d'une clause d'armure d'insigne (chantier 14 bis). Les formes à
/// seuil portent leur seuil dans <see cref="InsigniaClause.Threshold"/>. Requires (Sentinel's,
/// Prismatic) = supposé rempli, signalé dans le libellé. PerSignet (Artificer's) = valeur ×
/// signets équipés.</summary>
public enum InsigniaCond
{
    None, Attacking, Enchanted, NotEnchanted, Hexed, Stance, HoldingItem, Preparation,
    PetAlive, HasCondition, ActivatingSkill, WeaponSpell, ShoutChant,
    HealthBelow, MinionsAtLeast, SpiritsAtLeast, RechargingAtLeast, EnchantmentsAtLeast,
    PerSignet, Requires,
}

/// <summary>Une clause « Armor ±N (…) » d'un insigne : valeur signée + portée + condition.</summary>
public sealed record InsigniaClause(int Value, ArmorCalcViewModel.ArmorScope Scope,
                                    InsigniaCond Cond, int Threshold, string? CondFr, string? CondEn)
{
    /// <summary>Clause d'application dans la langue affichée (null si aucune).</summary>
    public string? DisplayCond => ZCodex.Core.Models.AppLanguage.IsFr ? CondFr : CondEn;
}

// Un insigne proposable : TOUTES ses clauses d'armure (rework 17/07 : plus seulement la 1re),
// réduction plate (Knight's), vulnérabilité sacrée par slot (Tormentor's) et réductions de durée
// (Lieutenant's) — parsées de la description parfaite (clauses séparées par « ; »).
public class InsigniaOption
{
    public int ModId { get; }
    private readonly string _display;
    // None (« aucun ») est un singleton statique → son libellé se calcule à la langue courante ;
    // les vrais insignes sont rebâtis par RebuildInsigniaOptions au switch.
    public string DisplayName => ModId == 0 ? L("(aucun)", "(none)") : _display;
    public string ShortName { get; }
    public IReadOnlyList<InsigniaClause> Clauses { get; }
    public IReadOnlyList<DurationReduction> Durations { get; }

    // Réduction PLATE de dégâts physiques de l'insigne (Knight's : −3). PAR PIÈCE : ne réduit que
    // les coups reçus sur CETTE localisation (contrairement à la rune d'Absorption, globale).
    public int FlatPhysical { get; }

    // Vulnérabilité sacrée de Tormentor's : « Holy damage you receive increased by N (on X armor) ».
    // GLOBALE et cumulable — la valeur dépend du SLOT où l'insigne est posé.
    private readonly int _holyChest, _holyLeg, _holyOther;
    public int HolyVulnAt(int pieceIndex) => pieceIndex switch
    {
        1 => _holyChest,   // Torse
        3 => _holyLeg,     // Jambes
        _ => _holyOther,   // Tête / Bras / Pieds
    };

    public static readonly InsigniaOption None =
        new(0, "(aucun)", "(aucun)", [], [], 0, 0, 0, 0);

    private InsigniaOption(int modId, string display, string shortName,
                           IReadOnlyList<InsigniaClause> clauses, IReadOnlyList<DurationReduction> durations,
                           int flatPhysical, int holyChest, int holyLeg, int holyOther)
    { ModId = modId; _display = display; ShortName = shortName; Clauses = clauses;
      Durations = durations; FlatPhysical = flatPhysical;
      _holyChest = holyChest; _holyLeg = holyLeg; _holyOther = holyOther; }

    // Libellé bilingue baké (reconstruit au switch via RebuildInsigniaOptions).
    private static string L(string fr, string en) => AppLanguage.IsFr ? fr : en;

    private static readonly Regex ArmorClauseRe = new(@"Armor ([+-]\d+)\s*(?:\(([^)]*)\))?", RegexOptions.Compiled);
    private static readonly Regex HolyVulnRe = new(
        @"Holy damage you receive increased by\s*\+?\s*(\d+)\s*\(on (chest|leg|other) armor\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HealthBelowRe = new(@"while health is below (\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CountRe = new(
        @"(?:while you control (?<n1>\d+) or more (?<what1>minions|spirits))|(?:while recharging (?<n2>\d+) or more skills)|(?:while affected by (?<n3>\d+) or more enchantment)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fabrique depuis un insigne : clauses « Armor ±N (…) » ET/OU réduction plate (Knight's) ET/OU
    // vulnérabilité sacrée (Tormentor's). Renvoie null si l'insigne n'a rien de tout ça (Survivor,
    // Radiant, Bloodstained, Stonefist…).
    public static InsigniaOption? FromMod(int modId, string name)
    {
        if (!GwEquipmentModDetails.ByModId.TryGetValue(modId, out var d)) return null;
        string desc = d.Description;

        var clauses = new List<InsigniaClause>();
        foreach (Match m in ArmorClauseRe.Matches(desc))
        {
            int value = int.Parse(m.Groups[1].Value);
            string paren = m.Groups[2].Success ? m.Groups[2].Value : "";
            var scope = ParseScope(paren);
            var (cond, threshold, condFr, condEn) = ParseCond(paren);
            clauses.Add(new(value, scope, cond, threshold, condFr, condEn));
        }

        var (flat, flatCond, _, _) = EquipmentModOption.ParseFlat(desc);
        // Réduction plate CONDITIONNELLE sur un insigne : hors périmètre (aucune dans le jeu) —
        // seule l'inconditionnelle (Knight's) est retenue pour rester lisible par pièce.
        if (flatCond != FlatCondition.None) flat = 0;

        int holyChest = 0, holyLeg = 0, holyOther = 0;
        foreach (Match m in HolyVulnRe.Matches(desc))
        {
            int v = int.Parse(m.Groups[1].Value);
            switch (m.Groups[2].Value.ToLowerInvariant())
            {
                case "chest": holyChest = v; break;
                case "leg": holyLeg = v; break;
                default: holyOther = v; break;
            }
        }

        // Réductions de durée (Lieutenant's : « Reduces Hex durations on you by 20% … ») — les
        // fragments « ; » sont recollés pour retrouver la phrase du jeu.
        var durations = EquipmentModOption.ParseDurations(desc.Replace(" ; ", " "));

        if (clauses.Count == 0 && flat == 0 && holyChest + holyLeg + holyOther == 0) return null;

        string shortNameEn = name.EndsWith(" Insignia", StringComparison.Ordinal) ? name[..^9] : name;
        string shortName = GwInsigniaFr.ShortName(modId, shortNameEn);   // FR (gwiki) en mode FR, sinon EN
        var bits = new List<string>();
        foreach (var c in clauses)
        {
            string tag = ScopeTag(c.Scope);
            bits.Add($"{(c.Value >= 0 ? "+" : "−")}{Math.Abs(c.Value)}"
                     + (tag.Length > 0 ? $" {tag}" : "")
                     + (c.DisplayCond is null ? "" : $" ({c.DisplayCond})"));
        }
        if (flat > 0) bits.Add($"−{flat} {L("dégâts physiques", "physical damage")}");
        if (holyChest + holyLeg + holyOther > 0)
            bits.Add(L($"vuln. sacrée +{holyOther}/+{holyChest}/+{holyLeg} (autre/torse/jambes, globale)",
                       $"holy vuln. +{holyOther}/+{holyChest}/+{holyLeg} (other/chest/legs, global)"));
        foreach (var du in durations) bits.Add($"−{du.Percent}% {du.ConditionFr}");
        string display = $"{shortName}  ({string.Join(" ; ", bits)})";
        return new InsigniaOption(modId, display, shortName, clauses, durations,
                                  flat, holyChest, holyLeg, holyOther);
    }

    private static string ScopeTag(ArmorCalcViewModel.ArmorScope s) => s switch
    {
        ArmorCalcViewModel.ArmorScope.Physical  => L("phys", "phys"),
        ArmorCalcViewModel.ArmorScope.Elemental => L("élém", "elem"),
        ArmorCalcViewModel.ArmorScope.Slashing  => L("tranch", "slash"),
        ArmorCalcViewModel.ArmorScope.Piercing  => L("perf", "pierc"),
        ArmorCalcViewModel.ArmorScope.Blunt     => L("cont", "blunt"),
        ArmorCalcViewModel.ArmorScope.Fire      => L("feu", "fire"),
        ArmorCalcViewModel.ArmorScope.Cold      => L("froid", "cold"),
        ArmorCalcViewModel.ArmorScope.Earth     => L("terre", "earth"),
        ArmorCalcViewModel.ArmorScope.Lightning => L("foudre", "light"),
        _ => "",
    };

    // La portée de type ne se lit qu'APRÈS « vs. » — sinon « requires 9 Fire Magic » (Prismatic)
    // matcherait « fire » et fausserait la clause en +5 vs feu.
    private static ArmorCalcViewModel.ArmorScope ParseScope(string paren)
    {
        var p = paren.ToLowerInvariant();
        int vs = p.IndexOf("vs.", StringComparison.Ordinal);
        if (vs < 0) return ArmorCalcViewModel.ArmorScope.All;
        p = p[vs..];
        return
            p.Contains("physical")  ? ArmorCalcViewModel.ArmorScope.Physical :
            p.Contains("elemental") ? ArmorCalcViewModel.ArmorScope.Elemental :
            p.Contains("fire")      ? ArmorCalcViewModel.ArmorScope.Fire :
            p.Contains("cold")      ? ArmorCalcViewModel.ArmorScope.Cold :
            p.Contains("earth")     ? ArmorCalcViewModel.ArmorScope.Earth :
            p.Contains("lightning") ? ArmorCalcViewModel.ArmorScope.Lightning :
            p.Contains("slashing")  ? ArmorCalcViewModel.ArmorScope.Slashing :
            p.Contains("piercing")  ? ArmorCalcViewModel.ArmorScope.Piercing :
            p.Contains("blunt")     ? ArmorCalcViewModel.ArmorScope.Blunt :
            ArmorCalcViewModel.ArmorScope.All;
    }

    // Toutes les formes conditionnelles réelles du catalogue d'insignes (inventaire wiki/Insignia,
    // descriptions GwEquipmentModDetails vérifiées le 17/07).
    private static (InsigniaCond Cond, int Threshold, string? CondFr, string? CondEn) ParseCond(string paren)
    {
        if (string.IsNullOrWhiteSpace(paren)) return (InsigniaCond.None, 0, null, null);
        var p = paren.ToLowerInvariant();

        if (p.Contains("for each equipped signet")) return (InsigniaCond.PerSignet, 0, "× sceaux équipés", "× equipped signets");
        if (HealthBelowRe.Match(paren) is { Success: true } hm)
        { int t = int.Parse(hm.Groups[1].Value); return (InsigniaCond.HealthBelow, t, $"PV < {t} %", $"Health < {t}%"); }
        if (CountRe.Match(paren) is { Success: true } cm)
        {
            if (cm.Groups["n1"].Success)
            {
                int t = int.Parse(cm.Groups["n1"].Value);
                bool minions = cm.Groups["what1"].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase);
                return minions ? (InsigniaCond.MinionsAtLeast, t, $"≥ {t} serviteurs", $"≥ {t} minions")
                               : (InsigniaCond.SpiritsAtLeast, t, $"≥ {t} esprits", $"≥ {t} spirits");
            }
            if (cm.Groups["n2"].Success)
            { int t = int.Parse(cm.Groups["n2"].Value); return (InsigniaCond.RechargingAtLeast, t, $"≥ {t} en recharge", $"≥ {t} recharging"); }
            int t3 = int.Parse(cm.Groups["n3"].Value);
            return (InsigniaCond.EnchantmentsAtLeast, t3, $"≥ {t3} ench.", $"≥ {t3} ench.");
        }
        if (p.Contains("while not affected by an enchantment")) return (InsigniaCond.NotEnchanted, 0, "non enchanté", "not enchanted");
        if (p.Contains("while affected by an enchantment"))     return (InsigniaCond.Enchanted, 0, "enchanté", "enchanted");
        if (p.Contains("while affected by a hex"))              return (InsigniaCond.Hexed, 0, "sous maléfice", "hexed");
        if (p.Contains("while attacking"))                      return (InsigniaCond.Attacking, 0, "en attaquant", "while attacking");
        if (p.Contains("while in a stance"))                    return (InsigniaCond.Stance, 0, "en posture", "in a stance");
        if (p.Contains("while holding an item"))                return (InsigniaCond.HoldingItem, 0, "objet en main", "holding an item");
        if (p.Contains("while using a preparation"))            return (InsigniaCond.Preparation, 0, "préparation", "using a preparation");
        if (p.Contains("while your pet is alive"))              return (InsigniaCond.PetAlive, 0, "familier en vie", "pet alive");
        if (p.Contains("while affected by a condition"))        return (InsigniaCond.HasCondition, 0, "sous condition", "with a condition");
        if (p.Contains("while activating skills"))              return (InsigniaCond.ActivatingSkill, 0, "en activation", "while activating");
        if (p.Contains("while affected by a weapon spell"))     return (InsigniaCond.WeaponSpell, 0, "sort d'arme", "weapon spell");
        if (p.Contains("while affected by a shout"))            return (InsigniaCond.ShoutChant, 0, "cri/écho/chant", "shout/echo/chant");
        if (p.Contains("requires"))                             return (InsigniaCond.Requires, 0, "requis supposé rempli", "requirement assumed met");
        return (InsigniaCond.None, 0, null, null);   // parenthèse purement « vs type » → portée seule
    }
}

// Réduction de durée d'une altération apportée par un mod (« Reduces X duration on you by N% »).
public sealed record DurationReduction(string ConditionFr, int Percent, bool Stacking);

/// <summary>Condition d'activation d'une réduction PLATE de dégâts physiques (Lot D).
/// Chance = probabiliste (« Luck of the Draw » −5 à 20 %) → compté en ESPÉRANCE, toujours actif.</summary>
public enum FlatCondition { None, Enchanted, Hexed, Stance, Chance }

/// <summary>Nature d'un mod d'équipement — pilote les limites réalistes (17/07, règle Philippe) :
/// 5 runes max (une par pièce), 2 inscriptions max (main principale + secondaire/bouclier),
/// 2 mods d'arme max (un par main) réduits à 1 si un bouclier est équipé (la main secondaire est
/// alors le bouclier, qui ne porte pas de mod d'arme).</summary>
public enum EquipModKind { Rune, Inscription, WeaponMod }

// Un mod d'équipement RÉEL sélectionnable (rune, mod d'arme, inscription) : effet d'armure et/ou
// réduction(s) de durée d'altération, parsés une fois depuis la description parfaite du jeu.
public class EquipmentModOption
{
    public int ModId { get; }
    public string Name { get; }
    public EquipModKind Kind =>
        Name.StartsWith('"') ? EquipModKind.Inscription :
        Name.StartsWith("Rune of", StringComparison.Ordinal) ? EquipModKind.Rune :
        EquipModKind.WeaponMod;
    public string EffectText { get; }              // résumé lisible (armure + durées)
    public bool HasArmor { get; }
    public int ArmorValue { get; }                 // signé
    public ArmorCalcViewModel.ArmorScope ArmorScope { get; }
    public ArmorCalculator.Category ArmorCategory { get; }
    public string? ArmorCondition { get; }
    public IReadOnlyList<DurationReduction> Durations { get; }

    // Réduction PLATE de dégâts physiques (Lot D) : Absorption (Non-stacking), inscriptions
    // conditionnelles (Enchanté/Maudit/Posture) et « Luck of the Draw » (probabiliste).
    // 0 = le mod ne réduit pas les dégâts. S'applique APRÈS le calcul d'AL, sur les dégâts.
    public int FlatPhysical { get; }
    public FlatCondition FlatCondition { get; }
    public double FlatChance { get; }          // 1.0 sauf « Chance: N% » → N/100
    public bool FlatNonStacking { get; }        // Absorption : seule la meilleure compte

    private EquipmentModOption(int modId, string name, string effectText, bool hasArmor, int armorValue,
        ArmorCalcViewModel.ArmorScope scope, ArmorCalculator.Category cat, string? cond,
        IReadOnlyList<DurationReduction> durations,
        int flatPhysical, FlatCondition flatCond, double flatChance, bool flatNonStacking)
    { ModId = modId; Name = name; EffectText = effectText; HasArmor = hasArmor; ArmorValue = armorValue;
      ArmorScope = scope; ArmorCategory = cat; ArmorCondition = cond; Durations = durations;
      FlatPhysical = flatPhysical; FlatCondition = flatCond; FlatChance = flatChance;
      FlatNonStacking = flatNonStacking; }

    /// <summary>Points effectivement retranchés : espérance pour une source probabiliste
    /// (« Luck of the Draw » : 5 × 20 % = 1), valeur pleine sinon.</summary>
    public int FlatValue => FlatCondition == FlatCondition.Chance
        ? (int)Math.Round(FlatPhysical * FlatChance, MidpointRounding.AwayFromZero)
        : FlatPhysical;

    // Nom affiché dans la langue courante (runes/inscriptions → GwEquipmentModsFr) ; Name reste
    // la clé EN (détection Kind, ligne de breakdown). EffectText est déjà bilingue (TranslateParen/L).
    public string DisplayName => $"{GwEquipmentModsFr.DisplayName(ModId, Name)}  —  {EffectText}";

    private static readonly Regex ArmorRe = new(@"Armor ([+-]\d+)\s*(?:\(([^)]*)\))?", RegexOptions.Compiled);
    private static readonly Regex DurRe = new(
        @"Reduces (.+?) durations? on you by (\d+)%\s*(?:\((Stacking|Non-stacking)\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Réduction plate, deux formes réelles du catalogue :
    //   « Reduces physical damage by 3 (Non-stacking) »        → runes d'Absorption
    //   « Received physical damage -2 (while Enchanted) »      → inscriptions (+ Knight's, Luck…)
    private static readonly Regex AbsorptionRe = new(
        @"Reduces physical damage by (\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReceivedRe = new(
        @"Received physical damage -(\d+)\s*(?:\(([^)]*)\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChanceRe = new(@"Chance:\s*(\d+)\s*%", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Lit une réduction plate de dégâts physiques dans une description parfaite.
    /// Renvoie (0, None, 1.0, false) si le mod n'en porte pas.</summary>
    public static (int Value, FlatCondition Cond, double Chance, bool NonStacking) ParseFlat(string desc)
    {
        if (AbsorptionRe.Match(desc) is { Success: true } ab)
            return (int.Parse(ab.Groups[1].Value), FlatCondition.None, 1.0,
                    desc.Contains("Non-stacking", StringComparison.OrdinalIgnoreCase));

        if (ReceivedRe.Match(desc) is { Success: true } rc)
        {
            int v = int.Parse(rc.Groups[1].Value);
            string paren = rc.Groups[2].Success ? rc.Groups[2].Value : "";
            if (ChanceRe.Match(paren) is { Success: true } ch)
                return (v, FlatCondition.Chance, int.Parse(ch.Groups[1].Value) / 100.0, false);
            var cond =
                paren.Contains("Enchant", StringComparison.OrdinalIgnoreCase) ? FlatCondition.Enchanted :
                paren.Contains("Hex", StringComparison.OrdinalIgnoreCase)     ? FlatCondition.Hexed :
                paren.Contains("Stance", StringComparison.OrdinalIgnoreCase)  ? FlatCondition.Stance :
                FlatCondition.None;
            return (v, cond, 1.0, false);
        }
        return (0, FlatCondition.None, 1.0, false);
    }

    /// <summary>Réductions de durée d'altération lues dans une description parfaite (« Reduces X
    /// durations on you by N% (Stacking/Non-stacking) »). Partagé avec les insignes (Lieutenant's).
    /// Les noms FR viennent de <see cref="GwConditionData.FrName"/> (carte partagée avec les pilules
    /// des attaques de référence, pour un matching cohérent).</summary>
    public static IReadOnlyList<DurationReduction> ParseDurations(string desc)
    {
        var durations = new List<DurationReduction>();
        foreach (Match dm in DurRe.Matches(desc))
        {
            int pct = int.Parse(dm.Groups[2].Value);
            bool stacking = dm.Groups[3].Value.Equals("Stacking", StringComparison.OrdinalIgnoreCase);
            foreach (var raw in dm.Groups[1].Value.Split(" and ", StringSplitOptions.TrimEntries))
                durations.Add(new DurationReduction(GwConditionData.DisplayName(raw), pct, stacking));
        }
        return durations;
    }

    private static string L(string fr, string en) => AppLanguage.IsFr ? fr : en;

    private static readonly Regex HealthBelowParenRe = new(@"while health is below (\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Traduction FR (en dur) du texte entre parenthèses d'une clause « Armor +N (...) » d'un mod
    // d'arme (formes figées du jeu). En anglais on garde le texte brut du wiki.
    private static string TranslateParen(string paren)
    {
        if (!AppLanguage.IsFr) return paren;
        string p = HealthBelowParenRe.Replace(paren, m => $"PV < {m.Groups[1].Value} %");
        (string en, string fr)[] map =
        {
            ("vs. physical damage", "vs. physique"), ("vs. elemental damage", "vs. élémentaire"),
            ("vs. Slashing damage", "vs. tranchant"), ("vs. Piercing damage", "vs. perforant"),
            ("vs. Blunt damage", "vs. contondant"), ("vs. Fire damage", "vs. feu"),
            ("vs. Cold damage", "vs. froid"), ("vs. Earth damage", "vs. terre"),
            ("vs. Lightning damage", "vs. foudre"),
            ("while affected by an Enchantment Spell", "enchanté"),
            ("while affected by a Hex Spell", "sous maléfice"),
            ("while affected by a Condition", "sous condition"),
            ("while attacking", "en attaquant"), ("while in a stance", "en posture"),
            ("while holding an item", "objet en main"), ("while activating skills", "en activation"),
            ("Requires 13 Strength", "Nécessite 13 en Force"),
        };
        foreach (var (en, fr) in map)
            p = p.Replace(en, fr, StringComparison.OrdinalIgnoreCase);
        return p;
    }

    // Fabrique un mod sélectionnable si sa description porte un effet d'armure ou de durée ; sinon null.
    public static EquipmentModOption? TryParse(int modId, string name, string desc)
    {
        // Armure : 1re clause « Armor ±N (…) ».
        bool hasArmor = false; int armorVal = 0; string? cond = null;
        var scope = ArmorCalcViewModel.ArmorScope.All;
        var cat = name.StartsWith('"') ? ArmorCalculator.Category.Core   // inscription = Core (Lot B)
                                       : ArmorCalculator.Category.Bonus;  // mod d'arme / rune = Bonus
        var am = ArmorRe.Match(desc);
        if (am.Success)
        {
            hasArmor = true;
            armorVal = int.Parse(am.Groups[1].Value);
            string paren = am.Groups[2].Success ? am.Groups[2].Value : "";
            (scope, cond) = ParseScope(paren);
        }

        // Durées d'altération.
        var durations = ParseDurations(desc);

        // Réduction plate de dégâts physiques (Absorption, inscriptions conditionnelles, Luck).
        var (flatVal, flatCond, flatChance, flatNonStack) = ParseFlat(desc);

        if (!hasArmor && durations.Count == 0 && flatVal == 0) return null;

        var parts = new List<string>();
        if (hasArmor) parts.Add($"{L("Armure", "Armor")} {(armorVal >= 0 ? "+" : "")}{armorVal}"
                                + (cond is null ? "" : $" ({TranslateParen(cond)})"));
        if (flatVal > 0) parts.Add($"−{flatVal} {L("dégâts physiques", "physical damage")}" + flatCond switch
        {
            FlatCondition.Enchanted => L(" (si enchanté)", " (if enchanted)"),
            FlatCondition.Hexed     => L(" (sous maléfice)", " (if hexed)"),
            FlatCondition.Stance    => L(" (si en posture)", " (in a stance)"),
            FlatCondition.Chance    => L($" ({flatChance * 100:0} % — compté en espérance)", $" ({flatChance * 100:0}% — counted in expectancy)"),
            _ => "",
        });
        foreach (var d in durations) parts.Add($"−{d.Percent}% {d.ConditionFr}");
        return new EquipmentModOption(modId, name, string.Join(" ; ", parts), hasArmor, armorVal, scope, cat,
                                      cond, durations, flatVal, flatCond, flatChance, flatNonStack);
    }

    private static (ArmorCalcViewModel.ArmorScope, string?) ParseScope(string paren)
    {
        var p = paren.ToLowerInvariant();
        var scope =
            p.Contains("physical")  ? ArmorCalcViewModel.ArmorScope.Physical :
            p.Contains("elemental") ? ArmorCalcViewModel.ArmorScope.Elemental :
            p.Contains("fire")      ? ArmorCalcViewModel.ArmorScope.Fire :
            p.Contains("cold")      ? ArmorCalcViewModel.ArmorScope.Cold :
            p.Contains("earth")     ? ArmorCalcViewModel.ArmorScope.Earth :
            p.Contains("lightning") ? ArmorCalcViewModel.ArmorScope.Lightning :
            p.Contains("slashing")  ? ArmorCalcViewModel.ArmorScope.Slashing :
            p.Contains("piercing")  ? ArmorCalcViewModel.ArmorScope.Piercing :
            p.Contains("blunt")     ? ArmorCalcViewModel.ArmorScope.Blunt :
            ArmorCalcViewModel.ArmorScope.All;
        return (scope, string.IsNullOrWhiteSpace(paren) ? null : paren.Trim());
    }
}

// Ligne du récap des réductions de durée (une altération + cumul + détail des sources).
public class DurationSummaryVM
{
    public string ConditionFr { get; init; } = "";
    public int TotalPercent { get; init; }
    public string SourcesText { get; init; } = "";
    public string HeaderText => $"{ConditionFr} : −{TotalPercent}%";
}

/// <summary>Une pilule de condition infligée par une attaque de référence (image Philippe : puce
/// verte « icône + 5 s (8) »). Durée effective = base × réduction d'équipement, arrondie au plus
/// proche (l'image montre 8 s −40 % → 5 s) ; la base n'apparaît entre parenthèses que si une source
/// la réduit réellement. Durée 0 (non annoncée dans la description) → icône seule. Icône chargée
/// depuis le cache statique des conditions (jamais de redécodage disque par binding).</summary>
public class ConditionPillVM
{
    private static readonly Dictionary<string, ImageSource> _iconCache = new();

    public ConditionPillVM(string conditionEn, int baseSeconds, int reductionPercent)
    {
        ConditionEn = conditionEn;
        ConditionFr = GwConditionData.DisplayName(conditionEn);
        BaseSeconds = baseSeconds;
        ReductionPercent = reductionPercent;
        EffectiveSeconds = reductionPercent > 0 && baseSeconds > 0
            ? Math.Max(0, (int)Math.Round(baseSeconds * (100 - reductionPercent) / 100.0,
                                          MidpointRounding.AwayFromZero))
            : baseSeconds;
    }

    public string ConditionEn { get; }
    public string ConditionFr { get; }
    public int BaseSeconds { get; }
    public int EffectiveSeconds { get; }
    public int ReductionPercent { get; }

    // Réduite = une source abaisse EFFECTIVEMENT la durée (base affichée en parens seulement alors).
    public bool IsReduced => ReductionPercent > 0 && BaseSeconds > 0 && EffectiveSeconds != BaseSeconds;
    public bool HasDuration => BaseSeconds > 0;

    public string DurationText => HasDuration ? $"{EffectiveSeconds} s" : "";
    public string BaseText => IsReduced ? $"({BaseSeconds})" : "";

    public string ToolTipText => !HasDuration ? ConditionFr
        : IsReduced ? $"{ConditionFr} : {EffectiveSeconds} s (base {BaseSeconds} s, −{ReductionPercent} %)"
        :             $"{ConditionFr} : {EffectiveSeconds} s";

    public ImageSource? Icon
    {
        get
        {
            if (_iconCache.TryGetValue(ConditionEn, out var cached)) return cached;
            var path = ConditionIconService.GetLocalPath(ConditionEn);
            if (!File.Exists(path)) return null;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(path);
            img.DecodePixelWidth = 32;
            img.EndInit();
            img.Freeze();
            _iconCache[ConditionEn] = img;
            return img;
        }
    }
}

// Contribution personnalisée éditable (ajout manuel — cas non couverts par le catalogue de mods).
public class CustomContribVM : ViewModelBase
{
    private readonly Action _onChanged;
    public CustomContribVM(Action onChanged) { _onChanged = onChanged; }

    public IReadOnlyList<ArmorCalculator.Category> Categories { get; } =
        [ArmorCalculator.Category.Core, ArmorCalculator.Category.Bonus, ArmorCalculator.Category.Special];
    public IReadOnlyList<ArmorCalcViewModel.ArmorScope> Scopes { get; } =
        Enum.GetValues<ArmorCalcViewModel.ArmorScope>();

    private string _label = "";
    public string Label { get => _label; set { if (SetField(ref _label, value)) _onChanged(); } }
    private int _value;
    public int Value { get => _value; set { if (SetField(ref _value, value)) _onChanged(); } }
    private ArmorCalculator.Category _category = ArmorCalculator.Category.Bonus;
    public ArmorCalculator.Category Category { get => _category; set { if (SetField(ref _category, value)) _onChanged(); } }
    private ArmorCalcViewModel.ArmorScope _scope = ArmorCalcViewModel.ArmorScope.All;
    public ArmorCalcViewModel.ArmorScope Scope { get => _scope; set { if (SetField(ref _scope, value)) _onChanged(); } }
}

// Une ligne de la table d'effets (Lot B) : coché + rang + valeur(s) résolue(s). Rework 17/07 :
// une ligne = UN skill avec TOUTES ses clauses (Résistances = +40 d'un côté et malus de l'autre
// en une seule case) ; les effets per-unit (IW, Mantra of Signets) sont multipliés par le compte
// d'« État du personnage » fourni par le VM.
public class ArmorEffectRowVM : ViewModelBase
{
    private readonly Action _onChanged;
    private readonly Func<int, int> _unitCountFor;
    private readonly Func<int, string?> _skillDisplayName;
    public IReadOnlyList<ArmorEffectsData.ArmorEffect> Clauses { get; }
    private ArmorEffectsData.ArmorEffect Primary => Clauses[0];

    public ArmorEffectRowVM(IReadOnlyList<ArmorEffectsData.ArmorEffect> clauses, Action onChanged,
                            Func<int, int> unitCountFor, Func<int, string?> skillDisplayName)
    {
        Clauses = clauses; _onChanged = onChanged; _unitCountFor = unitCountFor;
        _skillDisplayName = skillDisplayName;
        IsProgressive = clauses.Any(c => c.ValuesByRank.Count > 1);
        UsesUnitCount = Primary.SkillId is 33 or 18;   // IW / Mantra of Signets
    }

    // Nom affiché : SkillId != 0 → DisplayName de la compétence (catalogue) ; SkillId 0 → nom FR
    // du non-skill (map Core). Key (clé de profil) reste le nom EN — NE PAS traduire.
    public string Name =>
        Primary.SkillId != 0 && _skillDisplayName(Primary.SkillId) is { } d ? d
        : ArmorEffectsData.NonSkillDisplayName(Primary.Name);
    public string Key => Primary.Name;   // clé de profil (anciens profils : « Name|Scope », migrés au Load)
    public bool IsProgressive { get; }
    public bool UsesUnitCount { get; }
    public bool HasProjectileClause => Clauses.Any(c => c.Scope == ArmorEffectsData.Scope.Projectile);

    // Libellé bilingue baké (portées/groupes construits en chaîne).
    private static string L(string fr, string en) => ZCodex.Core.Models.AppLanguage.IsFr ? fr : en;

    private static string ScopeTag(ArmorEffectsData.Scope s) => s switch
    {
        ArmorEffectsData.Scope.Physical   => L(" (vs physique)", " (vs physical)"),
        ArmorEffectsData.Scope.Elemental  => L(" (vs élémentaire)", " (vs elemental)"),
        ArmorEffectsData.Scope.Slashing   => L(" (vs tranchant)", " (vs slashing)"),
        ArmorEffectsData.Scope.Projectile => L(" (projectiles)", " (projectiles)"),
        _ => "",
    };

    // Libellé de la ligne : nom + portée si la ligne n'a qu'une clause (multi-clauses : le détail
    // est porté par la valeur composite et le détail de cellule).
    public string Label => Clauses.Count == 1 ? Name + ScopeTag(Primary.Scope) : Name;

    /// <summary>Libellé d'une clause pour le détail de cellule (nom + portée de LA clause).</summary>
    public string LabelFor(ArmorEffectsData.ArmorEffect clause) => Name + ScopeTag(clause.Scope);

    public string? ConditionFr => Clauses.Select(ArmorEffectsData.DisplayCondition).FirstOrDefault(c => c is not null);

    public string Group =>
        Primary.IsEnemyInflicted ? L("Malus subis", "Incurred penalties")
        : Primary.SkillId == 0   ? L("Consommables & effets", "Consumables & effects")
        :                          L("Compétences alliées", "Allied skills");

    // Notifié quand la case passe à COCHÉE → le VM décoche les variantes PvE/PvP mutuellement
    // exclusives (Watch Yourself! vs (PvP), Save Yourselves! Kurzick vs Luxon…).
    public Action<ArmorEffectRowVM>? OnCheckedTrue { get; set; }

    /// <summary>Rafraîchit tous les libellés calculés (Label/Group/ConditionFr/Value) après un
    /// changement de langue.</summary>
    public void RaiseLanguageChanged() => OnPropertyChanged(string.Empty);

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetField(ref _isChecked, value)) return;
            if (value) OnCheckedTrue?.Invoke(this);
            _onChanged();
        }
    }

    // Décoche sans notifier (utilisé par l'exclusion mutuelle — le recompute est fait une fois).
    public void UncheckSilently()
    {
        if (!_isChecked) return;
        _isChecked = false;
        OnPropertyChanged(nameof(IsChecked));
    }

    private int _rank = 12;
    public int Rank
    {
        get => _rank;
        set
        {
            if (SetField(ref _rank, Math.Clamp(value, 0, 21)))
            {
                RaiseResolvedChanged();
                if (IsChecked) _onChanged();
            }
        }
    }

    /// <summary>Clauses avec leur valeur résolue au rang courant (× compte per-unit pour IW/Mantra).</summary>
    public IEnumerable<(ArmorEffectsData.ArmorEffect Clause, int Value)> ResolvedClauses()
    {
        int unit = UsesUnitCount ? _unitCountFor(Primary.SkillId) : 1;
        foreach (var c in Clauses)
            yield return (c, ArmorEffectsData.ValueAt(c, _rank) * unit);
    }

    // Valeur affichée : simple (« +40 ») pour une clause, composite (« +40 élém / −14 phys »)
    // pour les skills fusionnés.
    public string ResolvedValueText
    {
        get
        {
            if (Clauses.Count == 1)
            {
                int v = ResolvedClauses().First().Value;
                return (v >= 0 ? "+" : "") + v;   // la portée est déjà dans le Label
            }
            return string.Join(" / ", ResolvedClauses().Select(rc =>
                (rc.Value >= 0 ? "+" : "") + rc.Value + rc.Clause.Scope switch
                {
                    ArmorEffectsData.Scope.Physical   => L(" phys", " phys"),
                    ArmorEffectsData.Scope.Elemental  => L(" élém", " elem"),
                    ArmorEffectsData.Scope.Slashing   => L(" tranch", " slash"),
                    ArmorEffectsData.Scope.Projectile => L(" proj", " proj"),
                    _ => "",
                }));
        }
    }

    public void RaiseResolvedChanged() => OnPropertyChanged(nameof(ResolvedValueText));

    // Applique un état de profil sans relancer le recompute par champ (l'appelant le fait une fois).
    public void SetState(bool isChecked, int rank)
    {
        _rank = Math.Clamp(rank, 0, 21);
        _isChecked = isChecked;
        OnPropertyChanged(nameof(Rank));
        OnPropertyChanged(nameof(IsChecked));
        RaiseResolvedChanged();
    }
}

// Une ligne de résultats (une localisation ou l'espérance).
public class ResultRowVM
{
    public string Label { get; }
    public bool IsExpectancy { get; }
    public ObservableCollection<ResultCellVM> Cells { get; } = new();
    public ResultRowVM(string label, bool isExpectancy) { Label = label; IsExpectancy = isExpectancy; }
}

// Une cellule = AL effective d'une (localisation, colonne). Deux fabrications : normale (Result)
// ou espérance (AL équivalente + multiplicateur pré-calculé).
public class ResultCellVM : ViewModelBase
{
    public int Location { get; }
    public ArmorCalcViewModel.DamageColumn Column { get; }
    public bool IsExpectancy { get; }
    public ArmorCalculator.Result? Result { get; }
    public int EffectiveAl { get; }
    private readonly ArmorCalcViewModel _owner;

    public ResultCellVM(int loc, ArmorCalcViewModel.DamageColumn col, ArmorCalculator.Result res, ArmorCalcViewModel owner)
    {
        Location = loc; Column = col; Result = res; EffectiveAl = res.Effective; _owner = owner;
    }

    public ResultCellVM(int loc, ArmorCalcViewModel.DamageColumn col, double expectedMultiplier, int alEq, ArmorCalcViewModel owner)
    {
        Location = loc; Column = col; IsExpectancy = true; EffectiveAl = alEq; _owner = owner;
        _percentOverride = expectedMultiplier;
    }

    private readonly double? _percentOverride;

    // % de dégâts subis vs AL 60 (référence). Espérance : le multiplicateur pondéré directement.
    public string PercentText
    {
        get
        {
            double mult = _percentOverride ?? ArmorCalculator.DamageMultiplier(EffectiveAl);
            return $"{mult * 100:0}%";
        }
    }

    public string AlText => EffectiveAl.ToString();

    public bool IsSelected => ReferenceEquals(_owner.SelectedCell, this);
    // Re-cliquer la cellule active la DÉSÉLECTIONNE (retour à l'espérance + détail vide) —
    // rework 17/07.
    public void Select()
        => _owner.SelectedCell = ReferenceEquals(_owner.SelectedCell, this) ? null : this;
    public void RaiseSelectedChanged() => OnPropertyChanged(nameof(IsSelected));
}

// Une ligne de la table d'attaques de référence (Lot D) : skill + rang éditable + dégâts subis
// à AL 60 (catalogue) vs à l'AL du build. Les valeurs sont poussées par le VM (Apply) : la ligne
// ne calcule rien elle-même, elle affiche.
public class ReferenceAttackVM : ViewModelBase
{
    private readonly Action _onChanged;
    public Skill Skill { get; }

    public ReferenceAttackVM(Skill skill, int rank, Action onChanged)
    { Skill = skill; _rank = rank; _onChanged = onChanged; }

    public string Name => Skill.DisplayName;
    public string IconPath => Skill.IconPath;

    // Bascule de langue : le nom (DisplayName du skill) et la description résolue rebindent.
    public void RaiseLanguageChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ResolvedDescription));
    }

    // Tooltip « calculée » (contexte build) : description RÉSOLUE au rang + table de dégâts d'arme.
    // WeaponMasteryRank = le rang de la ligne (la maîtrise d'arme d'une attaque de référence EST son
    // rang) ; ignoré pour un sort (pas d'arme). Suivent le rang éditable de la ligne.
    public string ResolvedDescription => ReferenceAttack.ResolveDescription(Skill, Rank);
    public int WeaponMasteryRank => Rank;

    // Conditions infligées à la cible (pilules à droite du nom), poussées par le VM (ApplyConditions).
    public ObservableCollection<ConditionPillVM> Conditions { get; } = new();
    public bool HasConditions => Conditions.Count > 0;

    private int _rank;
    public int Rank
    {
        get => _rank;
        set
        {
            if (!SetField(ref _rank, Math.Clamp(value, 0, 21))) return;
            OnPropertyChanged(nameof(ResolvedDescription));
            OnPropertyChanged(nameof(WeaponMasteryRank));
            _onChanged();   // recalcule dégâts ET pilules de conditions (durée de base au rang)
        }
    }

    public void ApplyConditions(IReadOnlyList<ConditionPillVM> pills)
    {
        Conditions.Clear();
        foreach (var p in pills) Conditions.Add(p);
        OnPropertyChanged(nameof(HasConditions));
    }

    private ReferenceAttack.Result? _result;
    private string? _extraNote;   // note posée par le VM (ex : AL projectile appliquée)

    public void Apply(ReferenceAttack.Result r, string? extraNote = null)
    {
        _result = r;
        _extraNote = extraNote;
        OnPropertyChanged(nameof(Text60));
        OnPropertyChanged(nameof(TextCalc));
        OnPropertyChanged(nameof(DeltaText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(IsMitigated));
    }

    private static string Fmt(int lo, int hi) => lo == hi ? hi.ToString() : $"{lo}–{hi}";

    public string Text60   => _result is null ? "—" : Fmt(_result.Lo60, _result.Hi60);
    public string TextCalc => _result is null ? "—" : Fmt(_result.LoCalc, _result.HiCalc);

    public string TypeText => _result?.IgnoresArmor == true
        ? (AppLanguage.IsFr ? "ignore l'armure" : "ignores armor")
        : _result?.PrimaryTypeFr ?? "";

    // Δ vs AL 60 : négatif = dégâts réduits par le build. Vide si l'attaque ignore l'armure.
    public string DeltaText
    {
        get
        {
            if (_result is null || _result.IgnoresArmor) return "";
            int d = _result.DeltaPercent;
            return d == 0 ? "0 %" : $"{(d > 0 ? "+" : "")}{d} %";
        }
    }

    public bool IsMitigated => _result is { IgnoresArmor: false } r && r.DeltaPercent < 0;
    public string Notes
    {
        get
        {
            string baseNotes = _result?.Notes ?? "";
            if (string.IsNullOrEmpty(_extraNote)) return baseNotes;
            return baseNotes.Length == 0 ? _extraNote! : $"{baseNotes} · {_extraNote}";
        }
    }
}

// Une ligne du détail dépliable : en-tête de catégorie ou contribution valuée.
public class BreakdownLineVM
{
    public string Text { get; private set; } = "";
    public string ValueText { get; private set; } = "";
    public bool IsHeader { get; private set; }

    public static BreakdownLineVM Header(string t) => new() { Text = t, IsHeader = true };
    public static BreakdownLineVM Item(string label, int value) =>
        new() { Text = label, ValueText = (value >= 0 ? "+" : "") + value };
}
