namespace ZCodex.Scraper;

/// <summary>
/// Progressions saisies à la main pour les rares pages wiki sans table
/// <c>table.skill-progression</c> standard (<see cref="WikiProgressionParser"/> renvoie null) :
/// sinon leurs plages « a...b...c » de description ne se résolvent JAMAIS (ni tooltip, ni spike —
/// la valeur reste affichée en plage). Repli appliqué par <see cref="FillGaps"/> dans le scraper, juste
/// AVANT les textes français : ne remplit QUE si la progression scrapée est vide, donc sans effet dès que
/// le wiki fournit la table.
///
/// Valeurs prises du wiki (source brute), une variable = un tableau de 22 rangs (0..21) comme la
/// sortie du parser. Ancres concises vérifiées : indices 0 / 12 / 15 = les 3 valeurs de la plage.
/// </summary>
public static class ManualProgression
{
    public static readonly IReadOnlyDictionary<string, string[][]> ByName =
        new Dictionary<string, string[][]>(StringComparer.Ordinal)
        {
            // Rising Bile (Death Magic) — « deals 1...5...6 damage for each second ».
            // Formule wiki floor((rang+1)/3)+1 : ancres 0→1, 12→5, 15→6.
            ["Rising Bile"] =
            [
                ["1", "1", "2", "2", "2", "3", "3", "3", "4", "4", "4",
                 "5", "5", "5", "6", "6", "6", "7", "7", "7", "8", "8"],
            ],
        };

    /// <summary>Remplit les progressions vides par la table ci-dessus (JSON, comme la passe de scraping) ;
    /// renvoie le nombre de compétences remplies. À appeler AVANT <c>GwikiFrScraper.Apply</c> : son contrôle
    /// des plages compare la description française à la progression, et une progression encore vide marquait
    /// la page « suspecte » — la description de Rising Bile (Fiel) repassait alors en anglais à chaque mise à
    /// jour du catalogue (corrigé le 10/10/2026).</summary>
    public static int FillGaps(IEnumerable<ZCodex.Data.Entities.SkillEntity> skills)
    {
        int filled = 0;
        foreach (var s in skills)
            if (string.IsNullOrEmpty(s.Progression) && ByName.TryGetValue(s.Name, out var prog))
            {
                s.Progression = System.Text.Json.JsonSerializer.Serialize(prog);
                filled++;
            }
        return filled;
    }
}
