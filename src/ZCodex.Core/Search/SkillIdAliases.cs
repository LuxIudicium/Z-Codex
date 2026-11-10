using ZCodex.Core.Models;

namespace ZCodex.Core.Search;

/// <summary>
/// Numéros de compétence que l'on peut LIRE (dans un fichier, un code de template, un document de
/// session) alors que le catalogue range la compétence sous un autre numéro. Sans eux, la case
/// était vidée à l'ouverture, puis effacée au premier enregistrement.
/// </summary>
/// <remarks>
/// <para>Deux familles, relevées le 10/11/2026 sur les 273 fichiers de Philippe (30 touchés) :</para>
/// <para>• Les 20 compétences d'allégeance. Le jeu leur donne un id par variante — Luxon 1948–1957 et
/// 2051, Kurzick 2091–2100 (wiki « Skill_template_format/Skill_list », qui écrit chaque variante sous
/// son nom suffixé) — mais le catalogue les range sous des clés maison 900000+ : le scraper attendait
/// deux fois le même nom et ne les a jamais appariées. Corriger le scraper RENUMÉROTERAIT ces
/// compétences et casserait les fichiers enregistrés depuis (et les clés citées en dur, cf.
/// AttributeBoostData) : on traduit donc à la lecture.</para>
/// <para>• Les anciennes clés maison 5000–5002, d'un compteur « 5000++ » abandonné (cf.
/// SkillUpdateService.AssignIdentityKeys) : ces trois variantes PvP ont aujourd'hui leur vrai id.</para>
/// <para>Résolution par NOM, jamais par numéro : la cible suit le catalogue quel que soit le numéro
/// qu'il lui donne. Un numéro que le catalogue connaît déjà n'est jamais détourné.</para>
/// </remarks>
public static class SkillIdAliases
{
    // Vrais numéros du JEU : servent aussi dans l'autre sens, à l'export (cf. TryGetGameId).
    private static readonly Dictionary<int, string> AllegianceNameByGameId = new()
    {
        // Allégeance — Luxon
        [1948] = "Shadow Sanctuary (Luxon)",
        [1949] = "Ether Nightmare (Luxon)",
        [1950] = "Signet of Corruption (Luxon)",
        [1951] = "Elemental Lord (Luxon)",
        [1952] = "Selfless Spirit (Luxon)",
        [1953] = "Triple Shot (Luxon)",
        [1954] = "\"Save Yourselves!\" (Luxon)",
        [1955] = "Aura of Holy Might (Luxon)",
        [1957] = "Spear of Fury (Luxon)",
        [2051] = "Summon Spirits (Luxon)",
        // Allégeance — Kurzick
        [2091] = "Shadow Sanctuary (Kurzick)",
        [2092] = "Ether Nightmare (Kurzick)",
        [2093] = "Signet of Corruption (Kurzick)",
        [2094] = "Elemental Lord (Kurzick)",
        [2095] = "Selfless Spirit (Kurzick)",
        [2096] = "Triple Shot (Kurzick)",
        [2097] = "\"Save Yourselves!\" (Kurzick)",
        [2098] = "Aura of Holy Might (Kurzick)",
        [2099] = "Spear of Fury (Kurzick)",
        [2100] = "Summon Spirits (Kurzick)",
    };

    // Anciennes clés maison (compteur « 5000++ ») : à LIRE seulement, jamais à écrire — ce ne sont
    // pas des numéros du jeu.
    private static readonly Dictionary<int, string> LegacyNameByKey = new()
    {
        [5000] = "\"Help!\" (PvP)",
        [5001] = "Mighty Throw (PvP)",
        [5002] = "Farmer's Scythe (PvP)",
    };

    private static readonly Dictionary<string, int> AllegianceGameIdByName =
        AllegianceNameByGameId.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    /// <summary>
    /// Numéro du JEU d'une compétence d'allégeance, pour un code de template. Le catalogue range ces
    /// 20 compétences sous des clés maison 900000+ que le jeu ne connaît pas : un code qui les
    /// porte s'ouvre en jeu avec une case vide (constaté le 10/11/2026 : 900000 écrit pour
    /// "Save Yourselves!" (Kurzick) au lieu de 2097).
    /// </summary>
    public static bool TryGetGameId(string skillName, out int gameId)
        => AllegianceGameIdByName.TryGetValue(skillName, out gameId);

    /// <summary>
    /// Table numéro → compétence du catalogue, complétée des alias. À utiliser partout où l'on
    /// résout des numéros LUS ; une compétence résolue par alias garde son numéro de catalogue, que
    /// le prochain enregistrement écrira.
    /// </summary>
    public static Dictionary<int, Skill> ById(IEnumerable<Skill> catalog)
    {
        var byId = catalog.ToDictionary(s => s.Id, s => s);
        if (byId.Count == 0) return byId;

        var byName = new Dictionary<string, Skill>(StringComparer.Ordinal);
        foreach (var s in byId.Values) byName.TryAdd(s.Name, s);

        foreach (var (alias, name) in AllegianceNameByGameId.Concat(LegacyNameByKey))
            if (!byId.ContainsKey(alias) && byName.TryGetValue(name, out var skill))
                byId[alias] = skill;
        return byId;
    }
}
