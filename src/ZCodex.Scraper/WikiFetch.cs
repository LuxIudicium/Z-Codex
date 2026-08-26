using AngleSharp;
using AngleSharp.Dom;

namespace ZCodex.Scraper;

/// <summary>
/// Chargement d'une page du wiki en court-circuitant son cache.
/// </summary>
/// <remarks>
/// Le wiki est derrière un cache (« s-maxage=18000 », soit 5 h) qui sert AU HASARD des copies
/// d'âges différents pour une même adresse. Mesuré le 26 août 2026 : trois chargements successifs
/// de « Game_updates » ont renvoyé tantôt la page contenant la mise à jour du jour, tantôt une
/// page antérieure qui ne la contenait pas. Ce n'est ni le User-Agent ni l'encodage — les deux
/// ont été testés ; le tirage se fait entre nœuds de cache.
///
/// Un paramètre d'adresse inutilisé crée une entrée de cache neuve : la réponse revient avec un
/// « Age » de 0, donc à jour à tous les coups. MediaWiki, lui, ignore ce paramètre.
///
/// ⚠ À réserver aux chargements PEU NOMBREUX. Chaque appel force le serveur à régénérer la page
/// au lieu de servir une copie prête, et le wiki est tenu par une communauté : les ~1300 pages de
/// tables de progression d'un téléchargement de catalogue restent délibérément sur le cache
/// ordinaire (leur retard est borné à 5 h, ce qui est sans commune mesure avec le délai que met
/// le wiki à être édité après une mise à jour du jeu).
/// </remarks>
internal static class WikiFetch
{
    public static Task<IDocument> OpenFreshAsync(
        this IBrowsingContext context, string url, CancellationToken ct = default)
        => context.OpenAsync(Bust(url), ct);

    private static string Bust(string url)
        => $"{url}{(url.Contains('?') ? '&' : '?')}zcx={DateTime.UtcNow.Ticks}";
}
