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
    /// <summary>Les attaques ET les sorts (Protective Bond : « from a single attack or spell ») : pas un sceau, un cri…</summary>
    AttacksOrSpells,
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
    /// <summary>Plafond (lot 5) : un paquet ne retire pas plus de N % de la vie max (N = <see cref="DamageMitigationDescriptor.Fixed"/>).
    /// AVANT les % (expérience P2 de Philippe, 11/10/2026), après les hausses.</summary>
    Cap,
    /// <summary>Conversion en soin (lot 5) : jusqu'à N points du paquet deviennent un soin ; au-delà, on encaisse le
    /// surplus (Mark of Protection, Reversal of Fortune, Life Sheath). Après les réductions fixes.</summary>
    HealConversion,
    /// <summary>Soin déclenché par le coup (lot 5, Q22) : +N de vie par paquet reçu, sans toucher aux dégâts — seulement
    /// si le coup dépasse <see cref="DamageMitigationDescriptor.Above"/> avant plafond et réductions (Spirit Bond).</summary>
    HealOnHit,
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
    /// <summary>Nombre d'enchantements d'Assassin sur le défenseur, la compétence comprise (Shadow Form, lot 5) :
    /// valeur = <see cref="DamageMitigationDescriptor.Fixed"/> × nombre, 1 au moins.</summary>
    AssassinEnchantments,
    /// <summary>Rang de Faveur divine du LANCEUR (Seed of Life, lot 5) : valeur = <see cref="DamageMitigationDescriptor.Fixed"/>
    /// × rang ; le rang de titre de la compétence ne change que la durée.</summary>
    DivineFavor,
}

/// <summary>
/// Une réduction de dégâts REÇUS, appliquée APRÈS l'armure (chantier « calculateur d'armure : réductions de
/// dégâts », plan <c>docs/armor_calc_mitigation_plan.md</c>). Lot 1 = les pourcentages ; lot 3 = réductions fixes,
/// dégâts augmentés, critiques, redirection ; lot 4 = conversions de type ; lot 5 = plafonds et soins.
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
/// <paramref name="Above"/> = seuil d'un soin déclenché par le coup (lot 5) : il ne joue que si le paquet, avant
/// plafond et réductions, dépasse ce nombre (Spirit Bond : 50). 0 = tout paquet.
/// Lot 5 : pour une conversion en soin, <paramref name="AlsoLifeSteal"/> = elle convertit aussi le vol de vie
/// (Reversal of Fortune, Life Sheath) ; pour un soin déclenché, il se déclenche aussi sur un vol de vie (Weapon of Remedy).
/// </summary>
public sealed record DamageMitigationDescriptor(
    int SkillId, string Name, MitigationScope Scope, string? DamageType = null,
    int Index = -1, int Fixed = 0, string? ConditionFr = null, string? ConditionEn = null,
    MitigationKind Kind = MitigationKind.Percent, MitigationRankBox RankBox = MitigationRankBox.Attribute,
    int Offset = 0, int Cap = 0, bool AlsoLifeSteal = false, TypeConversion Conversion = TypeConversion.None,
    int Above = 0)
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

        // ── Lot 5 — plafonds : un paquet ne retire pas plus de N % de la vie max (saisie, défaut 480). Ordre de Philippe
        // (P2 et Q21, 11/10/2026) : après l'armure et les hausses, AVANT les % et les fixes. Le plus bas l'emporte.
        // Textes relus le 11/10/2026 ; la valeur est le % écrit en dur, le rang ne change que la durée.
        // « Incoming damage is reduced to 10% of target ally's maximum Health ».
        new(245, "Protective Spirit", MitigationScope.All, Fixed: 10, Kind: MitigationKind.Cap),
        // « cannot lose more than 10% maximum Health from a single attack » — anomalie wiki : sorts et sceaux aussi.
        new(982, "Shelter", MitigationScope.All, Fixed: 10, Kind: MitigationKind.Cap,
            ConditionFr: "à portée de l'esprit", ConditionEn: "within the spirit's range"),
        new(3016, "Shelter (PvP)", MitigationScope.All, Fixed: 10, Kind: MitigationKind.Cap,
            ConditionFr: "à portée de l'esprit", ConditionEn: "within the spirit's range"),
        // « cannot lose more than 5% max Health from a single attack or spell » — l'énergie du lanceur est ignorée.
        new(263, "Protective Bond", MitigationScope.AttacksOrSpells, Fixed: 5, Kind: MitigationKind.Cap,
            ConditionFr: "énergie du lanceur ignorée", ConditionEn: "caster's Energy loss ignored"),
        // « You cannot lose more than 20% of your max Health from a single hit » (sort d'objet : ses cendres en main).
        new(812, "Defiant Was Xinrae", MitigationScope.All, Fixed: 20, Kind: MitigationKind.Cap),
        // « The next time target ally takes damage from a foe, that damage is limited to 5% of target ally's max Health and
        // that ally steals 20...68...80 Health from that foe » — plafond + soin du vol, colonne 0.
        new(1750, "Xinrae's Weapon", MitigationScope.All, Fixed: 5, Kind: MitigationKind.Cap,
            ConditionFr: "prochain coup seulement ; + vol de vie (soin)", ConditionEn: "next hit only; + life steal (heal)"),
        new(1750, "Xinrae's Weapon", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit),

        // ── Lot 5 — conversions en soin : jusqu'à la valeur, le paquet devient un soin ; au-delà on encaisse le surplus
        // (wiki *Damage_reduction* : « heals the target for health equal to the amount which damage is reduced by »).
        // Après les réductions fixes. Affichage « 0 (soin X) » (Q9, Philippe, 11/10/2026).
        // « Converts incoming damage to healing (maximum 6...49...60) » — colonne 0, chaque paquet.
        new(269, "Mark of Protection", MitigationScope.All, Index: 0, Kind: MitigationKind.HealConversion,
            ConditionFr: "les dégâts deviennent un soin, jusqu'à la valeur", ConditionEn: "damage becomes healing, up to the value"),
        // « Converts the next incoming damage or life steal (maximum 15...67...80) to healing » — colonne 0.
        new(307, "Reversal of Fortune", MitigationScope.All, Index: 0, Kind: MitigationKind.HealConversion, AlsoLifeSteal: true,
            ConditionFr: "prochain coup seulement ; vol de vie compris", ConditionEn: "next hit only; life steal included"),
        // « Converts the next incoming damage or life steal (maximum 20...84...100) to healing » — colonne 1 (0 = conditions).
        new(1123, "Life Sheath", MitigationScope.All, Index: 1, Kind: MitigationKind.HealConversion, AlsoLifeSteal: true,
            ConditionFr: "prochain coup seulement ; vol de vie compris", ConditionEn: "next hit only; life steal included"),

        // ── Lot 5 — soins DÉCLENCHÉS par un coup (Q22 : toute la famille, 11/10/2026) : les dégâts ne bougent pas, le soin
        // s'affiche à côté. Toutes en colonne 0 sauf Supportive Spirit (0 = durée).
        // « Heals for 30...78...90 whenever target ally takes more than 50 damage » : sur le coup AVANT Protective Spirit
        // et les autres réductions (P2 de Philippe ; wiki : « even if it is reduced to 50 or below by other means »).
        new(1114, "Spirit Bond", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit, Above: 50,
            ConditionFr: "si le coup dépasse 50, avant plafond et réductions", ConditionEn: "if the hit exceeds 50, before caps and reductions"),
        new(2892, "Spirit Bond (PvP)", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit, Above: 50,
            ConditionFr: "si le coup dépasse 50, avant plafond et réductions", ConditionEn: "if the hit exceeds 50, before caps and reductions"),
        // « Heals for 5...29...35 whenever target takes damage ».
        new(285, "Healing Hands", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit),
        // « Target and adjacent allies gain 3...25...30 Health whenever this ally takes damage. Cannot self-target. »
        new(274, "Healing Seed", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit,
            ConditionFr: "lancée par un allié (pas sur soi)", ConditionEn: "cast by an ally (cannot self-target)"),
        // « Party members in earshot gain 15...63...75 Health the next time they take damage ».
        new(1564, "Ballad of Restoration", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit,
            ConditionFr: "prochain coup seulement", ConditionEn: "next hit only"),
        new(2877, "Ballad of Restoration (PvP)", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit,
            ConditionFr: "prochain coup seulement", ConditionEn: "next hit only"),
        // « Heals for 5...41...50 whenever target ally takes damage while knocked-down » — colonne 1.
        new(1391, "Supportive Spirit", MitigationScope.All, Index: 1, Kind: MitigationKind.HealOnHit,
            ConditionFr: "à terre seulement", ConditionEn: "while knocked down"),
        // « The next time target ally takes damage or life steal from a foe, this ally steals 20...68...80 Health ».
        new(1752, "Weapon of Remedy", MitigationScope.All, Index: 0, Kind: MitigationKind.HealOnHit, AlsoLifeSteal: true,
            ConditionFr: "prochain coup seulement ; vol de vie compris", ConditionEn: "next hit only; life steal included"),
        // « Heals party members for 2 for each rank in Divine Favor whenever target takes damage. Cannot self target. »
        // (PvE, titre Sunspear) — trouvée par le verrou 6 le 11/10/2026. Le perso protégé est du groupe, donc soigné ; la
        // case porte le rang de Faveur divine du lanceur.
        new(2105, "Seed of Life", MitigationScope.All, Fixed: 2, Kind: MitigationKind.HealOnHit,
            RankBox: MitigationRankBox.DivineFavor,
            ConditionFr: "lancée par un allié (pas sur soi) ; case = sa Faveur divine",
            ConditionEn: "cast by an ally (cannot self-target); box = their Divine Favor"),

        // ── Lot 5 — Shadow Form (Q24, 11/10/2026) : ce n'est PAS un plafond du défenseur (son « cannot deal more than »
        // limite les dégâts qu'il INFLIGE). « Gain 5 damage reduction for each Assassin enchantment on you » : fixe,
        // case = nombre d'enchantements d'Assassin, elle comprise. La variante PvP (esquive) est écartée.
        new(826, "Shadow Form", MitigationScope.All, Fixed: 5, Kind: MitigationKind.Flat,
            RankBox: MitigationRankBox.AssassinEnchantments,
            ConditionFr: "case = enchantements d'Assassin (−5 chacun, elle comprise)",
            ConditionEn: "box = Assassin enchantments (−5 each, itself included)"),
    ];

    /// <summary>Valeur de la ligne (positive : % pour une réduction, une hausse ou un plafond, points pour une réduction
    /// fixe ou un soin) au rang donné — <paramref name="rank"/> est la case de la ligne, donc le n° du coup, la Force ou
    /// le nombre d'enchantements selon <see cref="DamageMitigationDescriptor.RankBox"/>. 0 = rien à appliquer : ligne
    /// de critique, progression absente, ou compétence introuvable dans le catalogue.</summary>
    public static int ValueOf(DamageMitigationDescriptor descriptor, Skill? source, int rank)
    {
        if (descriptor.HasNoValue) return 0;
        int value;
        if (descriptor.RankBox is MitigationRankBox.HitNumber or MitigationRankBox.AssassinEnchantments)
            value = descriptor.Fixed * Math.Max(1, rank);
        else if (descriptor.RankBox is MitigationRankBox.Strength or MitigationRankBox.DivineFavor)
            value = descriptor.Fixed * Math.Max(0, rank);
        else if (descriptor.Fixed > 0) value = descriptor.Fixed;
        else if (descriptor.Index < 0 || source?.Progression is not { } prog || descriptor.Index >= prog.Length) return 0;
        else value = (SkillProgression.IntAt(prog[descriptor.Index], rank) ?? 0) + descriptor.Offset;
        if (descriptor.Cap > 0) value = Math.Min(value, descriptor.Cap);
        return Math.Max(0, value);
    }
}
