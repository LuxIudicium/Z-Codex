namespace ZCodex.App.Settings;

// État runtime de l'affichage « Dégâts selon l'armure » (menu Affichage), initialisé depuis
// AppSettings au démarrage. Lu par SkillTooltipControl à chaque ouverture d'infobulle —
// pas d'event de changement : une infobulle se recalcule en s'affichant (Loaded).
public static class ArmorDamageDisplay
{
    public static bool Enabled { get; set; } = true;

    // Jusqu'à 8 AL personnalisées (bouton « + » de la modale), en plus des fixes.
    public static List<int> CustomArmorLevels { get; set; } = new();

    // Niveau du personnage (lanceur/attaquant, 1–20) : lignes de skills en 3×niveau et seuil
    // de rang d'arme (niveau+4)/2. Niveau de la cible (1–40) : taux de critique uniquement —
    // le wiki ne le fait entrer nulle part ailleurs (les dégâts ne voient que l'AL de la cible).
    public static int CharacterLevel { get; set; } = 20;

    public static int TargetLevel { get; set; } = 20;

    /// <summary>
    /// Colonnes d'AL affichées : 60/80/100/120 fixes + les personnalisées insérées à leur place
    /// (marquées custom). Une personnalisée égale à une fixe ou en doublon n'ajoute pas de colonne.
    /// </summary>
    public static IReadOnlyList<(int Al, bool IsCustom)> Columns()
    {
        var cols = new List<(int Al, bool IsCustom)> { (60, false), (80, false), (100, false), (120, false) };
        foreach (int c in CustomArmorLevels)
            if (!cols.Any(x => x.Al == c))
                cols.Add((c, true));
        cols.Sort((a, b) => a.Al.CompareTo(b.Al));
        return cols;
    }
}
