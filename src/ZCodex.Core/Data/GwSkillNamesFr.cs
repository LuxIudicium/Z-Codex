using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Noms FRANÇAIS des compétences PvE que la mise à jour du catalogue (gwiki.fr) ne sait pas fournir, posés
/// au chargement du catalogue, par-dessus ce que porte la base (demande de Philippe du 10/10/2026).
///
/// - Variantes d'allégeance (Kurzick/Luxon) : gwiki.fr a bien les 20 pages (titres relevés sur
///   gwiki.fr/wiki/Compétence_PvE), mais leur lien interlangue donne le nom anglais SANS suffixe (et
///   « Summom Spirits », coquille), et leur id de jeu n'est pas la clé maison 900000+ de la base : la
///   jointure du scraper les manquait toutes. Le nom est posé ici dès le chargement ; la mise à jour du
///   catalogue retrouve en plus leur page par ce TITRE (GwikiFrScraper.ByKnownFrenchTitle) et en tire la
///   description, la caractéristique et le type français.
/// - Soul Ignition : la page gwiki.fr existe mais n'est pas traduite. Nom relevé en jeu par Philippe
///   (le client français fait foi).
///
/// Clé = nom anglais de la base. Une entrée ici gagne toujours : ce sont des noms vérifiés.
/// </summary>
public static class GwSkillNamesFr
{
    public static readonly IReadOnlyDictionary<string, string> ByEnglishName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["\"Save Yourselves!\" (Kurzick)"]   = "\"Sauvez votre peau !\" (Kurzick)",
            ["\"Save Yourselves!\" (Luxon)"]     = "\"Sauvez votre peau !\" (Luxon)",
            ["Aura of Holy Might (Kurzick)"]     = "Aura de la puissance sacrée (Kurzick)",
            ["Aura of Holy Might (Luxon)"]       = "Aura de la puissance sacrée (Luxon)",
            ["Elemental Lord (Kurzick)"]         = "Seigneur élémentaire (Kurzick)",
            ["Elemental Lord (Luxon)"]           = "Seigneur élémentaire (Luxon)",
            ["Ether Nightmare (Kurzick)"]        = "Cauchemar éthéré (Kurzick)",
            ["Ether Nightmare (Luxon)"]          = "Cauchemar éthéré (Luxon)",
            ["Selfless Spirit (Kurzick)"]        = "Esprit altruiste (Kurzick)",
            ["Selfless Spirit (Luxon)"]          = "Esprit altruiste (Luxon)",
            ["Shadow Sanctuary (Kurzick)"]       = "Sanctuaire de l'ombre (Kurzick)",
            ["Shadow Sanctuary (Luxon)"]         = "Sanctuaire de l'ombre (Luxon)",
            ["Signet of Corruption (Kurzick)"]   = "Sceau de corruption (Kurzick)",
            ["Signet of Corruption (Luxon)"]     = "Sceau de corruption (Luxon)",
            ["Spear of Fury (Kurzick)"]          = "Javelot de furie (Kurzick)",
            ["Spear of Fury (Luxon)"]            = "Javelot de furie (Luxon)",
            ["Summon Spirits (Kurzick)"]         = "Invocation des esprits (Kurzick)",
            ["Summon Spirits (Luxon)"]           = "Invocation des esprits (Luxon)",
            ["Triple Shot (Kurzick)"]            = "Triple tir (Kurzick)",
            ["Triple Shot (Luxon)"]              = "Triple tir (Luxon)",
            ["Soul Ignition"]                    = "Embrasement de l'âme",
        };

    /// <summary>Pose les noms de la table sur le catalogue chargé. À appeler AVANT la dérivation des noms
    /// « (PvP) », qui part du nom français de la compétence de base.</summary>
    public static void Apply(IEnumerable<Skill> skills)
    {
        foreach (var s in skills)
            if (ByEnglishName.TryGetValue(s.Name, out var fr))
                s.NameFr = fr;
    }
}
