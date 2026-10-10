using System.Text.RegularExpressions;
using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// VERROUS du chantier « calculateur d'armure : réductions de dégâts » (patron <see cref="SpikeBoostCoverage"/>).
/// Les harnais de ce projet meurent avec la session : le contrôle d'exhaustivité vit donc ici, dans le code,
/// et la prochaine passe le retrouve. Les deux listes <b>doivent rester VIDES</b>.
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

    /// <summary>Compétences que le motif attrape mais qui ne réduisent PAS les dégâts que le perso reçoit,
    /// chacune avec sa raison (clé = nom anglais de la base).</summary>
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
            || (d.Fixed == 0 && (s.Progression is not { } p || d.Index < 0 || d.Index >= p.Length)))];
    }
}
