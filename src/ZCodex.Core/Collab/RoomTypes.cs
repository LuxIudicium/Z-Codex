using System.Text.Json.Serialization;

namespace ZCodex.Core.Collab;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Session partagée (co-édition d'un teambuild en direct) — types communs.
//
// Le serveur (GWRank, Action Cable) n'est qu'un TUYAU AVEUGLE : il recopie nos paquets aux autres
// membres du salon sans rien y comprendre. Tout ce qui fait la session — qui est là, qui a pris
// quel personnage, comment deux modifications se rejoignent — vit ici, côté client.
// Spécification de la demande : docs/gwrank_collab_demande.md ; mesures du serveur réel : même
// dossier, docs/gwrank_collab_plan.md.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Limites annoncées par le serveur à la création du salon (<c>RoomLimits</c> de
/// l'OpenAPI). Seul l'hôte les reçoit : il les relaie aux autres dans ses paquets.</summary>
public sealed class GwRankRoomLimits
{
    public int Participants { get; set; } = 8;
    public int PayloadBytes { get; set; } = 16384;
    public int MessagesPerSecond { get; set; } = 10;
}

/// <summary>Réponse de <c>POST /api/v1/rooms</c>.</summary>
public sealed class GwRankRoom
{
    /// <summary>Code court, lisible à voix haute : <c>XXXX-XXX</c> (alphabet sans 0/O/1/I).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Secret du créateur. Ne quitte JAMAIS la machine de l'hôte : il n'est envoyé qu'au
    /// serveur, dans l'identifiant d'abonnement, et n'apparaît dans aucun paquet ni journal.</summary>
    public string CreatorSecret { get; set; } = string.Empty;

    public string WebsocketUrl { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public GwRankRoomLimits? Limits { get; set; }
}

/// <summary>Issue de la création d'un salon.</summary>
public sealed record RoomCreateResult(Sync.GwRankStatus Status, GwRankRoom? Room, bool RoomsFull, string? Message)
{
    public bool IsOk => Status == Sync.GwRankStatus.Ok && Room is not null;
}

/// <summary>État de la connexion au salon, tel que l'utilisateur doit le voir.</summary>
public enum RoomStatus
{
    Connecting,
    Connected,
    /// <summary>Connexion perdue, nouvelle tentative en cours. La session n'est PAS finie : le
    /// travail continue en local et sera fusionné au retour.</summary>
    Reconnecting,
    /// <summary>Terminée pour de bon (départ, salon fermé, refus définitif).</summary>
    Ended,
}

/// <summary>
/// Pourquoi une connexion s'est arrêtée. C'est ce qui permet d'afficher autre chose que
/// « déconnecté » (§4 n°7 de la demande) : un salon introuvable appelle une vérification du code,
/// un salon plein une autre session, une coupure réseau une simple attente.
/// </summary>
public enum RoomEndReason
{
    None,
    /// <summary>L'utilisateur a quitté la session.</summary>
    Left,
    /// <summary>Code inconnu du serveur (mesuré : <c>room_not_found</c>, fermeture 4404).</summary>
    NotFound,
    /// <summary>Salon au complet, ou serveur sans place (création : <c>rooms_full</c>).</summary>
    Full,
    /// <summary>Salon expiré ou fermé, sans plus de précision.</summary>
    Closed,
    /// <summary>Durée de vie atteinte : 2 h ABSOLUES depuis la création, que l'activité ne
    /// prolonge pas (<c>max_lifetime</c>).</summary>
    Expired,
    /// <summary>Le créateur est parti et n'est pas revenu dans les 5 minutes avec son secret
    /// (<c>creator_timeout</c>).</summary>
    HostGone,
    /// <summary>Secret de créateur ou clé refusés.</summary>
    Unauthorized,
    /// <summary>Trop de messages : le serveur demande de ralentir. Passager.</summary>
    RateLimited,
    /// <summary>Message trop gros pour le serveur. Passager : on redécoupe plus fin.</summary>
    TooLarge,
    /// <summary>Réseau coupé, serveur muet, relais silencieux. Passager.</summary>
    NetworkLost,
    /// <summary>Refus ou fermeture dont le motif n'est pas reconnu. Le motif brut est affiché.</summary>
    Unknown,
}

public static class RoomEndReasonExtensions
{
    /// <summary>Vrai pour les arrêts qu'une nouvelle tentative peut résoudre d'elle-même.</summary>
    public static bool IsTransient(this RoomEndReason r)
        => r is RoomEndReason.NetworkLost or RoomEndReason.RateLimited or RoomEndReason.TooLarge;
}

/// <summary>Fermeture d'une connexion : motif classé, plus ce que le serveur a dit mot pour mot.</summary>
public sealed record RoomCloseInfo(RoomEndReason Reason, string? RawReason, int? CloseCode, bool ServerAllowsReconnect)
{
    /// <summary>
    /// Classe un motif de fermeture. Liste officielle (OpenAPI de GWRank, 08/10/2026) :
    /// 1008 <c>invalid_payload</c> ; 1009 <c>payload_too_large</c> ; 1013 <c>stream_unavailable</c> ;
    /// 4401 <c>creator_secret_invalid</c> ou <c>creator_replaced</c> ; 4404 <c>room_not_found</c>,
    /// <c>max_lifetime</c> ou <c>creator_timeout</c> ; 4409 <c>room_full</c> ; 4429 <c>rate_limited</c>.
    /// Le motif arrive d'abord dans une trame <c>disconnect</c> SANS code, puis dans la fermeture
    /// avec son code : il doit donc se suffire à lui-même. Les motifs inconnus sont reconnus par
    /// mots-clés et par code, et tout ce qui n'est pas reconnu reste affiché tel quel plutôt que
    /// d'être réduit à « déconnecté ».
    /// </summary>
    public static RoomEndReason Classify(string? reason, int? closeCode)
    {
        var r = (reason ?? string.Empty).ToLowerInvariant();
        // Paquet mal formé : c'est notre faute, et le renvoyer à l'identique après reconnexion
        // donnerait le même refus en boucle. Fin de session, motif affiché.
        if (r.Contains("invalid_payload")) return RoomEndReason.Unknown;
        // AVANT le 4404 : l'expiration partage ce code avec le code inconnu.
        if (r.Contains("max_lifetime")) return RoomEndReason.Expired;
        if (r.Contains("creator_timeout")) return RoomEndReason.HostGone;
        // Serveur momentanément sans relais (1013 = « réessayez plus tard ») : on revient.
        if (r.Contains("unavailable") || closeCode == 1013) return RoomEndReason.NetworkLost;
        if (r.Contains("not_found") || r.Contains("unknown_room") || closeCode == 4404) return RoomEndReason.NotFound;
        if (r.Contains("full") || r.Contains("capacity") || closeCode == 4409) return RoomEndReason.Full;
        if (r.Contains("expired") || r.Contains("closed") || r.Contains("ended") || r.Contains("gone")
            || closeCode is 4410 or 4408) return RoomEndReason.Closed;
        if (r.Contains("unauthor") || r.Contains("forbidden") || r.Contains("secret") || r.Contains("token")
            || r.Contains("replaced")
            || closeCode is 4401 or 4403) return RoomEndReason.Unauthorized;
        if (r.Contains("rate") || r.Contains("too_many") || r.Contains("flood") || r.Contains("chatty")
            || r.Contains("throttl") || closeCode == 4429) return RoomEndReason.RateLimited;
        if (r.Contains("too_large") || r.Contains("payload") || r.Contains("size") || closeCode is 4413 or 1009)
            return RoomEndReason.TooLarge;
        // server_restart : redémarrage annoncé par Action Cable lui-même — on revient.
        if (r.Contains("restart")) return RoomEndReason.NetworkLost;
        return r.Length == 0 && closeCode is null or 1000 or 1001 or 1006 or 1011
            ? RoomEndReason.NetworkLost
            : RoomEndReason.Unknown;
    }
}

/// <summary>Échec d'ouverture d'une connexion au salon.</summary>
public sealed class RoomConnectException(RoomCloseInfo info, string message) : Exception(message)
{
    public RoomCloseInfo Info { get; } = info;
}

/// <summary>Un participant du salon, soi compris.</summary>
public sealed class RoomPeer
{
    public string ClientId { get; init; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsMe { get; init; }
    /// <summary>Participant en lecture seule : catalogue ou application trop anciens pour ce
    /// build. Il voit, mais ne renvoie rien — sinon il effacerait ce qu'il ne connaît pas.</summary>
    public bool ReadOnly { get; set; }
    /// <summary>Identifiant de connexion attribué par le serveur (<c>senderId</c>,
    /// <c>room.left</c>) ; change à chaque reconnexion. Null tant qu'on ne l'a pas vu.</summary>
    public string? ConnectionId { get; set; }
    /// <summary>Personnages pris : id → instant de la prise (ms Unix, horloge DU participant).</summary>
    public Dictionary<Guid, long> Claims { get; set; } = new();
    /// <summary>Rang d'arrivée dans la session, pour attribuer une couleur stable.</summary>
    public int ColorIndex { get; set; }
}

/// <summary>Un personnage pris, tel qu'il circule dans les paquets.</summary>
public sealed class RoomClaim
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("at")] public long At { get; set; }
}

/// <summary>Message ponctuel à afficher dans le bandeau de session.</summary>
public enum RoomNoticeKind
{
    PeerJoined,
    PeerLeft,
    /// <summary>L'hôte est parti : sans son retour, le serveur ferme le salon au bout de
    /// <see cref="RoomSession.HostGraceDelay"/>.</summary>
    HostLeft,
    /// <summary>Un personnage que je croyais prendre l'a été avant moi par quelqu'un d'autre.</summary>
    ClaimLost,
}

public sealed record RoomNotice(RoomNoticeKind Kind, string? Nick);
