using System.Net.Http;
using System.Text.Json;

namespace ZCodex.Scraper;

/// <summary>Une version publiée de Z-Codex, et la page où la télécharger.</summary>
public sealed record AppRelease(Version Version, string PageUrl);

/// <summary>
/// Dernière version de Z-Codex publiée, telle qu'annoncée par les Releases GitHub.
///
/// Le pendant de <see cref="GameUpdateChecker"/>, mais pour l'application elle-même : celui-ci
/// surveille les mises à jour DU JEU, celui-là les mises à jour DE L'OUTIL. Ici on se contente
/// de DÉTECTER et d'ouvrir la page de téléchargement dans le navigateur — l'application ne
/// télécharge ni n'exécute d'installateur toute seule. C'est un choix, pas une étape manquante :
/// « une application qui télécharge un .exe et le lance » est le comportement même d'un logiciel
/// malveillant, et l'installateur de Z-Codex n'est pas signé (il ferait sursauter les antivirus).
/// </summary>
public static class AppVersionChecker
{
    // L'API des Releases, et non la page HTML des releases : c'est un contrat stable, et surtout
    // « /latest » écarte d'office les BROUILLONS et les PRÉVERSIONS. On peut donc préparer une
    // version sur GitHub sans que le parc installé en soit averti — c'est le filet de sécurité
    // qui permet de tester une livraison avant de la rendre publique.
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/LuxIudicium/Z-Codex/releases/latest";

    // Page de repli : si la release ne déclare pas son URL, on envoie sur la liste des versions
    // plutôt que nulle part.
    private const string ReleasesPageUrl = "https://github.com/LuxIudicium/Z-Codex/releases";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // 15 s et non les 30 s des icônes : c'est une vérification d'arrière-plan au démarrage,
        // personne ne l'attend. Mieux vaut renoncer que retarder quoi que ce soit.
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub répond 403 à toute requête d'API sans User-Agent. Ce n'est pas de la politesse,
        // c'est une condition d'accès.
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex");
        return http;
    }

    /// <summary>
    /// Null si rien n'est publié, si GitHub est injoignable ou si l'étiquette de version est
    /// illisible. L'appelant traite ces trois cas de la même façon : silence.
    /// </summary>
    public static async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        using var res = await Http.GetAsync(LatestReleaseUrl, ct);
        // 404 tant qu'aucune version n'est publiée sur le dépôt : cas NORMAL, pas une panne.
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var version = ParseTag(root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null);
        if (version == null) return null;

        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        return new AppRelease(version, string.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url);
    }

    /// <summary>« v1.0.1 » → 1.0.1. Null si l'étiquette n'est pas un numéro de version.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        return Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var v) ? Normalize(v) : null;
    }

    /// <summary>
    /// Ramène un numéro à Majeur.Mineur.Correctif, et un « v1.1 » sans correctif à 1.1.0.
    ///
    /// À appliquer aux DEUX côtés avant de comparer, parce que les deux ne comptent pas leurs
    /// chiffres pareil : l'assembly s'annonce « 1.0.0.0 » là où l'étiquette git dit « v1.0.0 »,
    /// et .NET tient une révision ABSENTE pour inférieure à une révision nulle — donc 1.0.0
    /// &lt; 1.0.0.0, alors que l'humain lit deux fois le même numéro. Avec la comparaison
    /// « plus récent que » utilisée ici, l'oubli ne produirait pas de fausse alerte : il rendrait
    /// simplement l'égalité accidentelle plutôt qu'exacte, et laisserait « 1.0.1.0 » s'afficher
    /// à l'écran là où l'utilisateur attend « 1.0.1 ». On normalise donc une fois pour toutes,
    /// ici, plutôt que de faire dépendre la justesse du sens du signe au point d'appel.
    /// </summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
