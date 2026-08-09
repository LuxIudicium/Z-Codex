using ZCodex.Core.Models;
using ZCodex.Core.Templates;

namespace ZCodex.Core.Importers;

// Skill templates paw·ned² : fichier .txt contenant un unique code de template GW1
// (une ligne, sans en-tête). Décodé en un build à un seul personnage.
public static class SkillTemplateImporter
{
    public const string Extension = ".txt";

    public static TeamBuild? Import(string filePath, IReadOnlyDictionary<int, Skill> skillsById)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            var name = Path.GetFileNameWithoutExtension(filePath);
            return Parse(text, name, skillsById);
        }
        catch { return null; }
    }

    public static TeamBuild? Parse(string text, string name, IReadOnlyDictionary<int, Skill> skillsById)
    {
        var code = text.Trim();
        var character = GwTemplateCodec.Decode(code, skillsById);
        if (character == null) return null;
        character.Name = name;
        return new TeamBuild { Name = name, Characters = { character } };
    }
}
