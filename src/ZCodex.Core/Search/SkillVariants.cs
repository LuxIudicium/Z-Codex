using ZCodex.Core.Models;

namespace ZCodex.Core.Search;

// Comment traiter les compétences splittées PvE/PvP lors d'une recherche.
public enum SkillVariantMode
{
    AllVersions, // PvE + PvP (match par nom de base)
    PveOnly,     // uniquement la version PvE / non splittée
    PvpOnly,     // uniquement la version " (PvP)"
}

// Résolution des variantes PvE/PvP d'une compétence. Les compétences splittées portent
// le même nom + suffixe " (PvP)" (ex : "Empathy" / "Empathy (PvP)").
public static class SkillVariants
{
    private const string PvpSuffix = " (PvP)";

    public static bool IsPvpVariant(string name) =>
        name.EndsWith(PvpSuffix, StringComparison.OrdinalIgnoreCase);

    public static string BaseName(string name) =>
        IsPvpVariant(name) ? name[..^PvpSuffix.Length] : name;

    /// <summary>Nom de la variante « (PvP) » d'une compétence. Idempotent.</summary>
    public static string PvpName(string name) =>
        IsPvpVariant(name) ? name : name + PvpSuffix;

    /// <summary>
    /// Dictionnaire nom → id à écrire dans un template GW1, pour l'encodage.
    ///
    /// Le format de template ne porte PAS la distinction PvE/PvP : le jeu écrit toujours l'id de
    /// la version de BASE, et c'est le contexte (zone, type de personnage) qui décide des valeurs
    /// appliquées. Vérifié sur des codes produits par le jeu (28/07/2026, Philippe) :
    /// « Heal Party » en PvP donne 287 et non 3232, « Faux du fermier » donne 2015 dans les deux
    /// contextes. Une variante « (PvP) » doit donc encoder l'id de sa base — sinon le code est
    /// refusé ou mal interprété.
    ///
    /// Repli : une variante dont la base est absente du catalogue garde son propre id (rien de
    /// mieux à proposer). Les noms en doublon sont écrasés plutôt que de lever, contrairement au
    /// ToDictionary qu'appelaient les sites d'encodage.
    /// </summary>
    public static Dictionary<string, int> TemplateIdsByName(IEnumerable<Skill> catalog)
    {
        var all = catalog.ToList();

        var baseIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in all)
            if (!IsPvpVariant(s.Name))
                baseIds[s.Name] = s.Id;

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in all)
            map[s.Name] = baseIds.TryGetValue(BaseName(s.Name), out int id) ? id : s.Id;
        return map;
    }

    // Ids acceptables pour la compétence pickée selon le mode, en cherchant ses variantes
    // (même nom de base) dans le catalogue. Filet : ne renvoie jamais vide (retombe sur la pickée),
    // pour qu'une compétence non splittée reste trouvable quel que soit le mode.
    public static HashSet<int> ResolveGroup(Skill picked, SkillVariantMode mode, IEnumerable<Skill> catalog)
    {
        var baseName = BaseName(picked.Name);
        var siblings = catalog
            .Where(s => string.Equals(BaseName(s.Name), baseName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (siblings.Count == 0) siblings.Add(picked);

        IEnumerable<Skill> selected = mode switch
        {
            SkillVariantMode.PveOnly => siblings.Where(s => !IsPvpVariant(s.Name)),
            SkillVariantMode.PvpOnly => siblings.Where(s => IsPvpVariant(s.Name)),
            _                        => siblings,
        };

        var ids = selected.Select(s => s.Id).ToHashSet();
        if (ids.Count == 0) ids.Add(picked.Id);
        return ids;
    }
}
