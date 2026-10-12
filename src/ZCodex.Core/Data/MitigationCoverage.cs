using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// VERROUS du chantier « calculateur d'armure : réductions de dégâts » (patron <see cref="SpikeBoostCoverage"/>).
/// Les harnais de ce projet meurent avec la session : le contrôle d'exhaustivité vit donc ici, dans le code,
/// et la prochaine passe le retrouve. Les huit listes <b>doivent rester VIDES</b>.
/// </summary>
public static class MitigationCoverage
{
    // Les tournures d'une réduction EN POURCENTAGE des dégâts, relevées le 15/09/2026 sur les 1517
    // descriptions et revérifiées le 09/10/2026 : 14 compétences couvertes + les 8 écarts ci-dessous.
    private static readonly Regex PercentReduction = new(
        @"\d+(?:\.\.\.\d+)*% less damage"
        + @"|reduce[sd]?[^.]{0,40}damage[^.]{0,30}by \d+(?:\.\.\.\d+)*%"
        + @"|damage[^.]{0,40}reduced by \d+(?:\.\.\.\d+)*%"
        + @"|\bhalf damage\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Lot 3 (10/10/2026) : les autres tournures — réduction FIXE, dégâts AUGMENTÉS, critiques, redirection —,
    // relevées sur toute la base le 10/10/2026. Un nombre suivi de « % » relève du motif précédent.
    private static readonly Regex OtherReduction = new(
        @"take[s]? -?\d+(?:\.\.\.\d+)* less damage"
        + @"|take[s]? -\d+(?:\.\.\.\d+)* damage"
        + @"|reduce[sd]? (?:incoming )?damage[^.%]{0,40}by \d+(?:\.\.\.\d+)*(?![\d.]*%)"
        + @"|damage[^.%]{0,40}reduced by \d+(?:\.\.\.\d+)*(?![\d.]*%)"
        + @"|\bdamage reduction\b|\bincoming damage\b|\bdamage you take\b|\bnegates the next\b"
        + @"|take[s]? (?:\d+(?:\.\.\.\d+)*%|double) damage"
        + @"|immun\w* to critical|extra damage from critical"
        + @"|damage[^.]{0,40}redirected",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Lot 4 (11/10/2026) : les tournures d'une CONVERSION de type de dégâts, relevées sur toute la base — 5 conversions
    // couvertes + les écarts ci-dessous (dont les 3 conversions en soin, déjà renvoyées au lot 5).
    private static readonly Regex Conversion = new(
        @"\bconverts?\b[^.]{0,80}\bdamage\b"
        + @"|\b(?:attacks|arrows) (?:deal|do) (?:holy|fire|cold|earth|lightning|dark|chaos|shadow) damage\b"
        + @"|\bdamage is (?:holy|fire|cold|earth|lightning|dark|chaos) damage\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Lot 5 (11/10/2026) : les tournures d'un PLAFOND en % de la vie, d'une conversion en SOIN et d'un soin DÉCLENCHÉ par
    // un coup reçu, relevées sur toute la base. « (?:(?!\.\s).) » = tout sauf une fin de phrase : les plages « 5...29...35 »
    // ont des points.
    private static readonly Regex CapOrHeal = new(
        @"cannot lose more than \d+%|limited to \d+% of|reduced to \d+% of"
        + @"|\bconverts?\b(?:(?!\.\s).){0,90}\bto heal"
        + @"|\b(?:heals?|gains?|steals?)\b(?:(?!\.\s).){0,60}\b(?:whenever|the next time|each time|every time)\b(?:(?!\.\s).){0,40}\btakes? (?:more than \d+ )?damage\b"
        + @"|\b(?:whenever|the next time|each time|every time)\b(?:(?!\.\s).){0,40}\btakes? (?:more than \d+ )?damage\b(?:(?!\.\s).){0,60}\b(?:heals?|gains? \d|steals? \d)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Lot 6b (12/10/2026) : les tournures d'un MALUS porté par l'attaquant — dégâts infligés réduits, plus de critique,
    // attributs baissés, dégâts plafonnés —, relevées sur toute la base le 12/10/2026.
    private static readonly Regex AttackerPenalty = new(
        @"\b(?:deals?|do(?:es|ing)?)\s(?:-?\d+(?:\.\.\.\d+)*%?\s)?less damage"
        + @"|\bdeals? -\d+(?:\.\.\.\d+)* attack damage"
        + @"|\d+% of normal damage"
        + @"|\bremoves ability to land a critical|\bcannot (?:land|score|achieve) (?:a )?critical"
        + @"|\battributes are (?:set to )?0\b|\battributes are reduced by"
        + @"|\bcannot deal more than",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string OwnSkillPenalty = "malus propre de la compétence : déjà dans son calcul (WeaponStrike.ModsFor)";
    private const string AttackerBlock = "malus porté par l'attaquant : bloc Attaquant (lot 6b, AttackerPenaltyData)";

    /// <summary>Compétences que le motif des malus de l'attaquant attrape mais que <see cref="AttackerPenaltyData"/> ne
    /// couvre pas, chacune avec sa raison (lot 6b, clé = nom anglais de la base).</summary>
    public static readonly IReadOnlyDictionary<string, string> ExcludedAttackerPenalties =
        new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Dual Shot"]             = OwnSkillPenalty,
        ["Triple Shot (Kurzick)"] = OwnSkillPenalty,
        ["Triple Shot (Luxon)"]   = OwnSkillPenalty,
        ["Twin Moon Sweep (PvP)"] = OwnSkillPenalty,
        ["Lyssa's Assault"]       = OwnSkillPenalty,
        ["Spiritleech Aura"]      = "touche les esprits de l'attaquant, pas l'attaquant (écartée au cadrage du 12/10/2026)",
        ["Shadow Form"]           = "plafond des dégâts de l'attaquant (PvE) : écarté (Q30, Philippe, 12/10/2026)",
        ["Shadow Theft"]          = "attributs de l'attaquant −1…5 (PvE) : le rang de la ligne le simule (Q28 ne cite que Faiblesse et Wail of Doom)",
        ["Ursan Blessing"]        = "forme PvE : l'attaquant perd ses compétences, hors calculateur",
        ["Volfen Blessing"]       = "forme PvE : l'attaquant perd ses compétences, hors calculateur",
        ["Raven Blessing"]        = "forme PvE : l'attaquant perd ses compétences, hors calculateur",
    };

    private const string AttackerConverter =
        "convertit les attaques de l'ATTAQUANT : la liste de type de la ligne d'attaque le couvre (lot 4)";
    private const string AttackerHolyConverter =
        "rend sacrées les attaques de l'ATTAQUANT, sans la pénétration de Judge's Insight : reporté (lot 6)";

    /// <summary>Compétences que les motifs attrapent mais qui ne réduisent PAS les dégâts que le perso reçoit (ou que
    /// le calculateur ne traite pas encore), chacune avec sa raison (clé = nom anglais de la base).</summary>
    public static readonly IReadOnlyDictionary<string, string> Excluded = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Armor of Unfeeling"]    = "ne protège que les esprits du lanceur",
        ["Dual Shot"]             = "réduit les dégâts que l'attaquant inflige",
        ["Triple Shot (Kurzick)"] = "réduit les dégâts que l'attaquant inflige",
        ["Triple Shot (Luxon)"]   = "réduit les dégâts que l'attaquant inflige",
        ["Twin Moon Sweep (PvP)"] = "réduit les dégâts que l'attaquant inflige",
        ["Flurry"]                = AttackerBlock,
        ["Vapor Blade"]           = "réduit les dégâts que l'attaquant inflige",
        ["Life Attunement"]       = AttackerBlock,
        // Lot 3 (10/10/2026).
        ["Call of Protection"]    = "protège le familier, pas le perso (Q7, accord de Philippe du 10/10/2026)",
        ["Symbiotic Bond"]        = "redirige vers le perso des dégâts subis par le familier (Q7, écartée le 10/10/2026)",
        ["Divine Intervention"]   = "n'annule qu'un coup FATAL : dépend de la vie restante, hors calculateur",
        ["Judge's Intervention"]  = "n'annule qu'un coup FATAL : dépend de la vie restante, hors calculateur",
        ["Dulled Weapon"]         = AttackerBlock,
        // Lot 5 (11/10/2026).
        ["Shadow Form (PvP)"]     = "sorts qui échouent, attaques qui ratent : de l'esquive, hors calculateur comme le blocage (Q24)",
        // Lot 4 (11/10/2026) — convertisseurs propres à l'attaquant (Stone Striker, lui, est couvert côté défenseur).
        ["Avatar of Grenth"]         = AttackerConverter,
        ["Avatar of Melandru"]       = AttackerConverter,
        ["Avatar of Melandru (PvP)"] = AttackerConverter,
        ["Dust Cloak"]               = AttackerConverter,
        ["Dust Cloak (PvP)"]         = AttackerConverter,
        ["Grenth's Fingers"]         = AttackerConverter,
        ["Staggering Force"]         = AttackerConverter,
        ["Kindle Arrows"]            = AttackerConverter,
        ["Avatar of Balthazar"]      = AttackerHolyConverter,
        ["Heart of Holy Flame"]      = AttackerHolyConverter,
    };

    /// <summary>
    /// <b>Verrou 1 — réduction en % oubliée.</b> Les compétences du catalogue dont la description annonce une
    /// réduction de dégâts en pourcentage, sans descripteur dans <see cref="DamageMitigationData.All"/> ni
    /// raison écrite dans <see cref="Excluded"/>. <b>Doit rester VIDE.</b> Une compétence ajoutée ou réécrite
    /// par une mise à jour du jeu y apparaît d'elle-même.
    /// </summary>
    public static IReadOnlyList<string> UncoveredPercentReductions(IEnumerable<Skill> catalog)
    {
        var covered = DamageMitigationData.All.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        return [.. catalog
            .Where(s => PercentReduction.IsMatch(s.Description)
                        && !covered.Contains(s.Name) && !Excluded.ContainsKey(s.Name))
            .Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <b>Verrou 2 — descripteur orphelin.</b> Les descripteurs dont l'id ne désigne plus une compétence du
    /// même nom, ou dont la colonne de progression n'existe pas. <b>Doit rester VIDE.</b> Sans lui, une
    /// compétence renommée ferait une ligne qui vaut silencieusement 0 %.
    /// </summary>
    public static IReadOnlyList<DamageMitigationDescriptor> OrphanDescriptors(IEnumerable<Skill> catalog)
    {
        var byId = catalog.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        return [.. DamageMitigationData.All.Where(d =>
            !byId.TryGetValue(d.SkillId, out var s) || s.Name != d.Name
            || (d.ReadsProgression && (s.Progression is not { } p || d.Index < 0 || d.Index >= p.Length)))];
    }

    /// <summary>
    /// <b>Verrou 4 — réduction fixe, hausse, critique ou redirection oubliée (lot 3).</b> Les compétences du catalogue
    /// que <see cref="OtherReduction"/> attrape, sans descripteur ni raison écrite dans <see cref="Excluded"/>.
    /// <b>Doit rester VIDE.</b>
    /// </summary>
    public static IReadOnlyList<string> UncoveredOtherReductions(IEnumerable<Skill> catalog)
    {
        var covered = DamageMitigationData.All.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        return [.. catalog
            .Where(s => OtherReduction.IsMatch(s.Description)
                        && !covered.Contains(s.Name) && !Excluded.ContainsKey(s.Name))
            .Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <b>Verrou 5 — conversion de type oubliée (lot 4).</b> Les compétences du catalogue dont la description annonce
    /// une conversion du type de dégâts, sans descripteur ni raison écrite dans <see cref="Excluded"/>.
    /// <b>Doit rester VIDE.</b>
    /// </summary>
    public static IReadOnlyList<string> UncoveredConversions(IEnumerable<Skill> catalog)
    {
        var covered = DamageMitigationData.All.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        return [.. catalog
            .Where(s => Conversion.IsMatch(s.Description)
                        && !covered.Contains(s.Name) && !Excluded.ContainsKey(s.Name))
            .Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <b>Verrou 6 — plafond ou soin oublié (lot 5).</b> Les compétences du catalogue dont la description annonce un
    /// plafond en % de la vie, une conversion des dégâts en soin ou un soin déclenché par un coup reçu, sans descripteur
    /// ni raison écrite dans <see cref="Excluded"/>. <b>Doit rester VIDE.</b>
    /// </summary>
    public static IReadOnlyList<string> UncoveredCapsAndHeals(IEnumerable<Skill> catalog)
    {
        var covered = DamageMitigationData.All.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        return [.. catalog
            .Where(s => CapOrHeal.IsMatch(s.Description)
                        && !covered.Contains(s.Name) && !Excluded.ContainsKey(s.Name))
            .Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <b>Verrou 7 — malus de l'attaquant oublié (lot 6b).</b> Les compétences du catalogue dont la description annonce des
    /// dégâts INFLIGÉS réduits, plus de critique, des attributs baissés ou des dégâts plafonnés, sans descripteur dans
    /// <see cref="AttackerPenaltyData.All"/> ni <see cref="DamageMitigationData.All"/> (une réduction du défenseur peut
    /// partager la tournure), ni raison écrite. <b>Doit rester VIDE.</b>
    /// </summary>
    public static IReadOnlyList<string> UncoveredAttackerPenalties(IEnumerable<Skill> catalog)
    {
        var covered = AttackerPenaltyData.All.Select(d => d.Name)
            .Concat(DamageMitigationData.All.Select(d => d.Name)).ToHashSet(StringComparer.Ordinal);
        return [.. catalog
            .Where(s => AttackerPenalty.IsMatch(s.Description) && !covered.Contains(s.Name)
                        && !ExcludedAttackerPenalties.ContainsKey(s.Name) && !Excluded.ContainsKey(s.Name))
            .Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <b>Verrou 8 — malus orphelin (lot 6b).</b> Les descripteurs de <see cref="AttackerPenaltyData.All"/> (hors Faiblesse,
    /// qui n'est pas une compétence) dont l'id ne désigne plus une compétence du même nom, ou dont la colonne de progression
    /// n'existe pas. <b>Doit rester VIDE.</b> Sans lui, une compétence renommée ferait une case qui vaut silencieusement 0.
    /// </summary>
    public static IReadOnlyList<AttackerPenaltyDescriptor> OrphanAttackerPenalties(IEnumerable<Skill> catalog)
    {
        var byId = catalog.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        return [.. AttackerPenaltyData.All.Where(d => !d.IsWeakness && (
            !byId.TryGetValue(d.SkillId, out var s) || s.Name != d.Name
            || (d.ReadsProgression && (s.Progression is not { } p || d.Index < 0 || d.Index >= p.Length))))];
    }

    /// <summary>
    /// <b>Verrou 3 — effet d'armure sans compétence (lot 2).</b> Les effets de <see cref="ArmorEffectsData.All"/>
    /// qui se disent compétence (id ≠ 0) mais que <see cref="ArmorEffectsData.CatalogSkill"/> ne retrouve ni par
    /// id ni par nom. <b>Doit rester VIDE.</b> Sans compétence, la ligne perd son type, donc l'état du perso
    /// qu'elle coche (<see cref="CharacterStateLinks"/>) et sa famille exclusive, ainsi que son nom français —
    /// sans erreur. Vécu le 10/10/2026 : les 4 variantes Kurzick/Luxon de « Save Yourselves! » et Shadow Sanctuary.
    /// </summary>
    public static IReadOnlyList<string> UnlinkedArmorEffects(IEnumerable<Skill> catalog)
    {
        var list = catalog.ToList();
        var byId = list.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var byName = list.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return [.. ArmorEffectsData.All
            .Where(e => e.SkillId != 0 && ArmorEffectsData.CatalogSkill(e.SkillId, e.Name, byId, byName) is null)
            .Select(e => e.Name).Distinct().Order(StringComparer.Ordinal)];
    }
}
