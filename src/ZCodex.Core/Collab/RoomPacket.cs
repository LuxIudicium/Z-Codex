using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ZCodex.Core.Collab;

/// <summary>
/// Un paquet Z-Codex, tel qu'il voyage DANS le tuyau du serveur. Le serveur n'en voit qu'un bloc
/// Base64 opaque (<see cref="RoomCodec"/>) : il n'a jamais à connaître ce format, ni à suivre ses
/// versions (§2 de la demande).
///
/// Deux sortes (<see cref="Kind"/>) :
/// <list type="bullet">
/// <item><c>state</c> — le teambuild complet (<see cref="Doc"/>) avec qui l'envoie et ce qu'il a
///   pris. C'est l'envoi ORDINAIRE : à chaque modification, à chaque prise, à l'arrivée d'un
///   participant ;</item>
/// <item><c>hello</c> — sans document : un arrivant qui n'a rien reçu (<see cref="Need"/> &gt; 0),
///   ou un participant en lecture seule qui s'annonce.</item>
/// </list>
///
/// ⚠ Pourquoi presque tout est un état complet (mesuré le 29/09/2026) : le serveur garde le
/// DERNIER message du salon, quel qu'il soit, et le remet à chaque arrivant dans <c>room.ready</c>.
/// Des signes de vie ou un au revoir sans document l'écraseraient : l'arrivant recevrait un paquet
/// inutile. La présence (arrivées, départs) vient d'ailleurs, du serveur lui-même
/// (<c>room.joined</c> / <c>room.left</c>).
/// </summary>
public sealed class RoomPacket
{
    public const string KindHello = "hello";
    public const string KindState = "state";

    /// <summary>Version du protocole Z-Codex (pas celle du format .zcx).</summary>
    [JsonPropertyName("p")] public int Protocol { get; set; } = 2;
    [JsonPropertyName("k")] public string Kind { get; set; } = string.Empty;
    /// <summary>Identifiant de session de l'émetteur (stable à travers ses reconnexions,
    /// contrairement à l'identifiant de connexion du serveur).</summary>
    [JsonPropertyName("from")] public string From { get; set; } = string.Empty;
    [JsonPropertyName("nick")] public string? Nick { get; set; }
    [JsonPropertyName("host")] public bool Host { get; set; }
    [JsonPropertyName("ro")] public bool ReadOnly { get; set; }
    [JsonPropertyName("claims")] public List<RoomClaim>? Claims { get; set; }
    /// <summary>0 = rien ; n &gt; 0 = n-ième demande de l'état (à partir de 2, tout participant
    /// qui a le document répond, pas seulement le gardien désigné).</summary>
    [JsonPropertyName("need")] public int Need { get; set; }
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("app")] public string? App { get; set; }
    /// <summary>Version du format .zcx que l'émetteur sait écrire.</summary>
    [JsonPropertyName("fmt")] public int Format { get; set; }
    [JsonPropertyName("doc")] public JsonObject? Doc { get; set; }
    /// <summary>Horodatage de chaque unité du document (cf. <see cref="SharedTeamDoc"/>).</summary>
    [JsonPropertyName("stamps")] public Dictionary<string, UnitStamp>? Stamps { get; set; }
    /// <summary>Ordre horodaté des personnages, tel quel (cf. <see cref="SharedTeamDoc.Order"/>).</summary>
    [JsonPropertyName("order")] public List<string>? Order { get; set; }
    /// <summary>Limites du salon, relayées par l'hôte (seul à les recevoir du serveur).</summary>
    [JsonPropertyName("limits")] public GwRankRoomLimits? Limits { get; set; }
}

/// <summary>
/// Mise en forme des paquets pour le serveur : JSON → Deflate → trame binaire → Base64.
///
/// ⚠ Le serveur EXIGE du Base64 pur dans <c>payload</c> (mesuré le 29/09/2026 : tout autre texte,
/// même un simple préfixe « zcx1: », est refusé par <c>invalid_payload</c> et l'émetteur coupé).
/// La signature qui fait reconnaître nos paquets est donc DANS les octets : « ZX », version 1,
/// puis la sorte de trame.
///
/// Taille : le serveur refuse au-delà de <c>payloadBytes</c> (16 384) octets DÉCODÉS (mesuré :
/// 16 000 octets passent, 20 000 sont refusés par <c>payload_too_large</c>). Un .zcx se compresse
/// d'un facteur 6 à 10 — le plus gros de la bibliothèque de Philippe (28,7 Ko) tient en 2,6 Ko —
/// le découpage en morceaux reste donc l'exception.
/// </summary>
public static class RoomCodec
{
    private const byte Magic0 = (byte)'Z', Magic1 = (byte)'X', FrameVersion = 1;
    private const byte FrameWhole = 0, FrameChunk = 1;
    private const int HeaderWhole = 4;
    private const int HeaderChunk = 4 + 4 + 2;   // + identifiant du paquet + rang + nombre

    /// <summary>Plafond d'un paquet décompressé. Les paquets viennent d'inconnus : un envoi
    /// malveillant ne doit pas pouvoir faire exploser la mémoire (bombe de décompression).</summary>
    private const int MaxInflatedBytes = 4 * 1024 * 1024;
    public const int MaxChunks = 64;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Encode un paquet en un ou plusieurs blocs Base64 de <paramref name="maxBytes"/>
    /// octets décodés au plus.</summary>
    public static List<string> Encode(RoomPacket packet, int maxBytes)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(packet, Json);
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(json);
        var body = ms.ToArray();

        if (HeaderWhole + body.Length <= maxBytes)
        {
            var whole = new byte[HeaderWhole + body.Length];
            whole[0] = Magic0; whole[1] = Magic1; whole[2] = FrameVersion; whole[3] = FrameWhole;
            body.CopyTo(whole, HeaderWhole);
            return [Convert.ToBase64String(whole)];
        }

        int room = maxBytes - HeaderChunk;
        if (room < 64) throw new InvalidOperationException("taille de message admise trop petite");
        int n = (body.Length + room - 1) / room;
        if (n > MaxChunks) throw new InvalidOperationException($"paquet trop gros ({body.Length} octets compressés)");

        uint id = BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(4));
        var parts = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            int len = Math.Min(room, body.Length - i * room);
            var frame = new byte[HeaderChunk + len];
            frame[0] = Magic0; frame[1] = Magic1; frame[2] = FrameVersion; frame[3] = FrameChunk;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), id);
            frame[8] = (byte)i;
            frame[9] = (byte)n;
            Array.Copy(body, i * room, frame, HeaderChunk, len);
            parts.Add(Convert.ToBase64String(frame));
        }
        return parts;
    }

    /// <summary>La chaîne est-elle une trame Z-Codex ? Rend ses octets, ou null.</summary>
    public static byte[]? TryFrame(string? text)
    {
        if (text is null || text.Length < 8 || text.Length > 4 * MaxInflatedBytes) return null;
        var buffer = new byte[text.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(text, buffer, out int written) || written < HeaderWhole) return null;
        if (buffer[0] != Magic0 || buffer[1] != Magic1 || buffer[2] != FrameVersion) return null;
        return buffer.AsSpan(0, written).ToArray();
    }

    /// <summary>Décompresse le corps d'un paquet entier. Null si illisible — un paquet abîmé ou
    /// étranger est ignoré, jamais fatal.</summary>
    internal static RoomPacket? DecodeBody(ReadOnlySpan<byte> deflated)
    {
        try
        {
            using var input = new DeflateStream(new MemoryStream(deflated.ToArray()), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxInflatedBytes) return null;
            }
            return JsonSerializer.Deserialize<RoomPacket>(output.GetBuffer().AsSpan(0, (int)output.Length), Json);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            Debug.WriteLine($"[Room] paquet illisible : {ex.Message}");
            return null;
        }
    }

    /// <summary>Décode un paquet tenant en une seule trame (raccourci des tests).</summary>
    public static RoomPacket? DecodeWhole(string base64)
        => TryFrame(base64) is { } f && f[3] == FrameWhole ? DecodeBody(f.AsSpan(HeaderWhole)) : null;

    /// <summary>
    /// Toutes les chaînes d'un message du serveur qui sont des trames Z-Codex, à n'importe quelle
    /// profondeur : <c>payload</c> de <c>state.updated</c>, <c>state.payload</c> de
    /// <c>room.ready</c>… On cherche partout plutôt que de dépendre de l'enveloppe : si elle
    /// évolue, rien n'est à changer ici.
    /// </summary>
    public static List<string> ExtractPayloads(JsonElement message)
    {
        var found = new List<string>();
        void Walk(JsonElement e, int depth)
        {
            if (depth > 16) return;
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    var s = e.GetString();
                    if (TryFrame(s) is not null) found.Add(s!);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Walk(p.Value, depth + 1);
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) Walk(x, depth + 1);
                    break;
            }
        }
        Walk(message, 0);
        return found;
    }

    internal static bool IsChunk(byte[] frame) => frame[3] == FrameChunk;
    internal static ReadOnlySpan<byte> WholeBody(byte[] frame) => frame.AsSpan(HeaderWhole);

    internal static (uint Id, int Index, int Count, byte[] Data)? ChunkParts(byte[] frame)
    {
        if (frame.Length < HeaderChunk) return null;
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4));
        int index = frame[8], count = frame[9];
        if (count is < 1 or > MaxChunks || index >= count) return null;
        return (id, index, count, frame[HeaderChunk..]);
    }
}

/// <summary>Recolle les morceaux d'un paquet découpé. Un morceau orphelin (émetteur parti au
/// milieu) est oublié au bout de <see cref="Expiry"/>.</summary>
public sealed class RoomChunkAssembler
{
    public static TimeSpan Expiry { get; set; } = TimeSpan.FromSeconds(20);

    private sealed class Pending
    {
        public byte[]?[] Parts = [];
        public int Received;
        public DateTime StartedUtc = DateTime.UtcNow;
    }

    private readonly Dictionary<uint, Pending> _pending = new();

    /// <summary>Ajoute une trame reçue (Base64). Rend le paquet quand c'en est un entier, ou quand
    /// ce morceau complète le sien ; null sinon.</summary>
    public RoomPacket? Add(string base64)
    {
        if (RoomCodec.TryFrame(base64) is not { } frame) return null;
        if (!RoomCodec.IsChunk(frame)) return RoomCodec.DecodeBody(RoomCodec.WholeBody(frame));

        Purge();
        if (RoomCodec.ChunkParts(frame) is not { } c) return null;
        if (!_pending.TryGetValue(c.Id, out var p))
            _pending[c.Id] = p = new Pending { Parts = new byte[]?[c.Count] };
        if (p.Parts.Length != c.Count) return null;
        if (p.Parts[c.Index] is null) { p.Parts[c.Index] = c.Data; p.Received++; }
        if (p.Received < c.Count) return null;

        _pending.Remove(c.Id);
        var body = p.Parts.SelectMany(x => x!).ToArray();
        return RoomCodec.DecodeBody(body);
    }

    private void Purge()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _pending.Where(kv => now - kv.Value.StartedUtc > Expiry).Select(kv => kv.Key).ToList())
            _pending.Remove(key);
    }
}
