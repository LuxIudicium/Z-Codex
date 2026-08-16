using ZCodex.Core.Models;
using ZCodex.Core.Templates;

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

    // Seuils de caractéristiques, un par attribut contraint : le niveau du build ≥ ou ≤ valeur
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
            // ⚠ CharacterBuild.Attributes est clé par ID de template ("attr_17"), jamais par nom EN :
            // chercher "Strength" y renvoyait toujours 0 → « ≥ » sans jamais un résultat et « ≤ »
            // toujours vrai. Passer par GwTemplateCodec.AttributeKey, l'unique fabricant de la clé.
            int have = c.Attributes.GetValueOrDefault(GwTemplateCodec.AttributeKey(t.AttributeId));
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

// Seuil sur une caractéristique, identifiée par son ID de template (la clé réelle des builds,
// cf. GwTemplateCodec.AttributeKey) : niveau investi comparé à « value » selon « comparison ».
public sealed record AttributeThreshold(int AttributeId, int Value, AttributeComparison Comparison);
