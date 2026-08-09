using ZCodex.Core.Models;
using System.Text.RegularExpressions;

namespace ZCodex.Core.Data;

/// <summary>
/// Effets de l'attribut primaire Ritualiste « Spawning Power » (Puissance de l'invocation) :
/// 4 %/rang, ARRONDI au plus proche, sur (a) la DURÉE des sorts d'altération d'arme et (b) les
/// PV MAX des créatures créées — esprits (rituels d'asservissement ET rituels de la nature) et
/// serviteurs morts-vivants. Wiki : « creatures you create (or animate) have 4% more Health,
/// and weapon Spells you cast last 4% longer ».
///
/// Ne sont PAS modifiés par l'invocation : la durée de vie annoncée des esprits (lifespan),
/// l'armure, et le niveau de la créature (qui dépend de l'attribut propre de la compétence).
///
/// Règle d'arrondi vérifiée sur la table brute de breakpoints du wiki (Weapon_spell) : les
/// 1092 cellules (bases 1–52 × rangs 0–20) sont reproduites par la formule entière ci-dessous.
/// Comme pour l'Expertise, la valeur ne tombe JAMAIS pile sur ,5 (base×4×rang est multiple de 4,
/// donc jamais ≡ 50 mod 100) → la convention de demi-arrondi est sans effet observable.
/// Formule entière → pas de piège float.
/// </summary>
public static class SpawningPower
{
    /// <summary>Nom de l'attribut primaire Ritualiste (clé pour AttributeLevel).</summary>
    public const string AttributeName = "Spawning Power";

    /// <summary>Valeur relevée = arrondi(base × (100 + 4×rang) / 100).</summary>
    public static int Boost(int baseValue, int rank) => (baseValue * (100 + 4 * rank) + 50) / 100;

    public enum Kind { WeaponSpell, Spirit, Minion }

    /// <summary>
    /// Ce que l'invocation relève sur cette compétence. <paramref name="Base"/> / <paramref name="Boosted"/>
    /// = durée en secondes (sort d'arme) ou PV max (créature). Armor/Lifespan null hors serviteur/esprit.
    /// </summary>
    public sealed record Summon(
        Kind Type, int Base, int Boosted,
        string? Creature = null, int Level = 0, int? Armor = null,
        int? LifespanBase = null, int? LifespanBoosted = null);

    // ── Données wiki ──────────────────────────────────────────────────────────

    /// <summary>PV de base d'un esprit = 20 × niveau (wiki/Spirit).</summary>
    public static int SpiritHealth(int level) => 20 * level;

    /// <summary>Armure d'un esprit = 2 + 6 × niveau (wiki/Spirit). Non relevée par l'invocation.</summary>
    public static int SpiritArmor(int level) => 2 + 6 * level;

    /// <summary>PV de base d'un serviteur = 20 × niveau + 80. Vérifié sur les 7 types de serviteurs
    /// (154 cellules des tables du wiki) et recoupé par l'exemple de décomposition du wiki/Minion
    /// (niveau 18 → 440 PV → mort à 1 min 24 s, cf. <see cref="MinionLifespan"/>).</summary>
    public static int MinionHealth(int level) => 20 * level + 80;

    // Armure par niveau, relevée des tables du wiki (une page par serviteur) : elle varie selon le
    // TYPE (un bone fiend est bien plus fragile qu'un bone horror de même niveau) et n'est PAS une
    // fonction affine exacte du niveau (les arrondis du jeu sont irréguliers) → table brute, pas de
    // formule. Les niveaux absents sont ceux qu'aucun rang de Magie des morts ne produit.
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> MinionArmor =
        new Dictionary<string, IReadOnlyDictionary<int, int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["bone fiend"] = new Dictionary<int, int> { {1,6},{2,9},{3,12},{4,14},{5,17},{6,20},{7,23},{8,26},{10,32},{11,34},{12,37},{13,40},{14,43},{15,46},{16,49},{17,51},{18,54},{19,57},{20,60},{21,63},{22,66},{23,69} },
            ["bone horror"] = new Dictionary<int, int> { {1,9},{2,12},{3,16},{4,20},{5,24},{6,28},{7,31},{8,35},{10,42},{11,46},{12,50},{13,54},{14,58},{15,61},{16,65},{17,69},{18,72},{19,76},{20,80},{21,84},{22,88},{23,92} },
            ["bone minion"] = new Dictionary<int, int> { {0,1},{1,4},{2,7},{3,10},{4,13},{5,16},{6,19},{7,22},{8,25},{9,27},{10,30},{11,33},{12,36},{13,39},{14,42},{15,45},{16,48},{17,51} },
            ["flesh golem"] = new Dictionary<int, int> { {3,16},{4,20},{6,28},{7,31},{9,39},{10,42},{12,50},{13,54},{15,61},{16,65},{18,72},{19,76},{21,84},{22,88},{24,95},{25,99},{26,102},{28,110},{29,114},{31,121},{32,125},{34,132} },
            ["jagged horror"] = new Dictionary<int, int> { {0,5},{1,9},{2,12},{3,16},{4,20},{5,24},{6,28},{7,31},{8,35},{9,39},{10,42},{11,46},{12,50},{13,54},{14,58},{15,61},{16,65},{17,69},{18,73},{19,77},{20,81},{21,85} },
            ["shambling horror"] = new Dictionary<int, int> { {1,9},{2,12},{3,16},{4,20},{5,24},{6,28},{7,31},{8,35},{10,42},{11,46},{12,50},{13,54},{14,58},{15,61},{16,65},{17,69},{18,72},{19,76},{20,80},{21,84},{22,88},{23,92} },
            ["vampiric horror"] = new Dictionary<int, int> { {1,9},{2,12},{3,16},{4,20},{5,24},{6,28},{7,31},{8,35},{10,42},{11,46},{12,50},{13,54},{14,58},{15,61},{16,65},{17,69},{18,72},{19,76},{20,80},{21,84},{22,88},{23,92} },
        };

    /// <summary>Libellés français des serviteurs (affichage tooltip).</summary>
    public static readonly IReadOnlyDictionary<string, string> CreatureFr =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bone fiend"] = "démon squelette", ["bone horror"] = "horreur squelettique",
            ["bone minion"] = "serviteur squelette", ["flesh golem"] = "golem de chair",
            ["jagged horror"] = "horreur déchiquetée", ["shambling horror"] = "horreur dévastatrice",
            ["vampiric horror"] = "horreur vampirique",
        };

    /// <summary>Libellé d'un serviteur dans la langue affichée : valeur française de
    /// <see cref="CreatureFr"/> en FR, clé anglaise (nom du jeu) en EN.</summary>
    public static string CreatureLabel(string creature)
        => AppLanguage.IsFr ? CreatureFr.GetValueOrDefault(creature, creature) : creature;

    /// <summary>
    /// Durée de vie d'un serviteur, en secondes : il n'a PAS de durée annoncée, il meurt par
    /// décomposition. Dégénérescence = 1 pip à la création, +1 pip toutes les 20 s (1 pip = 2 PV/s,
    /// wiki/Minion). Reproduit l'exemple du wiki au chiffre près : niveau 18 (440 PV) → 84 s
    /// (« 1 minute 24 seconds »). L'invocation l'allonge donc INDIRECTEMENT, via les PV.
    /// </summary>
    public static int MinionLifespan(int health)
    {
        int seconds = 0;
        for (int pips = 1; ; pips++)
        {
            int rate = 2 * pips;                 // PV perdus par seconde sur ce palier de 20 s
            int chunk = rate * 20;
            if (health <= chunk)
                return seconds + (health + rate - 1) / rate;   // arrondi au supérieur (la seconde où il meurt)
            health -= chunk;
            seconds += 20;
        }
    }

    // ── Détection dans la description RÉSOLUE ─────────────────────────────────

    // Entier résolu (entre marqueurs, normal ou flux) OU entier fixe isolé — même idiome que
    // SkillDamage, insensible au marquage.
    private const string Value =
        @"(?:[" + SkillProgression.MarkChars + @"](?<num>\d+)[" + SkillProgression.MarkChars + @"]"
        + @"|(?<![\d." + SkillProgression.MarkChars + @"])(?<num>\d+)(?![\d.%" + SkillProgression.MarkChars + @"]))";

    private const string Creatures =
        "bone fiends?|bone horrors?|bone minions?|flesh golems?|jagged horrors?|shambling horrors?|vampiric horrors?";

    // « (10 seconds.) » / « (6 second[s].) » — la durée parenthésée d'un sort d'altération d'arme.
    private static readonly Regex DurationRegex = new(
        $@"\(\s*{Value}\s+second(?:s|\[s\])?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // « Creates a level 10 spirit » / « Create a level 14 Spirit that dies… »
    private static readonly Regex SpiritRegex = new(
        $@"level\s+{Value}\s+spirit", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // « a level 14 bone fiend » / « two level 12 bone minions » / « a level 17 masterless bone horror »
    // — sur la description RÉSOLUE (Value = entier isolé ou marqué).
    private static readonly Regex MinionRegex = new(
        $@"level\s+{Value}(?:\s+\w+)*?\s+(?<creature>{Creatures})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Même motif, mais DÉTECTION seule sur la description BRUTE : le niveau y est encore une plage
    // (« level 1...14...17 bone fiend »), que Value rejette par construction (entier isolé/marqué).
    // D'où ce jumeau tolérant aux plages — sans lui, aucune compétence de serviteur ne serait
    // reconnue hors contexte build.
    private static readonly Regex CreatesMinionRegex = new(
        $@"level\s+[\d.{SkillProgression.MarkChars}]+(?:\s+\w+)*?\s+(?:{Creatures})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>La Puissance de l'invocation a-t-elle un effet affichable sur cette compétence ?
    /// (sort d'altération d'arme, ou compétence créant un esprit / un serviteur).</summary>
    public static bool Affects(Skill skill) =>
        skill.SkillType == "Weapon Spell"
        || skill.SkillType is "Binding Ritual" or "Nature Ritual"
        || CreatesMinionRegex.IsMatch(skill.Description);

    /// <summary>
    /// Ce que l'invocation relève sur cette compétence, calculé depuis la description RÉSOLUE
    /// (valeurs déjà remplacées au rang de l'attribut propre de la skill). Null = rien à afficher
    /// (rang 0, pas de valeur détectée, ou description non résolue).
    /// </summary>
    public static Summon? Analyze(string resolved, Skill skill, int rank)
    {
        if (rank <= 0 || string.IsNullOrEmpty(resolved)) return null;

        if (skill.SkillType == "Weapon Spell")
        {
            if (DurationRegex.Match(resolved) is not { Success: true } m) return null;
            int seconds = int.Parse(m.Groups["num"].Value);
            return new Summon(Kind.WeaponSpell, seconds, Boost(seconds, rank));
        }

        // Serviteur : testé AVANT l'esprit — une même description peut nommer les deux
        // (Animate Shambling Horror crée une horreur titubante puis une horreur dentelée).
        if (MinionRegex.Match(resolved) is { Success: true } mm)
        {
            int level = int.Parse(mm.Groups["num"].Value);
            string creature = Singular(mm.Groups["creature"].Value);
            int hp = MinionHealth(level);
            int boosted = Boost(hp, rank);
            return new Summon(Kind.Minion, hp, boosted, creature, level,
                              ArmorOf(creature, level),
                              MinionLifespan(hp), MinionLifespan(boosted));
        }

        if (skill.SkillType is "Binding Ritual" or "Nature Ritual"
            && SpiritRegex.Match(resolved) is { Success: true } ms)
        {
            int level = int.Parse(ms.Groups["num"].Value);
            int hp = SpiritHealth(level);
            return new Summon(Kind.Spirit, hp, Boost(hp, rank), null, level, SpiritArmor(level));
        }

        return null;
    }

    // « bone minions » → « bone minion ». Les noms de la table sont au singulier.
    private static string Singular(string creature) =>
        creature.EndsWith('s') ? creature[..^1] : creature;

    // Null = niveau absent de la table (aucun rang de Magie des morts ne le produit) → armure non
    // affichée plutôt qu'une valeur inventée.
    private static int? ArmorOf(string creature, int level) =>
        MinionArmor.TryGetValue(creature, out var table) && table.TryGetValue(level, out int ar)
            ? ar : null;
}
