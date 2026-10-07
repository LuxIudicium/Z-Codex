using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ZCodex.Core.Collab;

/// <summary>
/// Adresses du serveur de salons, déduites de l'adresse de l'API REST (réglage « Serveur » de
/// GWRank). Un invité n'a que le CODE du salon : l'URL WebSocket rendue à la création n'est connue
/// que de l'hôte, il faut donc pouvoir la reconstruire.
/// </summary>
public static class RoomEndpoints
{
    /// <summary>Point d'entrée Action Cable : <c>wss://hôte/cable</c> (mesuré sur gwrank.com,
    /// chemin par défaut de Rails ; le même que celui qu'emploie le site lui-même).</summary>
    public static Uri CableUrl(string? baseUrl)
    {
        var b = new Uri((baseUrl is { Length: > 0 } s ? s : Sync.GwRankClient.DefaultBaseUrl).TrimEnd('/'));
        var scheme = b.Scheme == Uri.UriSchemeHttp ? "ws" : "wss";
        return new UriBuilder(scheme, b.Host, b.IsDefaultPort ? -1 : b.Port, "/cable").Uri;
    }

    /// <summary>
    /// Valeur de l'en-tête <c>Origin</c> pour une URL WebSocket : même hôte, en http(s).
    /// ⚠ OBLIGATOIRE : sans lui, le serveur refuse la poignée de main avec un 404 « Page not
    /// found » (mesuré le 29/09/2026) — Action Cable n'accepte que les origines de sa liste
    /// blanche, et un client natif n'en envoie aucune de lui-même.
    /// </summary>
    /// <summary>Origine du SITE (réglage « Serveur » de GWRank, production par défaut) : c'est elle
    /// qu'un navigateur présenterait, et donc celle que la liste blanche du serveur connaît — même
    /// si le WebSocket était un jour servi depuis un autre hôte.</summary>
    public static string SiteOrigin(string? baseUrl)
        => new Uri((baseUrl is { Length: > 0 } s ? s : Sync.GwRankClient.DefaultBaseUrl).TrimEnd('/'))
               .GetLeftPart(UriPartial.Authority);

    public static string OriginOf(Uri websocketUrl)
    {
        var scheme = websocketUrl.Scheme == "ws" ? "http" : "https";
        return new UriBuilder(scheme, websocketUrl.Host, websocketUrl.IsDefaultPort ? -1 : websocketUrl.Port).Uri
            .GetLeftPart(UriPartial.Authority);
    }
}

/// <summary>
/// Une connexion Action Cable (Rails) abonnée à UN canal.
///
/// Le protocole, mesuré sur le serveur réel le 29/09/2026 :
/// <list type="bullet">
/// <item>sous-protocole <c>actioncable-v1-json</c>, en-tête <c>Origin</c> exigé ;</item>
/// <item>le serveur ouvre par <c>{"type":"welcome"}</c> puis envoie <c>{"type":"ping"}</c> toutes
///   les 3 secondes — c'est notre signe de vie : sans ping pendant <see cref="StaleAfter"/>, la
///   connexion est tenue pour morte, même si Windows la croit ouverte (box, proxy) ;</item>
/// <item>abonnement : <c>{"command":"subscribe","identifier":"&lt;json&gt;"}</c>, réponse
///   <c>confirm_subscription</c> ou <c>reject_subscription</c> ;</item>
/// <item>refus du salon : <c>{"type":"disconnect","reason":"room_not_found","reconnect":false}</c>
///   puis fermeture WebSocket 4404 — les motifs sont donc des chaînes ET des codes 4xxx.</item>
/// </list>
/// </summary>
public sealed class ActionCableConnection : IAsyncDisposable
{
    public const string SubProtocol = "actioncable-v1-json";

    /// <summary>Le serveur pingue toutes les 3 s ; au-delà de quatre pings manqués, on arrête
    /// d'y croire (le client JavaScript officiel d'Action Cable prend 6 s, on laisse de la marge
    /// à un Wi-Fi qui hoquette).</summary>
    public static TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>Ce que l'abonné veut entendre. Fourni À L'OUVERTURE, et non par des événements
    /// posés après coup : le serveur envoie le dernier état du salon dès l'abonnement, parfois
    /// même AVANT la confirmation — un abonné arrivé une milliseconde trop tard le perdrait.</summary>
    public sealed class Handlers
    {
        /// <summary>Message du canal (champ <c>message</c> d'une trame). Appelé sur le fil de
        /// réception, dans l'ordre d'arrivée.</summary>
        public Action<JsonElement>? Message { get; init; }
        /// <summary>Appelé UNE fois, quelle que soit la cause de la fin.</summary>
        public Action<RoomCloseInfo>? Closed { get; init; }
        /// <summary>Trames hors protocole et hors canal (présence annoncée par le serveur, par
        /// exemple) : leur forme n'est pas documentée, elles ne servent qu'au diagnostic.</summary>
        public Action<string>? Unknown { get; init; }
    }

    private readonly ClientWebSocket _ws = new();
    private readonly string _identifier;
    private readonly Handlers _handlers;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _life = new();
    private readonly List<JsonElement> _early = [];
    private DateTime _lastHeardUtc = DateTime.UtcNow;
    private RoomCloseInfo? _staleInfo;
    private int _closedRaised;
    private Task? _loop;

    private ActionCableConnection(string identifier, Handlers handlers)
    {
        _identifier = identifier;
        _handlers = handlers;
    }

    public bool IsOpen => _ws.State == WebSocketState.Open && _closedRaised == 0;

    /// <summary>
    /// Ouvre la connexion et s'abonne. Rend la main une fois l'abonnement CONFIRMÉ ; lève
    /// <see cref="RoomConnectException"/> sur tout refus, avec le motif classé.
    /// </summary>
    /// <param name="origin">En-tête Origin à présenter ; null = déduit de <paramref name="url"/>.</param>
    public static async Task<ActionCableConnection> OpenAsync(Uri url, string identifier, Handlers handlers,
                                                              TimeSpan timeout, CancellationToken ct,
                                                              string? origin = null)
    {
        var c = new ActionCableConnection(identifier, handlers);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            c._ws.Options.AddSubProtocol(SubProtocol);
            c._ws.Options.SetRequestHeader("Origin", origin ?? RoomEndpoints.OriginOf(url));
            c._ws.Options.SetRequestHeader("User-Agent", "Z-Codex");
            // Le statut HTTP d'une poignée de main refusée (404 sans Origin, 502 d'un relais) ne
            // se lit qu'avec ce drapeau ; sans lui, tout échec ressemble à une panne réseau.
            c._ws.Options.CollectHttpResponseDetails = true;
            await c._ws.ConnectAsync(url, cts.Token);

            bool welcomed = false;
            while (true)
            {
                var text = await c.ReceiveTextAsync(cts.Token)
                           ?? throw c.CloseException("connexion fermée pendant l'ouverture");
                c._lastHeardUtc = DateTime.UtcNow;
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var type = TypeOf(root);
                switch (type)
                {
                    case "welcome":
                        if (!welcomed)
                        {
                            welcomed = true;
                            await c.SendRawAsync(JsonSerializer.Serialize(new { command = "subscribe", identifier }), cts.Token);
                        }
                        break;
                    case "ping":
                        break;
                    case "confirm_subscription":
                        c._loop = Task.Run(c.RunAsync);
                        return c;
                    case "reject_subscription":
                        throw new RoomConnectException(
                            new RoomCloseInfo(RoomEndReason.Unknown, "reject_subscription", null, false),
                            "reject_subscription");
                    case "disconnect":
                        var info = DisconnectInfo(root);
                        throw new RoomConnectException(info, info.RawReason ?? "disconnect");
                    default:
                        // Message du canal arrivé AVANT la confirmation : gardé pour l'abonné.
                        if (type is null && root.ValueKind == JsonValueKind.Object
                            && root.TryGetProperty("message", out var early))
                            c._early.Add(early.Clone());
                        else
                            handlers.Unknown?.Invoke(text);
                        break;
                }
            }
        }
        catch (RoomConnectException)
        {
            await c.DisposeAsync();
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await c.DisposeAsync();
            throw new RoomConnectException(new RoomCloseInfo(RoomEndReason.NetworkLost, "timeout", null, true),
                                           "timeout");
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or JsonException or HttpRequestException)
        {
            // Poignée de main refusée, DNS, TLS, réseau.
            var info = c.CloseInfoFromSocket() ?? c.HandshakeInfo(ex.Message);
            await c.DisposeAsync();
            throw new RoomConnectException(info, info.RawReason ?? ex.Message);
        }
        catch
        {
            await c.DisposeAsync();
            throw;
        }
    }

    /// <summary>Envoie une action au canal : <c>{"command":"message","identifier":…,"data":"&lt;json&gt;"}</c>
    /// — <c>data</c> est une CHAÎNE contenant du JSON, comme l'exige Action Cable.</summary>
    public Task SendActionAsync(object data, CancellationToken ct)
        => SendRawAsync(JsonSerializer.Serialize(new
        {
            command = "message",
            identifier = _identifier,
            data = JsonSerializer.Serialize(data),
        }), ct);

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(ct);
        try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        finally { _sendLock.Release(); }
    }

    /// <summary>Départ propre : désabonnement puis fermeture normale. Borné dans le temps — on
    /// ne retient pas la fermeture de l'application pour un serveur qui ne répond plus.</summary>
    public async Task CloseAsync(TimeSpan timeout)
    {
        // Levé AVANT d'écrire : la boucle de réception, réveillée par la fermeture, ne doit pas
        // faire passer ce départ voulu pour une coupure réseau.
        RaiseClosed(new RoomCloseInfo(RoomEndReason.Left, null, null, false));
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await SendRawAsync(JsonSerializer.Serialize(new { command = "unsubscribe", identifier = _identifier }), cts.Token);
                // CloseOutput et non Close : la réponse du serveur est lue par la boucle de
                // réception, déjà en attente — deux lectures simultanées sont interdites.
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            Debug.WriteLine($"[Room] fermeture : {ex.Message}");
        }
    }

    private async Task RunAsync()
    {
        var watchdog = Task.Run(WatchdogAsync);
        foreach (var early in _early) Dispatch(early);
        _early.Clear();

        RoomCloseInfo? info = null;
        try
        {
            while (!_life.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(_life.Token);
                if (text is null) break;
                _lastHeardUtc = DateTime.UtcNow;
                info = HandleFrame(text);
                if (info is not null) break;
            }
        }
        catch (OperationCanceledException) { /* arrêt demandé, ou chien de garde */ }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            Debug.WriteLine($"[Room] réception interrompue : {ex.Message}");
        }

        info ??= _staleInfo ?? CloseInfoFromSocket()
                 ?? new RoomCloseInfo(RoomEndReason.NetworkLost, null, null, true);
        _life.Cancel();
        try { _ws.Abort(); } catch (ObjectDisposedException) { }
        RaiseClosed(info);
        await watchdog.ConfigureAwait(false);
    }

    private async Task WatchdogAsync()
    {
        try
        {
            while (!_life.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _life.Token);
                if (DateTime.UtcNow - _lastHeardUtc > StaleAfter)
                {
                    // Plus un seul ping : la connexion est morte même si le socket se croit ouvert.
                    _staleInfo = new RoomCloseInfo(RoomEndReason.NetworkLost, "stale", null, true);
                    _life.Cancel();
                    try { _ws.Abort(); } catch (ObjectDisposedException) { }
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Traite une trame ; rend un motif de fin si elle termine la connexion.</summary>
    private RoomCloseInfo? HandleFrame(string text)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException) { _handlers.Unknown?.Invoke(text); return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { _handlers.Unknown?.Invoke(text); return null; }

            switch (TypeOf(root))
            {
                case "ping":
                case "welcome":
                case "confirm_subscription":
                    return null;
                case "disconnect":
                    return DisconnectInfo(root);
                case "reject_subscription":
                    return new RoomCloseInfo(RoomEndReason.Unknown, "reject_subscription", null, false);
                case null when root.TryGetProperty("message", out var msg):
                    Dispatch(msg.Clone());
                    return null;
                default:
                    _handlers.Unknown?.Invoke(text);
                    return null;
            }
        }
    }

    private void Dispatch(JsonElement message)
    {
        try { _handlers.Message?.Invoke(message); }
        catch (Exception ex) { Debug.WriteLine($"[Room] traitement d'un message : {ex}"); }
    }

    private static string? TypeOf(JsonElement root)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var t)
           && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

    private static RoomCloseInfo DisconnectInfo(JsonElement root)
    {
        var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        bool reconnect = root.TryGetProperty("reconnect", out var rc) && rc.ValueKind == JsonValueKind.True;
        var kind = RoomCloseInfo.Classify(reason, null);
        // « reconnect: false » sur un motif non reconnu : on respecte le serveur, pas de retour.
        if (!reconnect && kind == RoomEndReason.NetworkLost) kind = RoomEndReason.Unknown;
        return new RoomCloseInfo(kind, reason, null, reconnect);
    }

    private RoomCloseInfo? CloseInfoFromSocket()
    {
        if (_ws.CloseStatus is not { } status) return null;
        int code = (int)status;
        var reason = _ws.CloseStatusDescription;
        return new RoomCloseInfo(RoomCloseInfo.Classify(reason, code), reason, code, false);
    }

    /// <summary>Échec AVANT toute trame : on regarde le statut HTTP de la poignée de main.
    /// 5xx = serveur ou relais indisponible, passager ; 4xx = le point d'entrée refuse (origine,
    /// chemin), ce qu'aucune nouvelle tentative ne changera.</summary>
    private RoomCloseInfo HandshakeInfo(string message)
    {
        var http = _ws.HttpStatusCode;
        if (http is 0 or HttpStatusCode.SwitchingProtocols || (int)http >= 500)
            return new RoomCloseInfo(RoomEndReason.NetworkLost, message, null, true);
        return new RoomCloseInfo(RoomEndReason.Unknown, $"HTTP {(int)http}", null, false);
    }

    private RoomConnectException CloseException(string fallback)
    {
        var info = CloseInfoFromSocket() ?? new RoomCloseInfo(RoomEndReason.NetworkLost, null, null, true);
        return new RoomConnectException(info, info.RawReason ?? fallback);
    }

    /// <summary>Lit un message texte complet (éventuellement en plusieurs trames). Null = fermé.</summary>
    private async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await _ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
            // Garde-fou : un serveur (ou un intermédiaire) déréglé ne doit pas nous faire
            // accumuler de la mémoire sans fin. Nos paquets font quelques Ko.
            if (ms.Length > 4 * 1024 * 1024)
                throw new IOException("trame WebSocket démesurée");
            if (r.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private void RaiseClosed(RoomCloseInfo info)
    {
        if (Interlocked.Exchange(ref _closedRaised, 1) != 0) return;
        try { _handlers.Closed?.Invoke(info); }
        catch (Exception ex) { Debug.WriteLine($"[Room] Closed : {ex}"); }
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _life.Cancel();
        try { _ws.Abort(); } catch (ObjectDisposedException) { }
        if (_loop is { } loop)
        {
            try { await loop.ConfigureAwait(false); }
            catch (Exception ex) { Debug.WriteLine($"[Room] fin de boucle : {ex.Message}"); }
        }
        _ws.Dispose();
        _sendLock.Dispose();
        _life.Dispose();
    }
}
