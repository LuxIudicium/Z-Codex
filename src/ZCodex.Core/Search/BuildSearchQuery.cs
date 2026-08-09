using ZCodex.Core.Models;

namespace ZCodex.Core.Search;

// Requête de recherche de builds : critères combinés en ET logique.
// Un critère vide (groupes/dict vides, profession nulle) est ignoré.
public sealed class BuildSearchQuery
{
    // Compétences requises, un groupe par compétence pickée. Chaque groupe = Ids acceptables
    // (variantes PvE/PvP/toutes, résolues à la construction via SkillVariants.ResolveGroup).
    // Le build doit contenir AU MOINS UN Id de CHAQUE groupe : ET entre groupes, OU intra-groupe.
    // Ordre des slots ignoré (les slots = bindings de touches, aucune hiérarchie).
    public List<HashSet<int>> RequiredSkillGroups { get; } = new();

    // Seuils de caractéristiques, un par attribut contraint : build.Attributes[nom] ≥ ou ≤ valeur
    // selon l'opérateur choisi (au moins / au plus). Basé sur les points investis dans le template —
    // hors bonus runtime (mod of-the-profession, coiffe, consommables), qui ne sont pas persistés.
    public List<AttributeThreshold> AttributeThresholds { get; } = new();

    // Professions requises (null = indifférent).
    public Profession? Primary { get; set; }
    public Profession? Secondary { get; set; }

    public bool IsEmpty =>
        RequiredSkillGroups.Count == 0 && AttributeThresholds.Count == 0
        && Primary is null && Secondary is null;

    // Un perso matche si TOUS les critères renseignés sont satisfaits (ET logique).
    public bool Matches(CharacterBuild c)
    {
        if (Primary is { } p && c.PrimaryProfession != p) return false;
        if (Secondary is { } s && c.SecondaryProfession != s) return false;

        foreach (var t in AttributeThresholds)
        {
            // Attribut absent du build ⇒ 0 investi (les caracs non investies ne sont pas persistées).
            int have = c.Attributes.GetValueOrDefault(t.Attribute);
            bool ok = t.Comparison == AttributeComparison.AtMost ? have <= t.Value : have >= t.Value;
            if (!ok) return false;
        }

        if (RequiredSkillGroups.Count > 0)
        {
            var present = c.Skills.Where(sk => sk != null).Select(sk => sk!.Id).ToHashSet();
            foreach (var group in RequiredSkillGroups)
                if (!group.Overlaps(present)) return false;
        }

        return true;
    }
}

// Sens de comparaison d'un seuil de caractéristique.
public enum AttributeComparison { AtLeast, AtMost }

// Seuil sur une caractéristique : « nom » comparé à « valeur » selon « comparison ».
public sealed record AttributeThreshold(string Attribute, int Value, AttributeComparison Comparison);
