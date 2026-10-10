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
}

/// <summary>
/// Une réduction de dégâts REÇUS, appliquée APRÈS l'armure (chantier « calculateur d'armure : réductions de
/// dégâts », plan <c>docs/armor_calc_mitigation_plan.md</c>). Lot 1 = les pourcentages.
///
/// La valeur n'est jamais recopiée : <paramref name="Index"/> = colonne de progression SONDÉE dans la base
/// (lue au rang, donc une variante PvP ou une mise à jour du catalogue sont justes d'office) ;
/// <paramref name="Fixed"/> = pourcentage écrit en dur dans la description (Ether Prism 75 %).
/// <paramref name="ConditionFr"/> / <paramref name="ConditionEn"/> = condition INFORMATIVE : comme pour les
/// effets d'armure, cocher la ligne veut dire que la condition est remplie.
/// </summary>
public sealed record DamageMitigationDescriptor(
    int SkillId, string Name, MitigationScope Scope, string? DamageType = null,
    int Index = -1, int Fixed = 0, string? ConditionFr = null, string? ConditionEn = null);

public static class DamageMitigationData
{
    // Index sondés dans la base le 09/10/2026 (rang 0 / 12 / 15 confrontés à la description).
    // Le contrôle durable est MitigationCoverage, qui exige que tout ce que le motif attrape soit ici.
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
    ];

    /// <summary>Réduction en % au rang donné (positive). 0 = rien à appliquer : progression absente, ou
    /// compétence introuvable dans le catalogue.</summary>
    public static int PercentOf(DamageMitigationDescriptor descriptor, Skill? source, int rank)
    {
        if (descriptor.Fixed > 0) return descriptor.Fixed;
        if (descriptor.Index < 0 || source?.Progression is not { } prog || descriptor.Index >= prog.Length)
            return 0;
        return SkillProgression.IntAt(prog[descriptor.Index], rank) ?? 0;
    }
}
