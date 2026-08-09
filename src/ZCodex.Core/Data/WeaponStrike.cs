using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Dégâts d'arme des skills d'attaque (wiki/Damage_calculation) : plage de l'arme customisée
/// (×1.2 — décision Philippe : les armes sont toujours customisées), scaling par le rang de la
/// maîtrise de l'ARME (+5/rang jusqu'au seuil (niveau+4)/2 — rang 12 à niveau 20 —, +2
/// au-delà), critique = dégâts max ×√2 (équivalent AL−20), sauf faux ×1.09 (wiki/Critical_hit).
/// </summary>
public static class WeaponStrike
{
    /// <summary>Arme d'un type d'attaque. Mastery = attribut de réquisition (pas celui de la
    /// skill) ; pour une arme de LANCEUR c'est une clé d'identification (« Wand », « Staff »),
    /// pas un attribut : aucun rang ne scale leur coup, cf. IsCaster. DamageType = type NATIF
    /// standard de l'arme (utilitaire Spike : AL par type de la cible) — personnalisable par
    /// personnage : mods élémentaires sur toutes les armes, skins spéciaux (jitte contondante,
    /// pic colossal perforant, faux « Sufferer » ténèbres…).</summary>
    public sealed record Weapon(string NameFr, string NameEn, int Min, int Max, string Mastery, string DamageType,
                                bool IsScythe = false, bool IsCaster = false)
    {
        /// <summary>Nom de l'arme dans la langue affichée.</summary>
        public string DisplayName => AppLanguage.IsFr ? NameFr : NameEn;
    }

    private const double Customized = 1.2;

    private static readonly Weapon Axe     = new("hache",   "axe",     6, 28, "Axe Mastery",     "slashing");
    private static readonly Weapon Sword   = new("épée",    "sword",  15, 22, "Swordsmanship",   "slashing");
    private static readonly Weapon Hammer  = new("marteau", "hammer", 19, 35, "Hammer Mastery",  "blunt");
    private static readonly Weapon Scythe  = new("faux",    "scythe",  9, 41, "Scythe Mastery",  "slashing", IsScythe: true);
    private static readonly Weapon Spear   = new("lance",   "spear",  14, 27, "Spear Mastery",   "piercing");
    private static readonly Weapon Bow     = new("arc",     "bow",    15, 28, "Marksmanship",    "piercing");
    private static readonly Weapon Daggers = new("dagues",  "daggers", 7, 17, "Dagger Mastery",  "piercing");

    // Armes de LANCEUR (validées Philippe 2026-08-04) : baguette et bâton ont la MÊME plage,
    // 11–22 réquisition atteinte, et AUCUN attribut ne scale le coup — contrairement aux armes
    // martiales dont la maîtrise porte le strike level. On les ancre donc au strike level de
    // référence du modèle (CasterStrikeRank : le rang pour lequel la plage annoncée vaut
    // exactement contre AL 60), si bien que l'armure de la cible module toujours le coup sans
    // qu'aucun rang de perso n'intervienne. Réquisition supposée ATTEINTE : l'utilitaire Spike ne
    // modélise pas l'objet, donc pas la plage 0–3 (critique 4) du cas non atteint.
    // Type natif indéterminé (il dépend du skin : tout sauf tranchant/contondant/perforant) → on
    // pose « dark », le seul type qui ne prend AUCUN bonus d'AL de groupe : la cible encaisse sur
    // son AL de base tant que l'utilisateur n'a pas précisé le type sur la ligne.
    public const int CasterStrikeRank = 12;
    private static readonly Weapon Wand  = new("baguette", "wand",  11, 22, "Wand",  "dark", IsCaster: true);
    private static readonly Weapon Staff = new("bâton",    "staff", 11, 22, "Staff", "dark", IsCaster: true);

    // « Melee Attack » = arme libre : quand l'attribut de la skill EST une maîtrise d'arme
    // (attaques de mêlée Derviche en Maîtrise de la faux), on ne mappe que l'arme par DÉFAUT —
    // l'arme reste changeable (cf. IsFreeWeaponAttack). Sinon null (arme à déduire ou à choisir).
    private static readonly Dictionary<string, Weapon> ByMastery = new()
    {
        [Axe.Mastery] = Axe, [Sword.Mastery] = Sword, [Hammer.Mastery] = Hammer,
        [Scythe.Mastery] = Scythe, [Spear.Mastery] = Spear, [Bow.Mastery] = Bow,
        [Daggers.Mastery] = Daggers,
        // Clés des armes de lanceur : elles servent au choix manuel (ByMasteryName) mais ne
        // peuvent PAS sortir du mapping par attribut — aucune skill n'a « Wand » pour attribut.
        [Wand.Mastery] = Wand, [Staff.Mastery] = Staff,
    };

    // Armes proposables au choix manuel d'une attaque d'arme LIBRE : le TYPE d'attaque impose la
    // catégorie, pas l'arme précise — un « Melee Attack » se lance avec n'importe quelle arme de
    // corps à corps, donc sans arc ni javelot. Le javelot en mêlée est un type À PART
    // (« Spear Melee Attack » : Spear Swipe, seule de son espèce) où l'arme reste imposée.
    // « Ranged Attack » (Deft Strike, seule de son type) accepte aussi les armes de LANCEUR —
    // baguette et bâton (exception validée Philippe), d'où 4 options et non 2.
    private static readonly Weapon[] MeleeChoices = [Axe, Sword, Hammer, Scythe, Daggers];
    private static readonly Weapon[] RangedChoices = [Bow, Spear, Wand, Staff];

    /// <summary>Arme identifiée par le nom de sa maîtrise (ex. « Hammer Mastery »), ou null.</summary>
    public static Weapon? ByMasteryName(string? mastery)
        => string.IsNullOrEmpty(mastery) ? null : ByMastery.GetValueOrDefault(mastery);

    /// <summary>Rang qui scale le coup de <paramref name="weapon"/> : celui du perso dans la
    /// maîtrise de l'arme, ou <b>0</b> quand il n'a pas cet attribut du tout — un perso hors
    /// profession tient quand même l'arme et frappe à rang 0, comme en jeu (décision Philippe) ;
    /// ou le rang de référence fixe des armes de lanceur, qu'aucun attribut ne scale.</summary>
    public static int StrikeRank(Weapon weapon, Func<string, int?> attributeLevel)
        => weapon.IsCaster ? CasterStrikeRank : attributeLevel(weapon.Mastery) ?? 0;

    /// <summary>Arme du type d'attaque, ou null si pas une attaque d'arme (Pet/Ranged/Melee non mappée).
    /// Ambidextres (Lead/Off-Hand/Dual) : dégâts affichés pour UN coup (décision Philippe).</summary>
    public static Weapon? For(Skill skill) => skill.SkillType switch
    {
        "Axe Attack" => Axe,
        "Sword Attack" => Sword,
        "Hammer Attack" => Hammer,
        "Scythe Attack" => Scythe,
        "Spear Attack" or "Spear Melee Attack" => Spear,
        "Bow Attack" or "Half Range Bow Attack" => Bow,
        "Lead Attack" or "Off-Hand Attack" or "Dual Attack" => Daggers,
        "Melee Attack" => ByMastery.GetValueOrDefault(skill.Attribute),
        _ => null,
    };

    /// <summary>
    /// Attaque d'arme (distance, corps à corps, mêlée) — même non mappée (Melee Attack à
    /// attribut libre, Ranged Attack). Pour celles-ci l'UI ne mentionne PAS les bonus « +X »
    /// qui ignorent l'armure (décision Philippe) ; Pet Attack exclue (pas une arme).
    /// </summary>
    public static bool IsWeaponAttack(Skill skill)
        => skill.SkillType.EndsWith("Attack", StringComparison.Ordinal) && skill.SkillType != "Pet Attack";

    /// <summary>
    /// Attaque à arme LIBRE : le type n'impose pas l'arme (« Melee Attack », « Ranged Attack »).
    /// L'attribut ne donne que le défaut, y compris quand c'est une maîtrise — les attaques de
    /// mêlée Derviche en Maîtrise de la faux (Reap Impurities, Twin Moon Sweep, Pious Assault,
    /// Victorious Sweep) se lancent avec n'importe quelle arme de corps à corps, exactement comme
    /// leurs sœurs en Mysticisme (Mystic Sweep…). « Spear Melee Attack » n'en est PAS : Spear
    /// Swipe est une attaque de mêlée mais AU JAVELOT (exception validée Philippe).
    /// </summary>
    public static bool IsFreeWeaponAttack(Skill skill)
        => skill.SkillType is "Melee Attack" or "Ranged Attack";

    /// <summary>Armes proposables au choix manuel pour <paramref name="skill"/> — vide si le type
    /// impose l'arme (Axe Attack, Spear Melee Attack…), donc si le choix n'est pas offert.</summary>
    public static IReadOnlyList<Weapon> ChoicesFor(Skill skill) => skill.SkillType switch
    {
        "Melee Attack" => MeleeChoices,
        "Ranged Attack" => RangedChoices,
        _ => [],
    };

    // Types de dégâts qu'une arme peut porter (ComboBox « Type de dégâts de l'arme » du Spike —
    // liste validée Philippe 2026-08-06). Toute arme MARTIALE accepte un mod élémentaire ; le
    // reste dépend des SKINS de la catégorie, le premier de la liste étant son type natif.
    // Le mod élémentaire n'existe pas sur une arme de lanceur au sens « mod » mais le type y est
    // libre, cf. CasterTypes.
    private static readonly string[] ElementalTypes = ["fire", "cold", "earth", "lightning"];

    private static readonly Dictionary<string, string[]> SkinTypesByMastery = new()
    {
        [Axe.Mastery] = ["slashing", "piercing"],
        [Sword.Mastery] = ["slashing", "blunt"],
        [Hammer.Mastery] = ["blunt", "piercing"],
        // Faux : tranchante, plus les TÉNÈBRES de la faux « Sufferer ». La faux banane, elle
        // (contondante), est un item gag qu'on ne simule pas (décision Philippe).
        [Scythe.Mastery] = ["slashing", "dark"],
        // Armes de tir : perforantes et rien d'autre côté physique.
        [Spear.Mastery] = ["piercing"],
        [Bow.Mastery] = ["piercing"],
        // Dagues : perforantes ou tranchantes, jamais contondantes.
        [Daggers.Mastery] = ["piercing", "slashing"],
    };

    // Armes de LANCEUR : jamais physiques (ni tranchant, ni contondant, ni perforant) — le type
    // dépend du skin et se prend hors du physique. Le type « lumière » n'existe pas : il a été
    // absorbé par le sacré (wiki/Holy_damage, note de bas de page) — confirmé par la base, zéro
    // description ne parle de « light damage », et les « lumière » du FR sont des NOMS de
    // compétences (Lance de lumière) qui infligent du sacré.
    private static readonly string[] CasterTypes =
        [.. ElementalTypes, "chaos", "shadow", "dark", "holy"];

    /// <summary>Types de dégâts proposables pour <paramref name="weapon"/> — ceux des skins de sa
    /// catégorie (le premier = son type natif) puis les élémentaires, ou la palette non physique
    /// des armes de lanceur. Vide pour une arme hors catalogue.</summary>
    public static IReadOnlyList<string> DamageTypeChoices(Weapon weapon) => weapon.IsCaster
        ? CasterTypes
        : [.. SkinTypesByMastery.GetValueOrDefault(weapon.Mastery, []), .. ElementalTypes];

    /// <summary>Type porté par un mod ÉLÉMENTAIRE, donc occupant l'emplacement de préfixe de l'arme
    /// (cf. <see cref="SpikeWeaponMods"/> : il exclut alors fractionnement et vampirique). Les
    /// autres types non natifs de la liste viennent de SKINS, qui laissent le préfixe libre.</summary>
    public static bool IsElementalType(string? damageType)
        => damageType is not null && ElementalTypes.Contains(damageType);

    /// <summary>L'arme est un ARC : seule catégorie qui connaît la variante « arc corne »
    /// (+10 % de pénétration). Les 5 catégories d'arcs partagent plage et maîtrise
    /// (wiki/Bow) — seuls refire et portée changent —, d'où une simple case et non une arme à part.</summary>
    public static bool IsBow(Weapon weapon) => weapon.Mastery == Bow.Mastery;

    /// <summary>Modificateurs d'attaque lus dans la description résolue (exceptions Q7 validées).</summary>
    public sealed record AttackMods(double Multiplier, bool AlwaysCritical, bool NoWeaponDamage);

    // « each strike doing 25% less damage », « These arrows deal 25% less damage » (le nombre
    // peut être une variable résolue, donc entourée de marqueurs) ; « does 50% of normal damage ».
    private static readonly Regex LessDamageRegex = new(
        $@"[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?%\s+less damage",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NormalDamageRegex = new(
        @"(\d+)%\s+of normal damage", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Attaques d'arme SANS dégâts d'arme (validées Philippe, catalogue énuméré par harnais) :
    // dégât fixe qui REMPLACE le coup (« Hits for only X damage », « Deals X damage » comme
    // attaque — leur ligne « X — ignore l'armure » reste affichée), et vol de vie pur.
    // Les paquets fixes CONDITIONNELS (Swift Chop « if blocked », Pious Assault « removal
    // effect », Holy Spear « vs summoned »…) ne remplacent PAS l'arme → pas dans cette liste.
    private static readonly HashSet<string> NoWeaponDamageExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Concussion Shot", "Distracting Shot", "Needling Shot", "Power Shot", "Shattering Assault",
        "Vampiric Assault",
    };

    /// <summary>
    /// Modificateurs de la table d'arme pour une attaque : « no damage » (Distracting Blow),
    /// dégât fixe remplaçant l'arme et vol de vie pur → pas de table ; « X% less damage »
    /// (Dual/Triple Shot, Twin Moon Sweep) et « X% of normal damage » (Lyssa's Assault) →
    /// multiplicateur ; « always a critical hit » (Keen Chop) → ligne critique seule.
    /// </summary>
    public static AttackMods ModsFor(string skillName, string resolvedDescription)
    {
        bool noDamage = NoWeaponDamageExceptions.Contains(skillName)
            || resolvedDescription.Contains("no damage", StringComparison.OrdinalIgnoreCase);
        double mult = 1.0;
        if (LessDamageRegex.Match(resolvedDescription) is { Success: true } less)
            mult = (100 - int.Parse(less.Groups[1].Value)) / 100.0;
        else if (NormalDamageRegex.Match(resolvedDescription) is { Success: true } norm)
            mult = int.Parse(norm.Groups[1].Value) / 100.0;
        bool alwaysCrit = resolvedDescription.Contains("always a critical hit", StringComparison.OrdinalIgnoreCase);
        return new AttackMods(mult, alwaysCrit, noDamage);
    }

    // Strike level ×5 : +5 par rang jusqu'au seuil (niveau+4)/2 (12 à niveau 20), +2 par rang
    // au-delà. Seuil fractionnaire aux niveaux impairs : extension continue de la formule wiki.
    private static double StrikeLevel5(int rank, int level)
    {
        double threshold = (level + 4) / 2.0;
        return 5 * Math.Min(rank, threshold) + 2 * Math.Max(0, rank - threshold);
    }

    /// <summary>Dégâts d'un tirage <paramref name="baseDamage"/> de l'arme customisée, tronqués.
    /// <paramref name="multiplier"/> = malus du type « 25% less damage » (1.0 sinon).</summary>
    public static int DamageAt(int baseDamage, int masteryRank, int armorLevel, int armorPenetration,
                               double multiplier = 1.0, int characterLevel = 20)
        => (int)Math.Floor(baseDamage * Customized * multiplier
                           * Factor(masteryRank, armorLevel, armorPenetration, characterLevel));

    /// <summary>Dégâts d'un critique : max de l'arme customisée ×√2 (faux : ×1.09), tronqués.</summary>
    public static int CriticalAt(Weapon weapon, int masteryRank, int armorLevel, int armorPenetration,
                                 double multiplier = 1.0, int characterLevel = 20)
        => (int)Math.Floor(weapon.Max * Customized * multiplier * (weapon.IsScythe ? 1.09 : Math.Sqrt(2))
                           * Factor(masteryRank, armorLevel, armorPenetration, characterLevel));

    /// <summary>
    /// Probabilité (0–1) de critique contre une cible de niveau <paramref name="targetLevel"/> —
    /// formule d'Izzy (wiki/Damage_calculation), sans modificateur de critique d'arme. Le rang
    /// de Critical Strikes (+1 %/rang, toutes armes) se combine en probabilités indépendantes,
    /// pas en somme — wiki/Critical_hit : « multiplicative between ... base crit rate [and]
    /// Critical Strikes rank » (table Critical Eye × CS : 15 % et 15 % → 27,75 %). Bornée à
    /// 100% (dépassée dès maîtrise 12 contre une cible de bas niveau).
    /// </summary>
    public static double CriticalChance(int masteryRank, int attackerLevel, int targetLevel,
                                        int criticalStrikesRank = 0)
    {
        double cappedRank = Math.Min(masteryRank, (attackerLevel + 4) / 2.0);
        double chance = 0.05 * Math.Pow(2, (8 * attackerLevel + 4 * masteryRank + 6 * cappedRank
                                            - 15 * targetLevel - 100) / 40.0)
                        * (1 - 0.01 * masteryRank) + 0.01 * masteryRank;
        chance = 1 - (1 - chance) * (1 - 0.01 * criticalStrikesRank);
        return Math.Clamp(chance, 0.0, 1.0);
    }

    private static double Factor(int rank, int armorLevel, int penetration, int level)
    {
        double effectiveAl = armorLevel * (100 - penetration) / 100.0;
        return Math.Pow(2, (StrikeLevel5(rank, level) - effectiveAl) / 40.0);
    }
}
