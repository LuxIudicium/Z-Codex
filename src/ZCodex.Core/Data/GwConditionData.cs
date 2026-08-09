using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

// État d'une condition pour un perso (ou un teambuild agrégé) :
//   None      → aucune compétence du combo ne peut l'infliger,
//   Available → au moins une compétence du catalogue du combo peut l'infliger,
//   Inflicted → au moins une compétence ÉQUIPÉE peut l'infliger.
public enum ConditionCoverage { None, Available, Inflicted }

/// <summary>
/// Les 10 conditions GW1 (altérations) et l'évaluation « qui peut infliger quoi ».
/// Source par compétence : pages wiki « Skills that cause X » (passe scraper), stockées dans
/// Skill.Conditions — entrée "X" = infligée à la cible, "X:self" = subie par le LANCEUR
/// (elle ne compte comme infligeable que si une compétence de transfert est aussi présente).
/// </summary>
public static class GwConditionData
{
    public sealed record GwCondition(string Name, string Description, string DescriptionFr)
    {
        /// <summary>Description dans la langue affichée (bandeau — condition non infligée).</summary>
        public string DisplayDescription => AppLanguage.IsFr ? DescriptionFr : Description;
    }

    // Ordre canonique d'affichage = liste de la page wiki « Condition » (alphabétique).
    // Descriptions officielles EN (wiki) + FR validées par Philippe (20/07/2026).
    public static readonly IReadOnlyList<GwCondition> All =
    [
        new("Bleeding",      "While suffering from this injury, you lose Health over time.",
                             "Vous perdez progressivement des points de vie lorsque vous êtes affecté par cette condition."),
        new("Blind",         "While suffering from this ailment, your melee and missile attacks have 90% chance to \"miss.\" Your projectiles also have a greater chance to stray from their intended target.",
                             "Quand vous êtes frappé d'aveuglement, vos attaques au corps à corps et à distance ont 90% de risques de manquer leur cible. Vos projectiles ont aussi plus de risques de dévier de leur trajectoire."),
        new("Burning",       "While suffering from this Condition, you lose Health over time.",
                             "Vous perdez progressivement des points de vie lorsque vous êtes affecté par cette condition."),
        new("Cracked Armor", "While suffering from this Condition, you have -20 armor (minimum 60).",
                             "Vous avez -20 d'armure (minimum 60) lorsque vous êtes affecté par cette condition."),
        new("Crippled",      "While suffering from this injury, you move 50% slower.",
                             "Vous vous déplacez 50% plus lentement."),
        new("Dazed",         "While Dazed, you take twice as long to cast spells, and all your spells are easily interrupted.",
                             "Lorsque vous êtes affecté par la stupeur, le temps d'incantation des sorts est deux fois plus long et tous vos sorts peuvent facilement être interrompus."),
        new("Deep Wound",    "While suffering from this injury, your maximum Health is reduced by 20% and you receive less benefit from healing.",
                             "Lorsque vous êtes affecté par cette blessure, vos points de vie maximum sont réduits de 20% et les soins ont moins d'effet sur vous."),
        new("Disease",       "While suffering from this ailment, you lose Health over time. Disease is contagious between creatures of the same kind.",
                             "Vous perdez régulièrement des points de vie lorsque vous êtes affecté par cette condition. La maladie est contagieuse entre créatures de la même espèce."),
        new("Poison",        "While suffering from this injury, you lose Health over time.",
                             "Vous perdez régulièrement des points de vie si vous êtes affecté par cette condition."),
        new("Weakness",      "While suffering from this Condition, you deal less damage (66%) with attacks and all of your attributes are reduced by 1.",
                             "Vos attaques infligent moins de dégâts lorsque vous êtes frappé par cette condition."),
    ];

    // Noms français des conditions (+ « Hex » pour Lieutenant's) — clé partagée par l'affichage
    // des réductions de durée (récap + pilules des attaques de référence) et leur matching. Les
    // descriptions d'équipement ET Skill.Conditions utilisent les noms canoniques anglais → une
    // seule table suffit des deux côtés. Termes validés Philippe (18/07).
    public static readonly IReadOnlyDictionary<string, string> Fr =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Bleeding"] = "Saignement", ["Blind"] = "Aveuglement", ["Burning"] = "Brûlure",
        ["Cracked Armor"] = "Armure brisée", ["Crippled"] = "Infirmité", ["Dazed"] = "Stupeur",
        ["Deep Wound"] = "Blessure profonde", ["Disease"] = "Maladie", ["Poison"] = "Poison",
        ["Weakness"] = "Faiblesse", ["Hex"] = "Maléfice",
    };

    public static string FrName(string english) => Fr.GetValueOrDefault(english, english);

    /// <summary>Nom de la condition dans la langue affichée (FR de la table, anglais canonique sinon).</summary>
    public static string DisplayName(string english) => AppLanguage.IsFr ? FrName(english) : english;

    // Suffixe des entrées Skill.Conditions marquant une condition subie par le lanceur
    // (ex : Signet of Agony → "Bleeding:self").
    public const string SelfSuffix = ":self";

    public static bool IsSelf(string entry) => entry.EndsWith(SelfSuffix, StringComparison.Ordinal);
    public static string NameOf(string entry) => IsSelf(entry) ? entry[..^SelfSuffix.Length] : entry;

    // Compétences qui transfèrent les conditions du lanceur vers un ennemi (liste validée par
    // Philippe) : leur présence rend infligeables les conditions auto-infligées du même lot.
    private static readonly HashSet<string> TransferSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plague Sending", "Plague Touch", "Plague Signet",
        "Contagion", "Grenth's Fingers", "Grenth's Grasp",
    };

    // Nom de base : variante " (PvP)" et apostrophe typographique normalisées.
    private static string BaseName(string skillName)
    {
        var n = skillName.Replace('’', '\'');
        return n.EndsWith(" (PvP)", StringComparison.OrdinalIgnoreCase) ? n[..^6] : n;
    }

    public static bool IsTransferSkill(Skill s) => TransferSkills.Contains(BaseName(s.Name));

    // Compétences du lot pouvant infliger `condition` : directement (entrée cible), ou
    // auto-infligée SI le lot contient aussi une compétence de transfert.
    public static List<Skill> InflictingSkills(IReadOnlyCollection<Skill> set, string condition)
    {
        bool hasTransfer = set.Any(IsTransferSkill);
        var result = new List<Skill>();
        foreach (var s in set)
        {
            foreach (var entry in s.Conditions)
            {
                if (!string.Equals(NameOf(entry), condition, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsSelf(entry) && !hasTransfer) continue;
                result.Add(s);
                break;
            }
        }
        return result;
    }

    public static bool CanInflict(IReadOnlyCollection<Skill> set, string condition)
        => InflictingSkills(set, condition).Count > 0;
}
