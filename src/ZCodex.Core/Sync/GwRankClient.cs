using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZCodex.Core.Models;
using ZCodex.Core.Serialization;

namespace ZCodex.Core.Sync;

/// <summary>Issue d'un appel GWRank. Le code HTTP seul ne suffit pas à l'appelant : il doit
/// distinguer « jeton absent/invalide » (l'utilisateur doit agir) de « serveur injoignable »
/// (réessayer plus tard) — deux messages très différents dans l'interface.</summary>
public enum GwRankStatus
{
    Ok,
    /// <summary>Aucun jeton configuré : inutile d'appeler le réseau.</summary>
    NoToken,
    /// <summary>401 — jeton refusé par le serveur.</summary>
    Unauthorized,
    /// <summary>403 — build d'autrui, ou écriture non autorisée.</summary>
    Forbidden,
    /// <summary>404 — build inconnu du serveur.</summary>
    NotFound,
    /// <summary>422 — le serveur refuse le document (règles de format).</summary>
    Rejected,
    /// <summary>412 — le build a changé sur le serveur depuis la lecture dont vient l'empreinte
    /// envoyée en <c>If-Match</c>. RIEN n'a été modifié côté serveur : c'est le garde-fou qui
    /// évite d'écraser en silence le travail d'une autre machine.</summary>
    Conflict,
    /// <summary>Panne réseau, DNS, TLS, délai dépassé.</summary>
    Offline,
    /// <summary>429 — le serveur demande de lever le pied. Distinct d'une panne : il va très
    /// bien, c'est nous qui insistons trop.</summary>
    RateLimited,
    /// <summary>Tout le reste (5xx, réponse illisible).</summary>
    ServerError,
}

/// <summary>Ce qu'on peut espérer voir passer tout seul si on réessaie.</summary>
public static class GwRankStatusExtensions
{
    /// <summary>
    /// Vrai pour les pannes de PASSAGE : serveur qui redémarre, réseau qui hoquette, quota
    /// momentané. Faux pour tout ce qui vient du contenu ou des droits — réessayer un jeton
    /// refusé ou un document invalide donnerait exactement le même refus, en plus lent.
    /// </summary>
    public static bool IsTransient(this GwRankStatus s)
        => s is GwRankStatus.Offline or GwRankStatus.ServerError or GwRankStatus.RateLimited;
}

/// <summary>Résultat d'un appel : un statut, la charge utile si succès, un message si échec.</summary>
public sealed record GwRankResult<T>(GwRankStatus Status, T? Value, string? Message)
{
    public bool IsOk => Status == GwRankStatus.Ok;

    public static GwRankResult<T> Ok(T value) => new(GwRankStatus.Ok, value, null);
    public static GwRankResult<T> Fail(GwRankStatus s, string? m = null) => new(s, default, m);
}

public sealed class GwRankPagination
{
    public int Page { get; set; }
    public int PerPage { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>Résumé renvoyé par la liste et par l'upsert. Les champs suivent
/// <c>TeambuildSummary</c> de l'OpenAPI ; ceux qu'on n'exploite pas encore sont omis (les
/// propriétés inconnues sont ignorées à la désérialisation, comme partout ailleurs).</summary>
public class GwRankSummary
{
    public long Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Nom de compte du propriétaire (v3 de l'API). Vide sur un serveur plus ancien —
    /// auquel cas on ne peut RIEN affirmer sur l'auteur, et surtout pas que le build est à soi.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Empreinte SHA-256 du document stocké, <c>updatedAt</c> exclu, calculée par le
    /// SERVEUR. Deux usages : savoir si un document local est à jour sans le télécharger, et
    /// servir de <c>If-Match</c> au dépôt suivant. Ne se recalcule pas côté client — le serveur
    /// normalise certains champs (les étiquettes canoniques, par exemple « gvg » → « GvG »).</summary>
    public string DocumentHash { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];
    public string GameMode { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public string Visibility { get; set; } = string.Empty;
    /// <summary>« draft » ou « published » (v2 de l'API). Vide sur un serveur plus ancien.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Renvoyés par le seul PUT (UpsertResult). Absents d'une réponse de liste → false.
    public bool Created { get; set; }
    public bool Changed { get; set; }
}

public sealed class GwRankList
{
    public List<GwRankSummary> Teambuilds { get; set; } = [];
    public GwRankPagination? Pagination { get; set; }
}

/// <summary>Une entrée de <c>/export</c> : le résumé, plus le document `.zcx` intégral.
/// Le document est gardé en JSON BRUT (<see cref="System.Text.Json.Nodes.JsonNode"/>) et non
/// converti : les champs d'une version de format plus récente survivent ainsi à l'aller-retour,
/// alors que passer par le modèle les perdrait en silence.</summary>
public sealed class GwRankExportItem : GwRankSummary
{
    public System.Text.Json.Nodes.JsonNode? Document { get; set; }

    /// <summary>Le document tel qu'il sera écrit sur disque, ou null s'il manque.</summary>
    public string? DocumentJson => Document?.ToJsonString(
        new JsonSerializerOptions { WriteIndented = true });
}

public sealed class GwRankExport
{
    public List<GwRankExportItem> Teambuilds { get; set; } = [];
}

/// <summary>
/// Client de l'API GWRank (spec : <c>docs/gwrank_api_retours.md</c>, OpenAPI d'Arka).
///
/// Ne lève JAMAIS pour une panne réseau ou une réponse d'erreur : tout ressort en
/// <see cref="GwRankResult{T}"/>. Un envoi de build ne doit pas pouvoir tuer l'application ni
/// faire perdre le travail en cours — même politique que <c>AppVersionChecker</c>.
/// </summary>
public sealed class GwRankClient : IDisposable
{
    /// <summary>Serveur de production. Le champ est configurable pour pouvoir viser une instance
    /// de test sans recompiler.</summary>
    public const string DefaultBaseUrl = "https://gwrank.com";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public GwRankClient(string? token, string? baseUrl = null, HttpClient? http = null)
    {
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        // 30 s : un teambuild chargé peut peser quelques dizaines de ko et l'utilisateur ATTEND
        // le résultat, contrairement à la vérification de version au démarrage (15 s).
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex");
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HasToken = !string.IsNullOrWhiteSpace(token);
    }

    public bool HasToken { get; }

    private string Endpoint(string suffix = "") => $"{_baseUrl}/api/v1/teambuilds{suffix}";

    /// <summary>Vérifie que le jeton est accepté. Sert au bouton « Tester la connexion » des
    /// réglages : l'utilisateur doit pouvoir valider sa clé sans rien envoyer.</summary>
    public Task<GwRankResult<GwRankList>> TestConnectionAsync(CancellationToken ct = default)
        => ListAsync(perPage: 1, ct: ct);

    /// <summary>
    /// Liste paginée de RÉSUMÉS (sans les documents), donc peu coûteuse.
    ///
    /// ⚠ <paramref name="visibility"/> vide ne veut PAS dire « les miens » : le serveur renvoie
    /// alors tout ce que l'appelant peut voir, builds publics des autres joueurs compris. Seule la
    /// valeur <c>mine</c> filtre réellement — <c>public</c> et <c>all</c> sont acceptés avec un
    /// 200 mais IGNORÉS (mesuré sur le serveur : ils renvoient l'intégralité).
    /// </summary>
    public async Task<GwRankResult<GwRankList>> ListAsync(int page = 1, int perPage = 100,
                                                          string? visibility = null,
                                                          DateTime? updatedSince = null,
                                                          CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<GwRankList>.Fail(GwRankStatus.NoToken);
        var url = Endpoint($"?page={page}&per_page={perPage}")
                + (visibility is { Length: > 0 } v ? $"&visibility={Uri.EscapeDataString(v)}" : "")
                + UpdatedSinceParam(updatedSince, "&");
        return await SendAsync<GwRankList>(HttpMethod.Get, url, null, ct);
    }

    /// <summary>Les seuls builds de l'utilisateur. C'est la SEULE autorité sur « à qui est ce
    /// build » : déduire la propriété du nom d'auteur reviendrait à parier que deux joueurs ne
    /// portent jamais le même pseudo.</summary>
    public Task<GwRankResult<GwRankList>> ListMineAsync(int page = 1, int perPage = 100,
                                                        CancellationToken ct = default)
        => ListAsync(page, perPage, visibility: "mine", ct: ct);

    /// <summary>Le serveur veut un instant ISO 8601 ; une date mal formée est refusée en 400
    /// (<c>invalid_updated_since</c>), pas ignorée — contrairement aux paramètres inconnus.</summary>
    private static string UpdatedSinceParam(DateTime? since, string lead)
        => since is { } d
            ? $"{lead}updated_since={Uri.EscapeDataString(d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}"
            : "";

    /// <summary>
    /// Tout ce que l'appelant peut voir — ses builds ET les builds publics des autres — documents
    /// `.zcx` intégraux compris, en UN appel.
    ///
    /// C'est l'appel LOURD : la réponse porte tous les documents et n'est pas paginée. D'où
    /// <paramref name="updatedSince"/>, qui ne rapatrie que ce qui a bougé depuis cet instant.
    ///
    /// ⚠ Un filtre temporel ne dit JAMAIS ce qui a été SUPPRIMÉ. Un appelant qui s'en sert seul
    /// garderait indéfiniment des builds disparus : la liste des résumés reste l'autorité sur ce
    /// qui existe encore (cf. <see cref="GwRankBrowserCache"/>).
    /// </summary>
    public async Task<GwRankResult<GwRankExport>> ExportAsync(string? status = null,
                                                              DateTime? updatedSince = null,
                                                              CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<GwRankExport>.Fail(GwRankStatus.NoToken);
        var url = Endpoint("/export")
                + (status is { Length: > 0 } s ? $"?status={Uri.EscapeDataString(s)}" : "")
                + UpdatedSinceParam(updatedSince, status is { Length: > 0 } ? "&" : "?");
        return await SendAsync<GwRankExport>(HttpMethod.Get, url, null, ct);
    }

    /// <summary>Récupère un teambuild complet. Renvoie le JSON BRUT en plus du modèle : les champs
    /// d'une version de format plus récente ne survivent pas à un aller-retour par le modèle, et
    /// l'appelant peut vouloir écrire le document tel qu'il est arrivé.</summary>
    public async Task<GwRankResult<string>> GetRawAsync(string id, CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<string>.Fail(GwRankStatus.NoToken);
        return await SendRawAsync(HttpMethod.Get, Endpoint($"/{Uri.EscapeDataString(id)}"), null, ct);
    }

    /// <summary>
    /// Dépose (crée ou remplace) un teambuild. La clé est le <c>id</c> du document lui-même :
    /// l'appelant DOIT s'être assuré qu'aucun autre fichier local ne porte le même — sinon le
    /// second envoi écrase le premier côté serveur (cf. <see cref="GwRankSyncIndex"/>).
    /// </summary>
    /// <param name="ifMatch">Empreinte serveur (<c>documentHash</c>) rendue par le dernier dépôt
    /// de CE build depuis CE poste. Fournie, le serveur refuse en <see cref="GwRankStatus.Conflict"/>
    /// si le build a changé entre-temps, sans rien modifier — c'est ce qui empêche deux machines
    /// de s'écraser en silence. Null = dépôt inconditionnel (première fois, ou écrasement demandé
    /// explicitement par l'utilisateur).</param>
    public async Task<GwRankResult<GwRankSummary>> UploadAsync(TeamBuild build, bool isPublic = false,
                                                                string? ifMatch = null,
                                                                CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<GwRankSummary>.Fail(GwRankStatus.NoToken);
        if (build.Id == Guid.Empty)
            return GwRankResult<GwRankSummary>.Fail(GwRankStatus.Rejected, "identifiant de build vide");

        // Le serveur veut l'uuid canonique en minuscules ; "D" produit exactement ça.
        var id = build.Id.ToString("D");
        var visibility = isPublic ? "public" : "private";
        var url = Endpoint($"/{id}?visibility={visibility}");

        // On envoie la sérialisation du build OUVERT, pas l'octet du fichier sur disque : c'est
        // ce que l'utilisateur voit à l'écran qu'il croit envoyer.
        var body = TeamBuildSerializer.Serialize(build);
        // L'en-tête attend une étiquette d'entité, donc GUILLEMETS COMPRIS. Sans eux le serveur
        // ne reconnaît pas l'empreinte et refuse tout, y compris un dépôt légitime.
        var tag = ifMatch is { Length: > 0 } h ? (h.StartsWith('"') ? h : $"\"{h}\"") : null;
        var res = await SendAsync<GwRankSummary>(HttpMethod.Put, url, body, ct, tag);

        // ⚠ Le serveur répond 412 dans DEUX cas que l'utilisateur ne vit pas du tout pareil :
        // le build a changé ailleurs (conflit réel, il faut lui demander), ou il n'existe tout
        // simplement plus — supprimé depuis le site, ou base repartie de zéro. Dans ce
        // second cas il n'y a AUCUN travail à protéger : refuser reviendrait à réclamer un
        // arbitrage sur du vide, et un envoi de masse écarterait toute la bibliothèque d'un coup.
        // On ne le devine pas, on le vérifie.
        if (res.Status == GwRankStatus.Conflict && tag is not null)
        {
            var probe = await SendRawAsync(HttpMethod.Get, Endpoint($"/{id}"), null, ct);
            if (probe.Status == GwRankStatus.NotFound)
                return await SendAsync<GwRankSummary>(HttpMethod.Put, url, body, ct);
        }

        return res;
    }

    public async Task<GwRankResult<bool>> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<bool>.Fail(GwRankStatus.NoToken);
        var r = await SendRawAsync(HttpMethod.Delete, Endpoint($"/{Uri.EscapeDataString(id)}"), null, ct);
        return r.IsOk ? GwRankResult<bool>.Ok(true) : GwRankResult<bool>.Fail(r.Status, r.Message);
    }

    // ── Plomberie ─────────────────────────────────────────────────────────────

    private async Task<GwRankResult<T>> SendAsync<T>(HttpMethod method, string url, string? body,
                                                     CancellationToken ct, string? ifMatch = null)
    {
        var raw = await SendRawAsync(method, url, body, ct, ifMatch);
        if (!raw.IsOk) return GwRankResult<T>.Fail(raw.Status, raw.Message);

        try
        {
            var value = JsonSerializer.Deserialize<T>(raw.Value ?? "", Json);
            return value is null
                ? GwRankResult<T>.Fail(GwRankStatus.ServerError, "réponse vide")
                : GwRankResult<T>.Ok(value);
        }
        catch (JsonException ex)
        {
            // Réponse illisible = serveur cassé, ou portail captif qui renvoie sa page de
            // connexion. Le détail du parseur (« '<' is an invalid start of a value ») n'apprend
            // rien à un utilisateur : il part au journal, pas à l'écran.
            Debug.WriteLine($"[GwRank] réponse illisible sur {url} : {ex.Message}");
            return GwRankResult<T>.Fail(GwRankStatus.ServerError,
                "réponse inattendue du serveur (page d'erreur ou portail de connexion ?)");
        }
    }

    /// <summary>
    /// Attentes entre deux tentatives. Deux essais de rattrapage, courts : un redémarrage de
    /// serveur dure quelques secondes, et au-delà l'utilisateur préfère qu'on lui rende la main
    /// plutôt qu'on insiste dans une fenêtre figée.
    /// </summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    /// <summary>Plafond de l'attente réclamée par le serveur : un <c>Retry-After</c> généreux ne
    /// doit pas immobiliser l'application plusieurs minutes.</summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Émet la requête, en réessayant les pannes de passage.
    ///
    /// ⚠ Réessayer n'est acceptable que parce que TOUS les appels de ce client sont idempotents :
    /// le <c>PUT</c> est un upsert clé par <c>source_uuid</c>, donc une requête dont la réponse
    /// s'est perdue en route peut être rejouée sans créer de doublon ni écraser autre chose.
    /// </summary>
    private async Task<GwRankResult<string>> SendRawAsync(HttpMethod method, string url, string? body,
                                                          CancellationToken ct, string? ifMatch = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            var (result, retryAfter) = await SendOnceAsync(method, url, body, ct, ifMatch);
            if (result.IsOk || attempt >= RetryDelays.Length || !result.Status.IsTransient())
                return result;

            // Le serveur sait mieux que nous quand revenir : son Retry-After l'emporte, borné.
            var wait = retryAfter is { } ra && ra > TimeSpan.Zero
                ? (ra < MaxRetryAfter ? ra : MaxRetryAfter)
                : RetryDelays[attempt];
            Debug.WriteLine($"[GwRank] {result.Status} sur {method} {url} — nouvelle tentative dans {wait.TotalSeconds:F0} s");
            await Task.Delay(wait, ct);
        }
    }

    private async Task<(GwRankResult<string> Result, TimeSpan? RetryAfter)> SendOnceAsync(
        HttpMethod method, string url, string? body, CancellationToken ct, string? ifMatch)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null)
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // TryAddWithoutValidation : « * » n'est pas une étiquette d'entité valide au sens du
            // parseur de .NET, alors que le serveur l'accepte (« exige que le build existe »).
            if (ifMatch is { Length: > 0 })
                req.Headers.TryAddWithoutValidation("If-Match", ifMatch);

            using var res = await _http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode) return (GwRankResult<string>.Ok(text), null);

            var status = res.StatusCode switch
            {
                HttpStatusCode.Unauthorized          => GwRankStatus.Unauthorized,
                HttpStatusCode.Forbidden             => GwRankStatus.Forbidden,
                HttpStatusCode.NotFound              => GwRankStatus.NotFound,
                HttpStatusCode.UnprocessableEntity   => GwRankStatus.Rejected,
                HttpStatusCode.BadRequest            => GwRankStatus.Rejected,
                HttpStatusCode.PreconditionFailed    => GwRankStatus.Conflict,
                HttpStatusCode.TooManyRequests       => GwRankStatus.RateLimited,
                HttpStatusCode.RequestTimeout        => GwRankStatus.Offline,
                _                                    => GwRankStatus.ServerError,
            };
            return (GwRankResult<string>.Fail(status, DescribeError(text, res.StatusCode)),
                    RetryAfterOf(res));
        }
        // L'annulation vient de l'utilisateur (fermeture de la fenêtre) : ce n'est pas une panne,
        // et elle doit remonter telle quelle pour ne pas afficher un faux message d'erreur.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskCanceledException ex)   { return (GwRankResult<string>.Fail(GwRankStatus.Offline, ex.Message), null); }
        catch (HttpRequestException ex)    { return (GwRankResult<string>.Fail(GwRankStatus.Offline, ex.Message), null); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] {method} {url}: {ex}");
            return (GwRankResult<string>.Fail(GwRankStatus.ServerError, ex.Message), null);
        }
    }

    /// <summary>Délai réclamé par le serveur, en secondes ou en date HTTP — les deux formes sont
    /// admises par la norme, et un serveur peut employer l'une ou l'autre.</summary>
    private static TimeSpan? RetryAfterOf(HttpResponseMessage res)
    {
        var ra = res.Headers.RetryAfter;
        if (ra is null) return null;
        if (ra.Delta is { } d) return d;
        if (ra.Date is { } when) return when - DateTimeOffset.UtcNow;
        return null;
    }

    /// <summary>Extrait le premier message d'erreur exploitable d'une réponse d'échec. Le serveur
    /// renvoie soit la liste <c>errors</c> de l'OpenAPI, soit du texte brut (« HTTP Token: Access
    /// denied. » sur un 401) : les deux doivent pouvoir s'afficher.</summary>
    private static string DescribeError(string text, HttpStatusCode code)
    {
        if (string.IsNullOrWhiteSpace(text)) return $"HTTP {(int)code}";
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var parts = errors.EnumerateArray().Take(3).Select(e =>
                {
                    var path = e.TryGetProperty("path", out var p) ? p.GetString() : null;
                    var msg  = e.TryGetProperty("message", out var m) ? m.GetString()
                             : e.TryGetProperty("code", out var c) ? c.GetString() : null;
                    return string.IsNullOrEmpty(path) ? msg : $"{path} : {msg}";
                });
                return string.Join(" ; ", parts);
            }
        }
        catch (JsonException) { /* réponse non-JSON : on retombe sur le texte brut */ }

        return text.Length <= 200 ? text.Trim() : text[..200].Trim() + "…";
    }

    public void Dispose() => _http.Dispose();
}
