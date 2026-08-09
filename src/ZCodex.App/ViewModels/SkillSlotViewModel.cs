using ZCodex.Core.Data;
using ZCodex.Core.Models;

namespace ZCodex.App.ViewModels;

public class SkillSlotViewModel : ViewModelBase
{
    private Skill? _skill;

    public Skill? Skill
    {
        get => _skill;
        set
        {
            bool changed = SetField(ref _skill, value);
            OnPropertyChanged(nameof(HasSkill));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IsElite));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(WeaponMastery));
            OnPropertyChanged(nameof(WeaponMasteryRank));
            OnPropertyChanged(nameof(StrengthRank));
            OnPropertyChanged(nameof(CriticalStrikesRank));
            OnPropertyChanged(nameof(ExpertiseRank));
            OnPropertyChanged(nameof(SpawningPowerRank));
            RaiseBarredChanged();
            // La sélection spike visait la skill précédente : elle ne survit pas au remplacement.
            if (changed) IsSpikeSelected = false;
        }
    }

    // Slot compté dans le « Spike damage calculus » (cadre vert dans la fenêtre Spike).
    // Persisté (.pn3 v6) pour les membres du roster spike du build.
    private bool _isSpikeSelected;
    public bool IsSpikeSelected
    {
        get => _isSpikeSelected;
        set
        {
            if (!SetField(ref _isSpikeSelected, value)) return;
            // Le type d'arme custom et le compteur de ticks suivent la sélection :
            // décochée → retour au type natif et à 1 tick.
            if (!value)
            {
                SpikeWeaponDamageType = string.Empty;
                SpikeTicks = 1;
                SpikeOrder = 0;
                SpikeWeaponKind = string.Empty;
                SpikeProcs = 1;
                SpikeConditional = true;
                SpikeThreshold = -1;
                SpikeCasterCurrentHp = DefaultCasterHp;
                SpikeCasterMaxHp = DefaultCasterHp;
                SpikeWeaponModKey = string.Empty;   // le setter remet aussi SpikeSunderingProc à false
                SpikeHornbow = false;
            }
        }
    }

    // Type de dégât de l'arme pour CETTE attaque du spike (mods élémentaires, skins jitte/pic/
    // battlepick/« Sufferer », switch d'armes — ex. Backbreaker marteau puis dagues). Vide =
    // type natif de l'arme. Persisté (.pn3 v6) avec la sélection.
    private string _spikeWeaponDamageType = string.Empty;
    public string SpikeWeaponDamageType
    {
        get => _spikeWeaponDamageType;
        set
        {
            if (!SetField(ref _spikeWeaponDamageType, value)) return;
            // Exclusivité de l'emplacement de PRÉFIXE de l'arme : un mod ÉLÉMENTAIRE chasse le mod
            // physique, sans valeur cachée conservée (décision Philippe). Les autres types de la
            // liste sont des SKINS (hache perforante, faux « Sufferer »…) et laissent le préfixe libre.
            if (WeaponStrike.IsElementalType(value)) SpikeWeaponModKey = string.Empty;
        }
    }

    // Mod de PRÉFIXE physique simulé sur CETTE attaque du spike : clé SpikeWeaponMods
    // ("sundering" / "vampiric"), "" = aucun. Persisté (.zcx v18) avec la sélection.
    private string _spikeWeaponModKey = string.Empty;
    public string SpikeWeaponModKey
    {
        get => _spikeWeaponModKey;
        set
        {
            if (!SetField(ref _spikeWeaponModKey, value)) return;
            // La case du proc n'a de sens que sous « de fractionnement » : elle ne survit pas à un
            // changement de mod (même politique que le type d'arme à la désélection).
            if (SpikeWeaponMods.FromKey(value) != SpikeWeaponMod.Sundering) SpikeSunderingProc = false;
        }
    }

    // Proc 20 % du mod de fractionnement compté sur CETTE ligne : relève la pénétration de 20 points
    // pour TOUTE la ligne (coup d'arme et paquets soumis à l'armure), et pour toutes les frappes
    // d'une attaque multi-coups — raccourci optimiste assumé, comme « Visez les yeux ! ».
    // Persisté (.zcx v18).
    private bool _spikeSunderingProc;
    public bool SpikeSunderingProc
    {
        get => _spikeSunderingProc;
        set => SetField(ref _spikeSunderingProc, value);
    }

    // L'arc de CETTE attaque est un arc corne : +10 % de pénétration permanente (pas un proc).
    // Par ATTAQUE et non par perso, comme tout le reste du modèle (les switchs d'armes existent).
    // Ignoré hors arc. Persisté (.zcx v18).
    private bool _spikeHornbow;
    public bool SpikeHornbow
    {
        get => _spikeHornbow;
        set => SetField(ref _spikeHornbow, value);
    }

    // Nombre de ticks comptés pour CE skill du spike (dégâts périodiques « each second » ;
    // Savannah Heat cumulative — le tick n vaut n × la valeur). 1 = un seul tick.
    // Persisté (.pn3 v6) avec la sélection.
    private int _spikeTicks = 1;
    public int SpikeTicks
    {
        get => _spikeTicks;
        set => SetField(ref _spikeTicks, value);
    }

    // Nombre de procs comptés pour CE slot du spike : déclenchements d'un rider (conjuration, ordre,
    // hex à dégât…) sur la séquence d'attaques. Multiplie TOUTE la contribution de la skill (avec le
    // nb de coups intrinsèque). Réservé aux riders (liste blanche) hors attaque d'arme ; une attaque
    // reste à 1 proc. Défaut 1, remis à 1 à la désélection. Persisté (.pn3 v8).
    private int _spikeProcs = 1;
    public int SpikeProcs
    {
        get => _spikeProcs;
        set => SetField(ref _spikeProcs, value);
    }

    // Part de dégâts CONDITIONNELLE comptée pour CE slot du spike (« X more damage if [état] » :
    // Aftershock/assommé, Deathly Chill/>50 % PV, Smite & Spear of Light/attaque, Signet of
    // Shadows/aveuglé…). true = comptée (défaut, part supposée remplie — esprit « spike optimiste » ;
    // décocher pour l'exclure). Remis à true à la désélection. Persisté (.pn3 v9).
    private bool _spikeConditional = true;
    public bool SpikeConditional
    {
        get => _spikeConditional;
        set => SetField(ref _spikeConditional, value);
    }

    // Compte d'unités du paquet à SEUIL de CE slot du spike (« X for each [ressource] (maximum Y) » :
    // Symbolic Strike/signets, Signet of Deadly Corruption/conditions, Aneurysm/énergie…).
    // −1 = auto (défaut optimiste : le compte atteignant le plafond — même esprit que la case
    // conditionnelle cochée). Remis à −1 à la désélection. Persisté (.pn3 v10).
    private int _spikeThreshold = -1;
    public int SpikeThreshold
    {
        get => _spikeThreshold;
        set => SetField(ref _spikeThreshold, value);
    }

    // PV du lanceur pour Grenth's Balance (chantier 15) : le dégât = min((PVcible − PVlanceur)/2,
    // PVlanceur_max − PVlanceur), plancher 0 (mécanique « deux gobelets », cf. SpikeGrenth).
    // Défaut = 480 (lanceur niveau 20 à pleine vie → dégât 0 tant que non baissé). Remis à 480 à
    // la désélection. Persisté (.pn3 v12). Ignorés hors Grenth's Balance.
    public const int DefaultCasterHp = 480;
    private int _spikeCasterCurrentHp = DefaultCasterHp;
    public int SpikeCasterCurrentHp
    {
        get => _spikeCasterCurrentHp;
        set => SetField(ref _spikeCasterCurrentHp, value);
    }
    private int _spikeCasterMaxHp = DefaultCasterHp;
    public int SpikeCasterMaxHp
    {
        get => _spikeCasterMaxHp;
        set => SetField(ref _spikeCasterMaxHp, value);
    }

    // Ordre de cast dans le Spike (indice de sélection 1..N, pour Chain Combo). 0 = non sélectionné.
    // Ré-indexé par TeamBuildViewModel.CompactSpikeOrder à chaque (dé)sélection. Persisté (.pn3 v7).
    private int _spikeOrder;
    public int SpikeOrder
    {
        get => _spikeOrder;
        set => SetField(ref _spikeOrder, value);
    }

    // Arme choisie manuellement pour une attaque d'arme LIBRE non déductible (ex. Bull's Strike sur
    // un perso sans autre attaque d'arme dans sa barre). Clé = nom de la maîtrise (« Hammer Mastery »),
    // "" = automatique/aucune. Persisté (.pn3 v7) avec la sélection.
    private string _spikeWeaponKind = string.Empty;
    public string SpikeWeaponKind
    {
        get => _spikeWeaponKind;
        set
        {
            if (!SetField(ref _spikeWeaponKind, value)) return;
            // L'infobulle suit le choix : sa table d'arme et le rang qui la scale en dépendent.
            OnPropertyChanged(nameof(WeaponMastery));
            OnPropertyChanged(nameof(WeaponMasteryRank));
        }
    }

    // Arme effective de la ligne. Le choix manuel de la fenêtre Spike prime, mais UNIQUEMENT sur
    // une attaque à arme libre : sur une attaque à arme imposée (Axe Attack, Spear Melee Attack…)
    // un SpikeWeaponKind résiduel — hérité d'une skill remplacée dans le slot — ne doit pas
    // pouvoir travestir l'arme. Sinon : arme du type d'attaque, ou de l'attribut s'il est une
    // maîtrise. Null = pas une attaque d'arme, ou arme libre encore indéterminée.
    public WeaponStrike.Weapon? EffectiveWeapon =>
        _skill is not { } s ? null
        : (WeaponStrike.IsFreeWeaponAttack(s) ? WeaponStrike.ByMasteryName(_spikeWeaponKind) : null)
          ?? WeaponStrike.For(s);

    // Maîtrise de l'arme effective, pour l'infobulle (qui n'a pas accès au slot).
    public string? WeaponMastery => EffectiveWeapon?.Mastery;

    // Perso propriétaire (câblé par CharacterSlotViewModel).
    public CharacterSlotViewModel? Owner { get; set; }

    // Position 0–7 du slot dans la barre (câblée par CharacterSlotViewModel).
    public int SlotIndex { get; set; }

    // Slot d'une variante identique au slot homologue du parent — diff VIVANTE, affichage seul
    // (la donnée reste indépendante). Recalculé sur changement de ce slot ou du slot parent.
    public bool IsSameAsParent =>
        Owner?.Parent is { } p && SlotIndex < p.SkillSlots.Count && p.SkillSlots[SlotIndex].Skill == _skill;

    // Afficher la diagonale "hérité" : variante + identique au parent + toggle d'affichage actif.
    public bool ShowBarred =>
        Owner is { Parent: not null } o && IsSameAsParent && (o.OwnerBuild?.ShowInheritedAsBarred ?? true);

    public void RaiseBarredChanged()
    {
        OnPropertyChanged(nameof(IsSameAsParent));
        OnPropertyChanged(nameof(ShowBarred));
    }

    // Footer d'attribut primaire : bonus des carac primaires investies du perso (identique pour
    // toutes les skills) ; recalculé et poussé par le perso via RaiseTooltipChanged.
    public string Footer => Owner?.ComputeFooter() ?? string.Empty;

    // Description de l'infobulle : variables résolues au rang de l'attribut de la skill.
    public string Description => Owner is { } o && _skill is { } s ? o.ResolveDescription(s) : string.Empty;

    // Variante AFFICHÉE en mode FR (null → l'infobulle retombe sur Description, EN).
    public string? DisplayDescription => Owner is { } o && _skill is { } s ? o.ResolveDisplayDescription(s) : null;

    // Rang effectif de la maîtrise de l'ARME du type d'attaque (ex : Précision pour un Bow Attack
    // lié à Expertise — c'est la réquisition de l'arme qui scale les dégâts). Null = pas une
    // attaque d'arme, ou perso sans cette maîtrise → pas de table d'arme dans l'infobulle.
    public int? WeaponMasteryRank =>
        Owner is { } o && EffectiveWeapon is { } w ? WeaponStrike.StrikeRank(w, o.AttributeLevel) : null;

    // Rang de Force du perso pour la pénétration d'armure naturelle des attaques d'arme
    // (1 %/rang — wiki/Strength) : rang investi (PR = W) ou forcé à 5 par le mod « of the
    // Warrior », déjà couverts par AttributeLevel. Null hors attaque d'arme (sorts, Pet Attack
    // — exclusion explicite du wiki) ou sans Force → pas de pénétration naturelle.
    public int? StrengthRank =>
        Owner is { } o && _skill is { } s && WeaponStrike.IsWeaponAttack(s) ? o.AttributeLevel("Strength") : null;

    // Rang de Critical Strikes du perso pour le taux de critique des attaques d'arme (+1 %/rang
    // multiplicatif, TOUTES armes — wiki/Critical_Strikes) : rang investi (PR = A) ou forcé à 5
    // par le mod « of the Assassin », déjà couverts par AttributeLevel. Null hors attaque d'arme.
    public int? CriticalStrikesRank =>
        Owner is { } o && _skill is { } s && WeaponStrike.IsWeaponAttack(s) ? o.AttributeLevel("Critical Strikes") : null;

    // Rang d'Expertise du perso (attribut primaire Ranger) pour la réduction du coût en énergie :
    // 4 %/rang sur les attaques, compétences de contact (Touch), rituels et TOUS les skills Ranger.
    // Rang investi (PR = Ranger) ou forcé à 5 par le mod « of the Ranger », déjà couverts par
    // AttributeLevel. Null si la skill n'est pas concernée → coût de base affiché sans réduction.
    public int? ExpertiseRank =>
        Owner is { } o && _skill is { } s && Expertise.Affects(s) ? o.AttributeLevel(Expertise.AttributeName) : null;

    // Rang de Puissance de l'invocation du perso (attribut primaire Ritualiste) : +4 %/rang sur la
    // DURÉE des sorts d'altération d'arme et sur les PV MAX des créatures créées (esprits des rituels
    // d'asservissement ET de la nature, serviteurs morts-vivants). Rang investi (PR = Ritualiste) ou
    // forcé à 5 par le mod « of the Ritualist », déjà couverts par AttributeLevel. Null si la skill
    // n'est pas concernée → aucun bloc d'invocation dans l'infobulle.
    public int? SpawningPowerRank =>
        Owner is { } o && _skill is { } s && SpawningPower.Affects(s)
            ? o.AttributeLevel(SpawningPower.AttributeName) : null;

    // Réduction d'énergie du flux « énergie » actif (Jack of All Trades 20, All In 25 ; 0 sinon),
    // appliquée APRÈS Expertise. Identique pour toutes les skills du perso (portée = barre entière).
    public int FluxEnergyPercent => Owner?.FluxEnergyPercent ?? 0;

    // Flux Jack of All Trades : +15 % dégâts (table d'armure) et 25 % d'activation en moins. 0 sinon.
    public int FluxDamagePercent => Owner?.FluxDamagePercent ?? 0;
    public int FluxCastPercent   => Owner?.FluxCastPercent ?? 0;

    // Rituels de la nature actifs (environnement) + surcoût Roaring Winds, fournis par le perso.
    public IReadOnlySet<NatureRitualData.Ritual> NatureRituals =>
        Owner?.ActiveNatureRituals ?? CharacterSlotViewModel.NoRituals;
    public int RoaringWindsBonus => Owner?.RoaringWindsBonus ?? 0;

    // ── Durée d'enchantement (Lot D) : % applicables à CETTE compétence, fournis par le perso ──
    // L'arme « of Enchanting » et Tranquility ne touchent que les enchantements ; le prolongateur
    // personnel (Blessed Aura/Extend) gère lui-même sa cible (Monk/Derviche). L'infobulle compose.
    public int EnchantEnchantingPct =>
        Owner is { } o && _skill is { } s && NatureRitualData.IsEnchantment(s) ? o.EnchantingModPercent : 0;
    public int EnchantExtenderPct =>
        Owner is { } o && _skill is { } s ? o.ExtenderPercentFor(s) : 0;
    public int EnchantTranquilityPct =>
        Owner is { } o && _skill is { } s && NatureRitualData.IsEnchantment(s) ? o.TranquilityPercent : 0;

    // Permet au perso de pousser une mise à jour live de l'infobulle (footer + description).
    public void RaiseTooltipChanged()
    {
        OnPropertyChanged(nameof(Footer));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(DisplayDescription));
        OnPropertyChanged(nameof(WeaponMasteryRank));
        OnPropertyChanged(nameof(StrengthRank));
        OnPropertyChanged(nameof(CriticalStrikesRank));
        OnPropertyChanged(nameof(ExpertiseRank));
        OnPropertyChanged(nameof(SpawningPowerRank));
        OnPropertyChanged(nameof(FluxEnergyPercent));
        OnPropertyChanged(nameof(FluxDamagePercent));
        OnPropertyChanged(nameof(FluxCastPercent));
        OnPropertyChanged(nameof(NatureRituals));
        OnPropertyChanged(nameof(RoaringWindsBonus));
        OnPropertyChanged(nameof(EnchantEnchantingPct));
        OnPropertyChanged(nameof(EnchantExtenderPct));
        OnPropertyChanged(nameof(EnchantTranquilityPct));
    }

    private bool _hasViolation;

    // Ordre d'ajout d'une skill PvE dans ce slot (pour le cap PvE : on retire la plus récente).
    public long PveSeq { get; set; }

    public bool HasSkill      => _skill != null;
    public bool IsEmpty       => _skill == null;
    public bool IsElite       => _skill?.IsElite ?? false;
    public string DisplayName => _skill?.DisplayName ?? "?";

    // Bascule de langue : le nom de la compétence n'est pas seul à dépendre d'AppLanguage.IsFr —
    // le CORPS de l'infobulle en dépend aussi (Description/DisplayDescription), et l'infobulle est
    // construite d'emblée par slot : sans re-notification, elle gardait le texte de la langue
    // précédente jusqu'au prochain changement d'attribut. RaiseTooltipChanged couvre les deux.
    public void RaiseLanguageChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        RaiseTooltipChanged();
    }

    public bool HasViolation
    {
        get => _hasViolation;
        set => SetField(ref _hasViolation, value);
    }
}
