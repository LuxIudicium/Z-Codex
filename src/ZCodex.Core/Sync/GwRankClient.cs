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
    /// <summary>Panne réseau, DNS, TLS, délai dépassé.</summary>
    Offline,
    /// <summary>Tout le reste (5xx, réponse illisible).</summary>
    ServerError,
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

    public async Task<GwRankResult<GwRankList>> ListAsync(int page = 1, int perPage = 100,
                                                          CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<GwRankList>.Fail(GwRankStatus.NoToken);
        var url = Endpoint($"?page={page}&per_page={perPage}");
        return await SendAsync<GwRankList>(HttpMethod.Get, url, null, ct);
    }

    /// <summary>
    /// Tout ce que l'appelant peut voir — ses builds ET les builds publics des autres — documents
    /// `.zcx` intégraux compris, en UN appel.
    ///
    /// ⚠ Sans pagination ni filtre temporel côté serveur : la réponse grossit avec la
    /// bibliothèque publique entière, et il n'existe aucun moyen de ne demander que ce qui a
    /// changé. À n'appeler que sur action explicite de l'utilisateur, jamais en boucle.
    /// </summary>
    public async Task<GwRankResult<GwRankExport>> ExportAsync(string? status = null,
                                                              CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<GwRankExport>.Fail(GwRankStatus.NoToken);
        var url = Endpoint("/export") + (status is { Length: > 0 } s ? $"?status={Uri.EscapeDataString(s)}" : "");
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
    public async Task<GwRankResult<GwRankSummary>> UploadAsync(TeamBuild build, bool isPublic = false,
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
        return await SendAsync<GwRankSummary>(HttpMethod.Put, url, body, ct);
    }

    public async Task<GwRankResult<bool>> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!HasToken) return GwRankResult<bool>.Fail(GwRankStatus.NoToken);
        var r = await SendRawAsync(HttpMethod.Delete, Endpoint($"/{Uri.EscapeDataString(id)}"), null, ct);
        return r.IsOk ? GwRankResult<bool>.Ok(true) : GwRankResult<bool>.Fail(r.Status, r.Message);
    }

    // ── Plomberie ─────────────────────────────────────────────────────────────

    private async Task<GwRankResult<T>> SendAsync<T>(HttpMethod method, string url, string? body,
                                                     CancellationToken ct)
    {
        var raw = await SendRawAsync(method, url, body, ct);
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
            // Réponse illisible = serveur cassé ou portail captif qui renvoie du HTML.
            return GwRankResult<T>.Fail(GwRankStatus.ServerError, ex.Message);
        }
    }

    private async Task<GwRankResult<string>> SendRawAsync(HttpMethod method, string url, string? body,
                                                          CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null)
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var res = await _http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode) return GwRankResult<string>.Ok(text);

            return GwRankResult<string>.Fail(res.StatusCode switch
            {
                HttpStatusCode.Unauthorized          => GwRankStatus.Unauthorized,
                HttpStatusCode.Forbidden             => GwRankStatus.Forbidden,
                HttpStatusCode.NotFound              => GwRankStatus.NotFound,
                HttpStatusCode.UnprocessableEntity   => GwRankStatus.Rejected,
                HttpStatusCode.BadRequest            => GwRankStatus.Rejected,
                _                                    => GwRankStatus.ServerError,
            }, DescribeError(text, res.StatusCode));
        }
        // L'annulation vient de l'utilisateur (fermeture de la fenêtre) : ce n'est pas une panne,
        // et elle doit remonter telle quelle pour ne pas afficher un faux message d'erreur.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskCanceledException ex)   { return GwRankResult<string>.Fail(GwRankStatus.Offline, ex.Message); }
        catch (HttpRequestException ex)    { return GwRankResult<string>.Fail(GwRankStatus.Offline, ex.Message); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] {method} {url}: {ex}");
            return GwRankResult<string>.Fail(GwRankStatus.ServerError, ex.Message);
        }
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
