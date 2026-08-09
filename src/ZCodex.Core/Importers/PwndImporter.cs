using ZCodex.Core.Models;
using ZCodex.Core.Templates;

namespace ZCodex.Core.Importers;

public static class PwndImporter
{
    public const string Extension = ".pwnd";
    private const string Magic      = "pwnd";
    private const string Terminator = "CCg";

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
        if (!text.StartsWith(Magic, StringComparison.OrdinalIgnoreCase)) return null;

        int blockStart = text.IndexOf('>');
        int blockEnd   = text.LastIndexOf('<');
        if (blockStart < 0 || blockEnd <= blockStart) return null;

        // Strip all whitespace from the encoded block
        var content = new string(
            text[(blockStart + 1)..blockEnd].Where(c => !char.IsWhiteSpace(c)).ToArray());

        var characters = new List<CharacterBuild>();
        int pos = 0;

        while (pos < content.Length)
        {
            int delimIdx = content.IndexOf(Terminator, pos, StringComparison.Ordinal);
            if (delimIdx < 0) break;

            // Entry = [1-char prefix][template code][trailing A padding]
            var entry = content[pos..delimIdx];
            if (entry.Length > 1)
            {
                var templateCode = entry[1..]; // drop prefix character
                var build = GwTemplateCodec.Decode(templateCode, skillsById);
                characters.Add(build ?? new CharacterBuild());
            }

            pos = delimIdx + Terminator.Length;
        }

        if (characters.Count == 0) return null;

        return new TeamBuild { Name = name, Characters = characters };
    }
}
