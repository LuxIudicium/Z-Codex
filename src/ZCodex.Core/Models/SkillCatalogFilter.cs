namespace ZCodex.Core.Models;

/// <summary>
/// Skills présents sur le wiki mais inutilisables dans un build joueur : versions de
/// missions/PNJ temporaires (ex: variantes "(Saul D'Alessio)" de la War in Kryta) et
/// shouts de mission. On les exclut du catalogue (liste de skills + décodage de templates).
/// </summary>
public static class SkillCatalogFilter
{
    public static bool IsBuildUnusable(string name)
    {
        // Normalise l'apostrophe typographique (’) en apostrophe droite (') pour un match stable.
        var n = name.Replace('’', '\'');
        return n.Contains("Saul D'Alessio", System.StringComparison.OrdinalIgnoreCase)
            || n.Contains("Let's Get 'Em", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compétences gardées EN BASE mais masquées du catalogue chargé (décision de Philippe du 10/10/2026 :
    /// Charm Animal (Codex) n'a rien à faire dans Z-Codex). ⚠ Ne PAS les déplacer dans
    /// <see cref="IsBuildUnusable"/> : ce filtre-là s'applique AVANT l'attribution des clés maison 900000+
    /// (SkillUpdateService), qui suit l'ordre alphabétique — retirer « Charm Animal (Codex) » (900004) décalerait
    /// d'un cran toutes les clés suivantes (Elemental Lord 900005/900006 dans AttributeBoostData, builds enregistrés).
    /// </summary>
    public static bool IsHiddenFromCatalog(string name)
        => name == "Charm Animal (Codex)";
}
