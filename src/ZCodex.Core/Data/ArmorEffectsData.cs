using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

// GENERE par le harnais ArmorEffectsGen (scratchpad) le 2026-07-15, puis AJUSTE a la main le
// 2026-07-17 (rework chantier 14 bis) : IW passe en valeur UNITAIRE +5/comp. Illusion (le VM
// multiplie par le compte saisi), Convert Hexes fige a +10 (bug du jeu, note wiki), libelles.
// En cas de regeneration, reporter ces ajustements.
// Sources : descriptions RESOLUES de la DB skills (valeurs signees par rang 0..21, bakees) pour
// les competences ; pages wiki brutes (action=raw) pour les effets non-skill (SkillId 0).
// Regeneration : dotnet run --project ArmorEffectsGen
//
// Categories : wiki/List_of_armor_effects_by_category. Special = IAU!/Illusionary Weaponry/
// Mantra of Signets (bonus) et coup critique/Barbed Arrows/Healing Signet/Shadowy Burden/Massive
// Damage (malus). Tout le reste (dont Cracked Armor et les malus des Resistances) = Bonus.
public static class ArmorEffectsData
{
    /// <summary>Perimetre de type de degats d'un effet (colonnes de la grille du calculateur).
    /// All = toutes colonnes ; Physical = tranchant/perforant/contondant ; Elemental = feu/froid/
    /// terre/foudre ; Slashing = tranchant seul ; Projectile = attaques a projectile (informatif).
    /// Tenebres/chaos : aucun bonus type ne s'y applique (decision lore Philippe).</summary>
    public enum Scope { All, Physical, Elemental, Slashing, Projectile }

    /// <summary>Un effet d'armure externe. ValuesByRank : valeurs SIGNEES (malus negatif),
    /// longueur 1 (effet fixe) ou 22 (progressif, indexe par rang d'attribut 0..21). SkillId 0 =
    /// effet non-skill (consommable, benediction, flux, condition, coup critique). ConditionFr =
    /// condition d'application (INFORMATIVE : l'utilisateur coche l'effet lui-meme, seul Scope est
    /// calculatoire). IsEnemyInflicted = malus pose par l'adversaire (groupe UI « Malus subis »).</summary>
    public sealed record ArmorEffect(
        int SkillId, string Name, ArmorCalculator.Category Category, Scope Scope,
        string? ConditionFr, bool IsEnemyInflicted, IReadOnlyList<int> ValuesByRank);

    /// <summary>Valeur signee de l'effet au rang donne (clampe aux bornes du tableau).</summary>
    public static int ValueAt(ArmorEffect e, int rank)
        => e.ValuesByRank[System.Math.Clamp(rank, 0, e.ValuesByRank.Count - 1)];

    // Les clauses ConditionFr (informatives) sont peu nombreuses et distinctes → table FR→EN plutôt
    // que d'ajouter un champ a chacune des ~66 entrees. Toute nouvelle clause FR doit etre ajoutee ici.
    private static readonly IReadOnlyDictionary<string, string> ConditionEnMap = new Dictionary<string, string>
    {
        ["immobilisé"] = "immobilized",
        ["attaques à projectile uniquement (arc/lance) — visible sur les attaques de référence"]
            = "projectile attacks only (bow/spear) — visible on reference attacks",
        ["à l'arrêt"] = "while stationary",
        ["sur les AUTRES membres du groupe"] = "on OTHER party members",
        ["sous l'effet d'une condition"] = "while suffering from a condition",
        ["familier uniquement"] = "pet only",
        ["marteau équipé"] = "hammer equipped",
        ["cible envoûtée ou avec une condition"] = "target hexed or with a condition",
        ["bug du jeu : +10 fixe, quel que soit le nombre de hex retirés"]
            = "game bug: fixed +10 regardless of the number of hexes removed",
        ["+5 × comp. Illusion équipées (IW comprise) — compte dans « État du personnage »"]
            = "+5 × equipped Illusion skills (IW included) — counted in \"Character state\"",
        ["+3 × sceaux équipés — compte dans « État du personnage »"]
            = "+3 × equipped signets — counted in \"Character state\"",
        ["pendant l'activation"] = "while activating",
        ["pendant l'utilisation"] = "while in use",
    };

    /// <summary>Clause d'application de l'effet dans la langue affichee (null si aucune).</summary>
    public static string? DisplayCondition(ArmorEffect e)
        => e.ConditionFr is null ? null
           : AppLanguage.IsFr ? e.ConditionFr
           : ConditionEnMap.GetValueOrDefault(e.ConditionFr, e.ConditionFr);

    // Noms FR des effets NON-skill (SkillId 0 : consommables, benedictions, flux, conditions, coup
    // critique). Les effets a SkillId != 0 tirent leur nom FR du catalogue (DisplayName du skill).
    // Fournis/valides par Philippe.
    private static readonly IReadOnlyDictionary<string, string> NonSkillFr = new Dictionary<string, string>
    {
        ["Cracked Armor"]                = "Armure brisée",
        ["Critical hit"]                 = "Coup critique",
        ["Massive Damage (Flux)"]        = "Dégâts massifs (Flux)",
        ["Armor of Salvation"]           = "Armure du salut",
        ["Cloak of Faith"]               = "Cape de foi",
        ["Drake Kabob (Drake Skin)"]     = "Kébab de drake",
        ["War Supplies (Well-Supplied)"] = "Ravitaillements militaires",
        ["Shielding Branches"]           = "Armure de branches",
    };

    /// <summary>Nom affiche d'un effet NON-skill dans la langue courante (repli = le nom EN).</summary>
    public static string NonSkillDisplayName(string englishName)
        => AppLanguage.IsFr ? NonSkillFr.GetValueOrDefault(englishName, englishName) : englishName;

    public static readonly IReadOnlyList<ArmorEffect> All =
    [
        new(165, "Armor of Earth", ArmorCalculator.Category.Bonus, Scope.All, null, false, [24, 26, 29, 31, 34, 36, 38, 41, 43, 46, 48, 50, 53, 55, 58, 60, 62, 65, 67, 70, 72, 74]),
        new(166, "Kinetic Armor", ArmorCalculator.Category.Bonus, Scope.All, null, false, [20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72, 76, 80, 84, 88, 92, 96, 100, 104]),
        new(348, "\"Watch Yourself!\"", ArmorCalculator.Category.Bonus, Scope.All, null, false, [5, 6, 8, 9, 10, 12, 13, 14, 16, 17, 18, 20, 21, 22, 24, 25, 26, 28, 29, 30, 32, 33]),
        new(2858, "\"Watch Yourself!\" (PvP)", ArmorCalculator.Category.Bonus, Scope.All, null, false, [5, 6, 8, 9, 10, 12, 13, 14, 16, 17, 18, 20, 21, 22, 24, 25, 26, 28, 29, 30, 32, 33]),
        new(361, "Dolyak Signet", ArmorCalculator.Category.Bonus, Scope.All, "immobilisé", false, [10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52]),
        new(259, "Shield of Deflection", ArmorCalculator.Category.Bonus, Scope.All, null, false, [15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36]),
        new(238, "Armor of Mist", ArmorCalculator.Category.Bonus, Scope.All, null, false, [10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52]),
        new(1373, "Stone Sheath", ArmorCalculator.Category.Bonus, Scope.All, null, false, [1, 3, 5, 7, 9, 11, 13, 15, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42]),
        new(261, "Shield of Regeneration", ArmorCalculator.Category.Bonus, Scope.All, null, false, [20, 22, 23, 25, 27, 28, 30, 32, 33, 35, 37, 38, 40, 42, 43, 45, 47, 48, 50, 52, 53, 55]),
        new(913, "Tranquil Was Tanasen", ArmorCalculator.Category.Bonus, Scope.All, null, false, [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31]),
        new(2101, "Critical Agility", ArmorCalculator.Category.Bonus, Scope.All, null, false, [15, 17, 19, 21, 23, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25]),  // rang de titre Fer de lance (0..10)
        new(452, "Dryder's Defenses", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [34, 36, 37, 39, 41, 43, 44, 46, 48, 50, 51, 53, 55, 57, 58, 60, 62, 63, 65, 67, 69, 69]),
        new(1261, "Frigid Armor", ArmorCalculator.Category.Bonus, Scope.Physical, null, false, [10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52]),
        new(3029, "Bladeturn Refrain (PvP)", ArmorCalculator.Category.Bonus, Scope.Slashing, null, false, [10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52]),  // seule la variante PvP donne de l'armure
        new(239, "Ward Against Harm", ArmorCalculator.Category.Bonus, Scope.All, null, false, [12, 13, 14, 14, 15, 16, 17, 18, 18, 19, 20, 21, 22, 22, 23, 24, 25, 26, 26, 27, 28, 29]),
        new(239, "Ward Against Harm", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [12, 13, 14, 14, 15, 16, 17, 18, 18, 19, 20, 21, 22, 22, 23, 24, 25, 26, 26, 27, 28, 29]),  // supplément élémentaire cumulé au +N de base
        new(2806, "Ward Against Harm (PvP)", ArmorCalculator.Category.Bonus, Scope.All, null, false, [12, 13, 14, 14, 15, 16, 17, 18, 18, 19, 20, 21, 22, 22, 23, 24, 25, 26, 26, 27, 28, 29]),
        new(2806, "Ward Against Harm (PvP)", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [12, 13, 14, 14, 15, 16, 17, 18, 18, 19, 20, 21, 22, 22, 23, 24, 25, 26, 26, 27, 28, 29]),  // supplément élémentaire cumulé au +N de base
        new(72, "Elemental Resistance", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [40]),
        new(72, "Elemental Resistance", ArmorCalculator.Category.Bonus, Scope.Physical, null, false, [-24, -23, -22, -22, -21, -20, -19, -18, -18, -17, -16, -15, -14, -14, -13, -12, -11, -10, -10, -9, -8, -7]),  // malus vs physique (bug possible, note⁶ wiki)
        new(73, "Physical Resistance", ArmorCalculator.Category.Bonus, Scope.Physical, null, false, [40]),
        new(73, "Physical Resistance", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [-24, -23, -22, -22, -21, -20, -19, -18, -18, -17, -16, -15, -14, -14, -13, -12, -11, -10, -10, -9, -8, -7]),  // malus vs élémentaire (bug possible, note⁶ wiki)
        new(950, "Shadowy Burden", ArmorCalculator.Category.Special, Scope.All, null, true, [-20, -21, -21, -22, -23, -23, -24, -25, -25, -26, -27, -27, -28, -29, -29, -30, -31, -31, -32, -33, -33, -33]),  // −N vs vos attaques ; seulement si la cible n'a pas d'autre hex
        new(367, "\"Shields Up!\"", ArmorCalculator.Category.Bonus, Scope.Projectile, "attaques à projectile uniquement (arc/lance) — visible sur les attaques de référence", false, [60]),  // rework 17/07 : scope calculatoire, plus appliqué à la grille
        new(1589, "\"Stand Your Ground!\"", ArmorCalculator.Category.Bonus, Scope.All, "à l'arrêt", false, [24]),
        new(3032, "\"Stand Your Ground!\" (PvP)", ArmorCalculator.Category.Bonus, Scope.All, "à l'arrêt", false, [24]),
        new(2097, "\"Save Yourselves!\" (Kurzick)", ArmorCalculator.Category.Bonus, Scope.All, "sur les AUTRES membres du groupe", false, [100]),
        new(1954, "\"Save Yourselves!\" (Luxon)", ArmorCalculator.Category.Bonus, Scope.All, "sur les AUTRES membres du groupe", false, [100]),
        new(218, "Obsidian Flesh", ArmorCalculator.Category.Bonus, Scope.All, null, false, [20]),
        new(318, "Defy Pain", ArmorCalculator.Category.Bonus, Scope.All, null, false, [20]),
        new(3204, "Defy Pain (PvP)", ArmorCalculator.Category.Bonus, Scope.All, null, false, [20]),
        new(1540, "Conviction", ArmorCalculator.Category.Bonus, Scope.All, "sous l'effet d'une condition", false, [10]),
        new(1505, "Vow of Piety", ArmorCalculator.Category.Bonus, Scope.All, null, false, [24]),
        new(206, "Armor of Frost", ArmorCalculator.Category.Bonus, Scope.Physical, null, false, [40]),
        new(2220, "Great Dwarf Armor", ArmorCalculator.Category.Bonus, Scope.All, null, false, [24]),  // note³ bug ; +24 vs Destroyers ignoré (PvE spécifique)
        new(2231, "Ebon Battle Standard of Courage", ArmorCalculator.Category.Bonus, Scope.All, null, false, [24]),  // +24 vs Charr ignoré (PvE spécifique)
        new(447, "Otyugh's Cry", ArmorCalculator.Category.Bonus, Scope.All, "familier uniquement", false, [24]),
        new(1641, "Feigned Neutrality", ArmorCalculator.Category.Bonus, Scope.All, null, false, [80]),
        new(376, "Disciplined Stance", ArmorCalculator.Category.Bonus, Scope.All, null, false, [10]),
        new(375, "Dwarven Battle Stance", ArmorCalculator.Category.Bonus, Scope.All, "marteau équipé", false, [40]),
        new(467, "Fertile Season", ArmorCalculator.Category.Bonus, Scope.All, null, false, [8]),
        new(773, "Mighty Was Vorizun", ArmorCalculator.Category.Bonus, Scope.All, null, false, [15]),
        new(1219, "Protective Was Kaolai", ArmorCalculator.Category.Bonus, Scope.All, null, false, [10]),
        new(787, "Resilient Weapon", ArmorCalculator.Category.Bonus, Scope.All, "cible envoûtée ou avec une condition", false, [24]),
        new(1518, "Avatar of Balthazar", ArmorCalculator.Category.Bonus, Scope.Physical, null, false, [20]),
        new(1522, "Avatar of Melandru", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [30]),
        new(3271, "Avatar of Melandru (PvP)", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [30]),
        new(175, "Ward Against Elements", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [24]),
        new(2091, "Shadow Sanctuary (Kurzick)", ArmorCalculator.Category.Bonus, Scope.All, null, false, [40]),
        new(1948, "Shadow Sanctuary (Luxon)", ArmorCalculator.Category.Bonus, Scope.All, null, false, [40]),
        new(303, "Convert Hexes", ArmorCalculator.Category.Bonus, Scope.All, "bug du jeu : +10 fixe, quel que soit le nombre de hex retirés", false, [10]),  // note wiki Convert_Hexes
        new(2356, "\"I Am Unstoppable!\"", ArmorCalculator.Category.Special, Scope.All, null, false, [24]),
        new(33, "Illusionary Weaponry", ArmorCalculator.Category.Special, Scope.All, "+5 × comp. Illusion équipées (IW comprise) — compte dans « État du personnage »", false, [5]),  // valeur UNITAIRE, multipliée par le VM
        new(18, "Mantra of Signets", ArmorCalculator.Category.Special, Scope.All, "+3 × sceaux équipés — compte dans « État du personnage »", false, [3]),  // valeur UNITAIRE, multipliée par le VM
        new(1774, "Aggressive Refrain", ArmorCalculator.Category.Bonus, Scope.All, null, false, [-20]),  // malus permanent (bug possible, note⁶ wiki)
        new(1773, "Soldier's Fury", ArmorCalculator.Category.Bonus, Scope.All, null, false, [-20]),  // malus tant qu'actif (bug possible, note⁶ wiki)
        new(1470, "Barbed Arrows", ArmorCalculator.Category.Special, Scope.All, "pendant l'activation", false, [-40]),
        new(1, "Healing Signet", ArmorCalculator.Category.Special, Scope.All, "pendant l'utilisation", false, [-40]),
        new(0, "Cracked Armor", ArmorCalculator.Category.Bonus, Scope.All, null, true, [-20]),  // condition ; min 60 assuré par le plancher étape 2
        new(0, "Critical hit", ArmorCalculator.Category.Special, Scope.All, null, true, [-20]),  // coup critique (validé Philippe)
        new(0, "Massive Damage (Flux)", ArmorCalculator.Category.Special, Scope.All, null, false, [-20]),  // flux ; peut faire passer l'AL sous 0
        new(0, "Armor of Salvation", ArmorCalculator.Category.Bonus, Scope.All, null, false, [5]),  // effet d'objet (validé Philippe)
        new(0, "Cloak of Faith", ArmorCalculator.Category.Bonus, Scope.All, null, false, [10]),  // bénédiction Dwayna
        new(0, "Drake Kabob (Drake Skin)", ArmorCalculator.Category.Bonus, Scope.Physical, null, false, [5]),  // bug wiki : physique uniquement malgré la description
        new(0, "War Supplies (Well-Supplied)", ArmorCalculator.Category.Bonus, Scope.All, null, false, [5]),
        new(0, "Shielding Branches", ArmorCalculator.Category.Bonus, Scope.Elemental, null, false, [20]),  // bénédiction Melandru
    ];
}
