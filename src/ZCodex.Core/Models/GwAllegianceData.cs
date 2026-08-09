namespace ZCodex.Core.Models;

// Compétences d'allégeance (Kurzick/Luxon) : 10 skills × 2 variantes = 20 au total.
// Stockées avec Profession.None et Attribute "Allegiance rank" (skills PvE title-track),
// mais en réalité verrouillées à une profession précise → doivent déclencher une violation
// si ni la PR ni la SEC du personnage ne correspondent.
// Réf : https://wiki.guildwars.com/wiki/Allegiance_skill
public static class GwAllegianceData
{
    public const string AttributeName = "Allegiance rank";

    // Clé = nom de base (sans le suffixe " (Kurzick)"/" (Luxon)"), tel que stocké en base
    // (note : "Save Yourselves!" est stockée avec ses guillemets).
    private static readonly Dictionary<string, Profession> _byBaseName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["\"Save Yourselves!\""] = Profession.Warrior,
        ["Triple Shot"]          = Profession.Ranger,
        ["Selfless Spirit"]      = Profession.Monk,
        ["Signet of Corruption"] = Profession.Necromancer,
        ["Ether Nightmare"]      = Profession.Mesmer,
        ["Elemental Lord"]       = Profession.Elementalist,
        ["Shadow Sanctuary"]     = Profession.Assassin,
        ["Summon Spirits"]       = Profession.Ritualist,
        ["Spear of Fury"]        = Profession.Paragon,
        ["Aura of Holy Might"]   = Profession.Dervish,
    };

    // Profession requise par une skill d'allégeance, ou None si la skill n'en est pas une.
    public static Profession RequiredProfession(Skill skill)
    {
        if (!string.Equals(skill.Attribute, AttributeName, StringComparison.OrdinalIgnoreCase))
            return Profession.None;
        return _byBaseName.TryGetValue(StripVariant(skill.Name), out var p) ? p : Profession.None;
    }

    private static string StripVariant(string name)
    {
        if (name.EndsWith(" (Kurzick)", StringComparison.OrdinalIgnoreCase)) return name[..^10];
        if (name.EndsWith(" (Luxon)",   StringComparison.OrdinalIgnoreCase)) return name[..^8];
        return name;
    }
}
