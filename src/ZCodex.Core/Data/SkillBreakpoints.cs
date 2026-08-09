namespace ZCodex.Core.Data;

/// <summary>
/// Calcul des « hard breakpoints » d'une compétence : les niveaux de caractéristique
/// où la valeur d'une variable change, après être restée constante sur une plage.
/// Sert au saut Alt+molette dans l'éditeur d'attributs.
///
/// Définition retenue (validée Philippe) : une variable n'a de hard breakpoints que si
/// elle présente à la fois un changement ET un palier. Une variable qui change à chaque
/// niveau (ex. Force de l'Honneur) ou qui ne change jamais n'a aucun hard breakpoint.
/// </summary>
public static class SkillBreakpoints
{
    /// <summary>Niveau max considéré pour choisir la variable (couvre le niveau effectif 0–20).</summary>
    public const int SelectionMaxLevel = 20;

    /// <summary>
    /// Niveaux de breakpoint de la skill dans <c>[0..snapMaxLevel]</c>, triés croissants
    /// (rang 0 inclus). Vide si aucune variable à breakpoints (skill linéaire/constante).
    /// <paramref name="forcedVariableIndex"/> = override manuel (cas ambigus).
    /// </summary>
    public static IReadOnlyList<int> Compute(string[][]? progression, int snapMaxLevel, int? forcedVariableIndex = null)
    {
        var v = SelectVariable(progression, forcedVariableIndex);
        return v is null ? Array.Empty<int>() : BreakpointsOf(v, snapMaxLevel);
    }

    /// <summary>
    /// Variable retenue pour les breakpoints : override si fourni ; sinon, parmi les variables
    /// à hard breakpoints, celle qui en a le moins (la plus « en escalier »). null si aucune.
    /// </summary>
    public static string[]? SelectVariable(string[][]? progression, int? forcedVariableIndex = null)
    {
        if (progression is null || progression.Length == 0) return null;

        if (forcedVariableIndex is int fi)
            return fi >= 0 && fi < progression.Length ? progression[fi] : null;

        string[]? best = null;
        int bestCount = int.MaxValue;
        foreach (var v in progression)
        {
            if (!HasHardBreakpoints(v)) continue;
            int count = BreakpointsOf(v, SelectionMaxLevel).Count;
            if (count < bestCount) { bestCount = count; best = v; }
        }
        return best;
    }

    /// <summary>Nombre de variables à hard breakpoints. ≥2 = cas ambigu (override recommandé).</summary>
    public static int HardBreakpointVariableCount(string[][]? progression)
        => progression?.Count(HasHardBreakpoints) ?? 0;

    /// <summary>
    /// Rang max représenté par la table (= nb de colonnes − 1). Pour les rangs de titre : 10
    /// (EotN/Sunspear) ou 12 (Allegiance Kurzick/Luxon). Défaut 10 si pas de table exploitable.
    /// </summary>
    public static int RankMax(string[][]? progression)
        => progression is { Length: > 0 } && progression[0].Length > 0
            ? progression.Max(v => v.Length) - 1
            : 10;

    /// <summary>
    /// Prochain breakpoint strictement au-dessus (<paramref name="direction"/> &gt; 0) ou en-dessous
    /// (&lt; 0) du niveau courant. null si aucun (la molette ne fait alors rien).
    /// </summary>
    public static int? Snap(IReadOnlyList<int> breakpoints, int current, int direction)
    {
        if (direction > 0)
        {
            foreach (var b in breakpoints)
                if (b > current) return b; // triée croissante → premier = plus proche
            return null;
        }
        int? best = null;
        foreach (var b in breakpoints)
        {
            if (b < current) best = b;
            else break;
        }
        return best;
    }

    // Une variable a des hard breakpoints ssi elle change au moins une fois ET stagne au moins
    // une fois sur [0..SelectionMaxLevel] — sinon palier absent (linéaire) ou changement absent.
    private static bool HasHardBreakpoints(string[] v)
    {
        int limit = Math.Min(SelectionMaxLevel, v.Length - 1);
        bool change = false, plateau = false;
        for (int r = 1; r <= limit; r++)
        {
            if (v[r] != v[r - 1]) change = true;
            else                  plateau = true;
        }
        return change && plateau;
    }

    private static List<int> BreakpointsOf(string[] v, int maxLevel)
    {
        var result = new List<int>();
        int limit = Math.Min(maxLevel, v.Length - 1);
        for (int r = 0; r <= limit; r++)
            if (r == 0 || v[r] != v[r - 1]) result.Add(r);
        return result;
    }
}
