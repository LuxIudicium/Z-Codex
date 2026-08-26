using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZCodex.Core.Models;
using ZCodex.Core.Serialization;

namespace ZCodex.Core.Sync;

/// <summary>Ce que l'index retient d'un teambuild déjà déposé.</summary>
public sealed class GwRankEntry
{
    /// <summary>Chemin du fichier local au moment du dépôt. Vide si le build n'avait pas encore
    /// été enregistré.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Empreinte du contenu déposé, <c>updatedAt</c> EXCLU (cf. <see cref="ZcxHash"/>).</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Empreinte rendue par le SERVEUR au dernier dépôt (<c>documentHash</c>).
    ///
    /// Elle ne se recalcule pas en local — le serveur normalise certains champs, et l'empreinte
    /// porte sur ce qu'IL a stocké. Renvoyée en <c>If-Match</c> au dépôt suivant, elle fait
    /// refuser l'envoi si une autre machine a modifié le build entre-temps. Vide = dépôt
    /// inconditionnel, comme avant.
    /// </summary>
    public string ServerHash { get; set; } = string.Empty;

    /// <summary>Id numérique attribué par le serveur, pour un lien direct vers la fiche.</summary>
    public long ServerId { get; set; }

    /// <summary>Nom sous lequel ce build a été déposé. L'utilisateur peut le changer au moment de
    /// l'envoi : on le reprend tel quel au dépôt suivant, sinon son choix serait perdu à chaque
    /// fois au profit du nom de fichier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>« private » ou « public » lors du dernier dépôt. Sert à rappeler à l'utilisateur
    /// où en est CE build : repasser un build partagé en privé (ou l'inverse) sans s'en rendre
    /// compte est exactement ce qu'il ne faut pas pouvoir faire par inadvertance.</summary>
    public string Visibility { get; set; } = string.Empty;

    public DateTime LastUploadUtc { get; set; }
}

/// <summary>Verdict rendu avant un envoi.</summary>
public enum GwRankUploadCheck
{
    /// <summary>Rien de déposé sous cette identité, ou même fichier avec un contenu modifié.</summary>
    Ready,
    /// <summary>Contenu identique au dernier dépôt : l'envoi ne changerait rien.</summary>
    Unchanged,
    /// <summary>⚠ Cette identité est déjà prise par un AUTRE fichier local. Envoyer tel quel
    /// écraserait le build de l'autre fichier côté serveur.</summary>
    IdentityCollision,
}

/// <summary>Verdict + le chemin en conflit s'il y en a un.</summary>
public sealed record GwRankUploadVerdict(GwRankUploadCheck Check, string? ConflictingPath);

/// <summary>
/// Empreinte d'un teambuild, <c>updatedAt</c> exclu.
///
/// Pourquoi l'exclure : Z-Codex réécrit <c>updatedAt</c> à <c>UtcNow</c> à CHAQUE enregistrement,
/// même quand rien n'a changé (cf. <c>docs/zcx_format.md</c> §4). Le garder ferait voir un
/// changement à chaque sauvegarde et redéposerait sans cesse les mêmes builds.
/// </summary>
public static class ZcxHash
{
    public static string OfBuild(TeamBuild build) => OfJson(TeamBuildSerializer.Serialize(build));

    /// <summary>Empreinte d'un document `.zcx` déjà sérialisé. Passe par l'arbre JSON plutôt que
    /// par du remplacement de texte : une note d'utilisateur contenant « updatedAt » ne doit pas
    /// pouvoir fausser l'empreinte.</summary>
    public static string OfJson(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject root)
            {
                root.Remove("updatedAt");
                json = root.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Document illisible : on empreinte le texte tel quel. Il ne sera de toute façon pas
            // accepté par le serveur, mais l'appelant ne doit pas planter ici.
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}

/// <summary>
/// Index local des teambuilds déposés sur GWRank, dans <c>%AppData%\Z-Codex\gwrank_sync.json</c>.
///
/// Il existe pour une raison précise : le <c>id</c> d'un `.zcx` n'est PAS unique entre fichiers —
/// dupliquer un build dans l'explorateur produit deux fichiers de même identité (mesuré : 2 paires
/// sur les 265 fichiers de la bibliothèque de référence). L'API GWRank étant clé par cette
/// identité, déposer les deux ferait disparaître le premier SANS AVERTISSEMENT.
///
/// L'index retient donc quel FICHIER a déposé quelle identité, ce qui permet de repérer la
/// collision avant l'envoi plutôt qu'après la perte.
/// </summary>
public sealed class GwRankSyncIndex
{
    private static string FilePath => AppPaths.In("gwrank_sync.json");

    /// <summary>Clé = <c>id</c> du teambuild en minuscules.</summary>
    public Dictionary<string, GwRankEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static GwRankSyncIndex Load()
    {
        if (!File.Exists(FilePath)) return new GwRankSyncIndex();
        try
        {
            var loaded = JsonSerializer.Deserialize<GwRankSyncIndex>(File.ReadAllText(FilePath));
            if (loaded is null) return new GwRankSyncIndex();
            // Le dictionnaire désérialisé perd le comparateur insensible à la casse.
            loaded.Entries = new Dictionary<string, GwRankEntry>(loaded.Entries, StringComparer.OrdinalIgnoreCase);
            return loaded;
        }
        // Même politique qu'AppSettings : un index illisible ne vaut pas un envoi raté. On repart
        // de zéro — au pire on redépose des builds déjà à jour, ce qui est inoffensif.
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] index illisible ({FilePath}) — {ex.Message}");
            return new GwRankSyncIndex();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] écriture index impossible — {ex.Message}");
        }
    }

    /// <summary>
    /// À appeler AVANT tout envoi.
    ///
    /// <paramref name="filePath"/> null ou vide = build jamais enregistré sur disque : on ne peut
    /// alors rien affirmer sur une collision (aucun chemin à comparer), donc on laisse passer.
    /// </summary>
    public GwRankUploadVerdict Check(TeamBuild build, string? filePath)
    {
        var key = build.Id.ToString("D");
        if (!Entries.TryGetValue(key, out var entry))
            return new GwRankUploadVerdict(GwRankUploadCheck.Ready, null);

        // Identité déjà déposée par un AUTRE fichier : c'est la collision qu'on cherche.
        if (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(entry.FilePath)
            && !PathsEqual(entry.FilePath, filePath))
            return new GwRankUploadVerdict(GwRankUploadCheck.IdentityCollision, entry.FilePath);

        return ZcxHash.OfBuild(build) == entry.ContentHash
            ? new GwRankUploadVerdict(GwRankUploadCheck.Unchanged, null)
            : new GwRankUploadVerdict(GwRankUploadCheck.Ready, null);
    }

    /// <summary>Enregistre un dépôt réussi. Sans <see cref="Save"/> l'index reste en mémoire :
    /// l'appelant sauvegarde une fois à la fin d'un lot plutôt qu'à chaque fichier.</summary>
    public void Record(TeamBuild build, string? filePath, long serverId, string? visibility = null,
                       string? serverHash = null)
    {
        Entries[build.Id.ToString("D")] = new GwRankEntry
        {
            FilePath      = filePath ?? string.Empty,
            ContentHash   = ZcxHash.OfBuild(build),
            ServerHash    = serverHash ?? string.Empty,
            ServerId      = serverId,
            Visibility    = visibility ?? string.Empty,
            Name          = build.Name,
            LastUploadUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Empreinte serveur du dernier dépôt de ce build, à renvoyer en <c>If-Match</c>.
    ///
    /// Null quand on ne l'a pas — index d'avant cette fonction, ou build jamais déposé depuis ce
    /// poste. On dépose alors SANS condition : exiger une empreinte qu'on n'a pas ferait échouer
    /// des envois parfaitement légitimes, ce qui est pire que le risque qu'on cherche à couvrir.
    /// </summary>
    /// <summary>Cette identité a-t-elle déjà été déposée depuis ce poste ? Sépare « jamais
    /// envoyé » de « envoyé puis modifié », que <see cref="Check"/> confond tous deux en
    /// <see cref="GwRankUploadCheck.Ready"/>.</summary>
    public bool Knows(Guid id) => Entries.ContainsKey(id.ToString("D"));

    public string? ServerHashOf(Guid id)
        => Entries.TryGetValue(id.ToString("D"), out var e) && e.ServerHash.Length > 0
            ? e.ServerHash : null;

    /// <summary>Nom sous lequel ce build a été déposé, ou null s'il ne l'a jamais été depuis ce
    /// poste.</summary>
    public string? NameOf(Guid id)
        => Entries.TryGetValue(id.ToString("D"), out var e) && e.Name.Length > 0 ? e.Name : null;

    /// <summary>Visibilité du dernier dépôt de ce build (« private »/« public »), ou null s'il n'a
    /// jamais été déposé depuis ce poste.</summary>
    public string? VisibilityOf(Guid id)
        => Entries.TryGetValue(id.ToString("D"), out var e) && e.Visibility.Length > 0
            ? e.Visibility : null;

    /// <summary>Oublie une identité — après une suppression côté serveur, ou après avoir réattribué
    /// une identité neuve à un doublon.</summary>
    public void Forget(Guid id) => Entries.Remove(id.ToString("D"));

    /// <summary>
    /// Recherche INVERSE : sous quelle identité ce fichier a-t-il déjà été déposé ?
    ///
    /// Indispensable aux builds simples, qui vivent dans un `.txt` (format de template du jeu) et
    /// n'ont donc AUCUNE identité à eux. Sans cette reprise, chaque envoi d'un même build simple
    /// créerait un doublon de plus sur le serveur, puisque l'identité serait tirée au sort à
    /// chaque fois.
    /// </summary>
    public Guid? FindIdByPath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        foreach (var (key, entry) in Entries)
            if (!string.IsNullOrEmpty(entry.FilePath) && PathsEqual(entry.FilePath, filePath)
                && Guid.TryParse(key, out var id))
                return id;
        return null;
    }

    /// <summary>
    /// Répare une collision : donne une identité NEUVE au build, en préservant les identifiants
    /// des personnages (<c>locks</c> et <c>spike</c> les référencent — les changer casserait les
    /// cadenas et le roster du calculateur).
    ///
    /// Renvoie l'ancienne identité, pour que l'appelant puisse la citer dans son avertissement.
    /// </summary>
    public static Guid ReassignIdentity(TeamBuild build)
    {
        var previous = build.Id;
        build.Id = Guid.NewGuid();
        return previous;
    }

    /// <summary>
    /// Identifiant REPRODUCTIBLE tiré d'un chemin de fichier : le même chemin donne toujours le
    /// même Guid, sur toutes les machines.
    ///
    /// Sert aux builds simples. Un `.txt` de template ne stocke aucun identifiant, donc son
    /// personnage en reçoit un neuf à chaque ouverture — et comme cet identifiant fait partie du
    /// document, l'empreinte changeait à chaque réouverture : le « déjà à jour » ne tombait jamais
    /// et le serveur voyait bouger un build que personne n'avait touché.
    /// </summary>
    public static Guid DeterministicId(string path)
    {
        string seed;
        try { seed = Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant(); }
        catch { seed = path.ToLowerInvariant(); }

        // Les 16 premiers octets d'un SHA-256 suffisent : on cherche la reproductibilité, pas une
        // conformité RFC (ce Guid ne prétend pas être une UUID v5).
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, 16));
    }

    /// <summary>Comparaison de chemins tolérante à la casse et aux séparateurs — Windows accepte
    /// les deux, et l'utilisateur peut avoir ouvert le même fichier par deux chemins différents.</summary>
    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'),
                                 Path.GetFullPath(b).TrimEnd('\\', '/'),
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Chemin invalide (lecteur réseau tombé, caractères illégaux) : on compare le texte.
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
