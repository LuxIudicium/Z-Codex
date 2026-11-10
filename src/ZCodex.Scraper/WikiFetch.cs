using AngleSharp;
using AngleSharp.Dom;

namespace ZCodex.Scraper;

/// <summary>
/// Chargement d'une page du wiki : hors cache (<see cref="OpenFreshAsync"/>) ou par le cache
/// ordinaire (<see cref="OpenWikiPageAsync"/>), et dans les deux cas CONTRÔLÉE — ce qui revient
/// doit être une vraie page du wiki, sinon <see cref="WikiUnavailableException"/>.
/// </summary>
/// <remarks>
/// <para>Le contrôle : pendant une maintenance (10/11/2026), le wiki répondait « 200 OK » à toutes
/// ses adresses avec une page d'annonce de 6,7 Ko. AngleSharp ne signale rien dans ce cas, ni
/// d'ailleurs sur une erreur 5xx : il bâtit un document à partir de ce qu'il reçoit. Le scraper
/// y cherchait ses tables, n'en trouvait aucune et continuait. Une page du wiki, quel que soit son
/// habillage (bureau ou mobile), porte toujours son contenu dans <c>#mw-content-text</c> — repère
/// déjà lu par l'enrichissement des compétences orphelines, absent de la page de maintenance.</para>
///
/// <para>Le cache : le wiki est derrière un cache (« s-maxage=18000 », soit 5 h) qui sert AU
/// HASARD des copies d'âges différents pour une même adresse. Mesuré le 26 août 2026 : trois
/// chargements successifs de « Game_updates » ont renvoyé tantôt la page contenant la mise à jour
/// du jour, tantôt une page antérieure qui ne la contenait pas. Ce n'est ni le User-Agent ni
/// l'encodage — les deux ont été testés ; le tirage se fait entre nœuds de cache. Un paramètre
/// d'adresse inutilisé crée une entrée de cache neuve : la réponse revient avec un « Age » de 0,
/// donc à jour à tous les coups. MediaWiki, lui, ignore ce paramètre.</para>
///
/// <para>⚠ <see cref="OpenFreshAsync"/> est à réserver aux chargements PEU NOMBREUX. Chaque appel
/// force le serveur à régénérer la page au lieu de servir une copie prête, et le wiki est tenu par
/// une communauté : les ~1300 pages de tables de progression d'un téléchargement de catalogue
/// restent délibérément sur le cache ordinaire (leur retard est borné à 5 h, ce qui est sans
/// commune mesure avec le délai que met le wiki à être édité après une mise à jour du jeu).</para>
/// </remarks>
internal static class WikiFetch
{
    // Au-delà, la page est tenue pour muette. Une page saine répond en quelques secondes, même
    // régénérée hors cache ; un wiki en difficulté, lui, fait attendre (mesuré le 10/11/2026 :
    // ~13 s par requête avant un 503). AngleSharp coupe de lui-même à 45 s, mais sans le dire
    // distinctement — or on veut pouvoir l'annoncer.
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);

    public static Task<IDocument> OpenFreshAsync(
        this IBrowsingContext context, string url, CancellationToken ct = default)
        => OpenCheckedAsync(context, Bust(url), url, ct);

    public static Task<IDocument> OpenWikiPageAsync(
        this IBrowsingContext context, string url, CancellationToken ct = default)
        => OpenCheckedAsync(context, url, url, ct);

    private static async Task<IDocument> OpenCheckedAsync(
        IBrowsingContext context, string address, string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PageTimeout);

        IDocument? doc = null;
        Exception? error = null;
        try { doc = await context.OpenAsync(address, timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { error = ex; }

        // Le délai d'abord : selon le moment où il tombe, AngleSharp lève ou rend un document vide.
        if (timeout.IsCancellationRequested)
            throw new WikiUnavailableException(url, WikiFailure.Timeout, inner: error);
        // Réseau coupé, nom introuvable, connexion refusée : AngleSharp ne lève RIEN et rend un
        // document « 200 » de 0 octet (mesuré le 10/11/2026). Aucune vraie réponse du wiki n'est vide.
        if (doc is null || doc.Source.Length == 0)
            throw new WikiUnavailableException(url, WikiFailure.Unreachable, inner: error);

        int status = (int)doc.StatusCode;
        if (status == 429 || status >= 500)
            throw new WikiUnavailableException(url, WikiFailure.HttpError, status);
        if (doc.QuerySelector("#mw-content-text") is null)
            throw new WikiUnavailableException(url, WikiFailure.NotWikiPage);
        return doc;
    }

    private static string Bust(string url)
        => $"{url}{(url.Contains('?') ? '&' : '?')}zcx={DateTime.UtcNow.Ticks}";
}
