using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Famille de durée nommée dans la ligne « Durée de X effective » de l'infobulle (chantier infobulle, lot 4a).
/// Elle vient de la compétence CIBLE, jamais de l'allongeur : Sogolon était énergique vise les cris ET les chants,
/// et chacun garde son propre mot. <see cref="Enchantment"/> reste servie par l'ancienne ligne d'enchantement
/// (<see cref="EnchantmentDuration"/>), inchangée — décision Philippe du 16/09/2026 (« on reste sur la maquette »).
/// </summary>
public enum DurationFamily { None, Enchantment, Stance, Preparation, Hex, Shout, Chant }

/// <summary>
/// Compétence qui RALLONGE la durée propre d'autres compétences, togglée par icône sur la carte du perso
/// (icône allumée = effet actif, comme les lots 1 à 3). Pourcentage fixe (<paramref name="Percent"/>) ou lu dans
/// <c>Progression[<paramref name="PercentIndex"/>]</c> au rang de <paramref name="ScalingAttribute"/>.
/// <paramref name="BaseSkillId"/> = id PvE d'une variante « (PvP) » : l'icône est mémorisée sur l'id de base.
/// </summary>
public sealed record SkillDurationBoostDescriptor(
    int SkillId, Func<Skill, bool> Applies,
    int Percent = 0, int PercentIndex = -1, string? ScalingAttribute = null, int BaseSkillId = 0)
{
    /// <summary>Id sous lequel l'icône est mémorisée (et persistée) : l'id de base.</summary>
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;
}

public static class SkillDurationBoostData
{
    // ── Cibles (valeurs SkillType exactes de la base) ─────────────────────────
    private static bool IsStance(Skill s)       => s.SkillType == "Stance";
    private static bool IsPreparation(Skill s)  => s.SkillType == "Preparation";
    private static bool IsIllusionHex(Skill s)  => NatureRitualData.IsHex(s) && s.Attribute == "Illusion Magic";
    private static bool IsRitualistHex(Skill s) => NatureRitualData.IsHex(s) && s.Profession == Profession.Ritualist;

    // SkillId, colonnes de progression et caractéristiques relevés dans la base réelle (16/09/2026).
    // Les cinq allongeurs visent des familles DISJOINTES, et les deux paires qui pourraient se croiser sont déjà
    // exclusives côté carte du perso (Mantra de persévérance et Pose de pratique sont deux poses ; Lingwah et
    // Sogolon deux sorts d'objet) → au plus UN pourcentage s'applique à une compétence donnée.
    public static readonly IReadOnlyList<SkillDurationBoostDescriptor> All = new SkillDurationBoostDescriptor[]
    {
        new(14,   IsIllusionHex,  PercentIndex: 1, ScalingAttribute: "Inspiration Magic"),   // Mantra of Persistence : +10…34…40 %
        new(1223, IsRitualistHex, Percent: 50),                                              // Anguished Was Lingwah : +50 % (lot 2 pour l'énergie)
        new(2423, IsStance,       PercentIndex: 1, ScalingAttribute: "Deldrimor rank"),      // Dwarven Stability : +55…100 %
        new(449,  IsPreparation,  PercentIndex: 1, ScalingAttribute: "Expertise"),           // Practiced Stance : +30…246…300 % (lot 3 pour la recharge)
        new(1731, NatureRitualData.IsChantOrShout, PercentIndex: 0,
            ScalingAttribute: "Restoration Magic"),                                          // Vocal Was Sogolon : +20…44…50 %
    };

    private static readonly Dictionary<int, SkillDurationBoostDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);
    private static readonly HashSet<int> _toggleIds = All.Select(d => d.ToggleId).ToHashSet();

    public static SkillDurationBoostDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);

    /// <summary>
    /// Famille de durée de la compétence, qui décide le MOT de la ligne d'infobulle. Les enchantements sortent en
    /// premier : ils gardent leur ligne historique. <see cref="DurationFamily.None"/> = aucune ligne de durée.
    /// </summary>
    public static DurationFamily FamilyOf(Skill s) =>
        NatureRitualData.IsEnchantment(s) ? DurationFamily.Enchantment
        : IsStance(s)                     ? DurationFamily.Stance
        : IsPreparation(s)                ? DurationFamily.Preparation
        : NatureRitualData.IsHex(s)       ? DurationFamily.Hex
        : s.SkillType == "Shout"          ? DurationFamily.Shout
        : s.SkillType == "Chant"          ? DurationFamily.Chant
                                          : DurationFamily.None;

    /// <summary>Pourcentage cumulé (0 = rien) des allongeurs ACTIFS qui touchent <paramref name="target"/>. Chaque
    /// actif = le descripteur, la compétence équipée qui le porte (pour sa progression) et le rang de sa
    /// caractéristique d'échelle. Les familles étant disjointes, la somme ne compte en pratique qu'un seul terme.</summary>
    public static int PercentFor(
        Skill target, IEnumerable<(SkillDurationBoostDescriptor Descriptor, Skill Source, int Rank)> active)
    {
        int total = 0;
        foreach (var (d, source, rank) in active.DistinctBy(a => a.Descriptor.ToggleId))
        {
            if (!d.Applies(target)) continue;
            total += d.PercentIndex < 0
                ? d.Percent
                : SkillProgression.IntAt(source.Progression is { } p && d.PercentIndex < p.Length ? p[d.PercentIndex] : null, rank) ?? 0;
        }
        return Math.Max(0, total);
    }

    /// <summary>Durée rallongée : plancher(base × (1 + %)). Arrondi vers le bas, comme les enchantements
    /// (décision Philippe du 16/09/2026).</summary>
    public static int Extend(int baseSeconds, int percent) =>
        baseSeconds <= 0 || percent <= 0 ? baseSeconds : (int)Math.Floor(baseSeconds * (1 + percent / 100.0));

    // ── Durée PROPRE lue dans la description résolue ──────────────────────────
    // Même convention que EnchantmentDuration.Seconds (valeur entière éventuellement encadrée d'un marqueur de
    // couleur), élargie aux formes « (10 second[s]) » des crochets du wiki.
    private static readonly Regex SecondsRegex = new(
        $@"\(\s*[{SkillProgression.MarkChars}]?(\d+)[{SkillProgression.MarkChars}]?\s+second",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Noms de conditions, mêmes formes que ConditionInfliction.Forms. Une parenthèse-durée précédée, DANS SA
    // PHRASE, du nom d'une condition est la durée de CETTE condition, pas celle de la compétence : « Shout.
    // Inflicts Cripple and Weakness conditions (8 seconds). » n'a aucune durée propre. Relevé sur la base réelle
    // le 16/09/2026 : la règle écarte 5 cris (« You're All Alone! », Strike as One, « Finish Him! », « You Move
    // Like a Dwarf! », « You Are All Weaklings! ») et ne déplace aucune autre valeur — 0 écart sur les 257
    // enchantements de la ligne historique.
    private static readonly Regex ConditionWord = new(
        @"\b(?:Bleeding|Blind(?:ed|ness)?|Burning|on\s+fire|Cracked\s+Armor|Cripple(?:d)?|Daze(?:d)?"
        + @"|Deep\s+Wound|Disease(?:d)?|Poison(?:ed)?|Weakness|Weakened)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly char[] SegmentBounds = ['.', ';', ':'];

    /// <summary>Durée PROPRE (s) de la compétence, lue dans sa description RÉSOLUE : la 1re parenthèse
    /// « (N seconds) » qui n'appartient pas à une condition infligée. Null = aucune durée annoncée (effet
    /// immédiat, effet maintenu, charges) → aucune ligne de durée à afficher.</summary>
    public static int? OwnSeconds(string? resolved)
    {
        if (string.IsNullOrEmpty(resolved)) return null;
        foreach (Match m in SecondsRegex.Matches(resolved))
        {
            int start = resolved.LastIndexOfAny(SegmentBounds, Math.Max(0, m.Index - 1)) + 1;
            if (ConditionWord.IsMatch(resolved[start..m.Index])) continue;
            return int.Parse(m.Groups[1].Value);
        }
        return null;
    }
}
