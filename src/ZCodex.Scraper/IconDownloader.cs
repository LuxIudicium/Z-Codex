using System.IO;
using System.Net.Http;

namespace ZCodex.Scraper;

/// <summary>Bilan d'une passe de vérification d'icônes, agrégeable entre services.</summary>
public readonly record struct IconReport(int Ok, int Repaired, int Failed)
{
    public static IconReport operator +(IconReport a, IconReport b)
        => new(a.Ok + b.Ok, a.Repaired + b.Repaired, a.Failed + b.Failed);

    public int Total => Ok + Repaired + Failed;
}

/// <summary>
/// Socle commun des cinq services d'icônes à jeu fixe (stats, professions, conditions, flux,
/// vol de vie). Ces fichiers ne sont pas redistribués avec l'application — ce sont des
/// ressources du jeu — mais récupérés sur le wiki, sur la machine de l'utilisateur.
///
/// Chaque service se contentait de « le fichier existe → passer ». Trois défauts en
/// découlaient, et un utilisateur s'est retrouvé sans AUCUNE icône de coût/cast/recharge dans
/// ses infobulles, sans le moindre message :
///   - un téléchargement raté ne laissait aucune trace, et n'était jamais retenté dans la session ;
///   - un fichier VIDE ou TRONQUÉ satisfait <c>File.Exists</c> : il n'était donc plus jamais
///     retéléchargé et la panne devenait définitive, y compris après réinstallation puisque le
///     dossier de données survit à la désinstallation ;
///   - aucun scraping du catalogue ne les répare : ils ne sont tirés qu'au démarrage.
///
/// D'où les trois garanties d'ici : un fichier est jugé sur sa SIGNATURE et non sur sa
/// présence ; on n'écrit qu'après validation des octets reçus, via un temporaire renommé
/// (jamais de coquille à mi-chemin) ; et la passe RAPPORTE ce qu'elle a réparé ou raté.
/// </summary>
public static class IconDownloader
{
    // Client partagé par les cinq services. Chacun créait le sien : cinq rafales concurrentes
    // vers le wiki, en plus du scrape du catalogue lancé dans la foulée — de quoi se faire
    // étrangler par le serveur, ce qui est la panne d'origine la plus probable. Un seul client
    // réutilise ses connexions et sérialise la charge.
    private static readonly HttpClient Shared = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0");
        return http;
    }

    /// <summary>URL wiki d'un fichier, par son nom de page (<c>File:Energy.png</c>).</summary>
    public static string WikiUrl(string fileName)
        => $"https://wiki.guildwars.com/wiki/Special:FilePath/{fileName}";

    /// <summary>
    /// Le fichier est-il présent ET réellement exploitable ? « Présent » ne suffit pas : une
    /// écriture interrompue laisse un fichier de zéro octet, et un réseau qui répond une page
    /// d'erreur en 200 laisse du HTML sous un nom .png. Les deux bloquaient le retéléchargement.
    /// </summary>
    public static bool IsUsable(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            // Ces icônes pèsent quelques kilo-octets : on lit le fichier entier plutôt que de
            // jongler avec des lectures partielles, et on le juge sur ses deux extrémités.
            return File.Exists(path) && LooksLikeImage(File.ReadAllBytes(path));
        }
        catch { return false; }   // verrou, droits refusés → considéré à refaire
    }

    // PNG : en-tête 89 50 4E 47, bloc final IEND (+ son CRC). JPEG : SOI FF D8 FF, EOI FF D9.
    // Les deux seuls formats servis par le wiki pour ces icônes. Vérifier la QUEUE autant que la
    // tête est le seul moyen de repérer un fichier tronqué : une écriture coupée en cours de
    // route garde une signature de tête parfaitement valide. Vérifié sur les fichiers réels —
    // aucun des PNG ni des JPEG du wiki ne porte de remplissage après sa marque de fin.
    private static bool LooksLikeImage(byte[] data)
    {
        if (data.Length < 16) return false;

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return data[^8] == 0x49 && data[^7] == 0x45 && data[^6] == 0x4E && data[^5] == 0x44;

        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return data[^2] == 0xFF && data[^1] == 0xD9;

        return false;
    }

    /// <summary>
    /// Vérifie chaque icône et retélécharge celles qui manquent ou sont illisibles. Ne lève
    /// jamais : une icône absente dégrade l'affichage, elle ne doit pas empêcher l'application
    /// de démarrer. Le bilan retourné est le seul canal d'information — d'où son exploitation
    /// par « Extras → Vérifier les icônes ».
    /// </summary>
    public static async Task<IconReport> EnsureAsync(
        IEnumerable<(string Url, string LocalPath)> items, HttpClient? http = null)
    {
        http ??= Shared;
        var report = new IconReport();

        foreach (var (url, localPath) in items)
        {
            if (IsUsable(localPath))
            {
                // Reliquat d'une passe précédente interrompue : rien à réparer ici, mais on ne
                // laisse pas traîner le temporaire.
                TryDelete(localPath + ".tmp");
                report += new IconReport(1, 0, 0);
                continue;
            }

            try
            {
                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var bytes = await http.GetByteArrayAsync(url);

                // Validation AVANT écriture : une réponse 200 qui n'est pas une image (page
                // d'erreur du wiki, portail captif d'un réseau public) ne doit pas s'installer
                // en cache sous un nom d'icône, sinon elle y reste et masque la vraie panne.
                if (!LooksLikeImage(bytes))
                {
                    report += new IconReport(0, 0, 1);
                    continue;
                }

                // Écriture atomique : tant que le contenu n'est pas entièrement sur le disque il
                // porte un nom temporaire. Une fermeture de l'application au mauvais moment — le
                // premier démarrage dure plusieurs minutes, beaucoup d'utilisateurs coupent
                // pendant — laisse au pire un .tmp orphelin, jamais une icône à moitié écrite.
                var tmp = localPath + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes);

                report += await ReplaceAsync(tmp, localPath)
                    ? new IconReport(0, 1, 0)
                    : new IconReport(0, 0, 1);
            }
            catch
            {
                // Hors ligne, wiki injoignable, 429, antivirus : rien d'autre à faire que de le
                // compter. La prochaine passe (démarrage suivant ou menu Extras) réessaiera,
                // puisque rien d'inexploitable n'a été laissé sur le disque.
                report += new IconReport(0, 0, 1);
            }
        }

        return report;
    }

    /// <summary>
    /// Met le temporaire à la place de la cible, en réessayant brièvement.
    ///
    /// Le remplacement peut buter sur un verrou TRANSITOIRE de la cible : l'interface vient
    /// peut-être d'ouvrir la même icône corrompue pour tenter de l'afficher. Abandonner au
    /// premier échec, c'est ce qui laissait un fichier vide survivre à sa propre réparation —
    /// reproduit sur energy.png vidé à zéro octet, qui restait vide passage après passage
    /// pendant que son .tmp s'éternisait à côté. La cause première est traitée côté affichage
    /// (décodage en mémoire, jamais de fichier ouvert par WPF) ; ces essais sont la ceinture.
    /// </summary>
    private static async Task<bool> ReplaceAsync(string tmp, string dest)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Move(tmp, dest, overwrite: true);
                return true;
            }
            catch
            {
                await Task.Delay(200);
            }
        }

        TryDelete(tmp);   // échec définitif : pas de .tmp orphelin, la prochaine passe repartira propre
        return false;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* sans importance */ }
    }
}
