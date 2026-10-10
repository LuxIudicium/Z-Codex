using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Moteur PUR des réductions de dégâts REÇUS, après l'armure (chantier « calculateur d'armure : réductions
/// de dégâts », lots 1 et 3). Il ne calcule aucun dégât lui-même : il rend le facteur que
/// <see cref="ReferenceAttack"/> passe au paramètre <c>multiplier</c> de <see cref="SkillDamage.DamageAt"/> /
/// <see cref="WeaponStrike.DamageAt"/>, seul endroit où un dégât se calcule, puis les points à retirer du paquet.
///
/// Les pourcentages se MULTIPLIENT entre eux (wiki *Damage_reduction* : « stacks multiplicatively ») ; les hausses
/// (Frenzy) entrent dans le même produit. Un paquet qui ignore l'armure (sacré, ombre, perte de vie) n'en reçoit
/// aucun (décision 6.2 de Philippe) ; le vol de vie ne reçoit que les réductions fixes qui le citent (Q14).
///
/// <list type="bullet">
/// <item><b>Q5, ordre — CONFIRMÉ EN JEU</b> par l'expérience P1 de Philippe (10/10/2026, Mantra of Earth +
/// Stoneflesh Aura) : l'armure d'abord, puis les pourcentages, puis les réductions fixes (compétences, rune
/// d'Absorption, Knight's, inscriptions), que <see cref="ReferenceAttack"/> retranche ensuite. Ne pas rouvrir.</item>
/// <item><b>Q1, arrondi — VALIDÉ par Philippe</b> (10/10/2026) : le facteur entre dans le multiplicateur de
/// <c>DamageAt</c>, donc un seul arrondi tombe, à la fin (précédent du lot 6b du chantier infobulle).</item>
/// <item><b>Réduction fixe = sur le paquet ENTIER</b> (Philippe, 10/10/2026 : « tu calcules tous les dégâts et à la
/// fin, tu retires ce que Stoneflesh atténue » ; épée 22 + bonus 13 = 35, Stoneflesh −25 → 10) : arme ET « +X »,
/// plancher 0 à la fin.</item>
/// </list>
/// Le « +X » d'une attaque d'arme, compté sans armure depuis le chantier 10, ne reçoit AUCUN pourcentage
/// (confirmé par Philippe le 10/10/2026) — mais il reçoit les réductions fixes, puisqu'il est dans le paquet.
/// </summary>
public static class DamageMitigation
{
    /// <summary>Une ligne cochée, valeur déjà résolue à sa case (positive : % ou points selon la nature). On stocke
    /// l'écart au neutre, jamais un facteur : un <c>default</c> vaut 0, donc « rien ».</summary>
    public readonly record struct Active(DamageMitigationDescriptor Descriptor, int Value)
    {
        /// <summary>La ligne agit : une valeur non nulle, ou une ligne de critique (qui n'en a pas).</summary>
        public bool IsEffective => Descriptor is { } d && (Value > 0 || d.IsCritical);
    }

    /// <summary>Les réductions qui agissent sur le défenseur.</summary>
    public sealed record Context(IReadOnlyList<Active> Reductions)
    {
        public static readonly Context None = new([]);
    }

    /// <summary>Ce que devient un coup critique sur le défenseur.</summary>
    public enum CritRule
    {
        /// <summary>Critique ordinaire : max de l'arme ×√2.</summary>
        Normal,
        /// <summary>Balanced Stance : max de l'arme, sans le ×√2.</summary>
        NoExtra,
        /// <summary>Stoneflesh Aura, Stone Sheath : plus de critique du tout. L'emporte sur <see cref="NoExtra"/>.</summary>
        Immune,
    }

    /// <summary>La réduction touche-t-elle un paquet de ce type, venu d'un sort ou d'une attaque ?</summary>
    public static bool Applies(DamageMitigationDescriptor descriptor, string? damageType, bool isSpell, bool isAttack = false) =>
        descriptor.Scope switch
        {
            MitigationScope.All        => true,
            MitigationScope.DamageType => damageType is not null && damageType == descriptor.DamageType,
            MitigationScope.Spells     => isSpell,
            MitigationScope.Attacks    => isAttack,
            _ => false,
        };

    /// <summary>Facteur des dégâts d'un paquet qui respecte l'armure : produit des (1 − p) et des (1 + hausse).
    /// 1.0 = rien.</summary>
    public static double Factor(Context? context, string? damageType, bool isSpell, bool isAttack = false)
    {
        double factor = 1.0;
        foreach (var a in context?.Reductions ?? [])
        {
            if (a.Descriptor is not { } d || a.Value <= 0 || !Applies(d, damageType, isSpell, isAttack)) continue;
            if (d.Kind == MitigationKind.Percent) factor *= 1 - Math.Min(a.Value, 100) / 100.0;
            else if (d.Kind == MitigationKind.Increase) factor *= 1 + a.Value / 100.0;
        }
        return factor;
    }

    /// <summary>Points à retirer d'un paquet qui respecte l'armure, après les % : somme des réductions fixes qui le
    /// touchent (retirer l'une après l'autre avec plancher 0 ou la somme d'un coup revient au même).</summary>
    public static int Flat(Context? context, string? damageType, bool isSpell, bool isAttack = false) =>
        (context?.Reductions ?? [])
            .Where(a => a.Descriptor is { Kind: MitigationKind.Flat } d && a.Value > 0
                        && Applies(d, damageType, isSpell, isAttack))
            .Sum(a => a.Value);

    /// <summary>Points à retirer d'un paquet de VOL DE VIE : seules les réductions qui le citent (Q14).</summary>
    public static int FlatLifeSteal(Context? context) =>
        (context?.Reductions ?? [])
            .Where(a => a.Descriptor is { Kind: MitigationKind.Flat, AlsoLifeSteal: true } && a.Value > 0)
            .Sum(a => a.Value);

    /// <summary>Règle des critiques sur le défenseur.</summary>
    public static CritRule CriticalRule(Context? context)
    {
        var kinds = (context?.Reductions ?? []).Where(a => a.Descriptor is not null).Select(a => a.Descriptor.Kind).ToList();
        return kinds.Contains(MitigationKind.CritImmune) ? CritRule.Immune
             : kinds.Contains(MitigationKind.CritNoExtra) ? CritRule.NoExtra
             : CritRule.Normal;
    }

    /// <summary>Les dégâts de cette compétence sont-ils des dégâts de SORT ? Tout type qui contient
    /// « Spell » : sorts, maléfices, enchantements, sorts d'arme et d'objet, puits, protections.</summary>
    public static bool IsSpell(Skill skill) => skill.SkillType.Contains("Spell", StringComparison.Ordinal);

    /// <summary>Les dégâts de cette compétence sont-ils des dégâts d'ATTAQUE ? Tout type qui contient « Attack »
    /// (attaques d'arme, de mêlée, de familier…). Le coup d'arme d'une attaque l'est toujours.</summary>
    public static bool IsAttack(Skill skill) => skill.SkillType.Contains("Attack", StringComparison.Ordinal);
}
