using ZCodex.App.Settings;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Search;
using ZCodex.Scraper;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;

namespace ZCodex.App.ViewModels;

// VM de la fenêtre « Spike damage calculus » (chantier 11) : cible paramétrable (AL de base +
// bonus par groupe/type, niveau → taux de critique, PV max → Deep Wound), profils persistés
// dans settings.json, et une ligne de résultat par skill sélectionnée (cadre vert) du roster.
// Lanceurs TOUJOURS niveau 20 (décision Philippe). Recalcul à chaud : abonné à Mutated du
// build (toute mutation, y compris les clics de sélection) + CollectionChanged du roster
// (repopulation après undo/restauration, purge sur suppression de ligne).
public class SpikeViewModel : ViewModelBase
{
    private const int AttackerLevel = 20;

    private readonly AppSettings _settings;
    private readonly Action _onMutated;

    public TeamBuildViewModel Build { get; }

    public SpikeViewModel(TeamBuildViewModel build, AppSettings settings)
    {
        Build = build;
        _settings = settings;
        foreach (var p in settings.SpikeTargets) Profiles.Add(p);
        _onMutated = ScheduleRecalculate;
        build.Mutated += _onMutated;
        build.SpikeMembers.CollectionChanged += OnRosterChanged;
        Recalculate();
    }

    private void OnRosterChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRecalculate();

    // Recalcul REPOSTÉ sur la boucle de messages, jamais immédiat. Raison : Recalculate reconstruit
    // Rows (Clear + Add) et détruit donc les conteneurs des lignes ; or Mutated part le plus souvent
    // d'un contrôle DE la ligne (ComboBox d'arme, de type de dégâts, de ticks, case à cocher…).
    // Recalculer sur-le-champ arrache le ComboBox à WPF au milieu de la validation de sa sélection
    // et le Selector lève « La collection SelectedItems ne peut être changée que dans les modes à
    // sélection multiple » (reproduit en isolation : le montage est fautif quelle que soit la façon
    // dont ItemsSource est fourni — binding de ligne comme x:Static ; seul le report corrige).
    // Le drapeau coalesce au passage les rafales de Mutated en un seul recalcul.
    private bool _recalcPending;

    private void ScheduleRecalculate()
    {
        if (Application.Current?.Dispatcher is not { } dispatcher) { Recalculate(); return; }
        if (_recalcPending) return;
        _recalcPending = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _recalcPending = false;
            if (!_detached) Recalculate();
        }));
    }

    // À appeler à la fermeture de la fenêtre : le build survit au VM.
    private bool _detached;

    public void Detach()
    {
        _detached = true;
        Build.Mutated -= _onMutated;
        Build.SpikeMembers.CollectionChanged -= OnRosterChanged;
    }

    // ── Cible ─────────────────────────────────────────────────────────────────

    private int _baseArmor = 60;
    private int _targetLevel = 20;
    private int _maxHealth = DefaultHealth(20);
    private int _currentHealth = DefaultHealth(20); // PV courants de la cible (Grenth's Balance)
    private int _bonusPhysical, _bonusElemental;
    private int _bonusSlashing, _bonusPiercing, _bonusBlunt;
    private int _bonusFire, _bonusCold, _bonusEarth, _bonusLightning;
    private Profession _targetPrimaryProfession = Profession.None;

    private static int DefaultHealth(int level) => 100 + 20 * (level - 1);

    public int BaseArmor { get => _baseArmor; set { if (SetField(ref _baseArmor, value)) Recalculate(); } }

    // PV max auto-suivis tant que l'utilisateur ne les a pas personnalisés (niveau 20 → 480).
    // Les PV courants (Grenth's Balance) suivent le max tant qu'ils ne s'en sont pas écartés.
    public int TargetLevel
    {
        get => _targetLevel;
        set
        {
            bool healthWasAuto = _maxHealth == DefaultHealth(_targetLevel);
            bool currentWasAuto = _currentHealth == _maxHealth;
            if (!SetField(ref _targetLevel, value)) return;
            if (healthWasAuto)
            {
                _maxHealth = DefaultHealth(value);
                OnPropertyChanged(nameof(MaxHealth));
                if (currentWasAuto)
                {
                    _currentHealth = _maxHealth;
                    OnPropertyChanged(nameof(TargetCurrentHealth));
                }
            }
            Recalculate();
        }
    }

    public int MaxHealth
    {
        get => _maxHealth;
        set
        {
            bool currentWasAuto = _currentHealth == _maxHealth;
            if (!SetField(ref _maxHealth, value)) return;
            if (currentWasAuto)
            {
                _currentHealth = _maxHealth;
                OnPropertyChanged(nameof(TargetCurrentHealth));
            }
            Recalculate();
        }
    }

    // PV COURANTS de la cible : seule entrée de Grenth's Balance côté cible (le spike suppose
    // sinon la cible à pleine vie). Suit MaxHealth tant qu'on ne l'a pas édité à part.
    public int TargetCurrentHealth
    {
        get => _currentHealth;
        set { if (SetField(ref _currentHealth, value)) Recalculate(); }
    }
    public int BonusPhysical { get => _bonusPhysical; set { if (SetField(ref _bonusPhysical, value)) Recalculate(); } }
    public int BonusElemental { get => _bonusElemental; set { if (SetField(ref _bonusElemental, value)) Recalculate(); } }
    public int BonusSlashing { get => _bonusSlashing; set { if (SetField(ref _bonusSlashing, value)) Recalculate(); } }
    public int BonusPiercing { get => _bonusPiercing; set { if (SetField(ref _bonusPiercing, value)) Recalculate(); } }
    public int BonusBlunt { get => _bonusBlunt; set { if (SetField(ref _bonusBlunt, value)) Recalculate(); } }
    public int BonusFire { get => _bonusFire; set { if (SetField(ref _bonusFire, value)) Recalculate(); } }
    public int BonusCold { get => _bonusCold; set { if (SetField(ref _bonusCold, value)) Recalculate(); } }
    public int BonusEarth { get => _bonusEarth; set { if (SetField(ref _bonusEarth, value)) Recalculate(); } }
    public int BonusLightning { get => _bonusLightning; set { if (SetField(ref _bonusLightning, value)) Recalculate(); } }

    // Profession PRIMAIRE de la cible : entrée de simulation des flux Amateur Hour / There Can Be
    // Only One (aucun autre calcul ne l'utilise). Non persistée (ni profil, ni .pn3) — paramètre de
    // session propre à la fenêtre, défaut None = « non précisée » (aucun bonus lié à la profession).
    public Profession TargetPrimaryProfession
    {
        get => _targetPrimaryProfession;
        set { if (SetField(ref _targetPrimaryProfession, value)) Recalculate(); }
    }

    // ── Options du total ──────────────────────────────────────────────────────

    private bool _withDeepWound, _withCrackedArmor, _allCrits;

    // Deep Wound = min(20 % PV max, 100) ajoutés au total (wiki/Deep_Wound).
    public bool WithDeepWound { get => _withDeepWound; set { if (SetField(ref _withDeepWound, value)) Recalculate(); } }
    // Cracked Armor = −20 AL plancher 60 (wiki/Cracked_Armor), avant pénétration.
    public bool WithCrackedArmor { get => _withCrackedArmor; set { if (SetField(ref _withCrackedArmor, value)) Recalculate(); } }
    // « Visez les yeux ! » : toutes les attaques d'arme au dégât critique.
    public bool AllCrits { get => _allCrits; set { if (SetField(ref _allCrits, value)) Recalculate(); } }

    // ── Profils de cible (persistés dans settings.json) ───────────────────────

    public ObservableCollection<SpikeTargetProfile> Profiles { get; } = new();

    private SpikeTargetProfile? _selectedProfile;
    public SpikeTargetProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { if (SetField(ref _selectedProfile, value) && value is { } p) ApplyProfile(p); }
    }

    private string _profileName = string.Empty;
    public string ProfileName { get => _profileName; set => SetField(ref _profileName, value); }

    private void ApplyProfile(SpikeTargetProfile p)
    {
        _baseArmor = p.BaseArmor;
        _targetLevel = p.Level;
        _maxHealth = p.MaxHealth;
        _currentHealth = p.MaxHealth; // profil chargé = cible à pleine vie (PV courants suivent)
        _bonusPhysical = p.BonusPhysical; _bonusElemental = p.BonusElemental;
        _bonusSlashing = p.BonusSlashing; _bonusPiercing = p.BonusPiercing; _bonusBlunt = p.BonusBlunt;
        _bonusFire = p.BonusFire; _bonusCold = p.BonusCold;
        _bonusEarth = p.BonusEarth; _bonusLightning = p.BonusLightning;
        ProfileName = p.Name;
        foreach (var prop in new[]
        {
            nameof(BaseArmor), nameof(TargetLevel), nameof(MaxHealth), nameof(TargetCurrentHealth),
            nameof(BonusPhysical), nameof(BonusElemental),
            nameof(BonusSlashing), nameof(BonusPiercing), nameof(BonusBlunt),
            nameof(BonusFire), nameof(BonusCold), nameof(BonusEarth), nameof(BonusLightning),
        })
            OnPropertyChanged(prop);
        Recalculate();
    }

    // Enregistre la cible courante sous ProfileName (remplace un profil homonyme).
    public void SaveProfile()
    {
        var name = ProfileName.Trim();
        if (name.Length == 0) return;
        var existing = Profiles.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var profile = existing ?? new SpikeTargetProfile();
        profile.Name = name;
        profile.BaseArmor = _baseArmor;
        profile.Level = _targetLevel;
        profile.MaxHealth = _maxHealth;
        profile.BonusPhysical = _bonusPhysical; profile.BonusElemental = _bonusElemental;
        profile.BonusSlashing = _bonusSlashing; profile.BonusPiercing = _bonusPiercing;
        profile.BonusBlunt = _bonusBlunt;
        profile.BonusFire = _bonusFire; profile.BonusCold = _bonusCold;
        profile.BonusEarth = _bonusEarth; profile.BonusLightning = _bonusLightning;
        if (existing is null) Profiles.Add(profile);
        PersistProfiles();
        _selectedProfile = profile;
        OnPropertyChanged(nameof(SelectedProfile));
    }

    public void DeleteProfile()
    {
        if (_selectedProfile is not { } p) return;
        Profiles.Remove(p);
        _selectedProfile = null;
        OnPropertyChanged(nameof(SelectedProfile));
        PersistProfiles();
    }

    private void PersistProfiles()
    {
        _settings.SpikeTargets = Profiles.ToList();
        _settings.Save();
    }

    // ── Résultats ─────────────────────────────────────────────────────────────

    public ObservableCollection<SpikeRowViewModel> Rows { get; } = new();

    private string _totalText = string.Empty;
    public string TotalText { get => _totalText; private set => SetField(ref _totalText, value); }

    private bool _hasRows;
    public bool HasRows { get => _hasRows; private set => SetField(ref _hasRows, value); }

    // Description du flux actif du build, pour le tooltip de la colonne « Bonus Flux ».
    public string FluxDescription => Build.ActiveFlux is { } f
        ? $"{FluxData.Get(f).DisplayName} — {FluxData.Get(f).DisplayDescription}"
        : L("Aucun flux actif.", "No active flux.");

    // Libellé bilingue construit côté VM (les notes/labels du spike sont bakés en chaîne).
    private static string L(string fr, string en) => AppLanguage.IsFr ? fr : en;

    private static string TypeLabel(string? damageType) => SkillDamage.DisplayType(damageType);

    // Note répétée sur chaque paquet qui contourne l'armure (6 emplacements) : un seul point de
    // traduction.
    private static string ArmorIgnoring => L(" (ignore l'armure)", " (armor ignoring)");

    // Nom affiché du buff, dans la langue courante (cf. MemberBuffs.Names). Repli sur le nom
    // anglais du descripteur si la copie équipée a disparu entre deux recalculs.
    private static string BuffName(MemberBuffs? mb, SpikeBuff buff) =>
        mb?.Names.GetValueOrDefault(buff)
        ?? SpikeWeaponBuffs.All.First(d => d.Buff == buff).CoreName;

    // Options du compteur « Procs » (0..25) : occurrences d'une attaque / déclenchements d'un rider.
    public static IReadOnlyList<int> ProcOptions { get; } = Enumerable.Range(0, 26).ToList();

    public sealed record WeaponTypeOption(string Label, string Value);

    // Liste PAR LIGNE : le jeu de types dépend de l'ARME de la ligne (WeaponStrike.DamageTypeChoices
    // — arc/javelot perforants, marteau contondant ou perforant…). Valeur vide = type natif de
    // l'arme. Recalculée à chaque construction de ligne, donc dans la langue affichée : les
    // libellés viennent de SkillDamage.DisplayType, déjà bilingue et seule source des noms de
    // types dans toute l'app — dupliquer une table ici la ferait diverger.
    // Lot 6d-1 : la première entrée ne dit plus « type natif de l'arme » mais « déduit » — depuis que la
    // chaîne du § 6.1 tranche, le défaut n'est le type natif que si RIEN n'a converti l'attaque.
    private static IReadOnlyList<WeaponTypeOption> TypeOptions(IEnumerable<string> values) =>
        new[] { new WeaponTypeOption(L("(auto / déduit)", "(auto / deduced)"), "") }
            .Concat(values.Select(v => new WeaponTypeOption(SkillDamage.DisplayType(v), v)))
            .ToList();

    // Choix manuel de l'arme d'une attaque libre non déductible (ComboBox de la ligne). Valeur =
    // nom de la maîtrise (clé de WeaponStrike.ByMasteryName) ; vide = à choisir.
    public sealed record WeaponOption(string Label, string Mastery);

    // Recalculé à chaque construction de ligne (langue affichée) : « (auto) » + noms d'armes
    // localisés, restreints à la catégorie du type d'attaque — pas d'arc ni de javelot sur une
    // attaque de mêlée. D'où une liste PAR LIGNE et non une liste statique unique.
    private static IReadOnlyList<WeaponOption> WeaponOptionsFor(Skill skill) =>
        new[] { new WeaponOption(L("(auto / déduite)", "(auto / deduced)"), "") }
            .Concat(WeaponStrike.ChoicesFor(skill).Select(w => new WeaponOption(w.DisplayName, w.Mastery)))
            .ToList();

    // Mod de PRÉFIXE physique de la ligne (ComboBox « Mod »). Valeur = clé SpikeWeaponMods ;
    // vide = aucun. Reconstruit par ligne comme les deux listes ci-dessus (langue affichée).
    public sealed record WeaponModOption(string Label, string Key);

    private static IReadOnlyList<WeaponModOption> ModOptions() =>
    [
        new(L("Aucun", "None"), string.Empty),
        new(L("de fractionnement", "Sundering"), SpikeWeaponMods.ToKey(SpikeWeaponMod.Sundering)),
        new(L("vampirique", "Vampiric"), SpikeWeaponMods.ToKey(SpikeWeaponMod.Vampiric)),
    ];

    // Choix de la profession primaire de la cible (ComboBox du panneau Cible). Le libellé suit la
    // langue courante (la fenêtre Spike est reconstruite au switch) ; « — » = non précisée (None).
    public sealed record TargetProfessionOption(Profession Profession)
    {
        public string Label => Profession == Profession.None
            ? (AppLanguage.IsFr ? "— (non précisée)" : "— (unspecified)")
            : Profession.DisplayName();
    }

    public static IReadOnlyList<TargetProfessionOption> TargetProfessionOptions { get; } =
        new[] { new TargetProfessionOption(Profession.None) }
            .Concat(Enum.GetValues<Profession>()
                .Where(p => p != Profession.None)
                .Select(p => new TargetProfessionOption(p)))
            .ToList();

    // Reconstruit toutes les lignes : mêmes briques que la tooltip (SkillDamage/WeaponStrike,
    // pénétration = max(description, Force), bonus « +X » absorbé dans la table d'arme, aucune
    // ligne « +X » séparée sur une attaque d'arme), plus l'AL PAR TYPE de la cible, le vol et
    // la perte de vie. Les valeurs fixes (armor-ignoring, vol, perte) donnent min = max.
    public void Recalculate()
    {
        OnPropertyChanged(nameof(FluxDescription)); // le flux peut avoir changé (Mutated)
        Rows.Clear();
        var target = new SpikeTarget(_baseArmor, _targetLevel, _maxHealth,
            _bonusPhysical, _bonusElemental, _bonusSlashing, _bonusPiercing, _bonusBlunt,
            _bonusFire, _bonusCold, _bonusEarth, _bonusLightning);
        int totalMin = 0, totalMax = 0, totalFluxMin = 0, totalFluxMax = 0;

        // Chain Combo : bonus cumulatif précalculé par slot (chaîne PAR PERSO le long de l'ordre de cast).
        var chainCombo = Build.ActiveFlux == Flux.ChainCombo
            ? ComputeChainComboPct(Build.SpikeMembers) : null;

        // Buffs d'arme (chantier 14) : synchronise l'offre d'icônes de chaque carte membre et
        // précalcule les effets ACTIFS (proposé ∩ coché) par membre du roster.
        var buffCtx = SyncWeaponBuffs();

        // Lot 6e : les effets à CHARGES de la carte du perso (« vos 5 prochaines attaques », « Ends
        // after 3 attacks »). Même règle que Q22 pour les buffs d'arme — les charges vont aux
        // PREMIÈRES attaques d'arme dans l'ordre de cast, et ce qui reste retombe sur les coups normaux.
        var chargeCtx = ComputeBoostCharges();

        // Valeurs de vol de vie (3 et/ou 5) déclarées par les mods vampiriques des lignes : elles
        // n'ajoutent rien à leur ligne, elles font apparaître les lignes ARTIFICIELLES globales
        // ajoutées tout à la fin (chantier 16).
        var vampiricSteals = new SortedSet<int>();

        foreach (var member in Build.SpikeMembers)
        foreach (var slot in member.SkillSlots)
        {
            if (!slot.IsSpikeSelected || slot.Skill is not { } skill) continue;

            string resolved = member.ResolveDescription(skill);
            // Mode spike : lit aussi les paquets « X more damage » / « increases … by +X »
            // (Order of Pain, Strength of Honor, Winnowing) que la tooltip ignore.
            var analysis = SkillDamage.Analyze(resolved, skill.Name, includeMoreDamage: true);

            // Mind Wrack (core) : 2 déclencheurs → 2 lignes. Le 1er paquet = dégât PAR POINT d'énergie
            // perdu (procs = nb de points, variable → contrôle) ; le 2e = dégât à énergie 0 qui met
            // fin au hex, donc ne peut proc qu'UNE fois → pas de contrôle (décision Philippe).
            if (SpikeProcSkills.IsMindWrackDual(skill.Name))
            {
                // Vengeance s'applique aussi ici (« tout, sorts inclus » — décision Philippe), et
                // depuis le lot 6e les multiplicateurs de la carte avec elle : « Par le marteau
                // d'Ural ! » et l'Affinité vitale portent sur TOUS les dégâts du perso, sorts compris.
                // ⚠ Une arme nulle suffit : le périmètre « tous les dégâts » ne la regarde pas.
                bool mwVengeance = buffCtx.GetValueOrDefault(member) is { Vengeance: true };
                double mwFactor = (mwVengeance ? SpikeWeaponBuffs.VengeanceMultiplier : 1.0)
                    * member.SpikeDamageBoostsFor(skill, WeaponKind.None,
                        id => !SpikeBoostCoverage.IsCharged(id)).Multiplier;
                var mw = analysis.Rows.Where(r => r.Kind == SkillDamage.RowKind.Damage).ToList();
                if (mw.Count > 0)
                {
                    var t = BuildMindWrackRow(member, skill, slot, mw[0].Value,
                        Math.Max(0, slot.SpikeProcs), hasProcs: true,
                        L("par point d'énergie perdu", "per point of energy lost"),
                        mwVengeance, mwFactor);
                    Rows.Add(t.Row); totalMin += t.Min; totalMax += t.Max;
                    totalFluxMin += t.FluxMin; totalFluxMax += t.FluxMax;
                }
                if (mw.Count > 1)
                {
                    var t = BuildMindWrackRow(member, skill, slot, mw[1].Value,
                        1, hasProcs: false, L("à énergie 0", "at 0 energy"), mwVengeance, mwFactor);
                    Rows.Add(t.Row); totalMin += t.Min; totalMax += t.Max;
                    totalFluxMin += t.FluxMin; totalFluxMax += t.FluxMax;
                }
                continue;
            }

            // Grenth's Balance (chantier 15) : « No Attribute », aucun paquet fixe. Dégât calculé
            // des PV saisis (formule 2 gobelets, SpikeGrenth). Perte de PV → ignore l'armure, hors
            // flux (assiette de dégât nulle, comme le vol/la perte de vie).
            if (SpikeGrenth.IsBalance(skill.Name))
            {
                int dmg = SpikeGrenth.BalanceDamage(
                    slot.SpikeCasterCurrentHp, slot.SpikeCasterMaxHp, _currentHealth);
                Rows.Add(new SpikeRowViewModel
                {
                    IconPath = skill.IconPath,
                    SkillName = skill.DisplayName,
                    CharacterName = member.Name,
                    Detail = $"{L("perte de vie", "health loss")} {dmg}"
                           + $" = ({_currentHealth} − {slot.SpikeCasterCurrentHp}) / 2"
                           + (dmg > 0 && dmg == slot.SpikeCasterMaxHp - slot.SpikeCasterCurrentHp
                               ? L($" (plafonné aux PV manquants du lanceur : {dmg})",
                                   $" (capped at caster's missing health: {dmg})") : "")
                           + ArmorIgnoring,
                    RangeText = dmg.ToString(),
                    FluxBonusText = "—",
                    Slot = slot,
                    HasCasterHp = true,
                });
                totalMin += dmg; totalMax += dmg;
                continue;
            }

            // Grenth's Aura (chantier 15) : 2 vols de vie — par coup de faux (compteur Procs) +
            // effet initial de zone (one-time). Dual comme Mind Wrack (le proc multiplierait
            // sinon les deux). row[0] = par coup, row[1] = initial (ordre du texte, prouvé harnais).
            if (SpikeGrenth.IsAuraDual(skill.Name))
            {
                var steals = analysis.Rows.Where(r => r.Kind == SkillDamage.RowKind.LifeSteal).ToList();
                if (steals.Count > 0)
                {
                    int n = Math.Max(0, slot.SpikeProcs);
                    int dmg = steals[0].Value * n;
                    Rows.Add(new SpikeRowViewModel
                    {
                        IconPath = skill.IconPath, SkillName = skill.DisplayName, CharacterName = member.Name,
                        Detail = $"{L("vie volée", "life stolen")} {steals[0].Value}"
                               + (n != 1 ? L($" × {n} coups de faux", $" × {n} scythe hits") : "")
                               + ArmorIgnoring,
                        RangeText = dmg.ToString(), FluxBonusText = "—", Slot = slot, HasProcs = true,
                    });
                    totalMin += dmg; totalMax += dmg;
                }
                if (steals.Count > 1)
                {
                    Rows.Add(new SpikeRowViewModel
                    {
                        IconPath = skill.IconPath, SkillName = skill.DisplayName, CharacterName = member.Name,
                        Detail = L($"vie volée {steals[1].Value} (effet initial de zone, ignore l'armure)",
                                   $"life stolen {steals[1].Value} (initial area effect, armor ignoring)"),
                        RangeText = steals[1].Value.ToString(), FluxBonusText = "—", Slot = slot,
                    });
                    totalMin += steals[1].Value; totalMax += steals[1].Value;
                }
                continue;
            }

            var weapon = WeaponStrike.For(skill);
            var mods = WeaponStrike.ModsFor(skill.Name, resolved);
            bool isWeaponAttack = WeaponStrike.IsWeaponAttack(skill);
            // Toute compétence de TYPE attaque (les sous-types finissant par « Attack » : Melee/Axe/
            // Hammer/Sword/Dagger/Lead/Off-Hand/Dual/Scythe/Pet/Ranged/Bow/Spear…) est comptée 1 proc
            // max — ses coups intrinsèques suffisent (décision Philippe). ≠ isWeaponAttack qui, lui,
            // exclut Pet Attack (pas une frappe d'arme) pour la table de dégâts d'arme.
            bool isAttackType = skill.SkillType.EndsWith("Attack", StringComparison.Ordinal);
            int? masteryRank = slot.WeaponMasteryRank;
            // Buffs d'arme actifs du membre (chantier 14). Pénétration (wiki/Armor_penetration) :
            // Sundering Weapon (10 %, les 3 premières attaques) et DWG (20 %/10 % PvP, skills
            // Ritualist du porteur) sont des AP de BASE → rejoignent le pool MAX existant (AP
            // innée de la description, rang de Force) ; Judge's Insight (« adds +20% ») est un
            // BONUS → cumulé PAR-DESSUS le max de base.
            var mb = buffCtx.GetValueOrDefault(member);
            // Lot 6d-1, Q13 (27/09/2026) : la Clairvoyance du juge a DEUX interrupteurs — la case de cette
            // fenêtre (chantier 14) et l'icône de la carte du perso (lot 6b). Allumé d'un côté OU de
            // l'autre suffit, sinon le même effet aurait deux vérités et l'on verrait le sacré venir de
            // l'icône pendant que les 20 % de pénétration attendraient la case.
            bool judges = (mb is { Judges: true } || member.JudgesInsightLit) && isWeaponAttack;
            bool sundering = mb is not null && mb.SunderingSlots.Contains(slot);
            bool dwg = mb is { DwgPen: > 0 } && skill.Profession == Profession.Ritualist;
            int pen = Math.Max(analysis.ArmorPenetration, slot.StrengthRank ?? 0);
            if (sundering) pen = Math.Max(pen, SpikeWeaponBuffs.SunderingBasePen);
            if (dwg) pen = Math.Max(pen, mb!.DwgPen);
            if (judges) pen += SpikeWeaponBuffs.JudgesBonusPen;

            // Attaque d'arme LIBRE (non liée à une maîtrise : attaques de Force comme Bull's Strike,
            // Ranged Attack…) : déduire l'arme des autres attaques de la barre du perso ; la maîtrise
            // qui scale les dégâts devient celle de l'arme déduite (décision Philippe). Le « req. »
            // ne concerne QUE les armes/boucliers, pas les compétences → plus de « Req. non atteint ».
            // Attaque d'arme LIBRE (le TYPE n'impose pas l'arme : Melee Attack de Force comme
            // Bull's Strike, de Maîtrise de la faux comme Reap Impurities, Ranged Attack…) : le
            // choix de l'arme est TOUJOURS proposé sur la ligne (edge cases — décision Philippe),
            // restreint aux armes de la catégorie. Défaut = l'arme de l'attribut quand celui-ci
            // EST une maîtrise (la faux des attaques de mêlée Derviche : les chiffres ne bougent
            // donc pas, on ne fait qu'ouvrir le choix), sinon l'arme déduite des autres attaques
            // de la barre. Choix manuel prioritaire ; la maîtrise qui scale les dégâts d'ARME est
            // celle de l'arme retenue — le « +X » de la skill, lui, reste sur son propre attribut.
            var choices = WeaponStrike.ChoicesFor(skill);
            bool weaponDeduced = false, canChooseWeapon = false;
            if (WeaponStrike.IsFreeWeaponAttack(skill) && !mods.NoWeaponDamage)
            {
                canChooseWeapon = true;
                // Un choix persisté hors catégorie (.pn3 écrit avant ce filtrage : arc sur une
                // attaque de mêlée) est ignoré — on retombe sur le défaut.
                var picked = WeaponStrike.ByMasteryName(slot.SpikeWeaponKind);
                var chosen = picked is not null && choices.Contains(picked) ? picked : weapon;
                if (chosen is null && DeduceWeapon(member, choices) is { } deduced)
                {
                    chosen = deduced;
                    weaponDeduced = true;
                }
                if (chosen is not null)
                {
                    weapon = chosen;
                    masteryRank = WeaponStrike.StrikeRank(chosen, member.AttributeLevel);
                }
            }
            bool weaponTable = weapon is not null && masteryRank is not null && !mods.NoWeaponDamage;

            // Mods d'arme PHYSIQUES de la ligne (chantier 16) — évalués une fois l'arme arrêtée,
            // choix manuel compris. Le mod occupe l'emplacement de PRÉFIXE : il n'est proposé que
            // tant que le type de dégâts CHOISI n'est pas élémentaire (les deux se disputent la
            // place), et jamais sur une arme de lanceur. La règle lit le type choisi et non le type
            // effectif : Judge's Insight convertit en sacré mais ne consomme pas le préfixe (buff,
            // pas mod), les deux se cumulent donc.
            // Lot 6d-1, Q15 : le mod élémentaire du set d'armes ACTIF occupe lui aussi le préfixe, et
            // l'application le VOIT maintenant. La liste se grise donc toute seule dès qu'il est là,
            // sans attendre qu'on ait choisi un type élémentaire à la main sur la ligne.
            bool canChooseMod = isWeaponAttack && weapon is { IsCaster: false }
                                && !WeaponStrike.IsElementalType(slot.SpikeWeaponDamageType)
                                && !member.ElementalModAppliesTo(skill);
            var weaponMod = canChooseMod ? SpikeWeaponMods.FromKey(slot.SpikeWeaponModKey)
                                         : SpikeWeaponMod.None;
            // Les deux pénétrations sont de catégorie BONUS (wiki/Armor_penetration) : elles
            // s'AJOUTENT par-dessus le max des bases, comme Judge's Insight juste au-dessus.
            bool sunderingMod = weaponMod == SpikeWeaponMod.Sundering && slot.SpikeSunderingProc;
            bool isBowRow = isWeaponAttack && weapon is { } bw && WeaponStrike.IsBow(bw);
            bool hornbow = isBowRow && slot.SpikeHornbow;
            if (sunderingMod) pen += SpikeWeaponMods.SunderingBonusPen;
            if (hornbow) pen += SpikeWeaponMods.HornbowBonusPen;

            // ── Lot 6e : les effets de la carte que la fenêtre ne comptait pas ────────────────
            // Le périmètre se juge sur l'arme de CETTE ligne (forçage manuel compris) — tranché par
            // Philippe le 27/09/2026 : la Méthode de l'Assassin (dagues) et celle du Maître (hors
            // dagues) sont des complémentaires exacts, c'est la seule façon de ne jamais appliquer
            // la mauvaise. Les effets à charges ne valent que sur les lignes qui en consomment une.
            var bc = chargeCtx.GetValueOrDefault(member);
            var rowKind = weapon is { } rowWeapon ? WeaponStrike.KindOf(rowWeapon) : WeaponKind.None;
            // Tout ce qui est permanent compte ; un effet à charges ne compte que si CETTE ligne en
            // consomme une (les N premières attaques d'arme, dans l'ordre de cast).
            var boosts = member.SpikeDamageBoostsFor(skill, rowKind, id =>
                !SpikeBoostCoverage.IsCharged(id)
                || (bc is not null && bc.Slots.TryGetValue(id, out var lit) && lit.Contains(slot)));
            // Pénétration : de BASE en MAX avec le pool existant (jamais cumulée), en BONUS par-dessus.
            pen = Math.Max(pen, boosts.BasePenetration);
            pen += boosts.BonusPenetration;
            if (weaponMod == SpikeWeaponMod.Vampiric)
                vampiricSteals.Add(SpikeWeaponMods.VampiricSteal(weapon!));

            var damage = analysis.Rows.Where(r => r.Kind == SkillDamage.RowKind.Damage).ToList();
            var respecting = damage.Where(r => !r.IgnoresArmor).ToList();
            var ignoring = damage.Where(r => r.IgnoresArmor).ToList();
            // Le « +X » NON conditionnel d'une attaque d'arme est absorbé dans la table d'arme.
            // Le « +X more damage if [état] » CONDITIONNEL, lui, reste dans `ignoring` : il est
            // posé au-dessus du coup d'arme et piloté par la case (sinon il serait jeté ici, comme
            // avant — sous-compte des attaques conditionnelles type Final Thrust).
            SkillDamage.Row? thresholdRow = null; // paquet à SEUIL « X for each [ressource] (max Y) »
            var bonusRow = ignoring.FirstOrDefault(r => r.IsBonus && !r.Conditional);
            int bonus = 0;
            if (isWeaponAttack && bonusRow is not null)
            {
                // Attaque d'arme à SEUIL (Symbolic Strike +12/signet, Blades of Steel +14/attaque) :
                // le « +X » plié dans l'arme vaut min(parUnité × compte, plafond) au lieu de parUnité.
                if (bonusRow.IsThreshold)
                {
                    bonus = ThresholdListed(bonusRow, ThresholdCount(slot, bonusRow));
                    thresholdRow = bonusRow;
                }
                else bonus = bonusRow.Value;
                ignoring.RemoveAll(r => r.IsBonus && !r.Conditional);
            }

            int min = 0, max = 0;
            // Dégâts seuls (arme/sort/armor-ignoring) hors vol et perte de vie : assiette du %
            // de flux (décision Philippe : « deal X% additional damage »).
            int dmgMin = 0, dmgMax = 0;
            var parts = new List<string>();
            bool hasConditional = false;  // au moins une part « X more damage if [état] » détectée
            string? conditionText = null; // clause de la 1re part conditionnelle (tooltip de la case)

            if (weaponTable)
            {
                // Type de dégât de la ligne — DÉDUIT depuis le lot 6d-1 (§ 6.1 du plan) : la chaîne de
                // conversion du perso décide, et la liste déroulante ne sert plus qu'à FORCER (Q6/Q15).
                // Ce qui entre dans la chaîne : les 13 convertisseurs personnels allumés, le mod
                // élémentaire du set actif, la Clairvoyance du juge (les deux interrupteurs, Q13), Grand
                // brasier, Brasier, et Briseur de pierre qui garde le dernier mot. Ce qui en sort ensuite :
                // Hiver, qui passe l'élémentaire REÇU en froid — dans l'infobulle ce n'était qu'une
                // étiquette, ici la cible a une armure PAR TYPE et le chiffre bouge vraiment (Q16).
                // Un type persisté hors catalogue de l'arme (.pn3 écrit avant ce filtrage, ou arme changée
                // sur une attaque libre : « contondant » gardé en passant de l'épée à la hache) est
                // ignoré — on retombe sur la déduction, comme pour SpikeWeaponKind.
                string? picked = WeaponStrike.DamageTypeChoices(weapon!).Contains(slot.SpikeWeaponDamageType)
                    ? slot.SpikeWeaponDamageType : null;
                var lineType = member.SpikeDamageTypeFor(skill, picked, weapon!.DamageType,
                                                         mb is { Judges: true });
                string? weaponType = lineType.Received;
                int al = target.EffectiveArmor(weaponType, _withCrackedArmor);
                int rank = masteryRank!.Value;
                // Great Dwarf Weapon : « +X weapon damage » = vrai dégât d'ARME, ajouté AVANT
                // customisation/critique/armure (≠ un « +X damage » plat qui ignore l'armure).
                var w = mb is { GdwBonus: > 0 }
                    ? weapon! with { Min = weapon.Min + mb.GdwBonus, Max = weapon.Max + mb.GdwBonus }
                    : weapon!;
                // ⚠ Le multiplicateur d'ARME (Rafale ×0,75, lot 6e) entre ICI et nulle part ailleurs :
                // il ne touche ni le « +X » absorbé de la compétence, ni les paquets, ni les bonus des
                // autres effets — même canal que dans l'infobulle, qui le compose avec mods.Multiplier.
                double weaponMult = mods.Multiplier * boosts.WeaponMultiplier;
                int wMin, wMax;
                if (_allCrits || mods.AlwaysCritical)
                    wMin = wMax = WeaponStrike.CriticalAt(w, rank, al, pen,
                        weaponMult, AttackerLevel) + bonus;
                else
                {
                    wMin = WeaponStrike.DamageAt(w.Min, rank, al, pen,
                        weaponMult, AttackerLevel) + bonus;
                    wMax = WeaponStrike.DamageAt(w.Max, rank, al, pen,
                        weaponMult, AttackerLevel) + bonus;
                }
                min += wMin; max += wMax; dmgMin += wMin; dmgMax += wMax;
                string range = wMin == wMax ? wMax.ToString() : $"{wMin}–{wMax}";
                string label = weaponType == weapon!.DamageType
                    ? weapon.DisplayName : $"{weapon.DisplayName} ({TypeLabel(weaponType)})";
                parts.Add(bonus > 0 ? $"{label} {range} {L($"(+{bonus} compris)", $"(+{bonus} included)")}" : $"{label} {range}");
                // Taux de critique. ⚠ Lot 6e : les effets allumés (Œil critique, « Craignez-moi ! »,
                // les deux Méthodes, Siphon de force, « Visez les yeux ! ») entrent par le paramètre
                // `boostPercent` de CriticalChance, qui porte la règle MULTIPLICATIVE validée en jeu le
                // 27/09/2026 (Q14). Ils ne déplacent AUCUN chiffre de la fourchette — elle est calculée
                // en coup non critique —, seulement ce taux affiché : c'est tout ce qu'un taux peut
                // faire dans une fenêtre qui annonce un min–max et non une espérance.
                parts.Add(mods.AlwaysCritical ? L("critique forcé", "forced critical")
                    : $"{L("crit", "crit")} {100 * WeaponStrike.CriticalChance(rank, AttackerLevel, _targetLevel, slot.CriticalStrikesRank ?? 0, boosts.CriticalPercent):0} %");
                if (weaponDeduced) parts.Add(L("arme déduite", "deduced weapon"));
                // Un effet a changé le type : la ligne le DIT, sinon une colonne d'armure inattendue
                // (Briseur de pierre en terre, Hiver en froid) passerait pour un bug de calcul.
                if (lineType.Converted) parts.Add(L("type converti", "converted type"));
                if (thresholdRow is { } tr)
                    parts.Add($"{L("seuil", "threshold")} +{tr.Value} × {ThresholdCount(slot, tr)}"
                              + ThresholdCapNote(tr, ThresholdCount(slot, tr)));
            }
            else if (isWeaponAttack && (bonus > 0 || thresholdRow is not null))
            {
                // Attaque d'arme libre dont AUCUNE arme n'a pu être établie : ni choix manuel sur la
                // ligne, ni déduction possible (aucune attaque liée à une maîtrise dans la barre).
                // Le « +X » est compté à plat, coup d'arme non calculé. Ne couvre plus le perso qui
                // n'a pas la maîtrise de l'arme retenue : celui-là frappe désormais à rang 0.
                min += bonus; max += bonus; dmgMin += bonus; dmgMax += bonus;
                string undetermined = L(" (arme indéterminée)", " (weapon undetermined)");
                parts.Add(thresholdRow is { } utr
                    ? $"{L("seuil", "threshold")} +{utr.Value} × {ThresholdCount(slot, utr)} = {bonus}{undetermined}"
                    : $"+{bonus}{undetermined}");
            }

            // Compteur de ticks (skills à dégâts périodiques « each second ») : chaque paquet
            // périodique est répété SpikeTicks fois (borné à son propre max). Savannah Heat est
            // cumulative : le tick i vaut i × la valeur, l'armure et la troncature s'appliquant
            // tick par tick. maxTicks alimente le ComboBox de la ligne (1 = pas de compteur).
            // Compteur de PROJECTILES (Stone Daggers 2, Dancing Daggers 3) : même répétition du
            // paquet, mais compteur et libellé séparés — la sentinelle 0 du slot vaut « tous
            // touchent » (défaut optimiste), un choix explicite le réduit.
            int maxTicks = 1, maxProjectiles = 1;
            int TicksOf(SkillDamage.Row r)
            {
                if (r.TicksAreProjectiles)
                {
                    maxProjectiles = Math.Max(maxProjectiles, r.MaxTicks);
                    return slot.SpikeProjectiles <= 0 ? r.MaxTicks
                        : Math.Clamp(slot.SpikeProjectiles, 1, r.MaxTicks);
                }
                maxTicks = Math.Max(maxTicks, r.MaxTicks);
                return Math.Clamp(slot.SpikeTicks, 1, r.MaxTicks);
            }

            // Suffixe du détail : « × N ticks » ou « × N projectiles » selon la nature du paquet
            // répété (les deux mots sont identiques en FR et en EN).
            static string TimesLabel(SkillDamage.Row r, int t)
                => r.TicksAreProjectiles ? $" × {t} projectiles" : $" × {t} ticks";

            foreach (var r in respecting)
            {
                // Lot 6d-1 : Briseur de pierre force la terre sur TOUT ce que le perso infliger, sorts
                // compris, et Hiver passe l'élémentaire reçu en froid. Les deux déplacent donc aussi la
                // colonne d'armure des paquets qui ne sont pas un coup d'arme.
                string? rType = member.SpikePacketTypeFor(r.DamageType).Received;
                int al = target.EffectiveArmor(rType, _withCrackedArmor);
                if (r.IsThreshold)
                {
                    // Seuil armor-RESPECTING (ex. Doom 50 foudre/rituel, hors v1) : le plafond porte
                    // sur les dégâts LISTÉS (AL 60), l'armure/pénétration s'appliquent au total.
                    int count = ThresholdCount(slot, r);
                    int listed = ThresholdListed(r, count);
                    int dmg = SkillDamage.DamageAt(listed, al, pen, AttackerLevel);
                    min += dmg; max += dmg; dmgMin += dmg; dmgMax += dmg;
                    thresholdRow = r;
                    parts.Add($"{TypeLabel(rType)} {r.Value} × {count} = {listed}{ThresholdCapNote(r, count)} → {dmg}");
                    continue;
                }
                int t = TicksOf(r);
                if (r.TicksCumulative)
                {
                    var ticksDmg = new List<int>();
                    for (int i = 1; i <= t; i++)
                        ticksDmg.Add(SkillDamage.DamageAt(r.Value * i, al, pen, AttackerLevel));
                    int sum = ticksDmg.Sum();
                    min += sum; max += sum; dmgMin += sum; dmgMax += sum;
                    parts.Add(t > 1
                        ? $"{TypeLabel(rType)} {string.Join("+", ticksDmg)} "
                          + L("(ticks cumulatifs)", "(cumulative ticks)")
                        : $"{TypeLabel(rType)} {ticksDmg[0]}");
                }
                else
                {
                    int dmg = SkillDamage.DamageAt(r.Value, al, pen, AttackerLevel);
                    min += dmg * t; max += dmg * t; dmgMin += dmg * t; dmgMax += dmg * t;
                    parts.Add(t > 1 ? $"{TypeLabel(rType)} {dmg}{TimesLabel(r, t)}"
                                    : $"{TypeLabel(rType)} {dmg}");
                }
            }
            foreach (var r in ignoring)
            {
                // Paquet à SEUIL (« X for each [ressource] (maximum Y) » : Signet of Deadly
                // Corruption, Mystic Wrath, Aneurysm, part additionnelle de Signet of Rage) :
                // par-unité × compte choisi (défaut = max optimiste), plafonné au maximum annoncé.
                if (r.IsThreshold)
                {
                    int count = ThresholdCount(slot, r);
                    int listed = ThresholdListed(r, count);
                    min += listed; max += listed; dmgMin += listed; dmgMax += listed;
                    thresholdRow = r;
                    parts.Add($"{(r.IsBonus ? "+" : "")}{r.Value} × {count} = {listed}"
                              + ThresholdCapNote(r, count) + ArmorIgnoring);
                    continue;
                }
                // Part conditionnelle (« X more damage if [état] ») : comptée par défaut, exclue si
                // la case est décochée (slot.SpikeConditional). Non périodique → pas de ticks.
                if (r.Conditional)
                {
                    hasConditional = true;
                    conditionText ??= r.Condition;
                    string conditional = L("conditionnel", "conditional");
                    if (!slot.SpikeConditional)
                    {
                        parts.Add($"+{r.Value} {conditional} {L("(exclu)", "(excluded)")}");
                        continue;
                    }
                    min += r.Value; max += r.Value; dmgMin += r.Value; dmgMax += r.Value;
                    parts.Add($"+{r.Value} {conditional}");
                    continue;
                }
                int t = TicksOf(r);
                int total = r.TicksCumulative ? r.Value * t * (t + 1) / 2 : r.Value * t;
                min += total; max += total; dmgMin += total; dmgMax += total;
                parts.Add(t > 1 ? $"{r.Value}{TimesLabel(r, t)}{ArmorIgnoring}"
                                : $"{(r.IsBonus ? "+" : "")}{r.Value}{ArmorIgnoring}");
            }
            foreach (var r in analysis.Rows.Where(r => r.Kind == SkillDamage.RowKind.LifeSteal))
            {
                int t = TicksOf(r);
                min += r.Value * t; max += r.Value * t;
                string stolen = L("vie volée", "life stolen");
                parts.Add(t > 1 ? $"{stolen} {r.Value} × {t} ticks" : $"{stolen} {r.Value}");
            }
            foreach (var r in analysis.Rows.Where(r => r.Kind == SkillDamage.RowKind.HealthLoss))
            {
                int t = TicksOf(r);
                min += r.Value * t; max += r.Value * t;
                string loss = L("perte de vie", "health loss");
                parts.Add(t > 1 ? $"{loss} {r.Value} × {t} ticks" : $"{loss} {r.Value}");
            }

            // Coups intrinsèques (multi-frappe, table) : multiplient TOUTE la contribution (arme +
            // bonus + vol/perte — décision Philippe). Procs = déclenchements d'un RIDER (conjuration,
            // ordre, hex à dégât…) sur la séquence d'attaques ; réservé aux riders de la liste blanche
            // qui ne sont PAS de type attaque. Une compétence de type attaque est comptée 1 proc max —
            // ses coups suffisent ; un rider périodique compte via Ticks (pas Procs) ; un sort direct
            // (hors liste blanche) reste à ×1.
            // Buffs d'arme PAR COUP, ajoutés AVANT le ×coups (double frappe = 2 éclats/bonus) :
            // l'éclat de Splinter Weapon (4 premières attaques, sans type → ignore l'armure ;
            // cible unique : 1 éclat par coup sur la cible) et le +X de Brutal Weapon.
            if (isWeaponAttack && mb is not null)
            {
                if (mb.SplinterSlots.Contains(slot) && mb.SplinterBonus > 0)
                {
                    min += mb.SplinterBonus; max += mb.SplinterBonus;
                    dmgMin += mb.SplinterBonus; dmgMax += mb.SplinterBonus;
                    parts.Add($"+{mb.SplinterBonus} {BuffName(mb, SpikeBuff.SplinterWeapon)}{ArmorIgnoring}");
                }
                if (mb.BrutalBonus > 0)
                {
                    min += mb.BrutalBonus; max += mb.BrutalBonus;
                    dmgMin += mb.BrutalBonus; dmgMax += mb.BrutalBonus;
                    parts.Add($"+{mb.BrutalBonus} ({BuffName(mb, SpikeBuff.BrutalWeapon)})");
                }
            }

            // Lot 6e — les paquets et le vol de vie des effets de la carte, ajoutés AVANT le ×coups
            // comme ceux des buffs d'arme juste au-dessus : une double frappe reçoit deux fois le bonus,
            // puisque l'effet parle de « vos attaques » et non de la compétence.
            if (boosts.Packets is { Count: > 0 })
            {
                int bMin = 0, bMax = 0;
                foreach (var p in boosts.Packets)
                {
                    // Un « +X » ignore l'armure (règle maison du chantier), un paquet TYPÉ sans « + » la
                    // subit et passe donc par la formule, contre l'AL de son propre type.
                    int v = p.IgnoresArmor ? p.Value
                        : SkillDamage.DamageAt(p.Value,
                            target.EffectiveArmor(p.DamageType, _withCrackedArmor), pen, AttackerLevel);
                    bMin += v; bMax += v;
                }
                min += bMin; max += bMax; dmgMin += bMin; dmgMax += bMax;
            }
            // Vol de vie CONFÉRÉ par un effet (Arme du tourment, Aura de sangsue de l'esprit) : il entre
            // dans le total mais PAS dans l'assiette des dégâts — ni multiplicateur, ni flux, ni armure.
            // C'est la règle du chantier depuis toujours ; le vol de vie n'est pas un dégât.
            if (boosts.LifeSteal > 0) { min += boosts.LifeSteal; max += boosts.LifeSteal; }
            foreach (var s in boosts.Sources ?? [])
                parts.Add(BoostSourcePart(s));

            int coups = SpikeMultiHit.Hits(skill);
            bool showProcs = !isAttackType && SpikeProcSkills.IsProcCapable(skill.Name) && maxTicks <= 1;
            int procs = showProcs ? Math.Max(0, slot.SpikeProcs) : 1;
            int mult = coups * procs;
            if (coups > 1) parts.Add($"{L("coup", "hit")} ×{coups}");
            if (mult != 1) { min *= mult; max *= mult; dmgMin *= mult; dmgMax *= mult; }

            // Buffs d'arme : notes des effets déjà inclus dans les chiffres (type/pénétration),
            // puis parts additives. Anthem of Envy s'ajoute APRÈS le ×coups (le +X s'applique une
            // fois par usage du premier attack skill) ; Vengeance multiplie l'assiette DÉGÂTS
            // seule (vol/perte de vie exclus), le total suit du même delta — appliquée en dernier,
            // elle couvre donc aussi le +X d'Anthem (dégâts infligés par l'allié sous Vengeance).
            if (judges && weaponTable)
                parts.Add($"{BuffName(mb, SpikeBuff.JudgesInsight)} {L("(sacré, +20 % pén.)", "(holy, +20% pen.)")}");
            if (sundering && weaponTable)
                parts.Add($"{BuffName(mb, SpikeBuff.SunderingWeapon)} {L("(10 % pén.)", "(10% pen.)")}");
            if (dwg && respecting.Count > 0)
                parts.Add($"{BuffName(mb, SpikeBuff.DestructiveWasGlaive)} {L($"({mb!.DwgPen} % pén.)", $"({mb!.DwgPen}% pen.)")}");
            // Notes des mods de la ligne : seulement quand la pénétration porte sur quelque chose
            // (coup d'arme ou paquet soumis à l'armure), comme la note de DWG juste au-dessus.
            bool penMatters = weaponTable || respecting.Count > 0;
            if (sunderingMod && penMatters)
                parts.Add(L($"de fractionnement (+{SpikeWeaponMods.SunderingBonusPen} % pén.)",
                            $"Sundering (+{SpikeWeaponMods.SunderingBonusPen}% pen.)"));
            if (hornbow && penMatters)
                parts.Add(L($"arc corne (+{SpikeWeaponMods.HornbowBonusPen} % pén.)",
                            $"hornbow (+{SpikeWeaponMods.HornbowBonusPen}% pen.)"));
            if (mb is { GdwBonus: > 0 } && weaponTable)
                parts.Add($"{BuffName(mb, SpikeBuff.GreatDwarfWeapon)} "
                          + L($"(+{mb.GdwBonus} d'arme compris)", $"(+{mb.GdwBonus} weapon damage included)"));
            if (mb is not null && slot == mb.AnthemSlot && mb.AnthemBonus > 0)
            {
                min += mb.AnthemBonus; max += mb.AnthemBonus;
                dmgMin += mb.AnthemBonus; dmgMax += mb.AnthemBonus;
                parts.Add($"+{mb.AnthemBonus} ({BuffName(mb, SpikeBuff.AnthemOfEnvy)}, "
                          + L("cible > 50 %)", "target > 50%)"));
            }
            if (mb is not null && slot == mb.FtwSlot && mb.FtwBonus > 0)
            {
                min += mb.FtwBonus; max += mb.FtwBonus;
                dmgMin += mb.FtwBonus; dmgMax += mb.FtwBonus;
                parts.Add($"+{mb.FtwBonus} ({BuffName(mb, SpikeBuff.FindTheirWeakness)}, "
                          + $"{GwConditionData.DisplayName("Deep Wound")} → "
                          + L("toggle cible)", "target toggle)"));
            }
            // Multiplicateurs de DÉGÂTS, composés en un seul facteur : la case Vengeance de la fenêtre
            // (chantier 14) et ceux que le lot 6e apporte (Affinité vitale ×0,70, « Par le marteau
            // d'Ural ! » ×1,25…1,33). ⚠ Les composer AVANT de multiplier, et non les appliquer l'un
            // après l'autre : deux arrondis successifs ne donnent pas le même nombre qu'un seul.
            double damageFactor = (mb is { Vengeance: true } ? SpikeWeaponBuffs.VengeanceMultiplier : 1.0)
                                  * boosts.Multiplier;
            if (Math.Abs(damageFactor - 1.0) > 0.0001 && dmgMax > 0)
            {
                int vMin = (int)(dmgMin * damageFactor);
                int vMax = (int)(dmgMax * damageFactor);
                min += vMin - dmgMin; max += vMax - dmgMax;
                dmgMin = vMin; dmgMax = vMax;
                // « Vengeance » est identique en FR et EN (vérifié DB) : seule la virgule décimale change.
                if (mb is { Vengeance: true }) parts.Add(L("Vengeance (×1,25)", "Vengeance (×1.25)"));
            }

            int chainPct = chainCombo != null && chainCombo.TryGetValue(slot, out var cp) ? cp : 0;
            var (fluxMin, fluxMax) = FluxDamageBonus(
                Build.ActiveFlux, member, skill, dmgMin, dmgMax, chainPct, _targetPrimaryProfession);
            totalFluxMin += fluxMin; totalFluxMax += fluxMax;

            Rows.Add(new SpikeRowViewModel
            {
                IconPath = skill.IconPath,
                SkillName = skill.DisplayName,
                CharacterName = member.Name,
                Detail = parts.Count > 0 ? string.Join(" · ", parts)
                                         : L("rien de calculable", "nothing computable"),
                RangeText = parts.Count == 0 ? "—" : min == max ? max.ToString() : $"{min}–{max}",
                FluxBonusText = fluxMin == 0 && fluxMax == 0 ? "—"
                    : fluxMin == fluxMax ? $"+{fluxMax}" : $"+{fluxMin}–{fluxMax}",
                Slot = slot,
                IsWeaponRow = weaponTable,
                CanChooseWeapon = canChooseWeapon,
                WeaponOptions = canChooseWeapon ? WeaponOptionsFor(skill) : [],
                WeaponTypeOptions = weapon is { } wpn
                    ? TypeOptions(WeaponStrike.DamageTypeChoices(wpn)) : [],
                HasTicks = maxTicks > 1,
                TickOptions = maxTicks > 1 ? Enumerable.Range(1, maxTicks).ToList() : [],
                HasProjectiles = maxProjectiles > 1,
                ProjectileOptions = maxProjectiles > 1
                    ? Enumerable.Range(1, maxProjectiles).ToList() : [],
                HasProcs = showProcs,
                HasConditional = hasConditional,
                ConditionText = conditionText,
                HasThreshold = thresholdRow is not null,
                ThresholdCap = thresholdRow is { } th ? ThresholdCap(th) : 0,
                ThresholdOptions = thresholdRow is { } to
                    ? Enumerable.Range(0, ThresholdCap(to) + 1).ToList() : [],
                ThresholdClause = thresholdRow?.ThresholdClause,
                CanChooseMod = canChooseMod,
                ModOptions = canChooseMod ? ModOptions() : [],
                HasSunderingProc = weaponMod == SpikeWeaponMod.Sundering,
                IsBowRow = isBowRow,
            });
            totalMin += min; totalMax += max;
        }

        // Attaques NORMALES (lot 6d-2) : une ligne artificielle par perso COCHÉ, avant les lignes de vol
        // de vie — elle peut déclarer un mod vampirique, qui les fait apparaître (Q21).
        if (Build.ShowNormalAttacks)
            foreach (var member in Build.SpikeMembers)
            {
                if (!member.SpikeNormalRow) continue;
                var t = BuildNormalAttackRow(member, target, buffCtx.GetValueOrDefault(member),
                                             vampiricSteals, chargeCtx.GetValueOrDefault(member));
                Rows.Add(t.Row);
                totalMin += t.Min; totalMax += t.Max;
                totalFluxMin += t.FluxMin; totalFluxMax += t.FluxMax;
            }

        // Vol de vie des mods vampiriques : 1 ou 2 lignes ARTIFICIELLES globales (3 et/ou 5), tout à
        // la fin de la liste, colonne personnage vide — elles n'appartiennent à personne. Cocher
        // « vampirique » sur une ligne ne lui ajoute RIEN : c'est une déclaration qui fait
        // apparaître la ligne. Le compteur (le ComboBox des Procs) est saisi par l'utilisateur, qui
        // compte lui-même TOUTES les attaques vampiriques qui touchent, celles du spike comprises.
        foreach (int steal in vampiricSteals)
        {
            int hits = Math.Max(0, Build.VampiricHits(steal));
            int stolen = steal * hits;
            Rows.Add(new SpikeRowViewModel
            {
                IconPath = LifeStealIconService.GetLocalPath(),
                SkillName = L($"Vol de vie : {steal} (arme)", $"Life Stealing: {steal} (Weapon)"),
                CharacterName = string.Empty,
                Detail = $"{L("vie volée", "life stolen")} {steal}"
                       + (hits != 1 ? L($" × {hits} attaques", $" × {hits} attacks") : "")
                       + ArmorIgnoring,
                RangeText = stolen.ToString(),
                FluxBonusText = "—",
                HasProcs = true,
                ProcsGetter = () => Build.VampiricHits(steal),
                ProcsSetter = v => Build.SetVampiricHits(steal, v),
            });
            totalMin += stolen; totalMax += stolen;
        }

        int deepWound = _withDeepWound ? target.DeepWoundDamage : 0;
        int grandMin = totalMin + totalFluxMin + deepWound;
        int grandMax = totalMax + totalFluxMax + deepWound;
        HasRows = Rows.Count > 0;
        if (!HasRows)
            TotalText = string.Empty;
        else
        {
            var extras = new List<string>();
            if (totalFluxMin != 0 || totalFluxMax != 0)
                extras.Add(totalFluxMin == totalFluxMax
                    ? L($"Bonus Flux {totalFluxMax} compris", $"Flux bonus {totalFluxMax} included")
                    : L($"Bonus Flux {totalFluxMin}–{totalFluxMax} compris",
                        $"Flux bonus {totalFluxMin}–{totalFluxMax} included"));
            if (deepWound > 0)
                extras.Add($"{GwConditionData.DisplayName("Deep Wound")} {deepWound} "
                           + L("comprise", "included"));
            string total = L("Total : ", "Total: ");
            TotalText = (grandMin == grandMax ? $"{total}{grandMax}" : $"{total}{grandMin}–{grandMax}")
                + (extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "");
        }
    }

    // Mind Wrack (core) : construit une des 2 lignes. Paquet armor-ignoring (dégât sans type) ×
    // procs indépendants ; le flux s'applique à l'assiette de dégât comme pour les autres lignes.
    // vengeance → ×1,25 sur le total de la ligne (buff d'arme, « tout, sorts inclus »).
    private (SpikeRowViewModel Row, int Min, int Max, int FluxMin, int FluxMax) BuildMindWrackRow(
        CharacterSlotViewModel member, Skill skill, SkillSlotViewModel slot,
        int value, int procs, bool hasProcs, string label, bool vengeance, double factor)
    {
        // `factor` compose DÉJÀ Vengeance et les multiplicateurs du lot 6e ; `vengeance` ne sert plus
        // qu'à la note du détail. Un seul arrondi, comme sur les lignes d'attaque.
        int dmg = value * procs;
        if (Math.Abs(factor - 1.0) > 0.0001) dmg = (int)(dmg * factor);
        var (fluxMin, fluxMax) = FluxDamageBonus(
            Build.ActiveFlux, member, skill, dmg, dmg, 0, _targetPrimaryProfession);
        var row = new SpikeRowViewModel
        {
            IconPath = skill.IconPath,
            SkillName = skill.DisplayName,
            CharacterName = member.Name,
            Detail = $"{label}{L(" : ", ": ")}{value}" + (procs != 1 ? $" × {procs} procs" : "")
                   + ArmorIgnoring + (vengeance ? L(" · Vengeance (×1,25)", " · Vengeance (×1.25)") : ""),
            RangeText = dmg.ToString(),
            FluxBonusText = fluxMin == 0 && fluxMax == 0 ? "—"
                : fluxMin == fluxMax ? $"+{fluxMax}" : $"+{fluxMin}–{fluxMax}",
            Slot = slot,
            HasProcs = hasProcs,
        };
        return (row, dmg, dmg, fluxMin, fluxMax);
    }

    /// <summary>
    /// Ligne ARTIFICIELLE des attaques NORMALES d'un perso (lot 6d-2, Q17/Q19-Q22) : ses coups d'arme
    /// hors compétence, comptés à la main. Elle n'a pas de slot — son état vit sur le perso, comme les
    /// icônes de buffs — et elle ne compte QUE le coup d'arme : les conjurations, Ordres, Honneur, Cent
    /// lames & co. ont déjà leur propre ligne à compteur « Procs », où l'utilisateur compte lui-même les
    /// déclenchements des coups normaux. Les ajouter ici les compterait deux fois.
    ///
    /// L'arme est celle du set ACTIF, sinon celle déduite de la barre ; le type de dégâts sort de la
    /// chaîne du § 6.1 (Hiver et Briseur de pierre compris) et décide donc l'AL par type de la cible.
    ///
    /// ⚠ Buffs à charges (Q22) : les attaques du spike passent d'abord, les charges RESTANTES vont aux
    /// premiers coups normaux — d'où le calcul coup par coup, seul moyen de sommer une fourchette dont
    /// les termes diffèrent. Deux buffs allumés n'entrent jamais ici, et c'est leur TEXTE qui le dit :
    /// l'Hymne d'envie vise le prochain « attack SKILL » et Destructive Was Glaive les « Ritualist
    /// skills » — un coup normal n'est ni l'un ni l'autre.
    ///
    /// ⚠ La pénétration du rang de FORCE n'entre pas non plus : l'attribut primaire du Guerrier
    /// pénètre « with your attack skills », pas avec les coups normaux (à confirmer par Philippe).
    /// </summary>
    private (SpikeRowViewModel Row, int Min, int Max, int FluxMin, int FluxMax) BuildNormalAttackRow(
        CharacterSlotViewModel member, SpikeTarget target, MemberBuffs? mb, SortedSet<int> vampiricSteals,
        BoostCharges? bc)
    {
        string title = L("Attaque normale", "Normal attack");
        string? icon = ProfessionIconService.GetLocalPath(member.PrimaryProfession);

        // Arme du set actif, sinon celle déduite des attaques de la barre (Q17). Toutes les armes du
        // catalogue sont recevables ici : aucun type d'attaque ne restreint un coup normal.
        var kind = member.ActiveSetWeaponKind;
        var weapon = WeaponStrike.ForKind(kind);
        bool deduced = false;
        if (weapon is null && DeduceWeapon(member, WeaponStrike.All) is { } fallback)
        {
            weapon = fallback;
            kind = WeaponStrike.KindOf(fallback);
            deduced = true;
        }

        int hits = Math.Max(0, member.SpikeNormalHits);

        // Ni set d'armes renseigné, ni attaque d'arme dans la barre : rien à calculer, mais la ligne
        // reste affichée — l'utilisateur a coché la case, il doit voir pourquoi elle ne rend rien.
        if (weapon is null)
            return (new SpikeRowViewModel
            {
                IconPath = icon,
                SkillName = title,
                CharacterName = member.Name,
                Detail = L("arme indéterminée : renseignez le set d'armes actif du perso",
                           "weapon undetermined: fill in the character's active weapon set"),
                RangeText = "—",
                FluxBonusText = "—",
                HasProcs = true,
                ProcsGetter = () => member.SpikeNormalHits,
                ProcsSetter = v => member.SpikeNormalHits = v,
            }, 0, 0, 0, 0);

        int rank = WeaponStrike.StrikeRank(weapon, member.AttributeLevel);
        bool judges = mb is { Judges: true } || member.JudgesInsightLit;
        var lineType = member.SpikeNormalAttackType(kind, weapon.DamageType, mb is { Judges: true });
        int al = target.EffectiveArmor(lineType.Received, _withCrackedArmor);

        // Mod de PRÉFIXE de la ligne : proposé tant que le mod élémentaire du set actif ne prend pas la
        // place (Q15), et jamais sur une arme de lanceur — mêmes règles que les lignes d'attaque.
        bool canChooseMod = !weapon.IsCaster && !member.ElementalModOnNormalAttack(kind);
        var weaponMod = canChooseMod ? SpikeWeaponMods.FromKey(member.SpikeNormalWeaponModKey)
                                     : SpikeWeaponMod.None;
        // Déclarer « vampirique » ici n'ajoute rien à la ligne : cela fait apparaître la ligne globale
        // de vol de vie, exactement comme sur une ligne de compétence.
        if (weaponMod == SpikeWeaponMod.Vampiric) vampiricSteals.Add(SpikeWeaponMods.VampiricSteal(weapon));
        bool sunderingMod = weaponMod == SpikeWeaponMod.Sundering && member.SpikeNormalSunderingProc;
        bool isBow = WeaponStrike.IsBow(weapon);
        bool hornbow = isBow && member.SpikeNormalHornbow;

        // Pénétrations qui ne dépendent pas du coup : Judge's Insight et les deux mods sont des BONUS,
        // ils s'ajoutent par-dessus le pool de BASE (ici vide hors Arme de fractionnement).
        int bonusPen = (judges ? SpikeWeaponBuffs.JudgesBonusPen : 0)
                     + (sunderingMod ? SpikeWeaponMods.SunderingBonusPen : 0)
                     + (hornbow ? SpikeWeaponMods.HornbowBonusPen : 0);

        // Charges RESTANTES après les attaques du spike (Q22).
        int sunderCharges = mb is { SunderingActive: true }
            ? Math.Max(0, SpikeWeaponBuffs.SunderingAttacks - mb.SunderingSlots.Count) : 0;
        int splinterCharges = mb is { SplinterActive: true, SplinterBonus: > 0 }
            ? Math.Max(0, SpikeWeaponBuffs.SplinterAttacks - mb.SplinterSlots.Count) : 0;
        // « Find Their Weakness! » ne couvre QUE la première attaque : sa charge ne retombe sur un coup
        // normal que si le spike du perso n'a aucune attaque d'arme pour la consommer.
        int ftwCharges = mb is { FtwActive: true, FtwSlot: null, FtwBonus: > 0 } ? 1 : 0;

        // ── Lot 6e : les effets de la carte sur un COUP NORMAL ───────────────────────────────
        // Q20 (lot 6d-2) : « TOUT ce qui est allumé, charges comprises ». Cible NULLE = un coup normal,
        // pas une compétence : le périmètre se juge alors sur la seule arme. Deux sortes d'appels :
        //  • celui-ci, les PERMANENTS en bloc — chaque coup les reçoit, tous de la même façon ;
        //  • puis un appel PAR effet à charges, pour connaître SON bonus à lui : c'est le seul moyen de
        //    savoir lequel s'arrête au 3ᵉ coup et lequel tient jusqu'au 8ᵉ.
        var plain = member.SpikeDamageBoostsFor(null, kind, id => !SpikeBoostCoverage.IsCharged(id));
        // Les paquets d'un effet, ramenés au +X PLAT que le coup encaisse : un « +X » ignore l'armure et
        // passe tel quel, un paquet typé sans « + » passe par la formule, contre l'AL de SON type.
        // (Aucun des 23 n'est du second genre aujourd'hui — mais le jeter en silence serait un piège.)
        int FlatOf(DamageBoosts b) => (b.Packets ?? []).Sum(p => p.IgnoresArmor
            ? p.Value
            : SkillDamage.DamageAt(p.Value, target.EffectiveArmor(p.DamageType, _withCrackedArmor),
                                   bonusPen, AttackerLevel));

        var boostCharges = new List<SpikeNormalAttack.BoostCharge>();
        var chargedParts = new List<string>();
        foreach (var rule in SpikeBoostCoverage.Charges)
        {
            // Charges RESTANTES après les attaques du spike (Q22), bornées au nombre de coups tapés.
            int left = bc is not null && bc.Remaining.TryGetValue(rule.SkillId, out int r) ? r : 0;
            if (left <= 0 || hits <= 0) continue;
            // CET effet SEUL : le prédicat écarte tout le reste, permanents compris — sinon leur bonus
            // reviendrait dans chaque appel et serait compté autant de fois qu'il y a d'effets à charges.
            var one = member.SpikeDamageBoostsFor(null, kind, id => id == rule.SkillId);
            if (one.Sources is not { Count: > 0 }) continue;
            int flat = FlatOf(one);
            int covered = Math.Min(left, hits);
            if (flat != 0 || one.LifeSteal > 0)
                boostCharges.Add(new SpikeNormalAttack.BoostCharge(covered, flat, one.LifeSteal));
            foreach (var s in one.Sources)
                chargedParts.Add($"{BoostSourcePart(s)} {ChargeNote(covered)}");
        }

        // Great Dwarf Weapon : vrai dégât d'ARME, donc dans la plage avant armure et critique.
        var w = mb is { GdwBonus: > 0 }
            ? weapon with { Min = weapon.Min + mb.GdwBonus, Max = weapon.Max + mb.GdwBonus } : weapon;
        int brutal = mb?.BrutalBonus ?? 0;

        // Lot 6e : le +X permanent des effets de la carte rejoint celui de l'Arme brutale — même place
        // dans le calcul (après l'armure, sur chaque coup), donc un seul terme.
        int flatPerHit = brutal + FlatOf(plain);

        var charges = new SpikeNormalAttack.Charges(
            SunderingHits: sunderCharges,
            SplinterHits: splinterCharges, SplinterBonus: mb?.SplinterBonus ?? 0,
            FtwHits: ftwCharges, FtwBonus: mb?.FtwBonus ?? 0,
            Boosts: boostCharges);
        var (min, max, steal) = SpikeNormalAttack.Damage(
            w, rank, al, bonusPen, hits, _allCrits, flatPerHit, charges, AttackerLevel,
            stealPerHit: plain.LifeSteal, weaponMultiplier: plain.WeaponMultiplier);
        // Coup de RÉFÉRENCE du détail : sans aucune charge, donc le régime permanent de la ligne.
        var (nudeMin, nudeMax) = SpikeNormalAttack.Hit(w, rank, al, bonusPen, flatPerHit, _allCrits,
                                                       AttackerLevel, plain.WeaponMultiplier);

        // ⚠ Le vol de vie sort de l'assiette des DÉGÂTS : ni multiplicateur, ni flux (règle du chantier).
        int dmgMin = min, dmgMax = max;
        double damageFactor = (mb is { Vengeance: true } ? SpikeWeaponBuffs.VengeanceMultiplier : 1.0)
                              * plain.Multiplier;
        bool vengeance = mb is { Vengeance: true } && dmgMax > 0;
        if (Math.Abs(damageFactor - 1.0) > 0.0001 && dmgMax > 0)
        {
            dmgMin = (int)(dmgMin * damageFactor);
            dmgMax = (int)(dmgMax * damageFactor);
            min = dmgMin; max = dmgMax;
        }
        min += steal; max += steal;

        var parts = new List<string>
        {
            lineType.Received == weapon.DamageType
                ? $"{weapon.DisplayName} {Range(nudeMin, nudeMax)}"
                : $"{weapon.DisplayName} ({TypeLabel(lineType.Received)}) {Range(nudeMin, nudeMax)}",
            _allCrits
                ? L("critique forcé", "forced critical")
                : $"{L("crit", "crit")} {100 * WeaponStrike.CriticalChance(rank, AttackerLevel, _targetLevel, member.AttributeLevel("Critical Strikes") ?? 0, plain.CriticalPercent):0} %",
            hits == 1 ? L("1 coup", "1 hit") : L($"× {hits} coups", $"× {hits} hits"),
        };
        if (deduced) parts.Add(L("arme déduite", "deduced weapon"));
        if (lineType.Converted) parts.Add(L("type converti", "converted type"));
        if (judges)
            parts.Add($"{BuffName(mb, SpikeBuff.JudgesInsight)} {L("(sacré, +20 % pén.)", "(holy, +20% pen.)")}");
        if (sunderCharges > 0 && hits > 0)
            parts.Add($"{BuffName(mb, SpikeBuff.SunderingWeapon)} "
                      + ChargeNote(Math.Min(sunderCharges, hits))
                      + L($" ({SpikeWeaponBuffs.SunderingBasePen} % pén.)",
                          $" ({SpikeWeaponBuffs.SunderingBasePen}% pen.)"));
        if (splinterCharges > 0 && hits > 0)
            parts.Add($"+{mb!.SplinterBonus} {BuffName(mb, SpikeBuff.SplinterWeapon)} "
                      + ChargeNote(Math.Min(splinterCharges, hits)) + ArmorIgnoring);
        if (brutal > 0)
            parts.Add($"+{brutal} ({BuffName(mb, SpikeBuff.BrutalWeapon)})");
        if (mb is { GdwBonus: > 0 })
            parts.Add($"{BuffName(mb, SpikeBuff.GreatDwarfWeapon)} "
                      + L($"(+{mb.GdwBonus} d'arme compris)", $"(+{mb.GdwBonus} weapon damage included)"));
        if (ftwCharges > 0 && hits > 0)
            parts.Add($"+{mb!.FtwBonus} ({BuffName(mb, SpikeBuff.FindTheirWeakness)}, "
                      + $"{GwConditionData.DisplayName("Deep Wound")} → "
                      + L("toggle cible", "target toggle") + $", {ChargeNote(Math.Min(ftwCharges, hits))})");
        if (sunderingMod)
            parts.Add(L($"de fractionnement (+{SpikeWeaponMods.SunderingBonusPen} % pén.)",
                        $"Sundering (+{SpikeWeaponMods.SunderingBonusPen}% pen.)"));
        if (hornbow)
            parts.Add(L($"arc corne (+{SpikeWeaponMods.HornbowBonusPen} % pén.)",
                        $"hornbow (+{SpikeWeaponMods.HornbowBonusPen}% pen.)"));
        // Lot 6e : les effets de la carte, permanents puis ceux qui s'épuisent (avec leur note de charge).
        foreach (var s in plain.Sources ?? []) parts.Add(BoostSourcePart(s));
        parts.AddRange(chargedParts);
        if (vengeance) parts.Add(L("Vengeance (×1,25)", "Vengeance (×1.25)"));

        // Flux : les deux flux qui dépendent d'une COMPÉTENCE (Chain Combo par l'ordre de cast, Amateur
        // Hour par la profession de la compétence) ne peuvent rien dire d'un coup normal ; les deux qui
        // dépendent du PERSO (Jack of All Trades, There Can Be Only One) s'appliquent.
        var (fluxMin, fluxMax) = FluxDamageBonus(
            Build.ActiveFlux, member, null, dmgMin, dmgMax, 0, _targetPrimaryProfession);

        return (new SpikeRowViewModel
        {
            IconPath = icon,
            SkillName = title,
            CharacterName = member.Name,
            Detail = string.Join(" · ", parts),
            RangeText = Range(min, max),
            FluxBonusText = fluxMin == 0 && fluxMax == 0 ? "—"
                : fluxMin == fluxMax ? $"+{fluxMax}" : $"+{fluxMin}–{fluxMax}",
            HasProcs = true,
            ProcsGetter = () => member.SpikeNormalHits,
            ProcsSetter = v => member.SpikeNormalHits = v,
            CanChooseMod = canChooseMod,
            ModOptions = canChooseMod ? ModOptions() : [],
            ModKeyGetter = () => member.SpikeNormalWeaponModKey,
            ModKeySetter = v => member.SpikeNormalWeaponModKey = v ?? string.Empty,
            HasSunderingProc = weaponMod == SpikeWeaponMod.Sundering,
            SunderingProcGetter = () => member.SpikeNormalSunderingProc,
            SunderingProcSetter = v => member.SpikeNormalSunderingProc = v,
            IsBowRow = isBow,
            HornbowGetter = () => member.SpikeNormalHornbow,
            HornbowSetter = v => member.SpikeNormalHornbow = v,
        }, min, max, fluxMin, fluxMax);
    }

    private static string Range(int min, int max) => min == max ? max.ToString() : $"{min}–{max}";

    /// <summary>
    /// Le terme du DÉTAIL qui nomme un effet du lot 6e. Sans lui, la ligne afficherait un nombre plus
    /// gros sans dire d'où il vient — et ces effets-là, contrairement aux 9 buffs d'arme, n'ont pas
    /// d'interrupteur dans cette fenêtre : leur seule trace serait le chiffre.
    ///
    /// ⚠ Un paquet NÉGATIF s'écrit avec son signe (« −42 Arme du tourment ») : un malus affiché en
    /// « +−42 » ou, pire, sans signe, se lirait comme un gain.
    /// </summary>
    private static string BoostSourcePart(DamageBoostSource s) => s.Kind switch
    {
        DamageBoostKind.Damage => s.Value >= 0 ? $"+{s.Value} {s.Name}" : $"−{-s.Value} {s.Name}",
        DamageBoostKind.CriticalChance => $"{s.Name} (+{s.Value} % crit)",
        DamageBoostKind.BasePenetration or DamageBoostKind.BonusPenetration =>
            L($"{s.Name} ({s.Value} % pén.)", $"{s.Name} ({s.Value}% pen.)"),
        DamageBoostKind.Multiplier =>
            $"{s.Name} (×{FormatFactor(1.0 + s.Value / 100.0)})",
        DamageBoostKind.WeaponMultiplier =>
            L($"{s.Name} (×{FormatFactor(1.0 + s.Value / 100.0)} sur l'arme)",
              $"{s.Name} (×{FormatFactor(1.0 + s.Value / 100.0)} on weapon)"),
        DamageBoostKind.LifeSteal => L($"vie volée {s.Value} ({s.Name})", $"life stolen {s.Value} ({s.Name})"),
        _ => s.Name,
    };

    // ⚠ Le séparateur décimal suit la LANGUE AFFICHÉE et non la culture du système : tout le reste de
    // la fenêtre écrit « ×1,25 » en français avec une virgule posée à la main (la culture du process
    // peut être invariante, cf. InvariantGlobalization).
    private static string FormatFactor(double factor)
    {
        string s = factor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return AppLanguage.IsFr ? s.Replace('.', ',') : s;
    }

    // « sur le 1er coup » / « sur les N premiers coups » — note de charge d'un buff (lot 6d-2, Q22).
    private static string ChargeNote(int count) => count <= 1
        ? L("sur le 1er coup", "on the 1st hit")
        : L($"sur les {count} premiers coups", $"on the first {count} hits");

    // Bonus de dégâts dû au flux actif du build, par ligne (colonne « Bonus Flux » + Total).
    // Assiette = dégâts SEULS (dmgMin/dmgMax), hors vol/perte de vie (décision Philippe).
    // chainComboPct = bonus de chaîne précalculé pour ce slot (Chain Combo), 0 sinon.
    // targetPrimary = profession primaire choisie pour la cible (Amateur Hour / There Can Be Only One).
    // skill null = une ATTAQUE NORMALE (lot 6d-2) : les deux flux qui interrogent la compétence
    // (Amateur Hour par sa profession, Chain Combo par son ordre de cast) n'ont alors rien à dire.
    private static (int Min, int Max) FluxDamageBonus(
        Flux? flux, CharacterSlotViewModel member, Skill? skill, int dmgMin, int dmgMax,
        int chainComboPct, Profession targetPrimary)
    {
        if (dmgMin == 0 && dmgMax == 0) return (0, 0);
        return flux switch
        {
            Flux.JackOfAllTrades when member.MeetsJackOfAllTrades
                => (Pct(dmgMin, 15), Pct(dmgMax, 15)),
            Flux.ChainCombo when chainComboPct > 0
                => (Pct(dmgMin, chainComboPct), Pct(dmgMax, chainComboPct)),
            // Amateur Hour : la compétence relève de la profession SECONDAIRE du perso ET la cible a
            // pour profession PRIMAIRE cette même secondaire (description FluxData validée).
            Flux.AmateurHour when skill is not null && member.SecondaryProfession != Profession.None
                    && skill.Profession == member.SecondaryProfession
                    && targetPrimary == member.SecondaryProfession
                => (Pct(dmgMin, 30), Pct(dmgMax, 30)),
            // There Can Be Only One : la cible partage la profession PRIMAIRE du perso.
            Flux.ThereCanBeOnlyOne when member.PrimaryProfession != Profession.None
                    && targetPrimary == member.PrimaryProfession
                => (Pct(dmgMin, 30), Pct(dmgMax, 30)),
            _ => (0, 0),
        };
    }

    // Arme déduite pour une attaque d'arme LIBRE : la plus fréquente parmi les attaques de la barre
    // du perso liées à une maîtrise (Hammer/Axe/Sword/Bow…), en ne retenant que les armes de la
    // catégorie <paramref name="allowed"/> — déduire un arc pour un Bull's Strike n'a pas de sens.
    // Null si aucune → repli (choix manuel).
    private static WeaponStrike.Weapon? DeduceWeapon(
        CharacterSlotViewModel member, IReadOnlyList<WeaponStrike.Weapon> allowed)
        => member.SkillSlots
            .Select(s => s.Skill).Where(sk => sk is not null)
            .Select(sk => WeaponStrike.For(sk!)).Where(w => w is not null && allowed.Contains(w))
            .GroupBy(w => w!.Mastery)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.First();

    // Chain Combo : bonus cumulatif PAR PERSO le long de l'ordre de cast (indices de sélection).
    // +5 % à chaque changement de caractéristique (plafond 30 %), remis à 0 si même carac ; la 1re
    // compétence = 0 %. Le bonus courant s'applique aux dégâts de la compétence qui l'obtient.
    private static Dictionary<SkillSlotViewModel, int> ComputeChainComboPct(
        IEnumerable<CharacterSlotViewModel> members)
    {
        var map = new Dictionary<SkillSlotViewModel, int>();
        foreach (var member in members)
        {
            int chain = 0;
            string? prev = null;
            foreach (var slot in member.SkillSlots
                         .Where(s => s.IsSpikeSelected && s.Skill != null)
                         .OrderBy(s => s.SpikeOrder))
            {
                string attr = slot.Skill!.Attribute ?? string.Empty;
                chain = prev is null ? 0
                    : string.Equals(attr, prev, StringComparison.Ordinal) ? 0
                    : Math.Min(chain + 5, 30);
                map[slot] = chain;
                prev = attr;
            }
        }
        return map;
    }


    // ── Buffs d'arme (chantier 14) ─────────────────────────────────────────────
    // Effets ACTIFS d'un membre : slots couverts par Sundering Weapon (les 3 premières attaques
    // d'arme dans l'ordre de cast), Judge's Insight (sacré + bonus d'AP), Vengeance (×1,25),
    // AP de base de DWG (> 0 seulement chez son porteur, 10 % si la copie équipée est la PvP),
    // premier attack skill + valeur du +X d'Anthem of Envy ; lot 2 : slots couverts par Splinter
    // Weapon (4 premières attaques) + son éclat, +X par coup de Brutal Weapon, +X de dégâts
    // d'ARME de Great Dwarf Weapon, première attaque + valeur de « Find Their Weakness! ».
    // Les bonus valent 0 quand le buff est inactif.
    // Names = nom AFFICHÉ de la compétence de chaque buff, pris sur la copie équipée (donc dans la
    // langue courante, via Skill.DisplayName) plutôt que codé en dur : les libellés ne peuvent pas
    // diverger de la DB. Suffixe « (PvP) » retiré — la copie PvP est signalée à part, dans le
    // tooltip du toggle.
    // Sundering/Splinter/FtwActive : le buff est ALLUMÉ, indépendamment du nombre d'attaques qu'il a
    // couvertes. Indispensable au lot 6d-2 : une liste de slots vide ne dit pas si le buff est éteint ou
    // s'il est allumé sans aucune attaque à couvrir — et c'est ce second cas qui donne toutes ses charges
    // aux coups normaux (Q22).
    private sealed record MemberBuffs(HashSet<SkillSlotViewModel> SunderingSlots, bool Judges,
                                      bool Vengeance, int DwgPen,
                                      SkillSlotViewModel? AnthemSlot, int AnthemBonus,
                                      HashSet<SkillSlotViewModel> SplinterSlots, int SplinterBonus,
                                      int BrutalBonus, int GdwBonus,
                                      SkillSlotViewModel? FtwSlot, int FtwBonus,
                                      IReadOnlyDictionary<SpikeBuff, string> Names,
                                      bool SunderingActive, bool SplinterActive, bool FtwActive);

    // Synchronise la rangée d'icônes de chaque carte membre (offre = buffs équipés sur un membre
    // du roster, cadre vert NON requis — le buff se lance AVANT le spike ; DWG proposé à son seul
    // porteur) et précalcule les effets actifs (proposé ∩ coché). La rangée n'est reconstruite
    // que si son offre change (instances stables) ; HasSpikeBuffToggles est UI-only, filtré du
    // dirty tracking (cf. TeamBuildViewModel.OnChildChanged) — pas de boucle Mutated/recalcul.
    private Dictionary<CharacterSlotViewModel, MemberBuffs> SyncWeaponBuffs()
    {
        var equipped = new List<(SpikeWeaponBuffs.Descriptor D, CharacterSlotViewModel Wearer, Skill Copy)>();
        foreach (var m in Build.SpikeMembers)
        foreach (var s in m.SkillSlots)
            if (s.Skill is { } sk && SpikeWeaponBuffs.FromSkillName(sk.Name) is { } d)
                equipped.Add((d, m, sk));

        // Valeurs résolues au rang du LANCEUR (max si plusieurs copies équipées) : Anthem au
        // rang de Leadership du chanteur, Splinter au Channeling du Rt, FTW au Command, etc.
        int ResolvedMax(SpikeBuff b, Func<string, int> parse) => equipped
            .Where(e => e.D.Buff == b)
            .Select(e => parse(e.Wearer.ResolveDescription(e.Copy)))
            .DefaultIfEmpty(0).Max();
        int anthemBonus   = ResolvedMax(SpikeBuff.AnthemOfEnvy,      SpikeWeaponBuffs.BonusDamage);
        int splinterBonus = ResolvedMax(SpikeBuff.SplinterWeapon,    SpikeWeaponBuffs.SplinterDamage);
        int brutalBonus   = ResolvedMax(SpikeBuff.BrutalWeapon,      SpikeWeaponBuffs.BonusDamage);
        int gdwBonus      = ResolvedMax(SpikeBuff.GreatDwarfWeapon,  SpikeWeaponBuffs.BonusDamage);
        int ftwBonus      = ResolvedMax(SpikeBuff.FindTheirWeakness, SpikeWeaponBuffs.BonusDamage);

        // Nom affiché de chaque buff équipé, dans la langue courante (cf. MemberBuffs.Names).
        var buffNames = equipped
            .GroupBy(e => e.D.Buff)
            .ToDictionary(g => g.Key, g => SkillVariants.BaseName(g.First().Copy.DisplayName));

        var ctx = new Dictionary<CharacterSlotViewModel, MemberBuffs>();
        foreach (var m in Build.SpikeMembers)
        {
            var offered = equipped
                .Where(e => !e.D.SelfOnly || ReferenceEquals(e.Wearer, m))
                .GroupBy(e => e.D.Key).Select(g => g.First())
                .OrderBy(e => e.D.Buff).ToList();

            if (!m.SpikeBuffToggles.Select(t => t.Descriptor.Key)
                    .SequenceEqual(offered.Select(e => e.D.Key)))
            {
                m.SpikeBuffToggles.Clear();
                foreach (var e in offered)
                    m.SpikeBuffToggles.Add(new SpikeBuffToggleViewModel
                    {
                        Member = m,
                        Descriptor = e.D,
                        IconPath = e.Copy.IconPath,
                        IsWeaponSpell = string.Equals(e.Copy.SkillType, "Weapon Spell", StringComparison.Ordinal),
                        Tooltip = e.D.Buff switch
                        {
                            SpikeBuff.AnthemOfEnvy      => $"{e.D.DisplayTooltip} {L($"— ici +{anthemBonus}", $"— here +{anthemBonus}")}",
                            SpikeBuff.SplinterWeapon    => $"{e.D.DisplayTooltip} {L($"— ici +{splinterBonus}", $"— here +{splinterBonus}")}",
                            SpikeBuff.BrutalWeapon      => $"{e.D.DisplayTooltip} {L($"— ici +{brutalBonus}", $"— here +{brutalBonus}")}",
                            SpikeBuff.GreatDwarfWeapon  => $"{e.D.DisplayTooltip} {L($"— ici +{gdwBonus}", $"— here +{gdwBonus}")}",
                            SpikeBuff.FindTheirWeakness => $"{e.D.DisplayTooltip} {L($"— ici +{ftwBonus}", $"— here +{ftwBonus}")}",
                            SpikeBuff.DestructiveWasGlaive when IsPvpCopy(e.Copy)
                                => $"{e.D.DisplayTooltip} {L("— copie PvP équipée : 10 %", "— PvP copy equipped: 10%")}",
                            _ => e.D.DisplayTooltip,
                        },
                    });
                m.RaiseSpikeBuffTogglesChanged();
            }
            else
                foreach (var t in m.SpikeBuffToggles) t.RaiseActiveChanged(); // réaligne après undo/chargement

            bool Active(SpikeBuff b) => offered.Any(e => e.D.Buff == b && m.IsSpikeBuffActive(e.D.Key));

            // Attaques d'arme du spike dans l'ordre de cast : assiette de Sundering (3 premières),
            // de Splinter (4 premières), d'Anthem et de FTW (la première — « next attack [skill] » ;
            // Pet Attack exclue, comme la table d'arme).
            var sunderingSlots = new HashSet<SkillSlotViewModel>();
            var splinterSlots = new HashSet<SkillSlotViewModel>();
            SkillSlotViewModel? anthemSlot = null, ftwSlot = null;
            if (Active(SpikeBuff.SunderingWeapon) || Active(SpikeBuff.AnthemOfEnvy)
                || Active(SpikeBuff.SplinterWeapon) || Active(SpikeBuff.FindTheirWeakness))
            {
                var attacks = m.SkillSlots
                    .Where(s => s.IsSpikeSelected && s.Skill is { } k && WeaponStrike.IsWeaponAttack(k))
                    .OrderBy(s => s.SpikeOrder).ToList();
                if (Active(SpikeBuff.SunderingWeapon))
                    foreach (var s in attacks.Take(SpikeWeaponBuffs.SunderingAttacks)) sunderingSlots.Add(s);
                if (Active(SpikeBuff.SplinterWeapon))
                    foreach (var s in attacks.Take(SpikeWeaponBuffs.SplinterAttacks)) splinterSlots.Add(s);
                if (Active(SpikeBuff.AnthemOfEnvy)) anthemSlot = attacks.FirstOrDefault();
                if (Active(SpikeBuff.FindTheirWeakness)) ftwSlot = attacks.FirstOrDefault();
            }
            int dwgPen = Active(SpikeBuff.DestructiveWasGlaive)
                ? offered.Where(e => e.D.Buff == SpikeBuff.DestructiveWasGlaive)
                    .Select(e => SpikeWeaponBuffs.DwgBasePen(IsPvpCopy(e.Copy))).Max()
                : 0;

            ctx[m] = new MemberBuffs(sunderingSlots, Active(SpikeBuff.JudgesInsight),
                Active(SpikeBuff.Vengeance), dwgPen, anthemSlot,
                anthemSlot is not null ? anthemBonus : 0,
                splinterSlots, splinterBonus,
                Active(SpikeBuff.BrutalWeapon) ? brutalBonus : 0,
                Active(SpikeBuff.GreatDwarfWeapon) ? gdwBonus : 0,
                // ⚠ Le +X de FTW n'est plus conditionné à ftwSlot : la ligne d'attaque le lit toujours
                // avec `slot == mb.FtwSlot`, qui exige déjà un slot, tandis que la ligne d'ATTAQUE
                // NORMALE en a besoin justement quand il n'y a AUCUNE attaque dans le spike (Q22).
                ftwSlot, Active(SpikeBuff.FindTheirWeakness) ? ftwBonus : 0,
                buffNames,
                Active(SpikeBuff.SunderingWeapon), Active(SpikeBuff.SplinterWeapon),
                Active(SpikeBuff.FindTheirWeakness));
        }
        return ctx;
    }

    private static bool IsPvpCopy(Skill s) => s.Name.EndsWith("(PvP)", StringComparison.OrdinalIgnoreCase);

    // ── Effets à CHARGES du lot 6e ────────────────────────────────────────────
    //
    // ⚠ Ce que le § 6.12 du plan n'avait pas vu : 4 des 23 descripteurs du lot s'épuisent après N
    // attaques (« Esquive ceci ! » 1, « Je suis le plus fort ! » 5…8, « Visez les yeux ! » 1, Arme du
    // tourment 3). L'infobulle ne montre qu'un coup isolé, donc la question ne s'y pose jamais ; la
    // fenêtre Spike, elle, compte une SÉQUENCE, et sans ça « Je suis le plus fort ! » donnerait son +20
    // à toutes les attaques du spike.
    //
    // La règle est celle de Q22 (lot 6d-2), étendue et non rouverte. Le comptage suit donc exactement le
    // patron d'Arme de fractionnement : les N premières attaques d'ARME du perso dans l'ordre de cast.

    /// <summary>Ce qu'un effet à charges couvre chez un perso : les lignes qui en consomment une, et ce
    /// qu'il reste pour les coups normaux.</summary>
    private sealed record BoostCharges(IReadOnlyDictionary<int, HashSet<SkillSlotViewModel>> Slots,
                                       IReadOnlyDictionary<int, int> Remaining,
                                       IReadOnlyDictionary<int, int> Total);

    private Dictionary<CharacterSlotViewModel, BoostCharges> ComputeBoostCharges()
    {
        var ctx = new Dictionary<CharacterSlotViewModel, BoostCharges>();
        foreach (var m in Build.SpikeMembers)
        {
            var slots = new Dictionary<int, HashSet<SkillSlotViewModel>>();
            var remaining = new Dictionary<int, int>();
            var total = new Dictionary<int, int>();

            // Les attaques d'ARME du spike, dans l'ordre de cast — la même assiette que les buffs à
            // charges du chantier 14 (Pet Attack exclue, comme la table d'arme).
            var attacks = m.SkillSlots
                .Where(s => s.IsSpikeSelected && s.Skill is { } k && WeaponStrike.IsWeaponAttack(k))
                .OrderBy(s => s.SpikeOrder).ToList();

            foreach (var rule in SpikeBoostCoverage.Charges)
            {
                int count = ChargeCountOf(m, rule);
                if (count <= 0) continue;
                total[rule.SkillId] = count;
                slots[rule.SkillId] = [.. attacks.Take(count)];
                remaining[rule.SkillId] = Math.Max(0, count - attacks.Count);
            }
            ctx[m] = new BoostCharges(slots, remaining, total);
        }
        return ctx;
    }

    /// <summary>Nombre d'attaques que cet effet couvre chez ce perso, 0 s'il n'est pas allumé. Littéral
    /// pour trois d'entre eux ; « Je suis le plus fort ! » le lit dans sa PROPRE progression (colonne 0,
    /// 5…8 selon le rang de titre) — sondé dans la base, pas recopié.</summary>
    private static int ChargeCountOf(CharacterSlotViewModel member, SpikeBoostCoverage.ChargeRule rule)
    {
        if (member.LitBoostAt(rule.SkillId) is not var (source, rank)) return 0;
        if (rule.Fixed > 0) return rule.Fixed;
        return rule.Index >= 0 && source.Progression is { } prog && rule.Index < prog.Length
            ? SkillProgression.IntAt(prog[rule.Index], rank) ?? 0
            : 0;
    }

    // ── Seuils « X for each [ressource] (maximum Y) » (Symbolic Strike, Aneurysm…) ────────────
    // Compte maximal d'unités : celui qui atteint le plafond annoncé (⌈max / par-unité⌉), ou le
    // repli (8) quand aucun plafond n'est annoncé (Signet of Rage).
    private static int ThresholdCap(SkillDamage.Row r)
        => r.MaxDamage > 0 && r.Value > 0
            ? Math.Max(1, (int)Math.Ceiling(r.MaxDamage / (double)r.Value))
            : SpikeThresholdSkills.FallbackCap;

    // Compte retenu pour ce slot : choix utilisateur borné au cap, ou le cap (défaut optimiste,
    // sentinelle −1 = « auto/max » — même esprit que la case conditionnelle cochée par défaut).
    private static int ThresholdCount(SkillSlotViewModel slot, SkillDamage.Row r)
        => slot.SpikeThreshold < 0 ? ThresholdCap(r)
                                   : Math.Clamp(slot.SpikeThreshold, 0, ThresholdCap(r));

    // Dégâts LISTÉS du paquet à seuil : par-unité × compte, plafonné au maximum annoncé.
    private static int ThresholdListed(SkillDamage.Row r, int count)
        => r.MaxDamage > 0 ? Math.Min(r.Value * count, r.MaxDamage) : r.Value * count;

    // Note « (plafond N) » quand la multiplication déborde le maximum annoncé.
    private static string ThresholdCapNote(SkillDamage.Row r, int count)
        => r.MaxDamage > 0 && r.Value * count > r.MaxDamage
            ? L($" (plafond {r.MaxDamage})", $" (cap {r.MaxDamage})") : "";

    // Pourcentage d'un montant de dégâts, tronqué (comme les dégâts GW1).
    private static int Pct(int value, int percent) => (int)Math.Floor(value * percent / 100.0);
}

// Une ligne du panneau de résultats : une skill sélectionnée, son détail par paquets et sa
// fourchette min–max contre la cible courante. Slot/IsWeaponRow alimentent le ComboBox de
// type d'arme des lignes d'attaque (binding direct sur SpikeWeaponDamageType du slot ; son
// changement notifie → Mutated → Recalculate reconstruit les lignes).
public sealed class SpikeRowViewModel
{
    public string? IconPath { get; init; }
    public string SkillName { get; init; } = string.Empty;
    public string CharacterName { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string RangeText { get; init; } = string.Empty;
    public SkillSlotViewModel? Slot { get; init; }
    public bool IsWeaponRow { get; init; }
    // Attaque d'arme libre : la ligne propose TOUJOURS un ComboBox de choix d'arme (défaut = arme
    // de l'attribut si c'est une maîtrise, sinon déduite). Les options sont propres à la ligne :
    // seules les armes de la catégorie du type d'attaque (mêlée / distance) y figurent.
    public bool CanChooseWeapon { get; init; }
    public IReadOnlyList<SpikeViewModel.WeaponOption> WeaponOptions { get; init; } = [];

    // Types de dégâts proposables pour l'arme de CETTE ligne : le physique est retiré sur une
    // arme de lanceur, qui ne peut pas l'être.
    public IReadOnlyList<SpikeViewModel.WeaponTypeOption> WeaponTypeOptions { get; init; } = [];
    // Compteur de ticks des skills périodiques : 1..max (binding direct sur SpikeTicks du
    // slot, même mécanique de reconstruction que le type d'arme).
    public bool HasTicks { get; init; }
    public IReadOnlyList<int> TickOptions { get; init; } = [];
    // Compteur de PROJECTILES des sorts multi-projectiles : 1..N (N = projectiles annoncés).
    // Le slot stocke 0 = « auto/tous » (défaut optimiste) ; le ComboBox affiche le compte EFFECTIF
    // et écrit le choix explicite → Mutated → recalcul, comme le compteur de seuil.
    public bool HasProjectiles { get; init; }
    public IReadOnlyList<int> ProjectileOptions { get; init; } = [];
    public int ProjectilesValue
    {
        get
        {
            int max = ProjectileOptions.Count > 0 ? ProjectileOptions[^1] : 1;
            return Slot is { } s && s.SpikeProjectiles > 0 ? Math.Min(s.SpikeProjectiles, max) : max;
        }
        set { if (Slot is not null) Slot.SpikeProjectiles = value; }
    }

    // Colonne « Bonus Flux » : bonus de dégâts du flux actif pour cette ligne ("—" si aucun).
    public string FluxBonusText { get; init; } = "—";

    // Compteur « Procs » : déclenchements d'un rider (conjuration, ordre, hex à dégât…) sur la
    // séquence d'attaques. Présent sur les riders de la liste blanche uniquement — absent des sorts
    // directs ET des attaques d'arme (comptées 1 proc, leurs coups suffisent). Écrit dans le slot
    // (SpikeProcs) → Mutated → recalcul.
    public bool HasProcs { get; init; }

    // Lignes ARTIFICIELLES de vol de vie (mods vampiriques) : elles n'ont pas de slot, leur
    // compteur vit au niveau du BUILD (une valeur par vol de 3 / de 5). Ces deux délégués
    // réaiguillent le même ComboBox « Procs » vers lui ; absents, on retombe sur le slot.
    public Func<int>? ProcsGetter { get; init; }
    public Action<int>? ProcsSetter { get; init; }

    public int ProcsValue
    {
        get => ProcsGetter is { } g ? g() : Slot?.SpikeProcs ?? 1;
        set
        {
            if (ProcsSetter is { } s) s(value);
            else if (Slot is not null) Slot.SpikeProcs = value;
        }
    }

    // Mod de PRÉFIXE physique de la ligne (aucun / de fractionnement / vampirique). Proposé tant
    // que le type de dégâts choisi n'est pas élémentaire — les deux occupent le même emplacement.
    // Mêmes délégués que le compteur de procs : sans slot (ligne d'ATTAQUE NORMALE), l'état vit sur
    // le perso, et le même ComboBox du gabarit y est réaiguillé.
    public bool CanChooseMod { get; init; }
    public IReadOnlyList<SpikeViewModel.WeaponModOption> ModOptions { get; init; } = [];
    public Func<string?>? ModKeyGetter { get; init; }
    public Action<string?>? ModKeySetter { get; init; }

    public string? ModKeyValue
    {
        get => ModKeyGetter is { } g ? g() : Slot?.SpikeWeaponModKey;
        set
        {
            if (ModKeySetter is { } s) s(value);
            else if (Slot is not null) Slot.SpikeWeaponModKey = value ?? string.Empty;
        }
    }

    // Case « Proc du fractionnement » : visible sous le mod de fractionnement seul. Cochée, elle
    // relève la pénétration de 20 points pour TOUTE la ligne (frappes multiples comprises).
    public bool HasSunderingProc { get; init; }
    public Func<bool>? SunderingProcGetter { get; init; }
    public Action<bool>? SunderingProcSetter { get; init; }

    public bool SunderingProcValue
    {
        get => SunderingProcGetter is { } g ? g() : Slot?.SpikeSunderingProc ?? false;
        set
        {
            if (SunderingProcSetter is { } s) s(value);
            else if (Slot is not null) Slot.SpikeSunderingProc = value;
        }
    }

    // Case « Arc corne » : visible quand l'arme de la ligne est un arc. +10 % de pénétration
    // permanente (pas un proc).
    public bool IsBowRow { get; init; }
    public Func<bool>? HornbowGetter { get; init; }
    public Action<bool>? HornbowSetter { get; init; }

    public bool HornbowValue
    {
        get => HornbowGetter is { } g ? g() : Slot?.SpikeHornbow ?? false;
        set
        {
            if (HornbowSetter is { } s) s(value);
            else if (Slot is not null) Slot.SpikeHornbow = value;
        }
    }

    // Part de dégâts conditionnelle (« X more damage if [état] ») : une case à cocher gate son
    // décompte (cochée = comptée, défaut). Liée directement à SpikeConditional du slot ; ConditionText
    // = clause capturée (« if target foe was above 50% Health »…) pour le tooltip de la case.
    public bool HasConditional { get; init; }
    public string? ConditionText { get; init; }

    // Compteur de SEUIL (« X for each [ressource] (maximum Y) ») : nombre d'unités comptées
    // (signets équipés, conditions sur la cible, enchantements…), 0..cap, cap = compte atteignant
    // le plafond annoncé (ou 8 sans plafond). Le slot stocke −1 = « auto/max » (défaut optimiste) ;
    // le ComboBox affiche le compte EFFECTIF et écrit le choix explicite → Mutated → recalcul.
    public bool HasThreshold { get; init; }
    public int ThresholdCap { get; init; }
    public IReadOnlyList<int> ThresholdOptions { get; init; } = [];
    public string? ThresholdClause { get; init; }
    public int ThresholdValue
    {
        get => Slot is { } s && s.SpikeThreshold >= 0
            ? Math.Min(s.SpikeThreshold, ThresholdCap) : ThresholdCap;
        set { if (Slot is not null) Slot.SpikeThreshold = value; }
    }

    // PV du lanceur pour Grenth's Balance (chantier 15) : deux champs sur la ligne (courants /
    // max), écrits dans le slot → Mutated → recalcul. HasCasterHp pilote leur visibilité.
    public bool HasCasterHp { get; init; }
    public int CasterCurrentHp
    {
        get => Slot?.SpikeCasterCurrentHp ?? SkillSlotViewModel.DefaultCasterHp;
        set { if (Slot is not null) Slot.SpikeCasterCurrentHp = value; }
    }
    public int CasterMaxHp
    {
        get => Slot?.SpikeCasterMaxHp ?? SkillSlotViewModel.DefaultCasterHp;
        set { if (Slot is not null) Slot.SpikeCasterMaxHp = value; }
    }
}

// Un toggle de buff d'arme sur la carte d'un membre du roster (fenêtre Spike, chantier 14) :
// icône grisée (inactif) / colorée (actif), clic → SetSpikeBuff du perso → Mutated → recalcul.
// Instances stables tant que l'offre du membre ne change pas ; RaiseActiveChanged réaligne
// l'icône quand l'état a bougé sans passer par le setter (undo, chargement .pn3).
public sealed class SpikeBuffToggleViewModel : ViewModelBase
{
    public required CharacterSlotViewModel Member { get; init; }
    public required SpikeWeaponBuffs.Descriptor Descriptor { get; init; }
    public string? IconPath { get; init; }
    public string Tooltip { get; init; } = string.Empty;
    // Sort d'altération d'arme (SkillType « Weapon Spell » : Sundering/Splinter/Brutal/Great
    // Dwarf) : un seul actif à la fois par cible (lore GW1). Renseigné par SyncWeaponBuffs.
    public bool IsWeaponSpell { get; init; }

    public bool IsActive
    {
        get => Member.IsSpikeBuffActive(Descriptor.Key);
        set
        {
            // Garde-fou GW1 : « a target can only have one weapon spell active at a time ;
            // recasting overwrites the previous one ». Activer un weapon spell éteint donc les
            // autres weapon spells du MÊME perso (leurs icônes se dé-surlignent via RaiseActiveChanged).
            if (value && IsWeaponSpell)
                foreach (var other in Member.SpikeBuffToggles)
                    if (!ReferenceEquals(other, this) && other.IsWeaponSpell
                        && Member.IsSpikeBuffActive(other.Descriptor.Key))
                    {
                        Member.SetSpikeBuff(other.Descriptor.Key, false);
                        other.RaiseActiveChanged();
                    }
            Member.SetSpikeBuff(Descriptor.Key, value);
            OnPropertyChanged();
        }
    }

    public void RaiseActiveChanged() => OnPropertyChanged(nameof(IsActive));
}
