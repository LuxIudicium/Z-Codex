using AngleSharp.Dom;

namespace ZCodex.Scraper;

/// <summary>
/// Parse la table de progression d'une page de compétence du wiki GW1.
/// La table canonique a <c>class="skill-progression"</c> (les tables d'aide, sans cette
/// classe, sont ignorées). Structure transposée : la 1re &lt;td&gt; porte les libellés
/// (div.attr = attribut, div.var = variables) ; chaque <c>div.column</c> est un rang
/// (div.attr = numéro de rang, div.var = valeurs dans l'ordre des libellés).
/// </summary>
public static class WikiProgressionParser
{
    /// <summary>Variables × rangs (Progression[v][rank]), ou null si pas de table exploitable.</summary>
    public static string[][]? Parse(IDocument doc)
    {
        var table = doc.QuerySelector("table.skill-progression");
        if (table is null) return null;

        var labelTd = table.QuerySelector("td");
        int nVars = labelTd?.QuerySelectorAll("div.var").Length ?? 0;
        if (nVars == 0) return null;

        var perVar = new List<List<string>>();
        for (int v = 0; v < nVars; v++) perVar.Add(new List<string>());

        foreach (var col in table.QuerySelectorAll("div.column"))
        {
            var vals = col.QuerySelectorAll("div.var").Select(d => d.TextContent.Trim()).ToList();
            if (vals.Count == 0) continue;
            for (int v = 0; v < nVars; v++)
                perVar[v].Add(v < vals.Count ? vals[v] : string.Empty);
        }

        return perVar[0].Count == 0 ? null : perVar.Select(l => l.ToArray()).ToArray();
    }
}
