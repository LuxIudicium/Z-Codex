using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>Ce que fait un malus porté par l'attaquant (calculateur d'armure, lot 6b).</summary>
public enum AttackerPenaltyKind
{
    /// <summary>−p % des dégâts de BASE de l'arme, avant l'armure, comme le malus propre d'une compétence (Dual Shot)
    /// et l'inscription : ni le « +X » ni les paquets de la compétence (wiki *Weakness*, bug de *Flurry*, anomalie de
    /// *Life_Attunement*). Les % se multiplient entre eux.</summary>
    WeaponPercent,
    /// <summary>−N points sur chaque paquet d'ATTAQUE qui respecte l'armure, coup entier (arme + « +X »), APRÈS l'armure et
    /// AVANT tout ce qui vient du défenseur (Q29 et Q32, Philippe, 12/10/2026).</summary>
    Flat,
    /// <summary>Comme <see cref="Flat"/>, et l'attaque vole autant de vie (Nightmare Weapon, Grenth's Aura) : le vol ne
    /// dépend pas des dégâts (notes du wiki).</summary>
    FlatToLifeSteal,
    /// <summary>Tous les attributs de l'attaquant à 0 (Wail of Doom) : aucune valeur, le rang EFFECTIF de chaque ligne
    /// tombe à 0.</summary>
    AttributesToZero,
}

/// <summary>
/// Un malus porté par l'ATTAQUANT (chantier « calculateur d'armure : réductions de dégâts », lot 6b, cadrage du 12/10/2026
/// au § 6.6b du plan). Même patron que <see cref="DamageMitigationDescriptor"/> : la valeur n'est jamais recopiée,
/// <paramref name="Index"/> = colonne de progression SONDÉE dans la base (lue au rang de la case, qui est l'attribut de CELUI
/// QUI LANCE le maléfice — un coéquipier du défenseur —, jamais celui de l'attaquant : Faiblesse et Wail of Doom n'y touchent
/// pas) ; <paramref name="Fixed"/> = valeur écrite en dur (Flurry 25 %).
/// <paramref name="SkillId"/> = 0 pour la condition Faiblesse (pas une compétence : nom tiré de <see cref="GwConditionData"/>).
/// <paramref name="AttributeLoss"/> = attributs retirés à l'attaquant (Faiblesse : 1 ; un attribut à 0 n'est pas touché).
/// <paramref name="NoCritical"/> = l'attaquant ne peut plus faire de critique (Dulled Weapon).
/// <paramref name="ScytheOnly"/> = seulement les attaques à la faux (Grenth's Aura).
/// </summary>
public sealed record AttackerPenaltyDescriptor(
    int SkillId, string Name, AttackerPenaltyKind Kind, int Index = -1, int Fixed = 0,
    int AttributeLoss = 0, bool NoCritical = false, bool ScytheOnly = false,
    string? ConditionFr = null, string? ConditionEn = null)
{
    /// <summary>La ligne a une case de rang : sa valeur se lit dans la progression.</summary>
    public bool ReadsProgression => Fixed == 0 && Kind != AttackerPenaltyKind.AttributesToZero;

    /// <summary>La condition Faiblesse, et non une compétence.</summary>
    public bool IsWeakness => SkillId == 0;
}

public static class AttackerPenaltyData
{
    /// <summary>Clé de la condition Faiblesse dans <see cref="GwConditionData"/> (nom canonique anglais).</summary>
    public const string WeaknessName = "Weakness";

    /// <summary>Rang par défaut d'une case (celui des lignes du défenseur).</summary>
    public const int DefaultRank = 12;

    // Textes relus le 12/10/2026 dans la base et le wiki (action=raw), index sondés au rang 12. Le contrôle durable est
    // MitigationCoverage (verrous 7 et 8), qui exige que tout ce que ses motifs attrapent soit ici ou écarté par écrit.
    public static readonly IReadOnlyList<AttackerPenaltyDescriptor> All =
    [
        // ── % sur l'arme seule, avant l'armure ──
        // « reduces the equipped weapon's base damage by 66% » + « reduces all attributes by 1 » (wiki *Weakness*).
        new(0, WeaknessName, AttackerPenaltyKind.WeaponPercent, Fixed: 66, AttributeLoss: 1,
            ConditionFr: "dégâts de l'arme seule ×0,34 ; attributs de l'attaquant −1",
            ConditionEn: "weapon damage only ×0.34; attacker's attributes −1"),
        // « You do 25% less damage » — bug wiki : les dégâts de base de l'arme seulement.
        new(344, "Flurry", AttackerPenaltyKind.WeaponPercent, Fixed: 25,
            ConditionFr: "dégâts de l'arme seule", ConditionEn: "weapon damage only"),
        // « This ally deals 30% less damage with attacks » — anomalie wiki : l'arme seule (lancée par un allié de l'attaquant).
        new(244, "Life Attunement", AttackerPenaltyKind.WeaponPercent, Fixed: 30,
            ConditionFr: "dégâts de l'arme seule", ConditionEn: "weapon damage only"),

        // ── Fixes, attaques seulement ──
        // « Removes ability to land a critical hit. Reduces damage by 3...17...20 » — colonne 1 (0 = durée) ; anomalie
        // wiki : les attaques seulement.
        new(1235, "Dulled Weapon", AttackerPenaltyKind.Flat, Index: 1, NoCritical: true,
            ConditionFr: "attaques seulement ; plus de critique", ConditionEn: "attacks only; no critical hits"),
        // « Target foe deals -5...41...50 attack damage » — colonne 1 (0 = durée).
        new(827, "Siphon Strength", AttackerPenaltyKind.Flat, Index: 1,
            ConditionFr: "attaques seulement", ConditionEn: "attacks only"),
        // « Target foe's attacks deal 1...12...15 less damage » — colonne 2 (0 = durée, 1 = dégâts renvoyés). La variante
        // PvP n'a plus de réduction (voir MitigationCoverage).
        new(26, "Empathy", AttackerPenaltyKind.Flat, Index: 2,
            ConditionFr: "attaques seulement", ConditionEn: "attacks only"),

        // ── Dégâts changés en vol de vie ──
        // « Target ally's attacks steal 10...42...50 Health but deal 10...42...50 less damage. Ends after 3 attacks. »
        new(795, "Nightmare Weapon", AttackerPenaltyKind.FlatToLifeSteal, Index: 0,
            ConditionFr: "attaques seulement ; autant de vol de vie", ConditionEn: "attacks only; as much life steal"),
        // « You deal 5...21...25 less damage and steal 5...21...25 Health when you hit with a scythe. »
        new(2013, "Grenth's Aura", AttackerPenaltyKind.FlatToLifeSteal, Index: 0, ScytheOnly: true,
            ConditionFr: "attaques à la faux ; autant de vol de vie", ConditionEn: "scythe attacks; as much life steal"),

        // ── Attributs ──
        // « Target foe's attributes are 0 » (rang de la case = durée seulement).
        new(764, "Wail of Doom", AttackerPenaltyKind.AttributesToZero,
            ConditionFr: "attributs de l'attaquant à 0", ConditionEn: "attacker's attributes at 0"),
    ];

    /// <summary>Le descripteur d'une compétence (ou de Faiblesse, id 0), null s'il n'y en a pas.</summary>
    public static AttackerPenaltyDescriptor? For(int skillId) => All.FirstOrDefault(d => d.SkillId == skillId);

    /// <summary>Valeur de la ligne au rang de sa case (points, ou % pour <see cref="AttackerPenaltyKind.WeaponPercent"/>).
    /// 0 = rien : progression absente ou compétence introuvable dans le catalogue.</summary>
    public static int ValueOf(AttackerPenaltyDescriptor descriptor, Skill? source, int rank)
    {
        if (descriptor.Kind == AttackerPenaltyKind.AttributesToZero) return 0;
        if (descriptor.Fixed > 0) return descriptor.Fixed;
        if (descriptor.Index < 0 || source?.Progression is not { } prog || descriptor.Index >= prog.Length) return 0;
        return Math.Max(0, SkillProgression.IntAt(prog[descriptor.Index], rank) ?? 0);
    }
}
