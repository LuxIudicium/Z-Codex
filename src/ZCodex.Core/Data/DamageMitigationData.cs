using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Portée d'une réduction de dégâts reçus.</summary>
public enum MitigationScope
{
    /// <summary>Tous les paquets qui respectent l'armure.</summary>
    All,
    /// <summary>Un seul type de dégâts (<see cref="DamageMitigationDescriptor.DamageType"/>) : les Mantras.</summary>
    DamageType,
    /// <summary>Les dégâts de SORT seulement (Veil of Thorns : « Spell damage is reduced »).</summary>
    Spells,
    /// <summary>Les dégâts d'ATTAQUE seulement (Life Bond : « the damage target ally takes from attacks »).</summary>
    Attacks,
}

/// <summary>Ce que vaut une ligne de réduction (lot 3 : au-delà des pourcentages du lot 1).</summary>
public enum MitigationKind
{
    /// <summary>−p % des dégâts (lot 1).</summary>
    Percent,
    /// <summary>+p % des dégâts reçus (Frenzy : « You take 175% damage » → +75). Même étape que les %.</summary>
    Increase,
    /// <summary>−N points par paquet, APRÈS les % (ordre confirmé en jeu par P1), plancher 0 sur le paquet entier.</summary>
    Flat,
    /// <summary>Plus aucun coup critique (Stoneflesh Aura, Stone Sheath).</summary>
    CritImmune,
    /// <summary>Un critique fait le max de l'arme SANS le ×√2 (Balanced Stance, réponse de Philippe du 10/10/2026).</summary>
    CritNoExtra,
    /// <summary>Change le type de dégâts REÇU (lot 4) : <see cref="DamageMitigationDescriptor.Conversion"/> dit lequel.</summary>
    Conversion,
}

/// <summary>Les conversions de type du lot 4. La règle de chacune vit dans <see cref="DamageBoostData"/> (chaîne du
/// chantier infobulle + <see cref="DamageBoostData.ReceivedType"/>) ; la ligne ne fait que l'allumer.</summary>
public enum TypeConversion
{
    None,
    /// <summary>Briseur de pierre, porté par le DÉFENSEUR : élém., physique, ténèbres, chaos et sacré reçus → terre.</summary>
    StoneStriker,
    /// <summary>Grand brasier (zone) : physique → feu.</summary>
    GreaterConflagration,
    /// <summary>Brasier (zone) : flèches → feu.</summary>
    Conflagration,
    /// <summary>Hiver (zone) : élémentaire → froid.</summary>
    Winter,
    /// <summary>Clairvoyance du juge, lancée sur l'ATTAQUANT : ses attaques → sacré, +20 % de pénétration.</summary>
    JudgesInsight,
}

/// <summary>Ce que veut dire la case du rang de la ligne. Shield of Absorption et Shield Stance n'ont pas besoin de
/// leur rang (il ne change que la durée) : la case porte la seule variable qui compte (accord de Philippe, 10/10/2026).</summary>
public enum MitigationRankBox
{
    /// <summary>Rang d'attribut du lanceur (défaut).</summary>
    Attribute,
    /// <summary>N° du coup reçu sous l'effet : valeur = <see cref="DamageMitigationDescriptor.Fixed"/> × n.</summary>
    HitNumber,
    /// <summary>Rang de Force du défenseur : valeur = <see cref="DamageMitigationDescriptor.Fixed"/> × rang, plafonnée.</summary>
    Strength,
}

/// <summary>
/// Une réduction de dégâts REÇUS, appliquée APRÈS l'armure (chantier « calculateur d'armure : réductions de
/// dégâts », plan <c>docs/armor_calc_mitigation_plan.md</c>). Lot 1 = les pourcentages ; lot 3 = réductions fixes,
/// dégâts augmentés, critiques, redirection ; lot 4 = conversions de type.
///
/// La valeur n'est jamais recopiée : <paramref name="Index"/> = colonne de progression SONDÉE dans la base
/// (lue au rang, donc une variante PvP ou une mise à jour du catalogue sont justes d'office) ;
/// <paramref name="Fixed"/> = valeur écrite en dur dans la description (Ether Prism 75 %, Union 15) ou pas d'une case
/// (Shield of Absorption 5 par coup, Shield Stance 2 par rang de Force). <paramref name="Offset"/> = écart ajouté à
/// la valeur lue (Frenzy : 175 % → +75). <paramref name="Cap"/> = plafond (Shield Stance 15).
/// <paramref name="ConditionFr"/> / <paramref name="ConditionEn"/> = condition INFORMATIVE : comme pour les
/// effets d'armure, cocher la ligne veut dire que la condition est remplie.
/// <paramref name="AlsoLifeSteal"/> = la réduction touche aussi le vol de vie (Q14 fermée le 10/10/2026 : Shielding
/// Hands et Union seulement, comme le disent leurs textes).
/// <paramref name="Conversion"/> = la conversion de type d'une ligne <see cref="MitigationKind.Conversion"/> (lot 4).
/// </summary>
public sealed record DamageMitigationDescriptor(
    int SkillId, string Name, MitigationScope Scope, string? DamageType = null,
    int Index = -1, int Fixed = 0, string? ConditionFr = null, string? ConditionEn = null,
    MitigationKind Kind = MitigationKind.Percent, MitigationRankBox RankBox = MitigationRankBox.Attribute,
    int Offset = 0, int Cap = 0, bool AlsoLifeSteal = false, TypeConversion Conversion = TypeConversion.None)
{
    /// <summary>Ligne de critique : elle n'a pas de valeur.</summary>
    public bool IsCritical => Kind is MitigationKind.CritImmune or MitigationKind.CritNoExtra;

    /// <summary>Ligne sans valeur chiffrée : critique ou conversion de type.</summary>
    public bool HasNoValue => IsCritical || Kind == MitigationKind.Conversion;

    /// <summary>La valeur se lit dans la progression de la base (colonne <see cref="Index"/>).</summary>
    public bool ReadsProgression => Fixed == 0 && !HasNoValue;
}

public static class DamageMitigationData
{
    // Index sondés dans la base le 09/10/2026 (lot 1) et le 10/10/2026 (lot 3), rang 0 / 12 / 15 confrontés à la
    // description. Le contrôle durable est MitigationCoverage, qui exige que tout ce que ses motifs attrapent soit ici.
    public static readonly IReadOnlyList<DamageMitigationDescriptor> All =
    [
        // « Reduces <type> damage you take by 26...45...50% » — colonne 1 (la 0 est la durée).
        new(6, "Mantra of Earth",     MitigationScope.DamageType, "earth",     Index: 1),
        new(7, "Mantra of Flame",     MitigationScope.DamageType, "fire",      Index: 1),
        new(8, "Mantra of Frost",     MitigationScope.DamageType, "cold",      Index: 1),
        new(9, "Mantra of Lightning", MitigationScope.DamageType, "lightning", Index: 1),

        // « All damage you take is reduced by 75% ».
        new(1377, "Ether Prism", MitigationScope.All, Fixed: 75),
        // « take half damage ».
        new(1037, "Dark Escape", MitigationScope.All, Fixed: 50),
        new(2136, "Smoke Powder Defense", MitigationScope.All, Fixed: 50,
            ConditionFr: "prochain coup seulement", ConditionEn: "next hit only"),
        // « takes 5...41...50% less damage » — colonne 1 (la 0 est le bonus de soin).
        new(260, "Aura of Faith", MitigationScope.All, Index: 1),
        // « Reduces damage by 20...44...50%. Cannot self-target. »
        new(270, "Life Barrier", MitigationScope.All, Index: 0,
            ConditionFr: "lancée par un allié (pas sur soi)", ConditionEn: "cast by an ally (cannot self-target)"),
        // « Reduces damage by 20...35% » — rang de TITRE (table de 11 valeurs, plateau au-delà).
        new(2112, "\"There's Nothing to Fear!\"", MitigationScope.All, Index: 0),
        // « take 5...29...35% less damage from Burning foes ».
        new(1597, "\"They're on Fire!\"", MitigationScope.All, Index: 0,
            ConditionFr: "contre les ennemis sous Brûlure", ConditionEn: "vs Burning foes"),
        // « Take 33% less damage from foes hexed with Water Magic » — même texte en PvP.
        new(236, "Mist Form", MitigationScope.All, Fixed: 33,
            ConditionFr: "contre les ennemis sous maléfice de Magie de l'eau", ConditionEn: "vs foes hexed with Water Magic"),
        new(2805, "Mist Form (PvP)", MitigationScope.All, Fixed: 33,
            ConditionFr: "contre les ennemis sous maléfice de Magie de l'eau", ConditionEn: "vs foes hexed with Water Magic"),
        // « Spell damage is reduced by 5...29...35% » — colonne 2 (0 = durée, 1 = dégâts perforants).
        new(1757, "Veil of Thorns", MitigationScope.Spells, Index: 2),

        // ── Lot 3 — redirection, vue du perso protégé : la part redirigée, il ne la subit pas ──
        // « Half of the damage target ally takes from attacks is redirected to you. » Le −3…30 réduit la moitié
        // redirigée : il profite au Moine, pas au perso (accord de Philippe du 10/10/2026).
        new(241, "Life Bond", MitigationScope.Attacks, Fixed: 50,
            ConditionFr: "lancé par un allié : la moitié part chez lui", ConditionEn: "cast by an ally: half goes to them"),
        // « Transfers 75% of incoming damage from you to your nearest servant. »
        new(138, "Dark Bond", MitigationScope.All, Fixed: 75,
            ConditionFr: "avec un serviteur à proximité", ConditionEn: "with a minion nearby"),

        // ── Lot 3 — dégâts AUGMENTÉS : « You take 175...135...125% damage » → +75…+35…+25 % ──
        new(346,  "Frenzy",            MitigationScope.All, Index: 0, Offset: -100, Kind: MitigationKind.Increase),
        new(3443, "Frenzy (PvP)",      MitigationScope.All, Index: 0, Offset: -100, Kind: MitigationKind.Increase),
        // Colonne 1 (la 0 est la durée).
        new(831,  "Primal Rage",       MitigationScope.All, Index: 1, Offset: -100, Kind: MitigationKind.Increase),
        new(3458, "Primal Rage (PvP)", MitigationScope.All, Index: 1, Offset: -100, Kind: MitigationKind.Increase),
        // « You take double damage ».
        new(1700, "Frenzied Defense",  MitigationScope.All, Fixed: 100, Kind: MitigationKind.Increase),

        // ── Lot 3 — réductions FIXES : retirées du paquet ENTIER (arme + « +X »), après les %, plancher 0 ──
        // « Reduces damage you take by 1...25...31, and you are immune to critical hits » — colonne 1.
        new(1375, "Stoneflesh Aura", MitigationScope.All, Index: 1, Kind: MitigationKind.Flat),
        new(1375, "Stoneflesh Aura", MitigationScope.All, Kind: MitigationKind.CritImmune,
            ConditionFr: "immunisé aux critiques", ConditionEn: "immune to critical hits"),
        // « Reduces incoming damage and life steal by 3...15...18 » — colonne 0.
        new(299, "Shielding Hands", MitigationScope.All, Index: 0, Kind: MitigationKind.Flat, AlsoLifeSteal: true,
            ConditionFr: "réduit aussi le vol de vie", ConditionEn: "also reduces life stealing"),
        // « Reduces incoming damage by 5 each time target ally takes damage » : CROISSANT, +5 par paquet reçu, même un
        // paquet à 0 (expérience P3 de Philippe, 10/10/2026). Tous les paquets d'une même ligne reçoivent le même n°.
        new(1399, "Shield of Absorption", MitigationScope.All, Fixed: 5, Kind: MitigationKind.Flat,
            RankBox: MitigationRankBox.HitNumber,
            ConditionFr: "case = n° du coup reçu (−5 par coup, coups à 0 compris)",
            ConditionEn: "box = hit number (−5 per hit, 0-damage hits count)"),
        // « The next damage this ally takes is reduced by 5...41...50. No effect unless this ally is hexed » — col. 1.
        new(848, "Reverse Hex", MitigationScope.All, Index: 1, Kind: MitigationKind.Flat,
            ConditionFr: "prochain coup seulement, si maudit", ConditionEn: "next hit only, if hexed"),
        // « Negates the next damage … (maximum 5...61...75) » : on renvoie jusqu'au maximum et on encaisse le surplus
        // (Philippe, 10/10/2026) → une réduction fixe du prochain coup. Colonne 0.
        new(1400, "Reversal of Damage", MitigationScope.All, Index: 0, Kind: MitigationKind.Flat,
            ConditionFr: "prochain coup seulement", ConditionEn: "next hit only"),
        // « takes damage or life steal, it is reduced by 15 » — 15 fixes, le rang ne change que l'esprit.
        new(911, "Union", MitigationScope.All, Fixed: 15, Kind: MitigationKind.Flat, AlsoLifeSteal: true,
            ConditionFr: "à portée de l'esprit ; réduit aussi le vol de vie",
            ConditionEn: "within the spirit's range; also reduces life stealing"),
        new(3005, "Union (PvP)", MitigationScope.All, Fixed: 15, Kind: MitigationKind.Flat, AlsoLifeSteal: true,
            ConditionFr: "à portée de l'esprit ; réduit aussi le vol de vie",
            ConditionEn: "within the spirit's range; also reduces life stealing"),
        // « Damage is reduced by 2 for each rank of Strength (maximum 15 damage reduction). No effect unless you have a
        // shield equipped. » La Force est celle du DÉFENSEUR : la case de la ligne la porte.
        new(378, "Shield Stance", MitigationScope.All, Fixed: 2, Cap: 15, Kind: MitigationKind.Flat,
            RankBox: MitigationRankBox.Strength,
            ConditionFr: "case = rang de Force (max −15) ; bouclier équipé",
            ConditionEn: "box = Strength rank (max −15); shield equipped"),
        // « You take 5...17...20 less damage from foes with a condition » — colonne 1 (la 0 est la Faiblesse).
        new(1515, "Armor of Sanctity", MitigationScope.All, Index: 1, Kind: MitigationKind.Flat,
            ConditionFr: "contre les ennemis sous condition", ConditionEn: "vs foes with a condition"),
        // « take -1...8...10 damage from foes with less Health than you » — colonne 1 (la 0 est la vie max).
        new(1531, "Intimidating Aura", MitigationScope.All, Index: 1, Kind: MitigationKind.Flat,
            ConditionFr: "contre les ennemis qui ont moins de vie que toi", ConditionEn: "vs foes with less Health than you"),
        // « +20 armor, and take 1...8...10 less damage » — colonne 1. La variante PvP n'a pas de réduction.
        new(318, "Defy Pain", MitigationScope.All, Index: 1, Kind: MitigationKind.Flat),
        // « You have 10 base damage reduction while casting binding rituals ».
        new(3003, "Armor of Unfeeling (PvP)", MitigationScope.All, Fixed: 10, Kind: MitigationKind.Flat,
            ConditionFr: "pendant l'incantation d'un rituel d'union", ConditionEn: "while casting binding rituals"),

        // ── Lot 3 — critiques ──
        // « do not take extra damage from critical hits » : le critique fait le max de l'arme, sans le ×√2.
        new(371, "Balanced Stance", MitigationScope.All, Kind: MitigationKind.CritNoExtra,
            ConditionFr: "critique = coup max de l'arme, sans surplus", ConditionEn: "critical = weapon's max hit, no extra damage"),
        // « +1...24...30 armor and immunity to critical hits » (l'armure est déjà dans ArmorEffectsData).
        new(1373, "Stone Sheath", MitigationScope.All, Kind: MitigationKind.CritImmune,
            ConditionFr: "immunisé aux critiques", ConditionEn: "immune to critical hits"),

        // ── Lot 4 — conversions de type : aucune valeur, la ligne change le type REÇU ; le rang ne change que la durée
        // ou le niveau de l'esprit. Textes relus le 11/10/2026, conditions de zone tirées des notes du wiki.
        // « Converts elemental and physical damage you take or deal to earth damage » — porté par le DÉFENSEUR ; il
        // convertit aussi les ténèbres, le chaos et le sacré reçus (anomalie du wiki, confirmée par Philippe le 11/10).
        new(1371, "Stone Striker", MitigationScope.All, Kind: MitigationKind.Conversion,
            Conversion: TypeConversion.StoneStriker,
            ConditionFr: "élém., physique, ténèbres, chaos et sacré reçus", ConditionEn: "elemental, physical, dark, chaos and holy taken"),
        // « Converts physical damage to fire damage for creatures in range » — l'ATTAQUANT doit être dans la zone.
        new(465, "Greater Conflagration", MitigationScope.All, Kind: MitigationKind.Conversion,
            Conversion: TypeConversion.GreaterConflagration,
            ConditionFr: "physique ; attaquant dans la zone de l'esprit", ConditionEn: "physical; attacker within the spirit's range"),
        // « All arrows deal fire damage for creatures in range » — une flèche déjà élémentaire (corde) le reste.
        new(466, "Conflagration", MitigationScope.All, Kind: MitigationKind.Conversion,
            Conversion: TypeConversion.Conflagration,
            ConditionFr: "flèches ; attaquant dans la zone de l'esprit", ConditionEn: "arrows; attacker within the spirit's range"),
        // « Converts elemental damage to cold damage for creatures in range » — toi OU l'attaquant dans la zone.
        new(462, "Winter", MitigationScope.All, Kind: MitigationKind.Conversion,
            Conversion: TypeConversion.Winter,
            ConditionFr: "élémentaire ; toi ou l'attaquant dans la zone de l'esprit", ConditionEn: "elemental; you or the attacker within the spirit's range"),
        // « Converts target ally's attacks to holy damage and adds +20% armor penetration » — lancée sur l'ATTAQUANT.
        // Le sacré d'une attaque d'arme RESPECTE l'armure, sans bonus typé (wiki *Holy_damage*) : armure de base.
        new(267, "Judge's Insight", MitigationScope.All, Kind: MitigationKind.Conversion,
            Conversion: TypeConversion.JudgesInsight,
            ConditionFr: "lancée sur l'attaquant : ses attaques, +20 % de pénétration", ConditionEn: "cast on the attacker: their attacks, +20% armor penetration"),
    ];

    /// <summary>Valeur de la ligne (positive : % pour une réduction ou une hausse, points pour une réduction fixe) au
    /// rang donné — <paramref name="rank"/> est la case de la ligne, donc le n° du coup ou la Force selon
    /// <see cref="DamageMitigationDescriptor.RankBox"/>. 0 = rien à appliquer : ligne de critique, progression
    /// absente, ou compétence introuvable dans le catalogue.</summary>
    public static int ValueOf(DamageMitigationDescriptor descriptor, Skill? source, int rank)
    {
        if (descriptor.HasNoValue) return 0;
        int value;
        if (descriptor.RankBox == MitigationRankBox.HitNumber) value = descriptor.Fixed * Math.Max(1, rank);
        else if (descriptor.RankBox == MitigationRankBox.Strength) value = descriptor.Fixed * Math.Max(0, rank);
        else if (descriptor.Fixed > 0) value = descriptor.Fixed;
        else if (descriptor.Index < 0 || source?.Progression is not { } prog || descriptor.Index >= prog.Length) return 0;
        else value = (SkillProgression.IntAt(prog[descriptor.Index], rank) ?? 0) + descriptor.Offset;
        if (descriptor.Cap > 0) value = Math.Min(value, descriptor.Cap);
        return Math.Max(0, value);
    }
}
