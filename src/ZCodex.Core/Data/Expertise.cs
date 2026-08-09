using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Réduction du coût en énergie par l'attribut primaire Ranger « Expertise » : 4 %/rang,
/// ARRONDI à l'entier le plus proche (min 0), sur les attaques, compétences de contact (Touch),
/// rituels et TOUS les skills de profession Ranger.
/// Règle vérifiée sur la table brute du wiki (Expertise) : base 1 reste à 1 jusqu'au rang 13
/// (1×0,48 = 0,48 → 0 — une troncature l'aurait mise à 0 dès le rang 1) ; base 10 au rang 6 =
/// 10×0,76 = 7,6 → 8. La valeur ne tombe jamais pile sur ,5 (produit toujours multiple de 4),
/// donc la convention de demi-arrondi est sans effet.
/// N'a d'effet que si Ranger est primaire (rang &gt; 0), le mod « of the Ranger » (Expertise
/// forcée à 5) étant déjà porté par CharacterSlotViewModel.AttributeLevel.
/// </summary>
public static class Expertise
{
    /// <summary>Nom de l'attribut Ranger primaire (clé pour AttributeLevel).</summary>
    public const string AttributeName = "Expertise";

    /// <summary>Le coût en énergie de cette compétence est-il réduit par l'Expertise ?</summary>
    public static bool Affects(Skill skill) =>
        skill.Profession == Profession.Ranger                             // tous les skills Ranger
        || skill.SkillType.EndsWith("Attack", StringComparison.Ordinal)   // toutes attaques, Pet Attack compris
        || skill.SkillType.Contains("Touch", StringComparison.Ordinal)    // Touch Skill/Spell/Signet/Hex
        || skill.SkillType is "Binding Ritual" or "Nature Ritual" or "Ebon Vanguard Ritual";

    /// <summary>Coût réduit = arrondi(base × (100 − 4×rang) / 100), plancher 0.</summary>
    public static int ReducedCost(int baseCost, int rank)
    {
        int remaining = Math.Max(0, 100 - 4 * rank);
        return (baseCost * remaining + 50) / 100;   // +50 = arrondi au plus proche (numérateur ≥ 0)
    }
}
