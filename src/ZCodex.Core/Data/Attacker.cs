using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// L'ATTAQUANT du calculateur d'armure (chantier « réductions de dégâts », lot 6) : UN SEUL pour toute la table des
/// attaques de compétences ; les attaques libres n'en reçoivent rien, leur valeur dit déjà ce que fait le coup à AL 60
/// (consigne de Philippe du 12/10/2026). Tout ce qu'il porte vaut des DEUX côtés d'une ligne (@AL 60 et build) : c'est
/// une propriété du coup, comme la pénétration propre d'une compétence. <see cref="Default"/> = niveau 20 sans rien de
/// spécial, c'est-à-dire le calcul d'avant le lot 6 (case « Attaquant » décochée).
///
/// Pénétration (règles du chantier infobulle, Q26 du 12/10) : les sources de BASE ne se cumulent pas, seule la plus
/// forte compte (Force, Arme de fractionnement le sort, Glaive était destructrice, Daoshen était cruel) ; les BONUS
/// s'ajoutent par-dessus (mod « de fractionnement » qui a fonctionné, arc corne ; la Clairvoyance du juge est une case
/// de conversion du lot 4, elle reste dans <see cref="DamageMitigation"/>). Valeurs et périmètres des sorts sont ceux
/// de <see cref="DamageBoostData"/>, ceux des mods ceux de <see cref="SpikeWeaponMods"/> : rien n'est recopié.
/// </summary>
/// <param name="Level">Niveau de l'attaquant, 1 à 33 (wiki *Level*, Q10). Sorts : frappe à 3 × niveau ; armes : seuil de
/// rang (niveau + 4) / 2.</param>
/// <param name="StrengthRank">Rang de Force : +1 % de pénétration de base par rang, attaques d'arme seulement.</param>
/// <param name="InscriptionPercent">Inscription de dégâts de l'arme (une seule par arme, wiki *Damage_calculation*) :
/// 0, 15 ou 20. Elle ne grossit que le COUP D'ARME (wiki *Inscription*, note 2) — ni le « +X » de la compétence, ni ses
/// paquets typés, ni un sort.</param>
/// <param name="PenetrationSkillIds">Sorts de pénétration actifs (ids de <see cref="PenetrationSkills"/>).</param>
/// <param name="SunderingMod">Le mod d'arme « de fractionnement » a fonctionné sur ce coup (Q17 : case « a fonctionné »).</param>
/// <param name="Hornbow">L'arc est un arc corne.</param>
public sealed record Attacker(int Level, int StrengthRank, int InscriptionPercent,
                              IReadOnlyCollection<int> PenetrationSkillIds, bool SunderingMod, bool Hornbow)
{
    public const int MinLevel = 1;
    /// <summary>Niveau le plus haut qu'on rencontre (wiki *Level*) — Q10, Philippe, 12/10/2026.</summary>
    public const int MaxLevel = 33;
    public const int DefaultLevel = 20;

    /// <summary>Niveau 20, rien de spécial : le calcul d'avant le lot 6.</summary>
    public static readonly Attacker Default = new(DefaultLevel, 0, 0, [], false, false);

    /// <summary>Les trois choix d'inscription (wiki *Inscription*, valeurs maximales).</summary>
    public static readonly IReadOnlyList<int> InscriptionChoices = [0, 15, 20];

    /// <summary>Inscriptions de dégâts par valeur, en modId (les noms de l'infobulle viennent du catalogue des mods) :
    /// +15 % « Strength and Honor », « Guided by Fate », « Dance with Death », « Too Much Information », « To the Pain! »,
    /// « Brawn over Brains » ; +20 % « Vengeance is Mine », « Don't Fear the Reaper ».</summary>
    public static readonly IReadOnlyList<int> Inscriptions15 = [338, 283, 287, 282, 289, 288];
    public static readonly IReadOnlyList<int> Inscriptions20 = [339, 286];

    /// <summary>
    /// Les sorts de pénétration que le bloc propose : tous les descripteurs de pénétration de
    /// <see cref="DamageBoostData"/>, sauf la Clairvoyance du juge (déjà une case de conversion, lot 4). Ajouter une
    /// source = ajouter son descripteur là-bas.
    /// </summary>
    public static IReadOnlyList<int> PenetrationSkills { get; } = DamageBoostData.All
        .Where(d => (d.Kind is DamageBoostKind.BasePenetration or DamageBoostKind.BonusPenetration)
                    && d.SkillId != ConditionDurationData.JudgesInsightSkillId)
        .Select(d => d.SkillId).Distinct().ToList();

    /// <summary>Multiplicateur de l'inscription, à poser sur le coup d'arme seulement.</summary>
    public double WeaponMultiplier => 1 + InscriptionPercent / 100.0;

    /// <summary>
    /// Pénétration que l'attaquant apporte à <paramref name="skill"/> : (base, bonus). <paramref name="weapon"/> = l'arme
    /// du coup (null si la compétence n'en a pas ou si elle est indéterminée) ; <paramref name="weaponType"/> = le type
    /// choisi sur la ligne. Le mod « de fractionnement » occupe le PRÉFIXE de l'arme : il n'existe pas sur une ligne
    /// réglée en type élémentaire, ni sur une arme de lanceur (règle de la fenêtre Spike, chantier 16).
    /// </summary>
    public (int Base, int Bonus) PenetrationFor(Skill skill, WeaponStrike.Weapon? weapon, string? weaponType)
    {
        bool weaponAttack = WeaponStrike.IsWeaponAttack(skill);
        int basePen = weaponAttack ? StrengthRank : 0, bonus = 0;
        var kind = weapon is null ? WeaponKind.None : WeaponStrike.KindOf(weapon);
        foreach (int id in PenetrationSkillIds)
            foreach (var d in DamageBoostData.DescriptorsFor(id))
            {
                if (!DamageBoostData.AffectsScope(d, skill, kind)) continue;
                if (d.Kind == DamageBoostKind.BasePenetration) basePen = Math.Max(basePen, d.Fixed);
                else if (d.Kind == DamageBoostKind.BonusPenetration) bonus += d.Fixed;
            }
        if (SunderingMod && weaponAttack && weapon is { IsCaster: false } && !WeaponStrike.IsElementalType(weaponType))
            bonus += SpikeWeaponMods.SunderingBonusPen;
        if (Hornbow && weaponAttack && weapon is { } bow && WeaponStrike.IsBow(bow))
            bonus += SpikeWeaponMods.HornbowBonusPen;
        return (basePen, bonus);
    }

    /// <summary>
    /// Même chose pour une ATTAQUE LIBRE, quand la case « Appliquer à l'attaque libre » est cochée (demande de Philippe
    /// du 12/10/2026). Elle n'est la compétence d'aucune profession : seuls les effets qui visent « les attaques » la
    /// touchent, et seulement si elle est une attaque (pas un sort) — Force, Arme de fractionnement, mod « de
    /// fractionnement » (pas en type élémentaire) ; l'arc corne si elle est à projectile. Glaive et Daoshen (compétences
    /// de Ritualiste) jamais.
    /// </summary>
    public (int Base, int Bonus) PenetrationForFreeAttack(bool isAttack, string type, bool projectile)
    {
        if (!isAttack) return (0, 0);
        int basePen = StrengthRank, bonus = 0;
        foreach (int id in PenetrationSkillIds)
            foreach (var d in DamageBoostData.DescriptorsFor(id).Where(d => d.Scope == DamageBoostScope.Attacks))
            {
                if (d.Kind == DamageBoostKind.BasePenetration) basePen = Math.Max(basePen, d.Fixed);
                else if (d.Kind == DamageBoostKind.BonusPenetration) bonus += d.Fixed;
            }
        if (SunderingMod && !WeaponStrike.IsElementalType(type)) bonus += SpikeWeaponMods.SunderingBonusPen;
        if (Hornbow && projectile) bonus += SpikeWeaponMods.HornbowBonusPen;
        return (basePen, bonus);
    }
}
