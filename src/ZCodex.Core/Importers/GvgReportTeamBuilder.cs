using System.Globalization;
using System.Text;
using System.Text.Json;
using ZCodex.Core.Models;

namespace ZCodex.Core.Importers;

/// <summary>Une des deux équipes d'un rapport, avec le nom de fichier qui lui revient.</summary>
public sealed class GvgReportTeam
{
    public required string FileName { get; init; }
    public required TeamBuild Build { get; init; }

    /// <summary>Guilde et classement, tels que le site les écrit — de quoi choisir cette équipe-ci.</summary>
    public required string Label { get; init; }

    /// <summary>Barres dont un emplacement au moins est resté vide (cf. GvgReportTeamBuilder).</summary>
    public int IncompleteBars { get; init; }
}

public sealed class GvgReportImport
{
    /// <summary>Les deux équipes, dans l'ordre du site (bleue puis rouge).</summary>
    public List<GvgReportTeam> Teams { get; } = [];

    public required string Date { get; init; }
    public required string Map { get; init; }

    /// <summary>Noms des compétences que le catalogue n'a pas su résoudre — emplacement laissé vide.</summary>
    public List<string> UnknownSkills { get; } = [];
}

/// <summary>
/// Convertit le rapport de match publié par gvg.report en deux team builds, un par guilde.
///
/// Ce que le site donne — et ce qu'il ne donne pas. Les barres ne sont PAS lues dans un template :
/// le site les reconstitue à partir des sorts réellement lancés pendant le match. Une compétence
/// jamais utilisée n'y figure donc pas, et l'emplacement reste vide. C'est un plafond du rapport,
/// pas un défaut de l'import : il n'existe aucune autre source. On compte ces barres trouées pour
/// que l'utilisateur le sache AVANT d'ouvrir le fichier. Les caractéristiques, elles, sont hors
/// de portée : le solveur du site ne les résout jamais entièrement, et il rend des rangs EFFECTIFS
/// (rune comprise), qui ne sont pas les points dépensés qu'attend un .zcx.
/// </summary>
public static class GvgReportTeamBuilder
{
    /// <summary>Séparateur des noms de fichier, tel que l'utilisateur l'écrit à la main.</summary>
    private const string Dot = "·";

    /// <summary>
    /// Retrouve l'identifiant du rapport dans ce que l'utilisateur a collé : l'URL complète de la
    /// page, ou l'identifiant seul. Renvoie <c>null</c> si rien n'y ressemble.
    /// </summary>
    public static string? ReportIdFrom(string urlOrId)
    {
        foreach (var part in urlOrId.Trim().Split(['/', '?', '#', '&'], StringSplitOptions.RemoveEmptyEntries))
            if (Guid.TryParse(part, out var id))
                return id.ToString();

        return null;
    }

    /// <summary>
    /// Construit les deux équipes depuis la charge « overview » de l'API. Renvoie <c>null</c> si le
    /// document n'a pas la forme attendue — le site peut changer, et une équipe à moitié lue serait
    /// pire qu'un message d'erreur.
    /// </summary>
    public static GvgReportImport? FromOverviewJson(string json,
                                                    IReadOnlyDictionary<int, Skill> skillsById,
                                                    string sourceUrl)
    {
        JsonElement overview;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("overview", out var found)) return null;
            overview = found.Clone();
        }
        catch (JsonException) { return null; }

        if (!overview.TryGetProperty("session", out var session)) return null;
        if (!overview.TryGetProperty("teams", out var teams) || teams.GetArrayLength() != 2) return null;
        if (!overview.TryGetProperty("players", out var players)) return null;

        var map = Text(session, "map_name");
        var date = DateTimeOffset
            .FromUnixTimeMilliseconds(session.TryGetProperty("started_at_ts", out var ts) ? ts.GetInt64() : 0)
            .ToLocalTime()
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var first = teams[0];
        var second = teams[1];
        var byName = NameIndex(skillsById);
        var unknown = new List<string>();

        var blue = BuildTeam(first, second, players, skillsById, byName, unknown, date, map, sourceUrl);
        var red = BuildTeam(second, first, players, skillsById, byName, unknown, date, map, sourceUrl);
        if (blue is null || red is null) return null;

        var import = new GvgReportImport { Date = date, Map = map };
        import.Teams.Add(blue);
        import.Teams.Add(red);
        import.UnknownSkills.AddRange(unknown.Distinct().Order());
        return import;
    }

    private static GvgReportTeam? BuildTeam(JsonElement team, JsonElement other, JsonElement players,
                                            IReadOnlyDictionary<int, Skill> skillsById,
                                            IReadOnlyDictionary<string, Skill> byName,
                                            List<string> unknown,
                                            string date, string map, string sourceUrl)
    {
        if (!team.TryGetProperty("team_id", out var teamId)) return null;

        var label = $"{Text(team, "label")} {Dot} rating {Number(team, "rating")}";
        var name = $"{date} {label} VS {Text(other, "label")} {Dot} rating {Number(other, "rating")} - {map}";

        var build = new TeamBuild { Name = name };
        int incomplete = 0;

        foreach (var player in players.EnumerateArray()
                                      .Where(p => Number(p, "team_id") == teamId.GetInt32())
                                      .OrderBy(p => Number(p, "display_order"))
                                      .ThenBy(p => Number(p, "player_number")))
        {
            var character = new CharacterBuild
            {
                PrimaryProfession = (Profession)Number(player, "primary_profession"),
                SecondaryProfession = (Profession)Number(player, "secondary_profession"),
                // Le nom reste « (unnamed) » : c'est le build qui compte, pas qui le portait. Le
                // pseudo va dans l'assignation, à sa place — la ligne du site qui dit qui a joué quoi.
                Assignment = Assignment(player),
            };

            if (player.TryGetProperty("skillbar", out var bar))
            {
                int slot = 0;
                foreach (var entry in bar.EnumerateArray())
                {
                    if (slot >= character.Skills.Length) break;
                    character.Skills[slot++] = Resolve(entry, skillsById, byName, unknown);
                }
            }

            if (character.Skills.Any(s => s is null)) incomplete++;
            build.Characters.Add(character);
        }

        if (build.Characters.Count == 0) return null;

        build.Notes = Notes(sourceUrl, incomplete);
        return new GvgReportTeam
        {
            FileName = FileNameOf(name),
            Build = build,
            Label = label,
            IncompleteBars = incomplete,
        };
    }

    /// <summary>
    /// Résout un emplacement de barre. L'identifiant du jeu suffit presque toujours ; le repli par
    /// nom couvre les rares compétences PvP auxquelles le wiki n'a jamais donné d'identifiant et que
    /// le catalogue range donc sous un numéro maison (Mighty Throw (PvP) et deux autres).
    /// </summary>
    private static Skill? Resolve(JsonElement entry,
                                  IReadOnlyDictionary<int, Skill> skillsById,
                                  IReadOnlyDictionary<string, Skill> byName,
                                  List<string> unknown)
    {
        int id = Number(entry, "skill_id");
        if (id > 0 && skillsById.TryGetValue(id, out var known)) return known;

        var name = Text(entry, "skill_name");
        if (name.Length == 0) return null;   // emplacement vide côté site : rien à signaler

        if (byName.TryGetValue(Normalize(name), out var found)) return found;

        unknown.Add(name.Replace('_', ' '));
        return null;
    }

    private static Dictionary<string, Skill> NameIndex(IReadOnlyDictionary<int, Skill> skillsById)
    {
        var index = new Dictionary<string, Skill>();
        foreach (var skill in skillsById.Values)
            index[Normalize(skill.Name)] = skill;
        return index;
    }

    // Les deux catalogues écrivent les mêmes compétences différemment : le site remplace les
    // espaces par des soulignés, suffixe « _PvP » au lieu de « (PvP) », et laisse tomber les
    // apostrophes une fois sur deux (« Harriers_Grasp » mais « Harrier's_Haste »).
    private static string Normalize(string name)
    {
        var text = name.Replace('_', ' ').Trim();
        if (text.EndsWith(" PvP", StringComparison.OrdinalIgnoreCase))
            text = string.Concat(text.AsSpan(0, text.Length - 4), " (PvP)");

        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (var c in text)
        {
            if (c is '\'' or '’' or '"') continue;
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string Assignment(JsonElement player)
    {
        // « Canadian Sauce (1) » : le numéro est déjà porté par la position dans l'équipe.
        var label = Text(player, "label");
        int paren = label.LastIndexOf('(');
        if (paren > 0) label = label[..paren];

        label = label.Trim();
        return label.Length == 0 ? "(unassigned)" : label;
    }

    private static string Notes(string sourceUrl, int incomplete)
    {
        var sb = new StringBuilder();
        sb.Append(AppLanguage.IsFr ? "Importé depuis " : "Imported from ").Append(sourceUrl);

        if (incomplete > 0)
            sb.Append(AppLanguage.IsFr
                ? $"\n{incomplete} barre(s) incomplète(s) : le rapport ne connaît que les compétences réellement lancées pendant le match."
                : $"\n{incomplete} incomplete bar(s): the report only knows the skills actually cast during the match.");

        return sb.ToString();
    }

    /// <summary>Nom du build débarrassé de ce que Windows refuse dans un nom de fichier.</summary>
    private static string FileNameOf(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '-' : c);

        return sb.ToString().Trim();
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    // Le contrôle du type est OBLIGATOIRE : le site écrit null (et non 0) dans l'emplacement vide
    // d'une barre incomplète, et TryGetInt32 LÈVE sur un null au lieu de renvoyer false.
    private static int Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int n) ? n : 0;
}
