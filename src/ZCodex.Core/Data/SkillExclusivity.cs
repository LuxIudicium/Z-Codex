namespace ZCodex.Core.Data;

/// <summary>
/// Familles dont un perso ne porte qu'un effet à la fois (wiki *Effect stacking* : « one stance, one
/// preparation, one glyph, one weapon spell, and one form at a time », plus un seul objet tenu, donc un
/// seul sort d'objet). Règle tranchée par Philippe au lot 3 du chantier infobulle.
///
/// Remontée ici depuis <c>CharacterSlotViewModel</c> au lot 1 du chantier « calculateur d'armure :
/// réductions de dégâts » pour que l'onglet Armure applique la MÊME règle sans la recopier.
/// </summary>
public static class SkillExclusivity
{
    /// <summary>Les types de compétence (tels que la base les écrit) qui forment une famille exclusive.</summary>
    public static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.Ordinal)
        { "Glyph", "Stance", "Preparation", "Form", "Item Spell", "Weapon Spell" };

    /// <summary>Famille exclusive d'un type de compétence, null s'il n'en a pas.</summary>
    public static string? FamilyOf(string? skillType) =>
        skillType is not null && Families.Contains(skillType) ? skillType : null;
}
