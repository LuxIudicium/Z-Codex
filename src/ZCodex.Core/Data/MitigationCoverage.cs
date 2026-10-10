using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// VERROUS du chantier « calculateur d'armure : réductions de dégâts » (patron <see cref="SpikeBoostCoverage"/>).
/// Les harnais de ce projet meurent avec la session : le contrôle d'exhaustivité vit donc ici, dans le code,
/// et la prochaine passe le retrouve. Les quatre listes <b>doivent rester VIDES</b>.
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

    /// <summary>Compétences que les motifs attrapent mais qui ne réduisent PAS les dégâts que le perso reçoit (ou que
    /// le calculateur ne traite pas encore), chacune avec sa raison (clé = nom anglais de la base).</summary>
    public static readonly IReadOnlyDictionary<string, string> Excluded = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Armor of Unfeeling"]    = "ne protège que les esprits du lanceur",
        ["Dual Shot"]             = "réduit les dégâts que l'attaquant inflige",
        ["Triple Shot (Kurzick)"] = "réduit les dégâts que l'attaquant inflige",
        ["Triple Shot (Luxon)"]   = "réduit les dégâts que l'attaquant inflige",
        ["Twin Moon Sweep (PvP)"] = "réduit les dégâts que l'attaquant inflige",
        ["Flurry"]                = "réduit les dégâts que l'attaquant inflige",
        ["Vapor Blade"]           = "réduit les dégâts que l'attaquant inflige",
        ["Life Attunement"]       = "malus porté par l'attaquant : reporté (décision de Philippe du 15/09/2026)",
        // Lot 3 (10/10/2026).
        ["Call of Protection"]    = "protège le familier, pas le perso (Q7, accord de Philippe du 10/10/2026)",
        ["Symbiotic Bond"]        = "redirige vers le perso des dégâts subis par le familier (Q7, écartée le 10/10/2026)",
        ["Divine Intervention"]   = "n'annule qu'un coup FATAL : dépend de la vie restante, hors calculateur",
        ["Judge's Intervention"]  = "n'annule qu'un coup FATAL : dépend de la vie restante, hors calculateur",
        ["Protective Spirit"]     = "plafond en % de la vie max : lot 5",
        ["Shadow Form"]           = "plafond et réduction par enchantement d'Assassin : lot 5",
        ["Mark of Protection"]    = "conversion en soin : lot 5",
        ["Reversal of Fortune"]   = "conversion en soin : lot 5",
        ["Life Sheath"]           = "conversion en soin : lot 5",
        ["Stone Striker"]         = "conversion de type : lot 4",
        ["Dulled Weapon"]         = "malus porté par l'attaquant : reporté (décision de Philippe du 15/09/2026)",
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
