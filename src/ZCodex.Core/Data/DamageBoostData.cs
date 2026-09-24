using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Ce qu'un effet actif fait aux dégâts (chantier infobulle, lot 6a).</summary>
public enum DamageBoostKind
{
    /// <summary>Paquet de dégâts ajouté à l'attaque → ligne « bonus d'effets » de la table (option B
    /// de la maquette, Q2 du cadrage).</summary>
    Damage,
    /// <summary>Chiffre RELEVÉ DANS LE TEXTE de la description de la compétence touchée (Q12) : les deux
    /// seuls cas du lot sont les attaques du familier et celles des esprits, dont aucun paquet n'est un
    /// bonus de l'attaque du perso.</summary>
    TextDamage,
    /// <summary>Chance de coup critique en points de pourcentage, ajoutée au taux affiché (Q7).</summary>
    CriticalChance,
    /// <summary>Pénétration d'armure de BASE : NON cumulable, seul le MAX compte (Q8).</summary>
    BasePenetration,
}

/// <summary>Qui l'effet touche. Les périmètres d'ARME réutilisent <see cref="ConditionWeaponScope"/>
/// (lot 4b) pour ne pas re-coder « vos flèches » ni « en mêlée » une seconde fois.</summary>
public enum DamageBoostScope
{
    /// <summary>« Your attacks » : toute attaque d'ARME du perso. Exclut le familier (glossaire G1 :
    /// Preneur d'Âmes ne touche que les attaques du LANCEUR) et les esprits.</summary>
    Attacks,
    /// <summary>« Your arrows » / « your bow attack skills ». Dans l'infobulle les deux désignent le même
    /// ensemble (glossaire G4) : les 48 compétences d'attaque à l'arc tirent toutes une flèche.</summary>
    BowAttacks,
    /// <summary>« In melee » (glossaire G2).</summary>
    MeleeAttacks,
    /// <summary>« While wielding daggers » (Méthode de l'Assassin).</summary>
    DaggerAttacks,
    /// <summary>« While holding a non-dagger weapon » (Méthode du Maître) : l'exact complément de
    /// <see cref="DaggerAttacks"/> — les deux ne peuvent donc jamais agir sur la même attaque.</summary>
    NonDaggerAttacks,
    /// <summary>Attaques du familier (Agression barbare).</summary>
    PetAttacks,
    /// <summary>Attaques des esprits contrôlés (Sceau de puissance spectrale).</summary>
    SpiritAttacks,
    /// <summary>« Your Ritualist skills » (Glaive était destructrice, Daoshen était cruel).</summary>
    RitualistSkills,
}

/// <summary>
/// Un effet actif du perso qui modifie les dégâts d'autres compétences (lot 6a). La valeur n'est PAS
/// recopiée ici : elle se lit dans la progression de la compétence, à l'index
/// <paramref name="Index"/> — tous SONDÉS dans la base réelle (§ 6.5 du plan), aucun deviné à l'œil,
/// et l'ordre des lignes de progression ne suit PAS celui de la description (« Craignez-moi ! » porte
/// son critique à l'index 2 alors que le texte le cite en second). <paramref name="Fixed"/> &gt; 0 =
/// valeur littérale de la description, sans progression (Sceau de force +5, les pénétrations).
/// <paramref name="RequiresElement"/> = l'effet exige que l'arme inflige ce type de dégâts (les 3
/// conjurations et l'Aura de poussière d'ébène) : le § 6.1 du plan dit comment on en juge.
/// <paramref name="BaseSkillId"/> = id PvE d'une variante « (PvP) » : l'icône est mémorisée sur l'id
/// de base. La caractéristique d'échelle n'est jamais listée : c'est TOUJOURS celle de la compétence
/// source elle-même, lue par l'appelant (donc la substitution du lot 5 s'y applique gratuitement).
/// </summary>
public sealed record DamageBoostDescriptor(
    int SkillId, DamageBoostScope Scope, DamageBoostKind Kind,
    int Index = -1, int Fixed = 0, string? DamageType = null, bool IsBonus = true,
    string? RequiresElement = null, int BaseSkillId = 0)
{
    /// <summary>Id sous lequel l'icône est mémorisée (et persistée) : l'id de base.</summary>
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;
}

/// <summary>Un paquet de dégâts ajouté par un effet actif : <paramref name="IgnoresArmor"/> suit la
/// règle maison (un bonus « +X » ignore l'armure, un paquet typé sans « + » la subit), donc les
/// conjurations rejoignent la ligne « bonus d'effets » sans colonne typée (Q3) tandis que les Flèches
/// de feu, seul paquet du lot sans « + », varient bien d'une colonne d'AL à l'autre.</summary>
public readonly record struct DamageBoostPacket(int Value, string? DamageType, bool IgnoresArmor);

/// <summary>
/// Ce que les effets actifs du perso font aux dégâts de UNE compétence (lot 6a).
/// <paramref name="Packets"/> = la ligne « bonus d'effets » ; <paramref name="CriticalPercent"/> =
/// points de pourcentage à ajouter au taux de critique affiché (plafonné à 100 par l'affichage, Q7) ;
/// <paramref name="BasePenetration"/> = pénétration de BASE la plus forte apportée par un effet (elle
/// entre en MAX avec celle de la description et le rang de Force) ; <paramref name="BonusPenetration"/>
/// = pénétration en BONUS, qui se CUMULE par-dessus le max de base (mod d'arme « de fractionnement »).
/// </summary>
public readonly record struct DamageBoosts(
    IReadOnlyList<DamageBoostPacket>? Packets = null,
    int CriticalPercent = 0,
    int BasePenetration = 0,
    int BonusPenetration = 0)
{
    /// <summary>Au moins un effet à afficher — sinon l'infobulle n'ajoute ni ligne ni chiffre.</summary>
    public bool Any => Packets is { Count: > 0 } || CriticalPercent > 0
                       || BasePenetration > 0 || BonusPenetration > 0;

    /// <summary>Somme des paquets contre une armure donnée : les paquets qui ignorent l'armure passent
    /// tels quels, les autres par la formule de dégâts. Un seul chiffre par colonne d'AL, comme la
    /// maquette (option B).</summary>
    public int TotalAt(int armorLevel, int armorPenetration, int casterLevel)
    {
        int total = 0;
        foreach (var p in Packets ?? [])
            total += p.IgnoresArmor ? p.Value
                   : SkillDamage.DamageAt(p.Value, armorLevel, armorPenetration, casterLevel);
        return total;
    }
}

/// <summary>
/// Les sources de bonus de dégâts portées par la CARTE DU PERSO (lot 6a du chantier infobulle,
/// recensement du § 6.2 du plan). Les effets reçus d'un allié (lot 6b), le bandeau d'équipe (lot 6c)
/// et la fenêtre Spike (lot 6d) viendront s'ajouter ici.
///
/// ⚠ Les trois noms français en « fractionnement / fragmentation » désignent trois choses
/// différentes (§ 6.4 du plan) : le mod d'arme Sundering (ici, pénétration en BONUS), le sort d'arme
/// Sundering Weapon 2148 (pénétration de BASE, lot 6b) et Splinter Weapon 792 (écartée du chantier).
/// </summary>
public static class DamageBoostData
{
    public const int ConjureFlameSkillId     = 182;
    public const int ConjureFrostSkillId     = 207;
    public const int ConjureLightningSkillId = 221;
    public const int IgniteArrowsSkillId     = 431;
    public const int FeralAggressionSkillId  = 2142;
    public const int GhostlyMightSkillId     = 1742;
    public const int GhostlyMightPvpSkillId  = 2966;
    public const int FearMeSkillId           = 366;
    public const int SiphonStrengthSkillId   = 827;

    /// <summary>Id d'icône du mod d'arme « de fractionnement » : ce n'est pas une compétence, donc un id
    /// réservé négatif, comme le mod « Furieux » du lot 1a (qui occupe −1).</summary>
    public const int SunderingModToggleId = -2;

    public static readonly IReadOnlyList<DamageBoostDescriptor> All = new DamageBoostDescriptor[]
    {
        // ── Paquets de dégâts ajoutés aux attaques du perso ───────────────────
        // Conjurations : « Your attacks hit for +X <élément> damage. No effect unless your weapon deals
        // <élément> damage. » Le bonus est un « +X » → il ignore l'armure (Q3), pas de colonne typée.
        new(ConjureFlameSkillId,     DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 0, DamageType: "fire",      RequiresElement: "fire"),
        new(ConjureFrostSkillId,     DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 0, DamageType: "cold",      RequiresElement: "cold"),
        new(ConjureLightningSkillId, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 0, DamageType: "lightning", RequiresElement: "lightning"),
        // Flèches enflammées (Kindle Arrows) : la SEULE qui convertit les flèches en feu — elle est donc
        // aussi dans ConditionDurationData.Converters, et c'est elle qui fait passer une Conjuration de
        // flamme. ⚠ Ne pas la confondre avec les Flèches de feu (glossaire G5).
        new(433,  DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 0, DamageType: "fire"),
        // Flèches de feu (Ignite Arrows) : « Your arrows deal X fire damage to target and foes adjacent »
        // — pas de « + », donc le SEUL paquet du lot qui SUBIT l'armure. Elle ne convertit rien.
        new(IgniteArrowsSkillId, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 0, DamageType: "fire", IsBonus: false),
        new(1199, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1),                       // Flèches de verre
        new(3145, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1, BaseSkillId: 1199),    // Flèches de verre (PvP)
        new(2145, DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1),                       // Concentration experte
        new(429,  DamageBoostScope.BowAttacks, DamageBoostKind.Damage, Index: 1),                       // Flèches de Melandru (cible enchantée : Q9)
        // Preneur d'Âmes : « Attacks deal +X damage » = les attaques du LANCEUR seul (glossaire G1).
        // ⚠ L'index 2 porte le SACRIFICE, aux chiffres identiques : sonde obligatoire.
        new(3423, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // Preneur d'Âmes (PvE)
        new(1736, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // Force de l'esprit (sous sort d'arme : Q9)
        new(1760, DamageBoostScope.MeleeAttacks, DamageBoostKind.Damage, Index: 0, DamageType: "earth", RequiresElement: "earth"), // Aura de poussière d'ébène
        new(944,  DamageBoostScope.Attacks,     DamageBoostKind.Damage, Fixed: 5),                       // Sceau de force (+5 littéral, charges non comptées)
        new(2355, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // « Je suis le plus fort ! » (PvE)
        new(2354, DamageBoostScope.Attacks,     DamageBoostKind.Damage, Index: 1),                       // « Esquive ceci ! » (PvE)

        // ── Chiffres relevés DANS LE TEXTE de la compétence touchée (Q12) ─────
        new(FeralAggressionSkillId, DamageBoostScope.PetAttacks,    DamageBoostKind.TextDamage, Index: 1),
        new(GhostlyMightSkillId,    DamageBoostScope.SpiritAttacks, DamageBoostKind.TextDamage, Index: 1),
        // ⚠ Mécanique DIVERGENTE en PvP (cas d'école des variantes « (PvP) ») : le core donne +5…9…10 à
        // TOUS les esprits contrôlés, la PvP +5…29…35 à UNE SEULE créature invoquée alliée, détruite au
        // bout de 10 s. Décision du 24/09/2026 : on l'applique quand même à tous les esprits, et la note
        // d'icône dit qu'un seul en profite en jeu — même idiome que « icône allumée = condition remplie ».
        new(GhostlyMightPvpSkillId, DamageBoostScope.SpiritAttacks, DamageBoostKind.TextDamage, Index: 0, BaseSkillId: GhostlyMightSkillId),

        // ── Critique ─────────────────────────────────────────────────────────
        // Recensement CLOS le 24/09/2026 : balayage des 1517 descriptions sur « chance … critical »,
        // 5 sources en tout dont 4 sur la carte du perso (la 5e, « Visez les yeux ! », est au bandeau →
        // lot 6c). ⚠ Elles s'ADDITIONNENT (Q7 : « s'ajoute tel quel au taux affiché »), contrairement aux
        // pénétrations de base — seul l'AFFICHAGE plafonne à 100 %.
        // « Craignez-moi ! » : en mêlée contre un ennemi IMMOBILE (Q9 : icône allumée = il l'est).
        // ⚠ Index 2, pas 0 ni 1.
        new(FearMeSkillId, DamageBoostScope.MeleeAttacks, DamageBoostKind.CriticalChance, Index: 2),
        new(1018, DamageBoostScope.Attacks,         DamageBoostKind.CriticalChance, Index: 1),  // Œil critique : toutes armes
        new(1649, DamageBoostScope.DaggerAttacks,   DamageBoostKind.CriticalChance, Index: 1),  // Méthode de l'Assassin : dagues
        new(2187, DamageBoostScope.NonDaggerAttacks, DamageBoostKind.CriticalChance, Index: 0), // Méthode du Maître : hors dagues
        // Siphon de force : maléfice posé sur l'ennemi, mais c'est NOTRE taux de critique qui monte, et
        // seulement contre cet ennemi-là (Q9 : icône allumée = c'est bien lui qu'on frappe). +50 littéral.
        new(SiphonStrengthSkillId, DamageBoostScope.Attacks, DamageBoostKind.CriticalChance, Fixed: 50),

        // ── Pénétration d'armure de BASE (non cumulable, seul le max compte) ──
        new(1732, DamageBoostScope.RitualistSkills, DamageBoostKind.BasePenetration, Fixed: 20),                     // Glaive était destructrice
        new(3157, DamageBoostScope.RitualistSkills, DamageBoostKind.BasePenetration, Fixed: 10, BaseSkillId: 1732),  // Glaive était destructrice (PvP)
        new(1218, DamageBoostScope.RitualistSkills, DamageBoostKind.BasePenetration, Fixed: 10),                     // Daoshen était cruel
    };

    private static readonly Dictionary<int, DamageBoostDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);

    private static readonly HashSet<int> _toggleIds =
        All.Select(d => d.ToggleId).Append(SunderingModToggleId).ToHashSet();

    public static DamageBoostDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);

    /// <summary>Valeur de l'effet au rang donné : le littéral de la description, ou la colonne de
    /// progression sondée. 0 = rien à afficher (progression absente, ou rang inconnu — un rang de titre
    /// que le perso n'a pas laisse la description en plage, donc l'effet n'a pas de chiffre non plus).</summary>
    public static int ValueOf(DamageBoostDescriptor descriptor, Skill source, int rank)
    {
        if (descriptor.Fixed > 0) return descriptor.Fixed;
        if (descriptor.Index < 0 || source.Progression is not { } prog || descriptor.Index >= prog.Length)
            return 0;
        return SkillProgression.IntAt(prog[descriptor.Index], rank) ?? 0;
    }

    // ── Périmètres ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Une PRÉPARATION ne porte AUCUN effet sur Tir de barrage (395) ni sur Volée (2144) : elles
    /// retirent toutes les préparations AVANT que les flèches touchent. Vaut pour le bonus de dégâts
    /// comme pour la CONVERSION du type de dégâts — sur un Tir de barrage, les Flèches enflammées ne
    /// convertissent rien, donc c'est le mod d'arme (une corde du froid, par exemple) qui décide.
    /// Bug relevé par Philippe à la QA du lot 6a ; la règle existait déjà au lot 4b pour les conditions,
    /// elle est ici partagée par les deux côtés au lieu d'être réécrite.
    /// </summary>
    public static bool PreparationLost(Skill source, Skill target) =>
        source.SkillType == "Preparation" && ConditionDurationData.RemovesPreparations(target);

    /// <summary>L'effet de <paramref name="source"/> touche-t-il <paramref name="target"/> ?
    /// <paramref name="equipped"/> = l'arme de main du set actif (elle décide du périmètre des attaques à
    /// arme libre, comme au lot 4b).</summary>
    public static bool Affects(DamageBoostDescriptor descriptor, Skill source, Skill target, WeaponKind equipped) =>
        !PreparationLost(source, target)
        && descriptor.Scope switch
        {
            DamageBoostScope.Attacks         => WeaponStrike.IsWeaponAttack(target),
            DamageBoostScope.BowAttacks      => ConditionDurationData.InWeaponScope(ConditionWeaponScope.Bow, target, equipped),
            DamageBoostScope.MeleeAttacks    => ConditionDurationData.InWeaponScope(ConditionWeaponScope.Melee, target, equipped),
            DamageBoostScope.DaggerAttacks   => ConditionDurationData.InWeaponScope(ConditionWeaponScope.Daggers, target, equipped),
            DamageBoostScope.NonDaggerAttacks => WeaponStrike.IsWeaponAttack(target)
                                                && !ConditionDurationData.InWeaponScope(ConditionWeaponScope.Daggers, target, equipped),
            DamageBoostScope.PetAttacks      => target.SkillType == "Pet Attack",
            DamageBoostScope.SpiritAttacks   => SpiritAttackRange(target) is not null,
            DamageBoostScope.RitualistSkills => IsRitualistSkill(target),
            _                                => false,
        };

    /// <summary>Compétence Ritualiste au sens de « Your Ritualist skills ». ⚠ Les compétences
    /// d'allégeance sont stockées <see cref="Profession.None"/> mais verrouillées à une profession :
    /// Convocation des esprits est bien une compétence Ritualiste.</summary>
    public static bool IsRitualistSkill(Skill skill) =>
        skill.Profession == Profession.Ritualist
        || GwAllegianceData.RequiredProfession(skill) == Profession.Ritualist;

    // ── Chiffre relevé dans le texte (Q12) ────────────────────────────────────

    // Le paquet de dégâts des attaques d'un esprit se repère à sa clause, telle quelle (§ 6.6) : ni Union
    // ni Destruction ne l'ont (réduction de dégâts subis, nova de mort) → le Sceau de puissance spectrale
    // ne les touche pas, sans avoir à les nommer.
    private static readonly Regex SpiritAttackRegex = new(
        @"\bits attacks deal\s+(?<range>\d+(?:\.\.\.\d+)+)\s+damage\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Premier paquet de dégâts d'une attaque de familier. « [^.;]*? » borne la recherche de la clause
    // conditionnelle à la PHRASE : sur les 16 attaques de familier, 2 seulement ont deux paquets (Coup
    // brutal, Assaut de Melandru PvP) et dans les deux le second est explicitement conditionnel, donc
    // aucun arbitrage à rendre (§ 6.6). Le paquet PAR-UNITÉ du Coup enragé (PvP) — « for each of your
    // recharging Beast Mastery skills » — est écarté : y ajouter le bonus le multiplierait par le compte.
    private static readonly Regex PetDamageRegex = new(
        @"\+?(?<range>\d+(?:\.\.\.\d+)+)\s+(?:more\s+)?damage\b(?<tail>[^.;]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConditionalTailRegex = new(
        @"\bif\b|\bfor each\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Plage du paquet « Its attacks deal X damage » d'un rituel d'asservissement, ou null.</summary>
    public static string? SpiritAttackRange(Skill skill) =>
        SpiritAttackRegex.Match(skill.Description) is { Success: true } m ? m.Groups["range"].Value : null;

    /// <summary>Plage du PREMIER paquet non conditionnel d'une attaque de familier, ou null (Morsure
    /// empoisonnée n'annonce aucun dégât ; le Coup enragé (PvP) n'a qu'un paquet par-unité).</summary>
    public static string? PetDamageRange(Skill skill)
    {
        foreach (Match m in PetDamageRegex.Matches(skill.Description))
            if (!ConditionalTailRegex.IsMatch(m.Groups["tail"].Value))
                return m.Groups["range"].Value;
        return null;
    }

    /// <summary>Index de la colonne de progression de <paramref name="target"/> que l'effet relève, ou −1 :
    /// la clause de l'esprit ou le premier paquet du familier, apparié à la progression par les ancres
    /// (donc jamais un index deviné).</summary>
    public static int TextBonusColumn(DamageBoostScope scope, Skill target) => scope switch
    {
        DamageBoostScope.SpiritAttacks => SkillProgression.ColumnOf(target.Progression, SpiritAttackRange(target)),
        DamageBoostScope.PetAttacks    => SkillProgression.ColumnOf(target.Progression, PetDamageRange(target)),
        _                              => -1,
    };

    // ── Type de dégâts effectif (§ 6.1 du plan) ───────────────────────────────

    private static readonly string[] Elemental = ["fire", "cold", "earth", "lightning"];

    /// <summary>Type de dégâts élémentaire ? (Hiver ne convertit QUE l'élémentaire.)</summary>
    public static bool IsElemental(string? type) => type is not null && Elemental.Contains(type);

    /// <summary>
    /// Tout ce qui peut convertir le type de dégâts des attaques d'un perso, au moment présent.
    /// <paramref name="LitConverters"/> = ses convertisseurs personnels ALLUMÉS, dans l'ordre de la barre ;
    /// <paramref name="ElementalMod"/> = l'élément du mod d'arme du set actif (null s'il n'y en a pas) ;
    /// <paramref name="JudgesInsight"/> = Clairvoyance du juge reçue et allumée ; les deux brasiers et
    /// Briseur de pierre sont à part parce que la chaîne leur donne une place FIXE, pas celle de la barre.
    /// </summary>
    public readonly record struct ConversionState(
        IReadOnlyList<DamageConverterDescriptor>? LitConverters = null,
        string? ElementalMod = null,
        bool JudgesInsight = false,
        bool GreaterConflagration = false,
        bool Conflagration = false,
        bool StoneStriker = false);

    /// <summary>
    /// Type de dégâts EFFECTIF des attaques de <paramref name="target"/> ; null = rien ne les a
    /// converties, donc l'application ne sait rien et l'on croit l'icône (§ 6.1 du plan).
    ///
    /// ⚠ La chaîne n'est PAS commutative (règle de Philippe du 16/09/2026) : le mod élémentaire de
    /// l'arme, puis les convertisseurs personnels dans l'ordre de la barre, puis la Clairvoyance du juge
    /// reçue, puis les esprits qui ne convertissent que ce qui est ENCORE physique (Grand brasier sur
    /// tout le physique, Brasier sur les seules flèches) — et Briseur de pierre a TOUJOURS le dernier mot.
    ///
    /// ⚠ MANQUE ASSUMÉ, à combler au lot 6c : Hiver (462) doit passer tout l'élémentaire en froid, mais il
    /// n'est pas encore au bandeau d'équipe. Tant qu'il n'y est pas, une conjuration de flamme reste
    /// active sous Grand brasier + Hiver, là où le froid devrait l'éteindre.
    /// </summary>
    public static string? EffectiveElement(ConversionState state, Skill target, WeaponKind equipped)
    {
        string? element = null;

        // Un mod élémentaire ne convertit QUE les attaques de l'arme qui le porte.
        if (state.ElementalMod is { } mod && ConditionDurationData.UsesEquippedWeapon(target, equipped))
            element = mod;

        foreach (var k in state.LitConverters ?? [])
            if (ConditionDurationData.InWeaponScope(k.Weapon, target, equipped))
                element = k.Element;

        if (state.JudgesInsight && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Physical, target, equipped))
            element = "holy";

        if (element is null && state.GreaterConflagration
            && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Physical, target, equipped))
            element = "fire";
        if (element is null && state.Conflagration
            && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Bow, target, equipped))
            element = "fire";

        if (state.StoneStriker && ConditionDurationData.InWeaponScope(ConditionWeaponScope.Physical, target, equipped))
            element = "earth";

        return element;
    }

    /// <summary>
    /// L'effet qui exige un type d'arme s'applique-t-il ? <paramref name="effectiveElement"/> = ce que la
    /// chaîne de conversion donne pour CETTE attaque, null = rien ne l'a convertie.
    ///
    /// C'est la seule entorse de tout le chantier à « icône allumée = l'effet s'applique » (§ 6.1,
    /// validée le 23/09/2026) : tant qu'aucun convertisseur n'est allumé et qu'aucun mod élémentaire
    /// n'est au set actif, on CROIT l'icône — le perso a peut-être une baguette de feu que
    /// l'application ne modélise pas. Dès qu'un convertisseur agit, l'application SAIT le type réel et
    /// seule la conjuration de ce type-là s'applique.
    /// </summary>
    public static bool ElementSatisfied(string? requiredElement, string? effectiveElement) =>
        requiredElement is null || effectiveElement is null || effectiveElement == requiredElement;

    /// <summary>
    /// Nom de la famille EXCLUSIVE des effets qui exigent un type d'arme, null pour les autres.
    ///
    /// ⚠ Règle relevée par Philippe à la QA du lot 6a : **une arme n'inflige qu'UN type de dégâts à la
    /// fois**. Les 3 conjurations et l'Aura de poussière d'ébène portent donc sur la même arme et sont
    /// mutuellement exclusives : au plus une peut agir. Sans ça, allumer les trois conjurations (ou une
    /// conjuration + l'Aura) additionnait leurs dégâts, ce qui est impossible en jeu — et le manquait
    /// précisément dans le seul cas où l'application ne connaît PAS le type de l'arme (aucun mod
    /// élémentaire au set actif), donc là où elle croit l'icône.
    ///
    /// En jeu les enchantements peuvent bel et bien tenir tous les trois en même temps ; c'est leur
    /// EFFET qui ne peut pas. Les rendre exclusives à l'allumage est donc une simplification de
    /// simulation — la même que pour les préparations —, et elle dit la vérité : l'icône allumée
    /// signifie « mon arme inflige ce type de dégâts ».
    /// </summary>
    public static string? ExclusiveElementFamily(int toggleId) =>
        _bySkillId.GetValueOrDefault(toggleId) is { RequiresElement: not null } ? "WeaponElement" : null;
}
