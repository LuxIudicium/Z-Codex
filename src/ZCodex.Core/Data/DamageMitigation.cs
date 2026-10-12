using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Moteur PUR des réductions de dégâts REÇUS, après l'armure (chantier « calculateur d'armure : réductions
/// de dégâts », lots 1, 3 et 4). Il ne calcule aucun dégât lui-même : il rend le type REÇU de chaque paquet
/// (conversions du lot 4 — c'est lui qui choisit la colonne d'armure et les réductions par type), le facteur que
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
/// <item><b>Lot 5, ordre complet — tranché par Philippe</b> (expérience P2 et Q21, 11/10/2026) : armure → hausses
/// (Frenzy…) → seuil de Spirit Bond → plafond (Protective Spirit…) → % → fixes → conversions en soin → soins déclenchés.
/// La liste compétence par compétence du wiki *Order_of_damage_modifiers* (qui se dit fautive) a été écartée.</item>
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
        /// <summary>La ligne agit : une valeur non nulle, ou une ligne sans valeur (critique, conversion de type).</summary>
        public bool IsEffective => Descriptor is { } d && (Value > 0 || d.HasNoValue);
    }

    /// <summary>Les conversions de type cochées (lot 4). Tout à faux = rien à convertir.</summary>
    public readonly record struct Conversions(bool StoneStriker, bool GreaterConflagration, bool Conflagration,
                                              bool Winter, bool JudgesInsight);

    /// <summary>Type d'un paquet tel que le défenseur le REÇOIT ; <see cref="Converted"/> = une conversion l'a changé
    /// (le type propre d'une compétence — Lightning Javelin, Judgment Strike — n'en est pas une).</summary>
    public readonly record struct HitType(string Received, bool Converted);

    /// <summary>Vie max du défenseur par défaut (Q15, Philippe, 11/10/2026 : saisie à la main, 480 par défaut).</summary>
    public const int DefaultMaxHealth = 480;

    /// <summary>Les réductions qui agissent sur le défenseur, et sa vie max (base des plafonds du lot 5).</summary>
    public sealed record Context(IReadOnlyList<Active> Reductions, int MaxHealth = DefaultMaxHealth)
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
            MitigationScope.AttacksOrSpells => isSpell || isAttack,
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

    /// <summary>Facteur des seules HAUSSES (Frenzy…) : elles passent AVANT le plafond (lot 5), sinon elles le
    /// perceraient — c'est aussi l'ordre du wiki *Order_of_damage_modifiers*.</summary>
    public static double IncreaseFactor(Context? context, string? damageType, bool isSpell, bool isAttack = false)
        => KindFactor(context, MitigationKind.Increase, damageType, isSpell, isAttack);

    /// <summary>Facteur des seules RÉDUCTIONS en %, appliqué APRÈS le plafond (expérience P2 de Philippe).</summary>
    public static double PercentFactor(Context? context, string? damageType, bool isSpell, bool isAttack = false)
        => KindFactor(context, MitigationKind.Percent, damageType, isSpell, isAttack);

    private static double KindFactor(Context? context, MitigationKind kind, string? damageType, bool isSpell, bool isAttack)
    {
        double factor = 1.0;
        foreach (var a in context?.Reductions ?? [])
        {
            if (a.Descriptor is not { } d || d.Kind != kind || a.Value <= 0 || !Applies(d, damageType, isSpell, isAttack)) continue;
            factor *= kind == MitigationKind.Percent ? 1 - Math.Min(a.Value, 100) / 100.0 : 1 + a.Value / 100.0;
        }
        return factor;
    }

    /// <summary>
    /// Plafond d'un paquet, en points (lot 5) : vie max × le plus petit % des plafonds qui le touchent ; null = aucun.
    /// Il porte sur le paquet ENTIER — arme, « +X », vulnérabilité sacrée — et aussi, au contraire des % et des fixes,
    /// sur les paquets qui ignorent l'armure (Q23, Philippe, 11/10/2026). Jamais sur un vol de vie (ce ne sont pas des
    /// dégâts).
    /// </summary>
    public static double? Cap(Context? context, string? damageType, bool isSpell, bool isAttack = false)
    {
        var percents = (context?.Reductions ?? [])
            .Where(a => a.Descriptor is { Kind: MitigationKind.Cap } d && a.Value > 0 && Applies(d, damageType, isSpell, isAttack))
            .Select(a => a.Value).ToList();
        return percents.Count == 0 ? null : (context?.MaxHealth ?? DefaultMaxHealth) * percents.Min() / 100.0;
    }

    /// <summary>Un paquet après tout le pipeline : ce que le défenseur perd, et le soin qu'il en tire (lot 5).</summary>
    public readonly record struct Outcome(int Taken, int Heal);

    /// <summary>
    /// Dernière étape d'un paquet (lot 5, Q22 / Q23) : les conversions en soin, puis les soins déclenchés par le coup.
    /// <paramref name="taken"/> = le paquet après plafond, % et fixes (déjà arrondi) ; <paramref name="before"/> = le même
    /// paquet AVANT plafond et réductions (après l'armure et les hausses) : c'est lui que lit le seuil de Spirit Bond
    /// (« Spirit Bond passe toujours avant Esprit prot », Philippe, 11/10/2026). <paramref name="lifeSteal"/> = le paquet
    /// est un vol de vie : seules les lignes qui le citent jouent.
    /// Chaque conversion convertit jusqu'à sa valeur ce qui reste du paquet : au total, min(paquet, somme des valeurs),
    /// quel que soit leur ordre.
    /// </summary>
    public static Outcome Heal(Context? context, int taken, int before, bool lifeSteal = false)
    {
        int heal = 0;
        foreach (var a in context?.Reductions ?? [])
        {
            if (a.Descriptor is not { Kind: MitigationKind.HealConversion } d || a.Value <= 0) continue;
            if (lifeSteal && !d.AlsoLifeSteal) continue;
            int converted = Math.Min(taken, a.Value);
            taken -= converted;
            heal += converted;
        }
        foreach (var a in context?.Reductions ?? [])
        {
            if (a.Descriptor is not { Kind: MitigationKind.HealOnHit } d || a.Value <= 0) continue;
            if (lifeSteal && !d.AlsoLifeSteal) continue;
            if (before > d.Above) heal += a.Value;
        }
        return new(taken, heal);
    }

    /// <summary>Une ligne cochée agit-elle à l'étape des soins ?</summary>
    public static bool HasHeals(Context? context) =>
        (context?.Reductions ?? []).Any(a => a.Descriptor is { Kind: MitigationKind.HealConversion or MitigationKind.HealOnHit }
                                             && a.Value > 0);

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

    /// <summary>Conversions de type allumées par les lignes cochées.</summary>
    public static Conversions ConversionsOf(Context? context)
    {
        bool On(TypeConversion c) => (context?.Reductions ?? []).Any(a => a.Descriptor?.Conversion == c);
        return new(On(TypeConversion.StoneStriker), On(TypeConversion.GreaterConflagration),
                   On(TypeConversion.Conflagration), On(TypeConversion.Winter), On(TypeConversion.JudgesInsight));
    }

    /// <summary>Type REÇU d'un paquet qui n'est pas un coup d'arme (sort, paquet typé d'une compétence) : seules les
    /// conversions de zone et du défenseur s'y appliquent. Null pour un paquet sans type.</summary>
    public static string? ReceivedType(Conversions c, string? type) =>
        DamageBoostData.ReceivedType(type, c.GreaterConflagration, c.Winter, c.StoneStriker);

    /// <summary>
    /// Type REÇU du coup d'arme de <paramref name="skill"/>. <paramref name="picked"/> = la liste de type de la ligne
    /// (mod élémentaire Icy / Ebon / Shocking / Fiery, ou skin — mêmes choix que le Spike) ; vide ou hors des choix de
    /// l'arme = le type natif de l'arme.
    ///
    /// Le type INFLIGÉ sort de la chaîne du chantier infobulle (<see cref="DamageBoostData.EffectiveElement"/> : type
    /// propre de la compétence, mod de la ligne, Clairvoyance du juge, Brasier), sans recopie ; le type REÇU ajoute
    /// Grand brasier, Hiver et le Briseur de pierre du défenseur (<see cref="DamageBoostData.ReceivedType"/>).
    /// </summary>
    public static HitType WeaponHitType(Conversions c, Skill skill, WeaponStrike.Weapon weapon, string? picked)
    {
        if (string.IsNullOrEmpty(picked) || !WeaponStrike.DamageTypeChoices(weapon).Contains(picked)) picked = null;
        var state = new DamageBoostData.ConversionState(
            ElementalMod: WeaponStrike.IsElementalType(picked) ? picked : null,
            JudgesInsight: c.JudgesInsight,
            Conflagration: c.Conflagration);
        string declared = picked ?? weapon.DamageType;
        string inflicted = DamageBoostData.EffectiveElement(state, skill, WeaponStrike.KindOf(weapon)) ?? declared;
        string received = ReceivedType(c, inflicted) ?? inflicted;
        string natural = DamageBoostData.IntrinsicType(skill) ?? declared;
        return new(received, received != natural);
    }

    /// <summary>Type REÇU d'une ATTAQUE LIBRE de type <paramref name="type"/> : la Clairvoyance du juge rend une attaque
    /// (pas un sort) sacrée, puis les conversions de zone et du défenseur. Brasier n'y touche pas : une attaque libre
    /// ne dit pas si c'est une flèche.</summary>
    public static HitType FreeAttackType(Conversions c, string type, bool isSpell)
    {
        string inflicted = c.JudgesInsight && !isSpell ? "holy" : type;
        string received = ReceivedType(c, inflicted) ?? inflicted;
        return new(received, received != type);
    }

    /// <summary>Pénétration BONUS de la Clairvoyance du juge (+20 %), sur les attaques de l'attaquant qu'elle touche —
    /// même constante et même règle que le Spike (cumulée par-dessus la pénétration propre).</summary>
    public static int JudgesBonusPen(Conversions c, bool isAttack) =>
        c.JudgesInsight && isAttack ? SpikeWeaponBuffs.JudgesBonusPen : 0;

    /// <summary>Les dégâts de cette compétence sont-ils des dégâts de SORT ? Tout type qui contient
    /// « Spell » : sorts, maléfices, enchantements, sorts d'arme et d'objet, puits, protections.</summary>
    public static bool IsSpell(Skill skill) => skill.SkillType.Contains("Spell", StringComparison.Ordinal);

    /// <summary>Les dégâts de cette compétence sont-ils des dégâts d'ATTAQUE ? Tout type qui contient « Attack »
    /// (attaques d'arme, de mêlée, de familier…). Le coup d'arme d'une attaque l'est toujours.</summary>
    public static bool IsAttack(Skill skill) => skill.SkillType.Contains("Attack", StringComparison.Ordinal);
}
