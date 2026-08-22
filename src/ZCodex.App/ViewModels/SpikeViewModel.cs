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
    private static IReadOnlyList<WeaponTypeOption> TypeOptions(IEnumerable<string> values) =>
        new[] { new WeaponTypeOption(L("type natif de l'arme", "weapon's native type"), "") }
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
                // Vengeance s'applique aussi ici (« tout, sorts inclus » — décision Philippe).
                bool mwVengeance = buffCtx.GetValueOrDefault(member) is { Vengeance: true };
                var mw = analysis.Rows.Where(r => r.Kind == SkillDamage.RowKind.Damage).ToList();
                if (mw.Count > 0)
                {
                    var t = BuildMindWrackRow(member, skill, slot, mw[0].Value,
                        Math.Max(0, slot.SpikeProcs), hasProcs: true,
                        L("par point d'énergie perdu", "per point of energy lost"),
                        mwVengeance);
                    Rows.Add(t.Row); totalMin += t.Min; totalMax += t.Max;
                    totalFluxMin += t.FluxMin; totalFluxMax += t.FluxMax;
                }
                if (mw.Count > 1)
                {
                    var t = BuildMindWrackRow(member, skill, slot, mw[1].Value,
                        1, hasProcs: false, L("à énergie 0", "at 0 energy"), mwVengeance);
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
            bool judges = mb is { Judges: true } && isWeaponAttack;
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
            bool canChooseMod = isWeaponAttack && weapon is { IsCaster: false }
                                && !WeaponStrike.IsElementalType(slot.SpikeWeaponDamageType);
            var weaponMod = canChooseMod ? SpikeWeaponMods.FromKey(slot.SpikeWeaponModKey)
                                         : SpikeWeaponMod.None;
            // Les deux pénétrations sont de catégorie BONUS (wiki/Armor_penetration) : elles
            // s'AJOUTENT par-dessus le max des bases, comme Judge's Insight juste au-dessus.
            bool sunderingMod = weaponMod == SpikeWeaponMod.Sundering && slot.SpikeSunderingProc;
            bool isBowRow = isWeaponAttack && weapon is { } bw && WeaponStrike.IsBow(bw);
            bool hornbow = isBowRow && slot.SpikeHornbow;
            if (sunderingMod) pen += SpikeWeaponMods.SunderingBonusPen;
            if (hornbow) pen += SpikeWeaponMods.HornbowBonusPen;
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
                // Type de dégât de l'arme : celui choisi pour CETTE attaque (mods/skins,
                // switch d'armes — décision Philippe), sinon le type natif de l'arme.
                // Judge's Insight convertit l'attaque en SACRÉ (prioritaire sur le choix
                // manuel) : l'AL de la cible retombe sur son armure de BASE — aucun bonus
                // « vs sacré » n'existe, c'est tout l'intérêt du buff contre l'anti-physique.
                // Un type persisté hors catalogue de l'arme (.pn3 écrit avant ce filtrage, ou
                // arme changée sur une attaque libre : « contondant » gardé en passant de l'épée
                // à la hache) est ignoré — on retombe sur le type natif, comme pour SpikeWeaponKind.
                string picked = slot.SpikeWeaponDamageType;
                string weaponType = judges ? "holy"
                    : string.IsNullOrEmpty(picked)
                      || !WeaponStrike.DamageTypeChoices(weapon!).Contains(picked)
                        ? weapon!.DamageType : picked;
                int al = target.EffectiveArmor(weaponType, _withCrackedArmor);
                int rank = masteryRank!.Value;
                // Great Dwarf Weapon : « +X weapon damage » = vrai dégât d'ARME, ajouté AVANT
                // customisation/critique/armure (≠ un « +X damage » plat qui ignore l'armure).
                var w = mb is { GdwBonus: > 0 }
                    ? weapon! with { Min = weapon.Min + mb.GdwBonus, Max = weapon.Max + mb.GdwBonus }
                    : weapon!;
                int wMin, wMax;
                if (_allCrits || mods.AlwaysCritical)
                    wMin = wMax = WeaponStrike.CriticalAt(w, rank, al, pen,
                        mods.Multiplier, AttackerLevel) + bonus;
                else
                {
                    wMin = WeaponStrike.DamageAt(w.Min, rank, al, pen,
                        mods.Multiplier, AttackerLevel) + bonus;
                    wMax = WeaponStrike.DamageAt(w.Max, rank, al, pen,
                        mods.Multiplier, AttackerLevel) + bonus;
                }
                min += wMin; max += wMax; dmgMin += wMin; dmgMax += wMax;
                string range = wMin == wMax ? wMax.ToString() : $"{wMin}–{wMax}";
                string label = weaponType == weapon!.DamageType
                    ? weapon.DisplayName : $"{weapon.DisplayName} ({TypeLabel(weaponType)})";
                parts.Add(bonus > 0 ? $"{label} {range} {L($"(+{bonus} compris)", $"(+{bonus} included)")}" : $"{label} {range}");
                parts.Add(mods.AlwaysCritical ? L("critique forcé", "forced critical")
                    : $"{L("crit", "crit")} {100 * WeaponStrike.CriticalChance(rank, AttackerLevel, _targetLevel, slot.CriticalStrikesRank ?? 0):0} %");
                if (weaponDeduced) parts.Add(L("arme déduite", "deduced weapon"));
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
                int al = target.EffectiveArmor(r.DamageType, _withCrackedArmor);
                if (r.IsThreshold)
                {
                    // Seuil armor-RESPECTING (ex. Doom 50 foudre/rituel, hors v1) : le plafond porte
                    // sur les dégâts LISTÉS (AL 60), l'armure/pénétration s'appliquent au total.
                    int count = ThresholdCount(slot, r);
                    int listed = ThresholdListed(r, count);
                    int dmg = SkillDamage.DamageAt(listed, al, pen, AttackerLevel);
                    min += dmg; max += dmg; dmgMin += dmg; dmgMax += dmg;
                    thresholdRow = r;
                    parts.Add($"{TypeLabel(r.DamageType)} {r.Value} × {count} = {listed}{ThresholdCapNote(r, count)} → {dmg}");
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
                        ? $"{TypeLabel(r.DamageType)} {string.Join("+", ticksDmg)} "
                          + L("(ticks cumulatifs)", "(cumulative ticks)")
                        : $"{TypeLabel(r.DamageType)} {ticksDmg[0]}");
                }
                else
                {
                    int dmg = SkillDamage.DamageAt(r.Value, al, pen, AttackerLevel);
                    min += dmg * t; max += dmg * t; dmgMin += dmg * t; dmgMax += dmg * t;
                    parts.Add(t > 1 ? $"{TypeLabel(r.DamageType)} {dmg}{TimesLabel(r, t)}"
                                    : $"{TypeLabel(r.DamageType)} {dmg}");
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
            if (mb is { Vengeance: true } && dmgMax > 0)
            {
                int vMin = (int)(dmgMin * SpikeWeaponBuffs.VengeanceMultiplier);
                int vMax = (int)(dmgMax * SpikeWeaponBuffs.VengeanceMultiplier);
                min += vMin - dmgMin; max += vMax - dmgMax;
                dmgMin = vMin; dmgMax = vMax;
                // « Vengeance » est identique en FR et EN (vérifié DB) : seule la virgule décimale change.
                parts.Add(L("Vengeance (×1,25)", "Vengeance (×1.25)"));
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
        int value, int procs, bool hasProcs, string label, bool vengeance)
    {
        int dmg = value * procs;
        if (vengeance) dmg = (int)(dmg * SpikeWeaponBuffs.VengeanceMultiplier);
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

    // Bonus de dégâts dû au flux actif du build, par ligne (colonne « Bonus Flux » + Total).
    // Assiette = dégâts SEULS (dmgMin/dmgMax), hors vol/perte de vie (décision Philippe).
    // chainComboPct = bonus de chaîne précalculé pour ce slot (Chain Combo), 0 sinon.
    // targetPrimary = profession primaire choisie pour la cible (Amateur Hour / There Can Be Only One).
    private static (int Min, int Max) FluxDamageBonus(
        Flux? flux, CharacterSlotViewModel member, Skill skill, int dmgMin, int dmgMax,
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
            Flux.AmateurHour when member.SecondaryProfession != Profession.None
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
    private sealed record MemberBuffs(HashSet<SkillSlotViewModel> SunderingSlots, bool Judges,
                                      bool Vengeance, int DwgPen,
                                      SkillSlotViewModel? AnthemSlot, int AnthemBonus,
                                      HashSet<SkillSlotViewModel> SplinterSlots, int SplinterBonus,
                                      int BrutalBonus, int GdwBonus,
                                      SkillSlotViewModel? FtwSlot, int FtwBonus,
                                      IReadOnlyDictionary<SpikeBuff, string> Names);

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
                ftwSlot, ftwSlot is not null ? ftwBonus : 0,
                buffNames);
        }
        return ctx;
    }

    private static bool IsPvpCopy(Skill s) => s.Name.EndsWith("(PvP)", StringComparison.OrdinalIgnoreCase);

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
    public bool CanChooseMod { get; init; }
    public IReadOnlyList<SpikeViewModel.WeaponModOption> ModOptions { get; init; } = [];

    // Case « Proc du fractionnement » : visible sous le mod de fractionnement seul. Cochée, elle
    // relève la pénétration de 20 points pour TOUTE la ligne (frappes multiples comprises).
    public bool HasSunderingProc { get; init; }

    // Case « Arc corne » : visible quand l'arme de la ligne est un arc. +10 % de pénétration
    // permanente (pas un proc).
    public bool IsBowRow { get; init; }

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
