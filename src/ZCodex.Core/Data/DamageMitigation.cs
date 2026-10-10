using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Moteur PUR des réductions de dégâts REÇUS, après l'armure (chantier « calculateur d'armure : réductions
/// de dégâts », lot 1). Il ne calcule aucun dégât lui-même : il rend le facteur que
/// <see cref="ReferenceAttack"/> passe au paramètre <c>multiplier</c> de <see cref="SkillDamage.DamageAt"/> /
/// <see cref="WeaponStrike.DamageAt"/>, seul endroit où un dégât se calcule.
///
/// Les pourcentages se MULTIPLIENT entre eux (wiki *Damage_reduction* : « stacks multiplicatively »).
/// Un paquet qui ignore l'armure (sacré, ombre, perte de vie) n'en reçoit aucun (décision 6.2 de Philippe).
///
/// ⚠ DEUX CHOIX PROVISOIRES, à confirmer par l'expérience en jeu P1 (§ 5 du plan) :
/// <list type="bullet">
/// <item><b>Q1, arrondi</b> : le facteur entre dans le multiplicateur de <c>DamageAt</c>, donc un seul
/// arrondi tombe, à la fin (précédent du lot 6b du chantier infobulle).</item>
/// <item><b>Q5, ordre</b> : les pourcentages passent AVANT les réductions fixes d'équipement (rune
/// d'Absorption, Knight's, inscriptions), que <see cref="ReferenceAttack"/> retranche ensuite.</item>
/// </list>
/// Si P1 dit autrement, c'est ici et dans <see cref="ReferenceAttack"/> que ça change, nulle part ailleurs.
/// </summary>
public static class DamageMitigation
{
    /// <summary>Une réduction cochée, valeur déjà résolue au rang (en %, positive). On stocke l'écart au
    /// neutre, jamais un facteur : un <c>default</c> vaut 0 %, donc « aucune réduction ».</summary>
    public readonly record struct Active(DamageMitigationDescriptor Descriptor, int Percent);

    /// <summary>Les réductions qui agissent sur le défenseur.</summary>
    public sealed record Context(IReadOnlyList<Active> Reductions)
    {
        public static readonly Context None = new([]);
    }

    /// <summary>La réduction touche-t-elle un paquet de ce type, venu d'un sort ou non ?</summary>
    public static bool Applies(DamageMitigationDescriptor descriptor, string? damageType, bool isSpell) =>
        descriptor.Scope switch
        {
            MitigationScope.All        => true,
            MitigationScope.DamageType => damageType is not null && damageType == descriptor.DamageType,
            MitigationScope.Spells     => isSpell,
            _ => false,
        };

    /// <summary>Facteur des dégâts d'un paquet qui respecte l'armure : produit des (1 − p). 1.0 = rien.</summary>
    public static double Factor(Context? context, string? damageType, bool isSpell)
    {
        double factor = 1.0;
        foreach (var a in context?.Reductions ?? [])
            if (a.Descriptor is { } d && a.Percent > 0 && Applies(d, damageType, isSpell))
                factor *= 1 - Math.Min(a.Percent, 100) / 100.0;
        return factor;
    }

    /// <summary>Les dégâts de cette compétence sont-ils des dégâts de SORT ? Tout type qui contient
    /// « Spell » : sorts, maléfices, enchantements, sorts d'arme et d'objet, puits, protections.</summary>
    public static bool IsSpell(Skill skill) => skill.SkillType.Contains("Spell", StringComparison.Ordinal);
}
