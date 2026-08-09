using System.Text.Json;
using ZCodex.Core;

namespace ZCodex.Scraper;

public class ScrapeInfo
{
    public DateTime LastScrapeDate { get; set; }
    public int SkillCount { get; set; }

    // Date de la mise à jour GW que l'utilisateur a choisi d'ignorer (case à cocher de la
    // modale). Distincte de LastScrapeDate — ne représente PAS un vrai scrapping, juste un
    // acquittement. Tant qu'aucune mise à jour plus récente n'apparaît, la modale ne revient pas.
    public DateTime? IgnoredUpdateDate { get; set; }

    // Faux entre la sauvegarde intermédiaire (catalogue de base écrit, avant les phases longues)
    // et la fin du scrapping. C'est ce drapeau qui permet de proposer une reprise au démarrage
    // suivant si le téléchargement a été coupé.
    // Défaut à VRAI : un scrape_info.json écrit par une version antérieure n'a pas le champ, et
    // il désigne bien un scrapping mené à son terme.
    public bool Complete { get; set; } = true;

    private static string FilePath => AppPaths.In("scrape_info.json");

    public static ScrapeInfo? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try { return JsonSerializer.Deserialize<ScrapeInfo>(File.ReadAllText(FilePath)); }
        catch { return null; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
