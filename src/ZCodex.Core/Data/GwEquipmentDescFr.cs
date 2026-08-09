using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Traduction FR des descriptions « parfaites » de mods d'équipement
/// (<see cref="GwEquipmentModDetails"/>), affichées à droite du nom dans les listes de l'éditeur
/// et du calculateur d'armure.
///
/// Ces descriptions sont FORMULAIRES : 253 textes distincts composés d'une trentaine de motifs
/// (« Armor +20 (vs. physical damage) », « Fast Casting +1 (Non-stacking) »…). On traduit donc
/// par motifs plutôt que par table — les caractéristiques et les altérations viennent des tables
/// FR déjà validées (<see cref="GwAttributeData"/>, <see cref="GwConditionData"/>).
///
/// Formulations calées sur le jeu FR (captures Philippe) et sur gwiki.fr :
/// « Armure +20 (contre les dégâts physiques) », « Récupération d'énergie +1 », « Énergie +5 »,
/// « Réduit de 20% la durée de "Stupeur" sur vous. (Non cumulable) », « Maîtrise de la hache +1 ».
/// NB : le jeu et gwiki écrivent « Energie » / « Ecu » sans accent ; on accentue les majuscules
/// (préférence Philippe 27/07 — l'omission ne se justifie qu'à l'écrit manuscrit).
///
/// Le texte ANGLAIS reste la donnée source : tous les parseurs (armure, durées, dégâts plats)
/// travaillent dessus. Cette classe ne sert qu'à l'affichage.
/// </summary>
public static class GwEquipmentDescFr
{
    private const RegexOptions Opt = RegexOptions.Compiled | RegexOptions.IgnoreCase;

    /// <summary>Description dans la langue courante ; l'anglais est renvoyé tel quel en mode EN.</summary>
    public static string Translate(string english)
    {
        if (!AppLanguage.IsFr || string.IsNullOrWhiteSpace(english)) return english;

        var segments = english.Split(" ; ", StringSplitOptions.TrimEntries).Select(Segment).ToList();
        for (int i = 1; i < segments.Count; i++) segments[i] = Continuation(segments[i]);
        return string.Join(" ; ", segments);
    }

    // Un point-virgule ne clôt pas la phrase : le segment suivant reprend en MINUSCULE, sauf nom
    // propre — « Dégâts +15% ; énergie -5 » (règle Philippe, 30/07/2026). L'anglais garde ses
    // capitales, c'est une convention propre au français.
    //
    // Relevé sur la totalité des descriptions : les seules têtes de segment non initiales sont
    // Armure, Dégâts, Énergie, Récupération, Régénération, Santé, « et », « that » et
    // « (Maximum… » — aucun nom propre, donc aucune exception à prévoir aujourd'hui. Si la table
    // de mods venait à en introduire un (attribut, altération), il faudrait l'exclure ici.
    //
    // Seule réserve : un segment ENTIÈREMENT entre parenthèses est un aparté qui suit un point
    // (« …de 1 seconde. ; (Maximum : 3 secondes) »), pas une continuation — il garde sa capitale.
    private static string Continuation(string segment) =>
        segment.Length > 0 && char.IsUpper(segment[0]) && !segment.StartsWith('(')
            ? char.ToLowerInvariant(segment[0]) + segment[1..]
            : segment;

    // ── un segment = tête + parenthèse finale optionnelle ─────────────────────
    private static readonly Regex SegRe = new(@"^(?<head>.*?)\s*(?:\((?<paren>[^)]*)\))?$", Opt);

    private static string Segment(string seg)
    {
        var m = SegRe.Match(seg);
        var head = m.Groups["head"].Value.Trim();
        var paren = m.Groups["paren"].Success ? m.Groups["paren"].Value.Trim() : null;

        // « (Maximum: 5 seconds) » : segment réduit à une parenthèse.
        if (head.Length == 0 && paren != null) return $"({Paren(paren)})";

        var fr = Head(head);
        return paren is null ? fr : $"{fr} ({Paren(paren)})";
    }

    // ── têtes ─────────────────────────────────────────────────────────────────
    private static readonly (Regex Re, string Fr)[] HeadRules =
    {
        (new(@"^Health ([+-]\d+)$", Opt),                          "Santé $1"),
        (new(@"^Energy ([+-]\d+)$", Opt),                          "Énergie $1"),
        (new(@"^Armor ([+-]\d+)$", Opt),                           "Armure $1"),
        (new(@"^Damage \+(\d+)%$", Opt),                           "Dégâts +$1%"),
        (new(@"^Energy regeneration:? ([+-]\d+)$", Opt),           "Récupération d'énergie $1"),
        (new(@"^Health regeneration:? ([+-]\d+)$", Opt),           "Régénération de santé $1"),
        (new(@"^Life Draining: (\d+), (\d+)$", Opt),               "Vol de vie : $1, $2"),
        (new(@"^Energy gain on hit: (\d+)$", Opt),                 "Gain d'énergie par coup : $1"),
        (new(@"^Armor penetration \+(\d+)%$", Opt),                "Pénétration d'armure +$1%"),
        (new(@"^Double Adrenaline on hit$", Opt),                  "Double gain d'adrénaline"),
        (new(@"^Enchantments last (\d+)% longer$", Opt),           "Les enchantements durent $1% plus longtemps"),
        (new(@"^Received physical damage -(\d+)$", Opt),           "Dégâts physiques reçus -$1"),
        (new(@"^Reduces physical damage by (\d+)$", Opt),          "Réduit les dégâts physiques de $1"),
        (new(@"^Holy damage you receive increased by (\d+)$", Opt),"Dégâts sacrés reçus augmentés de $1"),
        (new(@"^Item's attribute \+(\d+)$", Opt),                  "Caractéristique de l'objet +$1"),
        (new(@"^Reduces casting time of spells$", Opt),            "Réduit le temps d'incantation des sorts"),
        (new(@"^Halves casting time of (?:spells of item's attribute|item's attribute spells)$", Opt),
                                                                   "Réduit de moitié le temps d'incantation des sorts liés à la caractéristique de l'objet"),
        (new(@"^Halves casting time of spells$", Opt),             "Réduit de moitié le temps d'incantation des sorts"),
        (new(@"^Halves skill recharge of (?:spells of item's attribute|item's attribute spells)$", Opt),
                                                                   "Réduit de moitié le rechargement des sorts liés à la caractéristique de l'objet"),
        (new(@"^Halves skill recharge of spells$", Opt),           "Réduit de moitié le rechargement des sorts"),
        (new(@"^Increases knockdown time on foes by (\d+) seconds?\.?$", Opt),
                                                                   "Augmente de $1 seconde le temps de renversement des ennemis"),
        (new(@"^Reduces Hex durations? on you by (\d+)%$", Opt),   "Réduit de $1% la durée des maléfices sur vous"),
        // Insigne de Lieutenant : la phrase source est coupée en deux segments par « ; ».
        (new(@"^and damage dealt by you by (\d+)%$", Opt),         "et de $1% les dégâts que vous infligez"),
    };

    // « Cold damage » → « Dégâts du froid » (type d'arme élémentaire).
    private static readonly Dictionary<string, string> ElementDamage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cold"] = "Dégâts du froid", ["Fire"] = "Dégâts de feu",
        ["Earth"] = "Dégâts terrestres", ["Lightning"] = "Dégâts de foudre",
    };

    private static readonly Regex ElementRe   = new(@"^(\w+) damage$", Opt);
    private static readonly Regex LengthensRe = new(@"^Lengthens (.+?) durations? on foes by (\d+)%$", Opt);
    private static readonly Regex ReducesRe   = new(@"^Reduces (.+?) durations? on you by (\d+)%$", Opt);
    private static readonly Regex AttrRe      = new(@"^(.+?) \+(\d+)$", Opt);

    private static string Head(string head)
    {
        foreach (var (re, fr) in HeadRules)
            if (re.IsMatch(head)) return re.Replace(head, fr);

        if (ElementRe.Match(head) is { Success: true } el
            && ElementDamage.TryGetValue(el.Groups[1].Value, out var dmg)) return dmg;

        if (LengthensRe.Match(head) is { Success: true } lg)
            return $"Augmente de {lg.Groups[2].Value}% la durée de {Conds(lg.Groups[1].Value)} sur les ennemis";

        if (ReducesRe.Match(head) is { Success: true } rd)
            return $"Réduit de {rd.Groups[2].Value}% la durée de {Conds(rd.Groups[1].Value)} sur vous";

        // En dernier (motif large) : « <Caractéristique> +N ».
        if (AttrRe.Match(head) is { Success: true } at)
            return $"{GwAttributeData.DisplayName(at.Groups[1].Value)} +{at.Groups[2].Value}";

        return head;   // inconnu : on laisse l'anglais plutôt que d'inventer
    }

    private static string Cond(string en) => GwConditionData.Fr.GetValueOrDefault(en.Trim(), en.Trim());

    /// <summary>Liste d'altérations : « Dazed and Deep Wound » → « « Stupeur » et « Blessure profonde » ».
    /// Les runes de Récupération/Clarté/Pureté/Rétablissement en portent deux à la fois.</summary>
    private static string Conds(string en) =>
        string.Join(" et ", en.Split(" and ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                             .Select(c => $"« {Cond(c)} »"));

    // ── parenthèses ───────────────────────────────────────────────────────────
    private static readonly (Regex Re, string Fr)[] ParenRules =
    {
        (new(@"^Non-stacking$", Opt),                     "Non cumulable"),
        (new(@"^Stacking$", Opt),                         "Cumulable"),
        (new(@"^Chance: (\d+)%$", Opt),                   "Chance : $1%"),
        (new(@"^(\d+)% chance while using skills$", Opt),  "$1% de chances en utilisant des compétences"),
        (new(@"^Maximum: (\d+) seconds?$", Opt),          "Maximum : $1 secondes"),
        (new(@"^on chest armor$", Opt),                   "sur le torse"),
        (new(@"^on leg armor$", Opt),                     "sur les jambes"),
        (new(@"^on other armor$", Opt),                   "sur les autres pièces"),
        (new(@"^while attacking$", Opt),                  "en attaquant"),
        (new(@"^while casting$", Opt),                    "pendant l'incantation"),
        (new(@"^while activating skills$", Opt),          "en activant des compétences"),
        (new(@"^while holding an item$", Opt),            "avec un objet en main"),
        (new(@"^while using a Preparation$", Opt),        "avec une préparation"),
        (new(@"^while your pet is alive$", Opt),          "si votre familier est vivant"),
        (new(@"^while not affected by an Enchantment Spell$", Opt), "sans enchantement"),
        (new(@"^while (?:Enchanted|affected by an Enchantment Spell)$", Opt), "sous les effets d'un enchantement"),
        (new(@"^while (?:Hexed|affected by a Hex Spell)$", Opt),             "sous les effets d'un maléfice"),
        (new(@"^while (?:in a Stance)$", Opt),            "avec une pose de combat"),
        (new(@"^while affected by a Condition$", Opt),    "sous l'effet d'une altération"),
        (new(@"^while affected by a Weapon Spell$", Opt), "sous les effets d'un sort d'arme"),
        (new(@"^while affected by a Shout, Echo, or Chant$", Opt), "sous les effets d'un cri, d'un écho ou d'un chant"),
        // « N or more X » : le pluriel FR suit N (1 enchantement / 2 enchantements) → cf. Plural().
        (new(@"^while affected by (\d+) or more Enchantment Spells$", Opt),  "sous les effets de $1 enchantement§ ou plus"),
        (new(@"^while recharging (\d+) or more skills$", Opt), "avec $1 compétence§ ou plus en rechargement"),
        (new(@"^while you control (\d+) or more minions$", Opt), "avec $1 serviteur§ ou plus"),
        (new(@"^while you control (\d+) or more Spirits$", Opt), "avec $1 esprit§ ou plus"),
        (new(@"^while [Hh]ealth is below (\d+)%$", Opt),  "PV en dessous de $1%"),
        (new(@"^while [Hh]ealth is above (\d+)%$", Opt),  "PV au-dessus de $1%"),
        (new(@"^for each equipped Signet$", Opt),         "par sceau équipé"),
        (new(@"^vs\. Hexed foes$", Opt),                  "contre les ennemis sous maléfice"),
    };

    private static readonly Regex VsRe        = new(@"^vs\. (\w+) damage$", Opt);
    private static readonly Regex RequiresRe  = new(@"^requires (\d+) (.+)$", Opt);
    private static readonly Regex ReqVsRe     = new(@"^Requires (\d+) (.+?), vs\. (\w+) damage$", Opt);

    // « § » marque un pluriel conditionnel posé par les règles : « 1 enchantement§ » → sans « s »,
    // « 2 enchantement§ » → avec. (Le français ne pluralise qu'à partir de 2.)
    private static readonly Regex PluralRe = new(@"(\d+)( [^§]*)§", Opt);

    private static string Plural(string s) =>
        PluralRe.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value
                                 + (int.Parse(m.Groups[1].Value) >= 2 ? "s" : ""));

    private static string Paren(string paren)
    {
        foreach (var (re, fr) in ParenRules)
            if (re.IsMatch(paren)) return Plural(re.Replace(paren, fr));

        if (VsRe.Match(paren) is { Success: true } vs)
            return $"contre les dégâts {DamageType(vs.Groups[1].Value)}";

        if (ReqVsRe.Match(paren) is { Success: true } rv)
            return $"nécessite {rv.Groups[1].Value} en {GwAttributeData.DisplayName(rv.Groups[2].Value)}, "
                 + $"contre les dégâts {DamageType(rv.Groups[3].Value)}";

        if (RequiresRe.Match(paren) is { Success: true } rq)
            return $"nécessite {rq.Groups[1].Value} en {GwAttributeData.DisplayName(rq.Groups[2].Value)}";

        return paren;
    }

    // Accord au PLURIEL : « contre les dégâts physiques / élémentaires / tranchants ».
    private static readonly Dictionary<string, string> DamageTypesFr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["physical"] = "physiques", ["elemental"] = "élémentaires",
        ["fire"] = "de feu", ["cold"] = "de froid", ["earth"] = "de terre", ["lightning"] = "de foudre",
        ["slashing"] = "tranchants", ["piercing"] = "perforants", ["blunt"] = "contondants",
        ["holy"] = "sacrés", ["shadow"] = "d'ombre", ["chaos"] = "de chaos", ["dark"] = "de ténèbres",
    };

    private static string DamageType(string en) => DamageTypesFr.GetValueOrDefault(en, en);
}
