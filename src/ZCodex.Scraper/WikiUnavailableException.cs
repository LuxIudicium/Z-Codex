namespace ZCodex.Scraper;

/// <summary>Pourquoi une page du wiki n'a pas pu servir (affiché à l'utilisateur pendant les essais).</summary>
public enum WikiFailure
{
    Unreachable,    // pas de connexion, requête interrompue
    Timeout,        // pas de réponse dans le temps imparti (WikiFetch.PageTimeout)
    HttpError,      // 429 ou 5xx — cf. StatusCode
    NotWikiPage,    // réponse « 200 » qui n'est pas une page du wiki : maintenance, réponse vide
    NoData,         // vraie page du wiki, mais sans les données attendues (table absente…)
}

/// <summary>
/// Le wiki n'a pas rendu ce qu'il fallait : la mise à jour du catalogue S'ARRÊTE plutôt que
/// d'enregistrer un catalogue troué.
/// </summary>
/// <remarks>
/// Vécu en novembre 2026. Pendant une maintenance, le wiki servait à TOUTES ses adresses une même
/// page d'annonce avec un code 200 : aucune erreur, des tables vides, et le catalogue d'un joueur
/// remplacé par ce vide — tous ses builds affichaient des compétences inconnues. Le 08/11, une
/// seule page ratée (Ritualist) avait de même laissé un catalogue sans ritualistes, marqué complet.
/// </remarks>
public sealed class WikiUnavailableException : Exception
{
    /// <summary>Adresse de la page en cause ; vide quand c'est le filet de perte qui a tranché.</summary>
    public string Page { get; } = string.Empty;

    public WikiFailure Failure { get; }

    /// <summary>Code HTTP reçu, pour <see cref="WikiFailure.HttpError"/>.</summary>
    public int? StatusCode { get; }

    /// <summary>Filet de perte : les compétences du catalogue actuel absentes du nouveau.</summary>
    public IReadOnlyList<string> WouldLose { get; } = [];

    public WikiUnavailableException(string page, WikiFailure failure, int? statusCode = null, Exception? inner = null)
        : base($"{Describe(failure, statusCode)} : {page}", inner)
    {
        Page = page;
        Failure = failure;
        StatusCode = statusCode;
    }

    public WikiUnavailableException(IReadOnlyList<string> wouldLose)
        : base($"Le nouveau catalogue perdrait {wouldLose.Count} compétences") => WouldLose = wouldLose;

    // Texte des journaux (le message affiché, lui, est traduit par l'interface).
    private static string Describe(WikiFailure failure, int? statusCode) => failure switch
    {
        WikiFailure.Timeout     => "Pas de réponse du wiki dans le temps imparti",
        WikiFailure.HttpError   => $"Réponse {statusCode} du wiki",
        WikiFailure.NotWikiPage => "Pas une page du wiki (maintenance ?)",
        WikiFailure.NoData      => "Page du wiki sans les données attendues",
        _                       => "Wiki injoignable",
    };
}

/// <summary>
/// Une page obligatoire a échoué et va être redemandée : raison, secondes avant le nouvel essai
/// (0 = essai en cours), et numéro de ce nouvel essai sur le nombre prévu (« 1/2 »). Un avis
/// <c>null</c> signale que la page est finalement passée.
/// </summary>
public sealed record WikiRetryNotice(WikiFailure Failure, int? StatusCode, int SecondsLeft, int Retry, int Retries);
