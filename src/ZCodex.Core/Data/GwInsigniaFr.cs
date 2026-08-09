using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

// Noms FR des insignes (calculateur d'armure). Source officielle : gwiki.fr/wiki/Insigne (relevé
// le 2026-07-21). Mappés par ModId (cf. GwEquipmentModDetails.ByModId, entrées « … Insignia »).
// Le nom EN reste la clé interne ; ceci n'est qu'un nom court d'affichage.
public static class GwInsigniaFr
{
    private static readonly IReadOnlyDictionary<int, string> FrByModId = new Dictionary<int, string>
    {
        [290] = "Survivant",             [291] = "Rayonnement",       [292] = "Robuste",
        [293] = "Agitateur",             [294] = "Bénédiction",       [295] = "Héraut",
        [296] = "Factionnaire",          [297] = "Avant-garde",       [298] = "Infiltré",
        [299] = "Saboteur",              [300] = "Traqueur nocturne", [301] = "Virtuose",
        [302] = "Sang",                  [303] = "Persécuteur",       [304] = "Dentelle",
        [305] = "Maître des serviteurs", [306] = "Destructeur",       [307] = "Hydromancie",
        [308] = "Géomancie",             [309] = "Pyromancie",        [310] = "Aéromancie",
        [311] = "Vagabond",              [312] = "Disciple",          [313] = "Chevalier",
        [314] = "Lieutenant",            [315] = "Poing-de-fer",      [316] = "Dreadnaught",
        [317] = "Sentinelle",            [318] = "Givre",             [319] = "Bûcher",
        [320] = "Tonnerre",              [321] = "Éclaireur",         [322] = "Chaman",
        [323] = "Forge du fantôme",      [324] = "Mystique",          [358] = "Artisan",
        [359] = "Prodige",               [360] = "Fossoyeur",         [361] = "Prismatique",
        [362] = "Anachorète",            [363] = "Terrestre",         [364] = "Belluaire",
        [365] = "Marche-vent",           [366] = "Oubli",             [367] = "Centurion",
    };

    /// <summary>Nom court affiché de l'insigne : FR (gwiki) en mode FR si connu, sinon le court EN fourni.</summary>
    public static string ShortName(int modId, string englishShort)
        => AppLanguage.IsFr ? FrByModId.GetValueOrDefault(modId, englishShort) : englishShort;

    /// <summary>Idem depuis le nom COMPLET du catalogue (« Survivor Insignia » → « Survivant » / « Survivor »).
    /// Utilisé par l'éditeur d'équipement pour afficher les mêmes libellés que le calculateur d'armure.</summary>
    public static string ShortNameFromFull(int modId, string fullName)
        => ShortName(modId, fullName.EndsWith(" Insignia", StringComparison.Ordinal)
            ? fullName[..^9].TrimEnd() : fullName);

    // Forme employee dans un NOM COMPOSE d'objet : groupe prepositionnel, PAS un adjectif accorde
    // (Philippe 27/07 : « Robe krytienne DU SURVIVANT », « Gantelets de dragon DE DREADNAUGHT »).
    // L'article varie (du / de la / de l' / de) et n'est pas derivable du nom court -> defaut
    // mecanique « de <minuscule> » (elision d') + les formes confirmees.
    // Les 45 formes ont ete VERIFIEES ET CORRIGEES par Philippe (27/07) : source de verite
    // docs/equipment_insignia_fr.md. Certaines sont des ADJECTIFS NUS (« robuste »,
    // « mystique », « prismatique », « terrestre », « poing-de-fer ») : pas de preposition.
    private static readonly IReadOnlyDictionary<int, string> ComposedFrByModId = new Dictionary<int, string>
    {
        [290] = "du survivant",  // Survivor Insignia
        [291] = "du rayonnement",  // Radiant Insignia
        [292] = "robuste",  // Stalwart Insignia
        [293] = "d'agitateur",  // Brawler's Insignia
        [294] = "de la bénédiction",  // Blessed Insignia
        [295] = "de héraut",  // Herald's Insignia
        [296] = "de factionnaire",  // Sentry's Insignia
        [297] = "d'avant-garde",  // Vanguard's Insignia
        [298] = "de l'infiltré",  // Infiltrator's Insignia
        [299] = "de saboteur",  // Saboteur's Insignia
        [300] = "de traqueur nocturne",  // Nightstalker's Insignia
        [301] = "de virtuose",  // Virtuoso's Insignia
        [302] = "de Sang",  // Bloodstained Insignia
        [303] = "de persécuteur",  // Tormentor's Insignia
        [304] = "de dentelle",  // Bonelace Insignia
        [305] = "de Maître des serviteurs",  // Minion Master's Insignia
        [306] = "de destructeur",  // Blighter's Insignia
        [307] = "d'hydromancie",  // Hydromancer's Insignia
        [308] = "de géomancie",  // Geomancer's Insignia
        [309] = "de pyromancie",  // Pyromancer's Insignia
        [310] = "d'aéromancie",  // Aeromancer's Insignia
        [311] = "de vagabond",  // Wanderer's Insignia
        [312] = "de disciple",  // Disciple's Insignia
        [313] = "de chevalier",  // Knight's Insignia
        [314] = "de lieutenant",  // Lieutenant's Insignia
        [315] = "poing-de-fer",  // Stonefist Insignia
        [316] = "de dreadnaught",  // Dreadnought Insignia
        [317] = "de sentinelle",  // Sentinel's Insignia
        [318] = "de givre",  // Frostbound Insignia
        [319] = "du bûcher",  // Pyrebound Insignia
        [320] = "de tonnerre",  // Stormbound Insignia
        [321] = "d'éclaireur",  // Scout's Insignia
        [322] = "de chaman",  // Shaman's Insignia
        [323] = "de forge du fantôme",  // Ghost Forge Insignia
        [324] = "mystique",  // Mystic's Insignia
        [358] = "d'artisan",  // Artificer's Insignia
        [359] = "de prodige",  // Prodigy's Insignia
        [360] = "du fossoyeur",  // Undertaker's Insignia
        [361] = "prismatique",  // Prismatic Insignia
        [362] = "d'anachorète",  // Anchorite's Insignia
        [363] = "terrestre",  // Earthbound Insignia
        [364] = "de belluaire",  // Beastmaster's Insignia
        [365] = "du Marche-vent",  // Windwalker Insignia
        [366] = "de l'oubli",  // Forsaken Insignia
        [367] = "de centurion",  // Centurion's Insignia
    };

    /// <summary>Forme FR de l'insigne dans un nom d'objet compose (« du survivant »), ou null.</summary>
    public static string? ComposedForm(int modId) => ComposedFrByModId.GetValueOrDefault(modId);
}
