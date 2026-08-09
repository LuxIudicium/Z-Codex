using ZCodex.Core.Models;
using ZCodex.Core.Templates;

namespace ZCodex.Core.Importers;

/// <summary>
/// Assemble une équipe Z-Codex à partir d'un dossier d'équipe des build packs PvXwiki : un
/// fichier .txt par barre, nommé « &lt;n&gt; Standard.txt ».
///
/// Les dossiers de PLUS DE 8 barres ne sont PAS convertis. Le pack aplatit dans un même dossier
/// les membres de l'équipe ET les barres alternatives de la page PvX, sans rien qui permette de
/// les distinguer : « 7 Hero Soul Taker Mesmerway » en compte 11 (8 membres + 3 variantes),
/// « UW Triple Melee » en compte 28. Prendre les 8 premières fabriquerait des équipes fausses en
/// silence — on laisse ces dossiers en .txt, qui restent parfaitement ouvrables un par un.
/// </summary>
public static class PvxTeamBuilder
{
    /// <summary>Taille d'un groupe GW1, et donc d'une équipe Z-Codex.</summary>
    public const int MaxMembers = 8;

    /// <summary>
    /// Renvoie l'équipe, ou <c>null</c> si le dossier est vide, dépasse 8 barres, ou contient une
    /// barre illisible — mieux vaut pas d'équipe qu'une équipe amputée d'un membre.
    /// </summary>
    public static TeamBuild? FromFolder(string folder, IReadOnlyDictionary<int, Skill> skillsById)
    {
        var files = Directory.GetFiles(folder, "*" + SkillTemplateImporter.Extension);
        if (files.Length == 0 || files.Length > MaxMembers) return null;

        var team = new TeamBuild { Name = Path.GetFileName(folder) };

        foreach (var file in files.OrderBy(LeadingNumber)
                                  .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            CharacterBuild? character;
            try { character = GwTemplateCodec.Decode(File.ReadAllText(file).Trim(), skillsById); }
            catch { return null; }
            if (character is null) return null;

            character.Name = Path.GetFileNameWithoutExtension(file);
            team.Characters.Add(character);
        }

        return team;
    }

    // « 10 Standard » doit venir après « 9 Standard » : un tri purement textuel les inverserait,
    // et l'ordre des membres est la seule chose que le pack nous dise de la composition.
    private static int LeadingNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        int i = 0;
        while (i < name.Length && char.IsAsciiDigit(name[i])) i++;
        return i > 0 && int.TryParse(name[..i], out int n) ? n : int.MaxValue;
    }
}
