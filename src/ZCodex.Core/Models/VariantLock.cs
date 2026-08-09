namespace ZCodex.Core.Models;

// "Cadenas" indicé reliant des lignes (racines et/ou variantes) entre elles pour former des
// compositions cohérentes. Persisté depuis la v2 du format natif.
// Aucune restriction d'association : une ligne peut appartenir à plusieurs cadenas (indices distincts).
public class VariantLock
{
    public int Index { get; set; }                      // indice affiché sur le cadenas (1, 2, …)
    public string Color { get; set; } = string.Empty;   // couleur du cadre/cadenas
    public List<Guid> MemberIds { get; set; } = new();  // CharacterBuild.Id (racines OU variantes)
}
