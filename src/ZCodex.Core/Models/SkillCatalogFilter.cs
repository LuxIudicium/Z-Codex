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
}
