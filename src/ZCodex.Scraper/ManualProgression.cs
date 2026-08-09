namespace ZCodex.Scraper;

/// <summary>
/// Progressions saisies à la main pour les rares pages wiki sans table
/// <c>table.skill-progression</c> standard (<see cref="WikiProgressionParser"/> renvoie null) :
/// sinon leurs plages « a...b...c » de description ne se résolvent JAMAIS (ni tooltip, ni spike —
/// la valeur reste affichée en plage). Repli appliqué dans <see cref="SkillUpdateService"/> : ne
/// remplit QUE si la progression scrapée est vide, donc sans effet dès que le wiki fournit la table.
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
}
