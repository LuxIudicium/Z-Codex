using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Durée des enchantements dans l'infobulle (Lot D) : composition des modificateurs de durée et
/// lecture de la durée de base dans la description RÉSOLUE.
///
/// Modificateurs (décisions Philippe 19/07, cf. [[reference_gw1_nature_rituals]]) :
///  • « of Enchanting » (mod d'arme, <see cref="EnchantingModPercent"/>) : +20 %, TOUT enchantement,
///    auto depuis le set d'armes actif.
///  • Blessed Aura (<see cref="BlessedAuraSkillId"/>) : +Faveur divine %, enchantements MONK du
///    lanceur, toggle par perso.
///  • Extend Enchantments (<see cref="ExtendEnchantmentsSkillId"/>) : +Mystique %, enchantements
///    DERVICHE du lanceur, toggle par perso.
///  • Tranquility (rituel de la nature) : −Survie % (<see cref="NatureRitualData.TranquilityPercentAtRank"/>),
///    TOUT enchantement, toggle global.
///
/// Composition MULTIPLICATIVE (facteurs commutatifs), un SEUL arrondi au PLANCHER à la fin. Chaque
/// pourcentage vaut 0 si son modificateur ne s'applique pas (arme absente, mauvaise profession,
/// toggle éteint, rituel inactif) → moteur pur, l'applicabilité est décidée par l'appelant.
/// </summary>
public static class EnchantmentDuration
{
    /// <summary>Prolongateur d'arme « of Enchanting » : +20 % de durée.</summary>
    public const int EnchantingModPercent = 20;

    /// <summary>SkillId de Blessed Aura (Monk, Faveur divine) — source du toggle par perso.</summary>
    public const int BlessedAuraSkillId = 256;

    /// <summary>SkillId d'Extend Enchantments (Derviche, Mystique) — source du toggle par perso.</summary>
    public const int ExtendEnchantmentsSkillId = 1508;

    public readonly record struct Result(int Base, int Final, bool Changed);

    /// <summary>
    /// Durée finale = plancher(base × ∏ facteurs). Les hausses (+%) et la baisse Tranquility (−%)
    /// se multiplient ; l'ordre est indifférent. <paramref name="extenderPct"/> = LE prolongateur
    /// personnel qui s'applique (Blessed Aura sur enchantement Monk, OU Extend Enchantments sur
    /// enchantement Derviche — jamais les deux sur la même compétence) ; l'arme « of Enchanting »
    /// reste séparée car elle STACKE avec l'un ou l'autre. <paramref name="tranquilityPct"/> clampé
    /// à 99 par prudence (une baisse ≥100 % rendrait la durée nulle — n'arrive pas en pratique).
    /// </summary>
    public static Result Compose(int baseSeconds, int enchantingPct, int extenderPct, int tranquilityPct)
    {
        if (baseSeconds <= 0) return new(baseSeconds, baseSeconds, false);
        double factor = (1 + enchantingPct / 100.0)
                      * (1 + extenderPct / 100.0)
                      * (1 - Math.Clamp(tranquilityPct, 0, 99) / 100.0);
        int final = (int)Math.Floor(baseSeconds * factor);
        return new(baseSeconds, final, final != baseSeconds);
    }

    // « (10 seconds.) » / « (4 seconds) » : 1re durée parenthésée. Dans la description RÉSOLUE la
    // valeur est un entier unique, éventuellement encadré d'un marqueur de couleur (comme
    // SpawningPower.DurationRegex). Motif construit depuis SkillProgression.MarkChars → 100 % ASCII.
    private static readonly Regex SecondsRegex = new(
        $@"\(\s*[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+second",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Durée de base (s) lue dans la 1re parenthèse « (N seconds) » de la description
    /// résolue. Null = aucune durée parenthésée (enchantement maintenu / « jusqu'à déclenchement »)
    /// → aucune ligne de durée à afficher.</summary>
    public static int? Seconds(string? resolved)
    {
        if (string.IsNullOrEmpty(resolved)) return null;
        var m = SecondsRegex.Match(resolved);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>Enchantement Monk (cible de Blessed Aura).</summary>
    public static bool IsMonkEnchantment(Skill s) =>
        NatureRitualData.IsEnchantment(s) && s.Profession == Profession.Monk;

    /// <summary>Enchantement Derviche (cible d'Extend Enchantments).</summary>
    public static bool IsDervishEnchantment(Skill s) =>
        NatureRitualData.IsEnchantment(s) && s.Profession == Profession.Dervish;

    /// <summary>Vrai si ce mod d'arme est un « of Enchanting » (+20 % de durée d'enchantement).
    /// Détecté par l'ancre wiki du mod (robuste à un éventuel renumérotage des ids).</summary>
    public static bool IsEnchantingMod(int modId) =>
        GwEquipmentModDetails.ByModId.TryGetValue(modId, out var d) && d.WikiPath == "Of_Enchanting";
}
