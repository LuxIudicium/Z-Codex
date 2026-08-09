using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Détection des dégâts infligés dans une description résolue (valeurs entre marqueurs
/// <see cref="SkillProgression.Mark"/> ou entiers fixes) et classification armor-ignoring
/// selon wiki/Armor-ignoring_damage : sans type, bonus « +X » d'attaque, sacré et ombre
/// ignorent l'armure ; feu/froid/terre/foudre/ténèbres/chaos/physiques la respectent.
/// Les plages non résolues (a...b...c) sont ignorées — pas de rang, pas de calcul.
/// Détecte aussi (Kind) le vol de vie (« steal X Health », hors vol à un allié) et la perte
/// de vie infligée (« Causes X Health loss », « foe[s] lose[s] X Health » — jamais les coûts
/// du lanceur) — comptés par l'utilitaire Spike, ignorés par la tooltip.
/// </summary>
public static class SkillDamage
{
    /// <summary>Nature d'un paquet : dégât, vol de vie ou perte de vie sèche (les deux
    /// derniers ignorent l'armure par nature — wiki/Life_steal).</summary>
    public enum RowKind { Damage, LifeSteal, HealthLoss }

    /// <summary>Un paquet détecté. DamageType null = sans type. Value = valeur listée (AL 60).
    /// MaxTicks &gt; 1 = paquet périodique (« each second ») répétable jusqu'à MaxTicks fois ;
    /// TicksCumulative = le tick n vaut n × Value (Savannah Heat). Conditional = part additionnelle
    /// « X more damage if [état] » (checkbox du spike) ; Condition = clause capturée (tooltip).
    /// IsThreshold = paquet « X for each [ressource] (maximum Y) » (Symbolic Strike, Aneurysm…) :
    /// Value est la valeur PAR-UNITÉ, à multiplier par un compteur ×N plafonné à MaxDamage
    /// (0 = aucun plafond annoncé → repli <see cref="SpikeThresholdSkills.FallbackCap"/>) ;
    /// ThresholdClause = clause « for each … » capturée (tooltip du compteur).
    /// Index = position du match dans la description résolue (−1 si non posé) : permet à
    /// l'appelant de retrouver le segment de phrase porteur (ReferenceAttack : « si bloqué »).</summary>
    public sealed record Row(int Value, string? DamageType, bool IsBonus, bool IgnoresArmor,
                             RowKind Kind = RowKind.Damage,
                             int MaxTicks = 1, bool TicksCumulative = false,
                             bool Conditional = false, string? Condition = null,
                             bool IsThreshold = false, int MaxDamage = 0, string? ThresholdClause = null,
                             int Index = -1);

    /// <summary>Dégâts détectés + pénétration d'armure annoncée par la description (0 si aucune).</summary>
    public sealed record Analysis(IReadOnlyList<Row> Rows, int ArmorPenetration);

    /// <summary>Libellés français des types de dégâts (tooltip + utilitaire Spike).</summary>
    public static readonly IReadOnlyDictionary<string, string> TypeFr = new Dictionary<string, string>
    {
        ["fire"] = "feu", ["cold"] = "froid", ["earth"] = "terre", ["lightning"] = "foudre",
        ["holy"] = "sacré", ["shadow"] = "ombre", ["dark"] = "ténèbres", ["chaos"] = "chaos",
        ["slashing"] = "tranchant", ["piercing"] = "perforant", ["blunt"] = "contondant",
        ["physical"] = "physique",
    };

    /// <summary>Libellé d'un type de dégâts dans la langue affichée : valeur FR de <see cref="TypeFr"/>
    /// en français, clé anglaise du type en anglais. Type null (dégâts sans type) → « dégâts »/« damage ».</summary>
    public static string DisplayType(string? type)
    {
        if (type is null) return AppLanguage.IsFr ? "dégâts" : "damage";
        return AppLanguage.IsFr ? TypeFr.GetValueOrDefault(type, type) : type;
    }

    // Types de dégâts du wiki. « less/more damage » et « X% of that damage » ne matchent pas :
    // seul « <valeur> [type] damage » est retenu, et un nombre suivi de % est exclu.
    private const string Types = "fire|cold|earth|lightning|holy|shadow|dark|chaos|slashing|piercing|blunt|physical";

    // États de cible qui rendent une part « X more damage » CONDITIONNELLE (checkbox du spike) :
    // forme « … if <condition> » (Deathly Chill/>50 %, Smite/Spear of Light/attaque, Signet of
    // Shadows/Blinded…) ou « … to <état> foes » (Aftershock : « to knocked down foes »). Les
    // « to nearby/adjacent foes » (aire, ex. Shockwave) NE sont PAS des conditions → hors liste,
    // non captés (hors périmètre v1, décision Philippe). Liste semée par audit DB, à compléter.
    private const string ConditionStates =
        @"knocked[ -]?down|attacking|casting|hexed|enchanted|bleeding|poisoned|blind(?:ed)?|" +
        @"crippled|burning|dazed|weakened|maimed|moving|fleeing";

    // Valeur = entier résolu (entre marqueurs, normal OU boosté par flux) OU entier fixe isolé
    // (pas un morceau de plage a...b...c, pas suivi de % ni d'un marqueur). Classe de marqueurs
    // construite depuis MarkChars pour rester insensible au marquage flux.
    private static readonly string Value =
        $@"(?:[{SkillProgression.MarkChars}](?<num>\d+)[{SkillProgression.MarkChars}]" +
        $@"|(?<![\d.{SkillProgression.MarkChars}])(?<num>\d+)(?![\d.%{SkillProgression.MarkChars}]))";

    private static readonly Regex DamageRegex = new(
        $@"(?<plus>\+\s*)?{Value}\s+(?:(?<type>{Types})\s+)?damage\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Mode spike uniquement (tooltip du chantier 10 inchangée) : paquets formulés autrement que
    // « X [type] damage », que DamageRegex ignore volontairement. « X more damage » (Order of
    // Pain « 16 more damage », Strength of Honor « 25 more damage in melee ») et « increases
    // [physical] damage by +X » (Winnowing « +4 »). « X additional damage » (Visions of Regret
    // « 41 additional damage if not under… ») est un synonyme de « more ». Comptés comme bonus →
    // ignorent l'armure. Le groupe optionnel « cond » capture la clause conditionnelle qui suit le
    // paquet (« if … » ou « to <état> foes ») : sa présence marque la part conditionnelle (checkbox).
    private static readonly Regex MoreDamageRegex = new(
        $@"{Value}\s+(?:more|additional)\s+(?:(?<type>{Types})\s+)?damage\b" +
        $@"(?<cond>\s+if\b[^.;:]*|\s+to\s+(?:{ConditionStates})\b[^.;:]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IncreasesDamageRegex = new(
        $@"increases?\s+(?:(?<type>{Types})\s+)?damage\s+by\s+\+?{Value}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // « Steal (up to) X Health » : le lanceur (ou son esprit/arme/pet) vole la cible — sauf
    // vol à un allié (Taste of Death « from allied undead servant »).
    private static readonly Regex LifeStealRegex = new(
        $@"\bsteals?\s+(?:up\s+to\s+)?{Value}\s+Health\b(?!\s+from\s+allied)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Perte de vie INFLIGÉE à l'ennemi — deux formulations au catalogue (inventaire harnais
    // SpikeCheck, rang 12) : « Causes X Health loss » (Enfeebling Touch, Spoil Victor, Agony)
    // et « …foe[s] lose[s] X Health » (Pain of Disenchantment). Les coûts du lanceur
    // (« you lose », « this spirit loses », « Lose X Health » impératif, Signet of Binding)
    // ne matchent pas : il faut « causes » ou un sujet foe dans le même segment de phrase.
    private static readonly Regex HealthLossRegex = new(
        $@"\bcauses?\s+{Value}\s+Health loss\b|\bfoes?\b[^.;:]*?\blose[s]?\s+{Value}\s+Health\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PenetrationRegex = new(
        @"(?<pen>\d+)%\s+armor penetration", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Ticks (inventaire harnais SpikeCheck, rang 12 : 38 skills per-second au catalogue) ──
    // Formulation périodique : « each/every second » ou « every N seconds » (Meteor Shower).
    // « for each second since casting » = Savannah Heat, CUMULATIF (tick n = n × valeur).
    // « for each second [the Spirit was alive] » (Destruction, cumul à la mort) est exclu par
    // le lookbehind : « each second » précédé de « for » ne matche que la branche cumulative.
    private static readonly Regex TickPhraseRegex = new(
        $@"(?<cum>\bfor\s+each\s+second\s+since\s+casting\b)" +
        $@"|(?<!\bfor\s)\b(?:each|every)\s+second\b" +
        $@"|\bevery\s+{Value}\s+seconds\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Seuils « X for each [ressource statique] (maximum Y) » (tranche 2 spike) ──
    // « for each … » : localise le segment de phrase portant le paquet à SEUIL. Exclut seulement
    // « for each second since casting » (Savannah Heat, tick cumulatif géré plus haut) — les comptes
    // dérivés « for each second the Spirit was alive » (Destruction) et « for each point of Energy
    // lost » (Energy Surge/Burn) SONT captés (compte dérivé, décision Philippe). Le régex ne tourne
    // que sur les skills de SpikeThresholdSkills → aucune collatérale hors liste blanche.
    private static readonly Regex ForEachRegex = new(
        @"\bfor\s+each\b(?!\s+second\s+since\s+casting)[^.;:]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // « maximum <N> » dans le segment à seuil = plafond de dégâts TOTAL (Symbolic 70, Deadly 130,
    // Mystic 102, Blades 60, Aneurysm 24, Destruction 150). Absent → 0 (Signet of Rage, pas de
    // plafond annoncé → repli FallbackCap ; comptes dérivés en énergie → max synthétique ci-dessous).
    private static readonly Regex MaximumRegex = new(
        $@"maximum\s+{Value}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Compte dérivé « X for each point of Energy lost » (Energy Surge/Burn) : le plafond du compte
    // = l'énergie perdue annoncée (« Causes N Energy loss », rang-dépendante) → alimente un plafond
    // de dégâts synthétique (par-unité × énergie perdue) faute de « maximum N » explicite.
    private static readonly Regex EnergyLossRegex = new(
        $@"causes?\s+{Value}\s+Energy\s+loss", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // « maximum <N> [type] damage » : ce N est un PLAFOND, jamais un paquet à compter (Signet of
    // Mystic Wrath « maximum 102 holy damage », Destruction « maximum 150 damage », Club of a
    // Thousand Bears « maximum 60 damage ») → span à sauter dans la boucle DamageRegex (corrige le
    // sur-compte historique : le max était additionné comme un dégât de plus).
    private static readonly Regex MaxCapDamageRegex = new(
        $@"maximum\s+{Value}\s+(?:(?:{Types})\s+)?damage\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Durée d'effet : parenthèse « (5 seconds) », « (for 2 seconds) » (Spike Trap),
    // « (8 seconds.) » en tête (Balthazar's Aura), « (6 second[s].) » (Seeping Wound),
    // « (78 second lifespan) » (Agony) — ou « for 15 seconds » hors parenthèses (Storm of
    // Swords, « hexed for 3 seconds » de Smoldering Embers).
    private static readonly Regex DurationParenRegex = new(
        $@"\(\s*(?:for\s+)?{Value}\s+second(?:s|\[s\])?(?:\s+lifespan)?\s*\.?\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DurationForRegex = new(
        $@"\bfor\s+{Value}\s+seconds?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Mystic Sandstorm : « Lasts twice as long if you are enchanted » → double du max.
    private static readonly Regex TwiceAsLongRegex = new(
        @"lasts\s+twice\s+as\s+long", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly char[] SegmentBounds = ['.', ';', ':'];

    // Garde-fou : les esprits à longue vie (Agony, lifespan 78 s) donneraient des compteurs
    // absurdes — aucun spike ne dure plus de 30 s.
    private const int MaxTickCap = 30;

    // Exceptions nominatives (wiki/Armor-ignoring_damage) : type trompeur mais armor-ignoring.
    // Liste wiki incomplète — étendre ici si Philippe en identifie d'autres.
    private static readonly HashSet<string> IgnoringExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ear Bite", // « piercing damage » mais armor-ignoring
    };

    /// <summary>Analyse une description résolue. Aucun dégât calculable → Rows vide.
    /// <paramref name="includeMoreDamage"/> (mode spike) ajoute les paquets « X more damage » /
    /// « increases … by +X » que la tooltip ignore.</summary>
    public static Analysis Analyze(string resolvedDescription, string skillName,
                                   bool includeMoreDamage = false)
    {
        if (string.IsNullOrEmpty(resolvedDescription))
            return new Analysis([], 0);

        var tickSegments = FindTickSegments(resolvedDescription);
        // Spans « maximum N [type] damage » : plafonds, jamais des paquets → sautés partout (fix
        // sur-compte). Segments à seuil (« for each … ») : uniquement pour les skills de la liste
        // blanche, pour marquer le bon paquet par-unité + son plafond.
        var maxCapSpans = MaxCapSpans(resolvedDescription);
        var thresholdSegments = SpikeThresholdSkills.IsThresholdCapable(skillName)
            ? FindThresholdSegments(resolvedDescription) : null;

        var rows = new List<Row>();
        foreach (Match m in DamageRegex.Matches(resolvedDescription))
        {
            if (InSpan(maxCapSpans, m.Index)) continue; // « maximum N damage » = plafond, pas un paquet
            if (!int.TryParse(m.Groups["num"].Value, out int value)) continue;
            string? type = m.Groups["type"].Success ? m.Groups["type"].Value.ToLowerInvariant() : null;
            bool isBonus = m.Groups["plus"].Success;
            var (ticks, cum) = TicksAt(tickSegments, m.Index);
            var (isThreshold, maxDamage, clause) = ThresholdAt(thresholdSegments, m.Index);
            rows.Add(new Row(value, type, isBonus, IgnoresArmor(type, isBonus, skillName),
                             RowKind.Damage, ticks, cum,
                             IsThreshold: isThreshold, MaxDamage: maxDamage, ThresholdClause: clause,
                             Index: m.Index));
        }

        if (includeMoreDamage)
            foreach (var re in new[] { MoreDamageRegex, IncreasesDamageRegex })
                foreach (Match m in re.Matches(resolvedDescription))
                {
                    if (InSpan(maxCapSpans, m.Index)) continue;
                    if (!int.TryParse(m.Groups["num"].Value, out int value)) continue;
                    string? type = m.Groups["type"].Success ? m.Groups["type"].Value.ToLowerInvariant() : null;
                    // Part conditionnelle (« … if [état] » / « … to <état> foes ») : la fenêtre spike
                    // la compte par défaut mais la case peut l'exclure (IncreasesDamageRegex n'a pas
                    // de groupe « cond » → Success = false, toujours inconditionnel).
                    bool conditional = m.Groups["cond"].Success;
                    // Part à seuil (« +9 more holy damage for each adrenaline skill » de Signet of Rage).
                    var (isThreshold, maxDamage, clause) = ThresholdAt(thresholdSegments, m.Index);
                    // Bonus additionnel par coup/déclenchement → ignore l'armure (comme « +X damage »).
                    rows.Add(new Row(value, type, true, true, RowKind.Damage,
                                     TicksAt(tickSegments, m.Index).Ticks,
                                     Conditional: conditional,
                                     Condition: conditional ? m.Groups["cond"].Value.Trim() : null,
                                     IsThreshold: isThreshold, MaxDamage: maxDamage, ThresholdClause: clause,
                                     Index: m.Index));
                }

        // Overrides conditionnels nommés (mode spike) : paquets bonus dont la condition n'est pas
        // captée par MoreDamageRegex (condition détachée de « damage », état non-standard, « if »
        // en tête). On force la part conditionnelle (comptée par défaut, case pour l'exclure) —
        // le total par défaut est inchangé, seule une case apparaît. Cf. SpikeConditionalOverrides.
        if (includeMoreDamage)
        {
            var overrides = SpikeConditionalOverrides.For(skillName);
            if (overrides.Count > 0)
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i] is { Kind: RowKind.Damage, Conditional: false } row
                        && overrides.FirstOrDefault(o => o.Value == row.Value && o.Bonus == row.IsBonus) is { } ov)
                        rows[i] = row with { Conditional = true, Condition = ov.Display };
        }

        foreach (Match m in LifeStealRegex.Matches(resolvedDescription))
            if (int.TryParse(m.Groups["num"].Value, out int steal))
                rows.Add(new Row(steal, null, false, true, RowKind.LifeSteal,
                                 TicksAt(tickSegments, m.Index).Ticks));

        foreach (Match m in HealthLossRegex.Matches(resolvedDescription))
            if (int.TryParse(m.Groups["num"].Value, out int loss))
                rows.Add(new Row(loss, null, false, true, RowKind.HealthLoss,
                                 TicksAt(tickSegments, m.Index).Ticks));

        var pen = PenetrationRegex.Match(resolvedDescription);
        int penetration = pen.Success && int.TryParse(pen.Groups["pen"].Value, out int p) ? p : 0;
        return new Analysis(rows, penetration);
    }

    // Segments de phrase contenant une formulation périodique, avec leur nombre max de ticks.
    // La durée est cherchée : (1) parenthèse APRÈS la formulation dans le segment (« each
    // second (5 seconds) » — ignore « (3 second[s]) » du Burning de Ray of Judgment placé
    // avant) ; (2) « for N seconds » dans le segment ; (3) première parenthèse-durée de la
    // description (« (8 seconds.) » en tête, « (78 second lifespan) » d'Agony, « (3 seconds.) »
    // médian de Binding Chains). Rien de résoluble → pas de ticks (Destruction).
    private static List<(int Start, int End, int Ticks, bool Cumulative)>? FindTickSegments(string desc)
    {
        List<(int, int, int, bool)>? segments = null;
        foreach (Match m in TickPhraseRegex.Matches(desc))
        {
            int segStart = desc.LastIndexOfAny(SegmentBounds, m.Index) + 1;
            int segEnd = desc.IndexOfAny(SegmentBounds, m.Index + m.Length);
            if (segEnd < 0) segEnd = desc.Length;
            // « every N seconds » (Meteor Shower) : N secondes par tick, sinon 1.
            int interval = m.Groups["num"].Success
                && int.TryParse(m.Groups["num"].Value, out int iv) && iv > 0 ? iv : 1;

            int duration = 0;
            foreach (Match d in DurationParenRegex.Matches(desc))
            {
                if (d.Index < m.Index + m.Length) continue;
                if (d.Index >= segEnd) break;
                int.TryParse(d.Groups["num"].Value, out duration);
                break;
            }
            if (duration == 0)
                foreach (Match d in DurationForRegex.Matches(desc))
                    if (d.Index >= segStart && d.Index < segEnd)
                    {
                        int.TryParse(d.Groups["num"].Value, out duration);
                        break;
                    }
            if (duration == 0)
            {
                var d = DurationParenRegex.Match(desc);
                if (d.Success) int.TryParse(d.Groups["num"].Value, out duration);
            }
            if (duration <= 0) continue;

            int ticks = Math.Max(1, duration / interval);
            if (TwiceAsLongRegex.IsMatch(desc)) ticks *= 2;
            ticks = Math.Min(ticks, MaxTickCap);
            if (ticks > 1) (segments ??= []).Add((segStart, segEnd, ticks, m.Groups["cum"].Success));
        }
        return segments;
    }

    private static (int Ticks, bool Cumulative) TicksAt(
        List<(int Start, int End, int Ticks, bool Cumulative)>? segments, int index)
    {
        if (segments is not null)
            foreach (var s in segments)
                if (index >= s.Start && index < s.End)
                    return (s.Ticks, s.Cumulative);
        return (1, false);
    }

    // Spans « maximum N [type] damage » : plafonds à ne jamais compter comme paquets de dégâts.
    private static List<(int Start, int End)>? MaxCapSpans(string desc)
    {
        List<(int, int)>? spans = null;
        foreach (Match m in MaxCapDamageRegex.Matches(desc))
            (spans ??= []).Add((m.Index, m.Index + m.Length));
        return spans;
    }

    private static bool InSpan(List<(int Start, int End)>? spans, int index)
    {
        if (spans is not null)
            foreach (var s in spans)
                if (index >= s.Start && index < s.End)
                    return true;
        return false;
    }

    // Segments de phrase portant un paquet à SEUIL (« … for each [ressource] … »), avec le plafond
    // de dégâts annoncé dans le segment (« maximum N », 0 si aucun) et la clause capturée (tooltip).
    // Un seul « for each » par skill de la liste blanche en pratique ; on scanne quand même tous
    // les segments pour être robuste.
    private static List<(int Start, int End, int MaxDamage, string Clause)>? FindThresholdSegments(string desc)
    {
        List<(int, int, int, string)>? segments = null;
        foreach (Match m in ForEachRegex.Matches(desc))
        {
            int segStart = desc.LastIndexOfAny(SegmentBounds, m.Index) + 1;
            int segEnd = desc.IndexOfAny(SegmentBounds, m.Index);
            if (segEnd < 0) segEnd = desc.Length;
            int maxDamage = 0;
            var cap = MaximumRegex.Match(desc, segStart, segEnd - segStart);
            if (cap.Success) int.TryParse(cap.Groups["num"].Value, out maxDamage);
            else
            {
                // Comptes dérivés sans « maximum N damage » explicite : plafond de dégâts
                // synthétique = par-unité (1er « N damage » du segment) × une borne lue ailleurs
                // dans le texte (énergie perdue, ou durée de l'effet pour un « for each second »).
                var per = DamageRegex.Match(desc, segStart, segEnd - segStart);
                if (per.Success && int.TryParse(per.Groups["num"].Value, out int perUnit))
                {
                    if (m.Value.Contains("Energy", StringComparison.OrdinalIgnoreCase)
                        && EnergyLossRegex.Match(desc) is { Success: true } loss
                        && int.TryParse(loss.Groups["num"].Value, out int lost))
                        maxDamage = perUnit * lost;                 // Energy Surge/Burn : énergie perdue
                    else if (m.Value.Contains("second", StringComparison.OrdinalIgnoreCase)
                        && DurationParenRegex.Match(desc) is { Success: true } dur
                        && int.TryParse(dur.Groups["num"].Value, out int secs))
                        maxDamage = perUnit * secs;                 // Rising Bile : durée du hex (secondes)
                }
            }
            (segments ??= []).Add((segStart, segEnd, maxDamage, m.Value.Trim()));
        }
        return segments;
    }

    private static (bool IsThreshold, int MaxDamage, string? Clause) ThresholdAt(
        List<(int Start, int End, int MaxDamage, string Clause)>? segments, int index)
    {
        if (segments is not null)
            foreach (var s in segments)
                if (index >= s.Start && index < s.End)
                    return (true, s.MaxDamage, s.Clause);
        return (false, 0, null);
    }

    private static bool IgnoresArmor(string? type, bool isBonus, string skillName)
    {
        if (IgnoringExceptions.Contains(skillName)) return true;
        if (isBonus) return true;               // bonus d'attaque « +X damage »
        if (type is null) return true;          // dégâts sans type
        return type is "holy" or "shadow";      // sacré et ombre (mais pas ténèbres/dark)
    }

    /// <summary>
    /// Dégâts armor-respecting contre <paramref name="armorLevel"/> : valeur listée (exacte à
    /// AL 60, lanceur niveau 20) × 2^((3×niveau − AL_effectif)/40), tronqués à l'entier
    /// inférieur (wiki/Damage_calculation : Strike Level d'une skill = 3 × niveau du lanceur).
    /// La pénétration réduit l'AL effectif (25% : AL 60 → 45).
    /// </summary>
    public static int DamageAt(int value, int armorLevel, int armorPenetration, int casterLevel = 20)
    {
        double effectiveAl = armorLevel * (100 - armorPenetration) / 100.0;
        return (int)Math.Floor(value * Math.Pow(2, (3 * casterLevel - effectiveAl) / 40.0));
    }
}
