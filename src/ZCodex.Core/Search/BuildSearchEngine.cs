using System.IO;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Core.Serialization;

namespace ZCodex.Core.Search;

// Périmètres de recherche (cases à cocher de la modale). Combinables.
[Flags]
public enum SearchScope
{
    None = 0,
    SkillTemplates = 1, // .txt
    TeamBuilds = 2,     // .pn3 / .pwnd
}

public static class BuildSearchEngine
{
    // Aplati un perso et toutes ses variantes (récursif) — une variante est un perso complet.
    public static IEnumerable<CharacterBuild> Flatten(CharacterBuild c)
    {
        yield return c;
        foreach (var v in c.Variants)
            foreach (var f in Flatten(v))
                yield return f;
    }

    // Un teambuild matche si AU MOINS UN de ses persos (variantes incluses) satisfait tous les critères.
    public static bool TeamMatches(TeamBuild tb, BuildSearchQuery query) =>
        tb.Characters.SelectMany(Flatten).Any(query.Matches);

    // Scan récursif depuis root : renvoie les chemins des fichiers qui matchent,
    // templates de skills (.txt) d'abord, puis teambuilds (.pn3/.pwnd).
    public static List<string> SearchRoot(
        string root,
        BuildSearchQuery query,
        SearchScope scope,
        IReadOnlyDictionary<int, Skill> skillsById)
    {
        var templates = new List<string>();
        var teambuilds = new List<string>();
        if (query.IsEmpty || scope == SearchScope.None || !Directory.Exists(root))
            return templates;

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            bool isTemplate = ext == SkillTemplateImporter.Extension;
            bool isTeam = TeamBuildSerializer.IsNativeExtension(ext) || ext == PwndImporter.Extension;

            if (isTemplate && !scope.HasFlag(SearchScope.SkillTemplates)) continue;
            if (isTeam && !scope.HasFlag(SearchScope.TeamBuilds)) continue;
            if (!isTemplate && !isTeam) continue;

            TeamBuild? tb = null;
            try
            {
                tb = ext switch
                {
                    PwndImporter.Extension         => PwndImporter.Import(path, skillsById),
                    SkillTemplateImporter.Extension => SkillTemplateImporter.Import(path, skillsById),
                    _                               => TeamBuildSerializer.Load(path, skillsById),
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Search load '{path}': {ex.Message}");
            }
            if (tb == null) continue;

            if (TeamMatches(tb, query))
                (isTemplate ? templates : teambuilds).Add(path);
        }

        templates.AddRange(teambuilds); // templates d'abord, puis teambuilds
        return templates;
    }
}
