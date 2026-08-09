using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Chantier 14 bis (calculateur d'armure) — conditions infligées à la CIBLE par une attaque de
/// référence, avec leur durée de BASE au rang donné (secondes). La liste des conditions vient du
/// scraping wiki (<see cref="Skill.Conditions"/> ; entrées « :self » exclues — elles frappent le
/// lanceur, pas le défenseur). La durée est lue dans la description RÉSOLUE au rang (parité stricte
/// avec la tooltip : même pipeline <see cref="ReferenceAttack.ResolveDescription"/>, valeurs entre
/// marqueurs). La réduction d'équipement (runes/inscriptions) est appliquée par l'appelant (VM) —
/// ici, durée de base seule. Aucune skill n'inflige plus de 3 conditions à la fois (Philippe) —
/// Shockwave et Virulence sont les seules à 3.
/// </summary>
public static class ConditionInfliction
{
    /// <summary>Une condition infligée + sa durée de base (secondes) au rang. 0 = aucune durée
    /// lisible dans la description (durée fixe non annoncée, ou formulation atypique).</summary>
    public sealed record Inflicted(string Condition, int BaseSeconds);

    // Valeur = entier résolu (entre marqueurs, normal OU flux) OU entier fixe isolé (pas un morceau
    // de plage a...b...c, pas suivi de %) — même convention que SkillDamage.Value.
    private static readonly string Val =
        $@"(?:[{SkillProgression.MarkChars}](?<num>\d+)[{SkillProgression.MarkChars}]" +
        $@"|(?<![\d.{SkillProgression.MarkChars}])(?<num>\d+)(?![\d.%{SkillProgression.MarkChars}]))";

    // Formes textuelles (nom/adjectif) de chaque condition dans les descriptions concises du jeu.
    // La clé est le nom canonique (GwConditionData.All / Skill.Conditions). Le format DB écrit soit
    // le nom (« Inflicts Blindness condition »), soit l'adjectif (« are Blinded ») → les deux formes.
    private static readonly (string Name, string Form)[] Forms =
    [
        ("Bleeding",      @"Bleeding"),
        ("Blind",         @"Blind(?:ed|ness)?"),
        ("Burning",       @"Burning|on\s+fire"),
        ("Cracked Armor", @"Cracked\s+Armor"),
        ("Crippled",      @"Cripple(?:d)?"),
        ("Dazed",         @"Daze(?:d)?"),
        ("Deep Wound",    @"Deep\s+Wound"),
        ("Disease",       @"Disease(?:d)?"),
        ("Poison",        @"Poison(?:ed)?"),
        ("Weakness",      @"Weakness|Weakened"),
    ];

    private static readonly char[] SegmentBounds = ['.', ';', ':'];

    // Le format DB porte la durée juste après la condition, EN PARENTHÈSES : « (8 seconds) »,
    // « (10 second[s]) » (crochets littéraux du wiki), « (15 seconds) » ou même « (20) » nu (le mot
    // « seconds » est alors implicite — Poison Arrow). N = valeur résolue (marqueurs) ou entier fixe.
    private static readonly Regex ParenDuration = new(
        $@"\(\s*(?:for\s+)?{Val}(?:\s+second(?:s|\[s\])?)?\s*\.?\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Repli : « for N seconds » hors parenthèses (phrasé rare dans cette DB).
    private static readonly Regex ForSeconds = new(
        $@"\bfor\s+{Val}\s+second(?:s|\[s\])?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fenêtre de recherche de la parenthèse-durée après le mot de condition : assez large pour une
    // liste « Disease, Poison, and Weakness conditions (15 seconds) » mais bornée pour ne pas happer
    // une parenthèse d'une autre clause.
    private const int ParenLookahead = 80;

    /// <summary>Conditions infligées à la cible + durée de base au rang (ordre canonique, max 3).</summary>
    public static IReadOnlyList<Inflicted> For(Skill skill, int rank)
    {
        var targets = skill.Conditions
            .Where(c => !GwConditionData.IsSelf(c))
            .Select(GwConditionData.NameOf)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0) return [];

        string desc = ReferenceAttack.ResolveDescription(skill, rank);
        var result = new List<Inflicted>();
        foreach (var (name, form) in Forms)
        {
            if (!targets.Contains(name)) continue;
            result.Add(new Inflicted(name, DurationOf(desc, form)));
        }
        return result;
    }

    // Durée d'une condition : on localise le mot, puis la 1re parenthèse-durée dans la fenêtre qui
    // suit (format DB), sinon un « for N seconds » du même segment. 0 si rien de lisible (durée non
    // annoncée → pilule icône seule).
    private static int DurationOf(string desc, string form)
    {
        var wordRe = new Regex($@"\b(?:{form})\b", RegexOptions.IgnoreCase);
        foreach (Match w in wordRe.Matches(desc))
        {
            int end = w.Index + w.Length;
            int winEnd = Math.Min(desc.Length, end + ParenLookahead);
            var p = ParenDuration.Match(desc, end, winEnd - end);
            if (p.Success && int.TryParse(p.Groups["num"].Value, out int pv)) return pv;

            int segEndIdx = desc.IndexOfAny(SegmentBounds, w.Index);
            int segEnd = segEndIdx < 0 ? desc.Length : segEndIdx;
            var f = ForSeconds.Match(desc, end, Math.Max(0, segEnd - end));
            if (f.Success && int.TryParse(f.Groups["num"].Value, out int fv)) return fv;
        }
        return 0;
    }
}
