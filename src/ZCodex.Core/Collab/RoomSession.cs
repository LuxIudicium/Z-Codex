using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZCodex.Core.Collab;

/// <summary>Réglages d'une session. Les valeurs du protocole sont exposées pour le banc d'essai
/// et la sonde du serveur réel ; l'application garde les défauts.</summary>
public sealed class RoomSessionOptions
{
    public required Uri WebsocketUrl { get; init; }
    /// <summary>En-tête Origin (cf. <see cref="RoomEndpoints.SiteOrigin"/>). Null = déduit de l'URL WebSocket.</summary>
    public string? Origin { get; init; }
    public required string Code { get; init; }
    /// <summary>Hôte seulement. Ne sort jamais de la machine autrement que vers le serveur.</summary>
    public string? CreatorSecret { get; init; }
    public required string Nick { get; init; }
    public string AppVersion { get; init; } = string.Empty;
    public int FormatVersion { get; init; }
    /// <summary>Hôte seulement : limites rendues par la création du salon.</summary>
    public GwRankRoomLimits? Limits { get; init; }
    public DateTime? ExpiresAt { get; init; }
    /// <summary>Contexte où lever les événements (le fil de l'interface). Null = fil réseau.</summary>
    public SynchronizationContext? Context { get; init; }
    /// <summary>Imposé par les tests ; sinon tiré au hasard à chaque session.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Message d'envoi : <c>{"action":"receive","payload":"&lt;Base64&gt;"}</c>. Mesuré sur
    /// gwrank.com le 29/09/2026 : <c>receive</c> est la méthode par défaut d'Action Cable (elle est
    /// appelée aussi sans « action ») ; tout autre nom est ignoré en silence.
    /// </summary>
    public string PacketAction { get; init; } = "receive";
    public string PayloadField { get; init; } = "payload";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Au-delà, une coupure devient définitive et la session se termine.</summary>
    public TimeSpan ReconnectGiveUp { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Délai avant qu'un arrivant sans état demande le build aux autres.</summary>
    public TimeSpan NeedDelay { get; init; } = TimeSpan.FromSeconds(1.5);
}

public sealed class RoomStateEventArgs(JsonObject doc, IReadOnlyDictionary<string, UnitStamp>? stamps,
                                       IReadOnlyList<string>? order,
                                       string fromClientId, string? fromNick, long serial) : EventArgs
{
    public JsonObject Doc { get; } = doc;
    public IReadOnlyDictionary<string, UnitStamp>? Stamps { get; } = stamps;
    public IReadOnlyList<string>? Order { get; } = order;
    public string FromClientId { get; } = fromClientId;
    public string? FromNick { get; } = fromNick;
    /// <summary>Numéro d'arrivée, croissant sur la session.</summary>
    public long Serial { get; } = serial;
}

/// <summary>
/// Une session partagée : la connexion au salon et tout ce que le serveur ne fait pas pour nous.
///
/// Ce que fait le serveur (mesuré sur gwrank.com le 29/09/2026) :
/// <list type="bullet">
/// <item>il relaie un <c>payload</c> Base64 aux AUTRES (<c>state.updated</c>, avec l'identifiant de
///   connexion de l'émetteur), sans écho ;</item>
/// <item>il annonce les arrivées et les départs (<c>room.joined</c> / <c>room.left</c>), et donne à
///   l'arrivant la liste des présents et le DERNIER message du salon (<c>room.ready</c>) ;</item>
/// <item>il coupe au 11ᵉ message dans la seconde (<c>rate_limited</c>), au-delà de 16 384 octets
///   (<c>payload_too_large</c>), sur un contenu qui n'est pas du Base64 (<c>invalid_payload</c>) ;</item>
/// <item>le salon survit au départ de son créateur, qui peut revenir avec son secret.</item>
/// </list>
///
/// Ce qu'on fait par-dessus :
/// <list type="bullet">
/// <item><b>Chaque envoi est un état complet</b> (teambuild, pseudo, personnages pris). Comme le
///   serveur garde le dernier message pour les arrivants, celui-ci est toujours un vrai build.
///   Pas de signe de vie ni d'au revoir : la présence vient du serveur.</item>
/// <item><b>Identité stable</b> — l'identifiant de connexion change à chaque reconnexion ; chaque
///   paquet porte le nôtre (<see cref="ClientId"/>), et on rattache les deux à la réception.</item>
/// <item><b>À chaque arrivée</b>, chacun renvoie son état : l'arrivant apprend qui est là et qui a
///   pris quoi (le dernier message gardé ne dit que ce qu'avait son émetteur).</item>
/// <item><b>Prises</b> — en cas de prise simultanée, la plus ancienne gagne (à égalité, le plus
///   petit identifiant), même calcul partout.</item>
/// <item><b>Repli</b> — un arrivant qui n'a reçu aucun build le demande (<c>hello</c> + <c>need</c>) ;
///   le gardien (l'hôte, sinon le plus petit identifiant) répond.</item>
/// <item><b>Reconnexion</b> — attentes croissantes jusqu'à <see cref="RoomSessionOptions.ReconnectGiveUp"/>.</item>
/// <item><b>Débit</b> — un seul état en attente (le plus récent remplace le précédent), envois
///   espacés à 80 % de la limite, découpés s'ils dépassent la taille admise.</item>
/// </list>
///
/// Les événements sont levés dans <see cref="RoomSessionOptions.Context"/>.
/// </summary>
public sealed class RoomSession : IAsyncDisposable
{
    private readonly RoomSessionOptions _o;
    private readonly string _identifier;
    private readonly object _gate = new();
    private readonly RoomPeer _self;
    private readonly Dictionary<string, RoomPeer> _peers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _present = new(StringComparer.Ordinal);   // connexions présentes
    private readonly Dictionary<string, long> _lastStateSeq = new(StringComparer.Ordinal);
    private readonly RoomChunkAssembler _chunks = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _diagnostics = [];

    private ActionCableConnection? _conn;
    /// <summary>Connexions tombées ou fermées, libérées à la fin de la session.</summary>
    private readonly List<ActionCableConnection> _retired = [];
    private Task? _senderLoop, _timerLoop, _reconnectLoop;
    private bool _stateDue;              // un état complet doit partir
    private int _helloNeed = -1;         // un hello doit partir (valeur de need), -1 = non
    private DateTime? _announceAt;       // renvoi de notre état programmé (arrivée d'un autre)
    private JsonObject? _sharedDoc;      // ce qu'on envoie : le dernier état partagé connu
    private Dictionary<string, UnitStamp>? _sharedStamps;
    private List<string>? _sharedOrder;
    private string? _myConnId;
    private long _seq;
    private int _colorCounter;
    private GwRankRoomLimits _limits;
    private DateTime? _expiresAt;
    private DateTime? _hostAbsentSince;
    private TimeSpan _minGap;
    private int _maxBytes;
    private DateTime _lastSendUtc = DateTime.MinValue;
    private DateTime _connectedUtc = DateTime.UtcNow;
    private DateTime _lastNeedUtc = DateTime.MinValue;
    private int _needAttempt;            // demandes d'état déjà faites depuis la connexion
    private bool _wantState;
    private long _stateSerial;
    private bool _leaving;

    public RoomStatus Status { get; private set; } = RoomStatus.Connecting;
    public RoomEndReason EndReason { get; private set; }
    public string? EndDetail { get; private set; }

    public string Code => _o.Code;
    public bool IsHost => _o.CreatorSecret is not null;
    public string ClientId => _self.ClientId;
    public string Nick => _self.Nick;
    public DateTime? ExpiresAt { get { lock (_gate) return _expiresAt; } }

    /// <summary>Délai au-delà duquel le serveur ferme un salon dont le créateur est parti
    /// (<c>creator_timeout</c> ; mesuré : 5 min 24 s, le balayage n'est pas instantané).</summary>
    public static readonly TimeSpan HostGraceDelay = TimeSpan.FromMinutes(5);

    /// <summary>Depuis quand l'hôte est absent (UTC), vu d'un invité ; null s'il est là, ou si
    /// on est soi-même l'hôte.</summary>
    public DateTime? HostAbsentSince { get { lock (_gate) return _hostAbsentSince; } }
    public GwRankRoomLimits Limits { get { lock (_gate) return _limits; } }

    /// <summary>
    /// Le dernier état reçu (tout émetteur confondu), pour qui s'abonne en retard. ⚠ Le serveur
    /// remet le dernier état du salon DÈS l'abonnement, avant même que <see cref="StartAsync"/>
    /// ait rendu la main : un abonné à <see cref="StateReceived"/> doit relire ceci juste après
    /// s'être abonné, sinon il manque le tout premier état.
    /// </summary>
    public RoomStateEventArgs? LatestState { get; private set; }

    private readonly TaskCompletionSource<RoomStateEventArgs> _firstState =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Attend le premier état reçu (invité qui vient d'entrer). Null si rien n'arrive
    /// dans le délai, ou si la session se termine avant.</summary>
    public async Task<RoomStateEventArgs?> WaitForStateAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        cts.CancelAfter(timeout);
        try { return await _firstState.Task.WaitAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    public event Action? StatusChanged;
    public event Action? PeersChanged;
    public event Action<RoomStateEventArgs>? StateReceived;
    public event Action<RoomNotice>? Notice;

    private RoomSession(RoomSessionOptions o)
    {
        _o = o;
        _self = new RoomPeer
        {
            ClientId = o.ClientId ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Nick = o.Nick,
            IsHost = o.CreatorSecret is not null,
            IsMe = true,
        };
        _limits = o.Limits ?? new GwRankRoomLimits();
        _expiresAt = o.ExpiresAt;
        ApplyLimits();

        // L'identifiant d'abonnement documenté par l'OpenAPI. Le secret n'y figure que pour
        // l'hôte : c'est lui qui prouve au serveur que le salon est le sien.
        var id = new Dictionary<string, string> { ["channel"] = "RoomChannel", ["code"] = o.Code };
        if (o.CreatorSecret is { Length: > 0 } secret) id["creatorSecret"] = secret;
        _identifier = JsonSerializer.Serialize(id);
    }

    /// <summary>
    /// Ouvre la session : connexion et abonnement. Lève <see cref="RoomConnectException"/> si le
    /// salon refuse (code inconnu, secret refusé…) — l'appelant l'affiche, rien n'est laissé ouvert.
    /// L'hôte n'envoie rien ici : son premier état part dès que l'appelant le publie.
    /// </summary>
    /// <param name="wantState">Vrai pour un invité : s'il ne reçoit aucun build (salon encore
    /// vide), il le demande aux autres. Jamais pour l'hôte, qui l'a.</param>
    public static async Task<RoomSession> StartAsync(RoomSessionOptions o, bool wantState, CancellationToken ct)
    {
        var s = new RoomSession(o) { _wantState = wantState };
        try
        {
            s._conn = await s.ConnectAsync(ct);
        }
        catch
        {
            await s.DisposeAsync();
            throw;
        }
        s._connectedUtc = DateTime.UtcNow;
        s.Status = RoomStatus.Connected;
        s._senderLoop = Task.Run(s.SenderLoopAsync);
        s._timerLoop = Task.Run(s.TimerLoopAsync);
        return s;
    }

    // ── API publique ──────────────────────────────────────────────────────────

    /// <summary>Instantané des participants connus, soi compris (hôte d'abord, puis par ordre
    /// d'arrivée).</summary>
    public IReadOnlyList<RoomPeer> Peers
    {
        get
        {
            lock (_gate)
                return new[] { _self }.Concat(_peers.Values)
                    .OrderByDescending(p => p.IsHost).ThenBy(p => p.ColorIndex)
                    .Select(Copy).ToList();
        }
    }

    /// <summary>Connexions présentes dont on ne connaît pas encore le pseudo (arrivant qui ne
    /// s'est pas encore annoncé, participant en train de se reconnecter).</summary>
    public int UnknownParticipants
    {
        get
        {
            lock (_gate)
                return _present.Count(c => c != _myConnId && !_peers.Values.Any(p => p.ConnectionId == c));
        }
    }

    /// <summary>Preneur (identifiant de session) de la ligne <paramref name="nodeId"/>, ou null.</summary>
    public string? OwnerOf(Guid nodeId)
    {
        lock (_gate) return ResolveOwner(nodeId);
    }

    public RoomPeer? PeerOf(string clientId)
    {
        lock (_gate)
            return clientId == _self.ClientId ? Copy(_self)
                 : _peers.TryGetValue(clientId, out var p) ? Copy(p) : null;
    }

    public IReadOnlyCollection<Guid> MyClaims
    {
        get { lock (_gate) return _self.Claims.Keys.ToList(); }
    }

    /// <summary>Prend un personnage. Faux s'il est déjà pris par un autre (son pseudo en sortie).</summary>
    public bool TryClaim(Guid rootId, out string? takenBy)
    {
        lock (_gate)
        {
            takenBy = null;
            var owner = ResolveOwner(rootId);
            if (owner is not null && owner != _self.ClientId)
            {
                takenBy = _peers.TryGetValue(owner, out var p) ? p.Nick : null;
                return false;
            }
            _self.Claims[rootId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AnnounceSoon();
        }
        _signal.Release();
        Post(() => PeersChanged?.Invoke());
        return true;
    }

    public void Release(Guid rootId)
    {
        lock (_gate)
        {
            if (!_self.Claims.Remove(rootId)) return;
            AnnounceSoon();
        }
        _signal.Release();
        Post(() => PeersChanged?.Invoke());
    }

    /// <summary>Oublie les prises de lignes qui n'existent plus (personnage supprimé).</summary>
    public void ReleaseMissing(IReadOnlySet<Guid> existing)
    {
        bool changed;
        lock (_gate)
        {
            var gone = _self.Claims.Keys.Where(id => !existing.Contains(id)).ToList();
            foreach (var id in gone) _self.Claims.Remove(id);
            changed = gone.Count > 0;
            if (changed) AnnounceSoon();
        }
        if (!changed) return;
        _signal.Release();
        Post(() => PeersChanged?.Invoke());
    }

    /// <summary>Poste en lecture seule : il l'annonce aux autres et n'enverra plus d'état.</summary>
    public bool ReadOnly
    {
        get { lock (_gate) return _self.ReadOnly; }
        set
        {
            lock (_gate)
            {
                if (_self.ReadOnly == value) return;
                _self.ReadOnly = value;
                if (value) _stateDue = false;
                AnnounceSoon();
            }
            _signal.Release();
            Post(() => PeersChanged?.Invoke());
        }
    }

    /// <summary>Retient l'état partagé courant SANS l'envoyer : c'est ce que portera notre
    /// prochain envoi (réponse à un arrivant, prise, renvoi).</summary>
    public void SetShared(SharedTeamDoc shared)
    {
        lock (_gate)
        {
            _sharedDoc = (JsonObject)shared.Doc.DeepClone();
            _sharedStamps = new Dictionary<string, UnitStamp>(shared.Stamps);
            _sharedOrder = shared.Order.ToList();
        }
    }

    /// <summary>Retient l'état partagé ET l'envoie. Un état encore en attente est remplacé : seul
    /// le plus récent compte, les intermédiaires n'ont aucun intérêt pour les autres.</summary>
    public void Publish(SharedTeamDoc shared)
    {
        SetShared(shared);
        lock (_gate) AnnounceSoon();
        _signal.Release();
    }

    /// <summary>Diagnostic : trames du serveur qui n'étaient ni du protocole ni nos paquets.</summary>
    public IReadOnlyList<string> Diagnostics { get { lock (_gate) return _diagnostics.ToList(); } }

    /// <summary>Départ volontaire : désabonnement et fermeture (le serveur annonce le départ aux
    /// autres, qui libèrent aussitôt nos personnages). Borné à ~1 s.</summary>
    public async Task LeaveAsync()
    {
        ActionCableConnection? conn;
        lock (_gate)
        {
            if (_leaving) return;
            _leaving = true;
            conn = _conn;
        }
        if (conn is { IsOpen: true }) await conn.CloseAsync(TimeSpan.FromSeconds(1));
        End(RoomEndReason.Left, null);
    }

    public async ValueTask DisposeAsync()
    {
        List<ActionCableConnection> toDispose;
        lock (_gate)
        {
            _leaving = true;
            toDispose = [.. _retired];
            if (_conn is not null) toDispose.Add(_conn);
            _retired.Clear();
            _conn = null;
        }
        _cts.Cancel();
        foreach (var conn in toDispose) await conn.DisposeAsync();
        foreach (var t in new[] { _senderLoop, _timerLoop, _reconnectLoop })
            if (t is not null)
                try { await t.ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[Room] arrêt : {ex.Message}"); }
    }

    // ── Connexion ─────────────────────────────────────────────────────────────

    private Task<ActionCableConnection> ConnectAsync(CancellationToken ct)
        => ActionCableConnection.OpenAsync(_o.WebsocketUrl, _identifier, new ActionCableConnection.Handlers
        {
            Message = OnChannelMessage,
            Closed = OnClosed,
            Unknown = OnUnknownFrame,
        }, _o.ConnectTimeout, ct, _o.Origin);

    private void OnClosed(RoomCloseInfo info)
    {
        lock (_gate)
        {
            if (_leaving || Status == RoomStatus.Ended) return;
            if (_conn is not null) _retired.Add(_conn);
            _conn = null;
        }
        Debug.WriteLine($"[Room] connexion fermée : {info.Reason} ({info.RawReason}, {info.CloseCode})");

        if (info.Reason.IsTransient() || info.ServerAllowsReconnect)
        {
            if (info.Reason == RoomEndReason.RateLimited) lock (_gate) _minGap *= 2;
            if (info.Reason == RoomEndReason.TooLarge) lock (_gate) _maxBytes = Math.Max(1024, _maxBytes / 2);
            SetStatus(RoomStatus.Reconnecting);
            _reconnectLoop = Task.Run(ReconnectLoopAsync);
        }
        else
        {
            End(info.Reason, info.RawReason);
        }
    }

    private async Task ReconnectLoopAsync()
    {
        TimeSpan[] delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
                             TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];
        var started = DateTime.UtcNow;
        for (int attempt = 0; ; attempt++)
        {
            try { await Task.Delay(delays[Math.Min(attempt, delays.Length - 1)], _cts.Token); }
            catch (OperationCanceledException) { return; }

            lock (_gate) if (_leaving) return;
            try
            {
                var conn = await ConnectAsync(_cts.Token);
                bool keep;
                lock (_gate)
                {
                    keep = !_leaving;
                    if (keep)
                    {
                        _conn = conn;
                        _connectedUtc = DateTime.UtcNow;
                        _needAttempt = 0;
                        // Le serveur a annoncé notre départ dès la coupure : les autres ont libéré
                        // nos personnages, et l'un d'eux a pu en prendre un. Nos prises redeviennent
                        // RÉCENTES — sinon, étant les plus anciennes, elles le lui reprendraient à
                        // notre retour, pendant qu'il travaille dessus.
                        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        foreach (var id in _self.Claims.Keys.ToList()) _self.Claims[id] = now;
                        // On revient avec ce qu'on a fait pendant la coupure : les autres le
                        // fusionnent, et renvoient ce qu'ils ont de plus récent que nous.
                        AnnounceSoon();
                    }
                }
                if (!keep) { await conn.DisposeAsync(); return; }
                SetStatus(RoomStatus.Connected);
                _signal.Release();
                return;
            }
            catch (RoomConnectException ex) when (!ex.Info.Reason.IsTransient())
            {
                // Le salon a disparu pendant la coupure (expiration) : c'est une fin de session,
                // pas un code erroné — on le dit comme tel.
                End(ex.Info.Reason == RoomEndReason.NotFound ? RoomEndReason.Closed : ex.Info.Reason, ex.Info.RawReason);
                return;
            }
            catch (RoomConnectException ex)
            {
                Debug.WriteLine($"[Room] reconnexion ratée : {ex.Info.Reason} {ex.Message}");
            }
            catch (OperationCanceledException) { return; }

            if (DateTime.UtcNow - started > _o.ReconnectGiveUp)
            {
                End(RoomEndReason.NetworkLost, null);
                return;
            }
        }
    }

    private void End(RoomEndReason reason, string? detail)
    {
        lock (_gate)
        {
            if (Status == RoomStatus.Ended) return;
            EndReason = reason;
            EndDetail = detail;
            if (_conn is not null) _retired.Add(_conn);
            _conn = null;
        }
        SetStatus(RoomStatus.Ended);
        _cts.Cancel();
    }

    private void SetStatus(RoomStatus status)
    {
        lock (_gate)
        {
            if (Status == status) return;
            if (Status == RoomStatus.Ended) return;
            Status = status;
        }
        Post(() => StatusChanged?.Invoke());
    }

    // ── Réception ─────────────────────────────────────────────────────────────

    private void OnUnknownFrame(string text)
    {
        Debug.WriteLine($"[Room] trame inconnue : {Trunc(text)}");
        lock (_gate)
        {
            _diagnostics.Add(Trunc(text));
            if (_diagnostics.Count > 50) _diagnostics.RemoveAt(0);
        }
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private void OnChannelMessage(JsonElement message)
    {
        var type = Str(message, "type");
        switch (type)
        {
            case "room.ready":
                OnRoomReady(message);
                break;
            case "room.joined":
                OnJoined(Str(message, "connectionId"));
                break;
            case "room.left":
                OnLeft(Str(message, "connectionId"));
                break;
            case "room.expired":
                // La fermeture 4404 suit, avec le même motif ; la connexion qui DÉTECTE
                // l'expiration, elle, ne reçoit que la fermeture — les deux chemins finissent ici.
                var why = Str(message, "reason");
                var kind = RoomCloseInfo.Classify(why, null);
                End(kind is RoomEndReason.Expired or RoomEndReason.HostGone ? kind : RoomEndReason.Closed, why);
                break;
            default:
                // state.updated, ou toute enveloppe future : nos trames se reconnaissent partout.
                var payloads = RoomCodec.ExtractPayloads(message);
                if (payloads.Count == 0) { OnUnknownFrame(message.GetRawText()); return; }
                var sender = Str(message, "senderId");
                foreach (var p in payloads) ReceivePayload(p, sender, retained: false);
                break;
        }
    }

    /// <summary>
    /// Notre arrivée (ou notre retour) : notre identifiant de connexion, les présents, et le
    /// dernier message du salon. ⚠ Arrive AVANT la confirmation d'abonnement (mesuré).
    /// </summary>
    private void OnRoomReady(JsonElement message)
    {
        var notices = new List<RoomNotice>();
        lock (_gate)
        {
            _myConnId = Str(message, "connectionId");
            _present.Clear();
            if (message.TryGetProperty("participants", out var parts) && parts.ValueKind == JsonValueKind.Array)
                foreach (var c in parts.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.String) _present.Add(c.GetString()!);
            // Partis pendant notre absence (reconnexion) : le serveur ne nous l'a pas annoncé.
            // L'heure de départ de l'hôte n'est pas connue : « maintenant » est la plus tardive
            // possible, le bandeau dit donc « vers ».
            foreach (var gone in _peers.Values.Where(p => p.ConnectionId is { } c && !_present.Contains(c)).ToList())
                notices.Add(Forget(gone));
            if (Str(message, "expiresAt") is { } exp && DateTime.TryParse(exp, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var e))
                _expiresAt = e;
        }
        Post(() => PeersChanged?.Invoke());
        foreach (var n in notices) Post(() => Notice?.Invoke(n));

        // Le dernier message gardé par le serveur. Son émetteur n'est pas nommé et peut être
        // parti depuis : on n'en retient QUE le build, ni sa présence ni ses prises.
        if (message.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object)
            foreach (var p in RoomCodec.ExtractPayloads(state))
                ReceivePayload(p, sender: null, retained: true);
    }

    private void OnJoined(string? connId)
    {
        if (connId is null) return;
        lock (_gate)
        {
            if (connId == _myConnId) return;
            _present.Add(connId);
            // Un arrivant ne connaît du salon que son dernier message : chacun lui renvoie son
            // état (qui il est, ce qu'il a pris), avec un léger décalage aléatoire pour ne pas
            // tous parler dans la même milliseconde. Seulement avec un build à donner : un
            // message sans build écraserait celui que le serveur garde pour les arrivants.
            if (_announceAt is null && _sharedDoc is not null && !_self.ReadOnly)
                _announceAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(100 + Random.Shared.Next(500));
        }
        Post(() => PeersChanged?.Invoke());
    }

    private void OnLeft(string? connId)
    {
        if (connId is null) return;
        RoomNotice? notice = null;
        lock (_gate)
        {
            _present.Remove(connId);
            if (_peers.Values.FirstOrDefault(p => p.ConnectionId == connId) is { } gone) notice = Forget(gone);
        }
        Post(() => PeersChanged?.Invoke());
        if (notice is not null) Post(() => Notice?.Invoke(notice));
    }

    /// <summary>Retire un participant parti ; celui de l'hôte déclenche le compte à rebours de
    /// fermeture du salon. Sous <see cref="_gate"/>.</summary>
    private RoomNotice Forget(RoomPeer gone)
    {
        _peers.Remove(gone.ClientId);
        if (!gone.IsHost) return new RoomNotice(RoomNoticeKind.PeerLeft, gone.Nick);
        _hostAbsentSince = DateTime.UtcNow;
        return new RoomNotice(RoomNoticeKind.HostLeft, gone.Nick);
    }

    private void ReceivePayload(string base64, string? sender, bool retained)
    {
        RoomPacket? packet;
        lock (_gate) packet = _chunks.Add(base64);
        if (packet is not null) Handle(packet, sender, retained);
    }

    private void Handle(RoomPacket packet, string? senderConn, bool retained)
    {
        if (packet.From.Length == 0 || packet.From == _self.ClientId) return;   // notre propre état gardé

        RoomStateEventArgs? state = null;
        var notices = new List<RoomNotice>();
        bool peersChanged = false;
        lock (_gate)
        {
            if (_leaving) return;
            RoomPeer? peer = null;
            if (!retained)
            {
                if (!_peers.TryGetValue(packet.From, out peer))
                {
                    peer = new RoomPeer { ClientId = packet.From, ColorIndex = ++_colorCounter };
                    _peers[packet.From] = peer;
                    notices.Add(new RoomNotice(RoomNoticeKind.PeerJoined, packet.Nick));
                }
                if (senderConn is not null)
                {
                    peer.ConnectionId = senderConn;
                    _present.Add(senderConn);
                }
                if (packet.Nick is { Length: > 0 } nick) peer.Nick = nick.Length > 40 ? nick[..40] : nick;
                peer.IsHost = packet.Host;
                if (packet.Host) _hostAbsentSince = null;   // revenu (coupure réseau) : le salon vit
                peer.ReadOnly = packet.ReadOnly;
                peer.Claims = (packet.Claims ?? []).Where(c => c.Id != Guid.Empty)
                                .GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Min(c => c.At));
                peersChanged = true;

                // Les limites du salon ne sont connues que de l'hôte : il les relaie.
                if (packet.Host && packet.Limits is { } lim)
                {
                    _limits = lim;
                    ApplyLimits();
                }
            }

            if (packet.Kind == RoomPacket.KindState && packet.Doc is { } doc)
            {
                // Paquets d'un même émetteur hors d'ordre : le plus ancien est ignoré.
                bool fresh = retained || !_lastStateSeq.TryGetValue(packet.From, out var last) || packet.Seq > last;
                if (fresh)
                {
                    if (!retained) _lastStateSeq[packet.From] = packet.Seq;
                    state = new RoomStateEventArgs(doc, packet.Stamps, packet.Order, packet.From,
                                                   peer?.Nick ?? packet.Nick, ++_stateSerial);
                    LatestState = state;
                    _firstState.TrySetResult(state);
                }
            }

            if (!retained && packet.Kind == RoomPacket.KindHello && packet.Need > 0
                && _sharedDoc is not null && !_self.ReadOnly
                && (packet.Need >= 2 || IsKeeper(excluding: packet.From)))
                _stateDue = true;

            // Une prise que je croyais avoir, emportée par un autre arrivé avant moi.
            foreach (var id in _self.Claims.Keys.ToList())
            {
                var owner = ResolveOwner(id);
                if (owner is null || owner == _self.ClientId) continue;
                _self.Claims.Remove(id);
                notices.Add(new RoomNotice(RoomNoticeKind.ClaimLost, _peers.GetValueOrDefault(owner)?.Nick));
                AnnounceSoon();
            }
        }
        _signal.Release();

        if (peersChanged) Post(() => PeersChanged?.Invoke());
        foreach (var n in notices) Post(() => Notice?.Invoke(n));
        if (state is not null) Post(() => StateReceived?.Invoke(state));
    }

    /// <summary>Le gardien répond aux demandes d'état : l'hôte s'il est là, sinon le plus petit
    /// identifiant parmi ceux qui ont le document. Tous font le même calcul.</summary>
    private bool IsKeeper(string excluding)
    {
        var candidates = _peers.Values.Where(p => p.ClientId != excluding && !p.ReadOnly).ToList();
        if (_sharedDoc is not null && !_self.ReadOnly) candidates.Add(_self);
        var keeper = candidates.FirstOrDefault(p => p.IsHost)
                     ?? candidates.OrderBy(p => p.ClientId, StringComparer.Ordinal).FirstOrDefault();
        return keeper == _self;
    }

    /// <summary>Preneur d'une ligne : la prise la plus ancienne, à égalité le plus petit identifiant.
    /// Appelé sous verrou.</summary>
    private string? ResolveOwner(Guid id)
    {
        string? best = null;
        long bestAt = long.MaxValue;
        foreach (var p in _peers.Values.Append(_self))
        {
            if (!p.Claims.TryGetValue(id, out var at)) continue;
            if (at < bestAt || (at == bestAt && string.CompareOrdinal(p.ClientId, best) < 0))
            {
                best = p.ClientId;
                bestAt = at;
            }
        }
        return best;
    }

    // ── Émission ──────────────────────────────────────────────────────────────

    /// <summary>Notre état (ou, sans document, un simple hello) doit partir. Appelé sous verrou.</summary>
    private void AnnounceSoon()
    {
        if (_sharedDoc is not null && !_self.ReadOnly) _stateDue = true;
        else if (_helloNeed < 0) _helloNeed = 0;
    }

    /// <summary>Appelé sous verrou.</summary>
    private RoomPacket NewPacket(string kind)
    {
        var p = new RoomPacket
        {
            Kind = kind,
            From = _self.ClientId,
            Nick = _self.Nick,
            Host = IsHost,
            ReadOnly = _self.ReadOnly,
            App = _o.AppVersion,
            Format = _o.FormatVersion,
            Claims = _self.Claims.Select(kv => new RoomClaim { Id = kv.Key, At = kv.Value }).ToList(),
            Seq = ++_seq,
        };
        if (IsHost) p.Limits = _limits;
        return p;
    }

    private async Task SenderLoopAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _signal.WaitAsync(TimeSpan.FromSeconds(1), ct);
                while (!ct.IsCancellationRequested)
                {
                    RoomPacket? next = null;
                    ActionCableConnection? conn;
                    lock (_gate)
                    {
                        conn = _conn;
                        if (conn is null || !conn.IsOpen || _leaving) break;
                        if (_helloNeed >= 0)
                        {
                            next = NewPacket(RoomPacket.KindHello);
                            next.Need = _helloNeed;
                            _helloNeed = -1;
                        }
                        else if (_stateDue && _sharedDoc is not null && !_self.ReadOnly)
                        {
                            _stateDue = false;
                            next = NewPacket(RoomPacket.KindState);
                            next.Doc = (JsonObject)_sharedDoc.DeepClone();
                            next.Stamps = _sharedStamps;
                            next.Order = _sharedOrder;
                        }
                        if (next is null) break;
                    }

                    try
                    {
                        await SendPacketNowAsync(conn, next, ct);
                    }
                    catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or IOException
                                                  or ObjectDisposedException or InvalidOperationException)
                    {
                        // La connexion tombe : OnClosed prend la main, et notre état repartira au
                        // retour (AnnounceSoon de la reconnexion).
                        Debug.WriteLine($"[Room] envoi interrompu : {ex.Message}");
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SendPacketNowAsync(ActionCableConnection conn, RoomPacket packet, CancellationToken ct)
    {
        int maxBytes;
        lock (_gate) maxBytes = _maxBytes;
        foreach (var part in RoomCodec.Encode(packet, maxBytes))
        {
            TimeSpan wait;
            lock (_gate) wait = _lastSendUtc + _minGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            await conn.SendActionAsync(new Dictionary<string, string>
            {
                ["action"] = _o.PacketAction,
                [_o.PayloadField] = part,
            }, ct);
            lock (_gate) _lastSendUtc = DateTime.UtcNow;
        }
    }

    private async Task TimerLoopAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                bool signal = false;
                lock (_gate)
                {
                    if (_leaving) return;
                    var now = DateTime.UtcNow;

                    if (_announceAt is { } at && now >= at)
                    {
                        _announceAt = null;
                        AnnounceSoon();
                        signal = true;
                    }

                    // Arrivant toujours sans build : ni le dernier message du salon, ni personne ne
                    // le lui a donné. Il le demande ; à la 2e fois, tous ceux qui l'ont répondent
                    // (le gardien présumé est peut-être parti).
                    if (_wantState && _conn is not null && _sharedDoc is null && _stateSerial == 0 && _needAttempt < 6
                        && now - _connectedUtc >= _o.NeedDelay
                        && now - _lastNeedUtc >= TimeSpan.FromSeconds(_needAttempt == 0 ? 0 : _needAttempt == 1 ? 4 : 8))
                    {
                        _needAttempt++;
                        _lastNeedUtc = now;
                        _helloNeed = _needAttempt;
                        signal = true;
                    }
                }
                if (signal) _signal.Release();
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Divers ────────────────────────────────────────────────────────────────

    /// <summary>Appelé sous verrou ou au constructeur.</summary>
    private void ApplyLimits()
    {
        int mps = Math.Clamp(_limits.MessagesPerSecond, 1, 50);
        // 80 % de la limite : le serveur coupe au 11ᵉ message dans la seconde (mesuré).
        _minGap = TimeSpan.FromMilliseconds(Math.Ceiling(1000.0 / (mps * 0.8)));
        // Limite en octets DÉCODÉS (mesuré) ; marge pour l'en-tête de nos trames.
        int bytes = _limits.PayloadBytes is > 0 and <= 1_000_000 ? _limits.PayloadBytes : 16384;
        _maxBytes = Math.Max(1024, bytes - 64);
    }

    private void Post(Action a)
    {
        if (_o.Context is { } ctx) ctx.Post(_ => a(), null);
        else a();
    }

    private static RoomPeer Copy(RoomPeer p) => new()
    {
        ClientId = p.ClientId,
        Nick = p.Nick,
        IsHost = p.IsHost,
        IsMe = p.IsMe,
        ReadOnly = p.ReadOnly,
        ConnectionId = p.ConnectionId,
        Claims = new Dictionary<Guid, long>(p.Claims),
        ColorIndex = p.ColorIndex,
    };

    private static string Trunc(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
