using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Compétence qui fait lire à d'AUTRES compétences une caractéristique qui n'est pas la leur
/// (chantier infobulle, lot 5), togglée par icône sur la carte du perso comme les lots 1 à 4.
/// <paramref name="Attribute"/> = la caractéristique de remplacement ; <paramref name="Affects"/> =
/// les compétences touchées.
/// </summary>
public sealed record AttributeSubstitutionDescriptor(int SkillId, string Attribute, Func<Skill, bool> Affects);

/// <summary>
/// Les DEUX seules compétences du catalogue qui substituent une caractéristique. Recensement fait sur
/// les 1517 descriptions de la base (17/09/2026) : aucune troisième, et aucune des deux n'a de variante
/// « (PvP) ».
///
/// ⚠ Symbols of Inspiration, la troisième candidate du plan, est SORTIE du lot (décision Philippe du
/// 17/09/2026) : elle est élite et VOLE l'élite de sa cible, donc en jeu la barre ne porte jamais d'autre
/// élite sur laquelle son effet se verrait, et Z-Codex ne sait pas modéliser une élite volée. Si elle
/// revient un jour, son conflit avec le Sceau des illusions sur un sort élite hors Illusion se tranche
/// « selon l'ordre d'incantation » (réponse de Philippe, non implémentable en simulation).
/// </summary>
public static class AttributeSubstitutionData
{
    public const int SignetOfIllusionsSkillId = 1346;
    public const int SymbolicCeleritySkillId  = 1340;

    private const string IllusionMagic = "Illusion Magic";
    private const string FastCasting   = "Fast Casting";

    // Les deux périmètres sont DISJOINTS — les sorts d'un côté, les sceaux de l'autre — donc au plus une
    // substitution s'applique à une compétence donnée, et aucun arbitrage n'est nécessaire. Ni l'une ni
    // l'autre ne se substitue à elle-même (le Sceau des illusions n'est pas un sort, la Célérité
    // symbolique n'est pas un sceau), mais chacune substitue l'AUTRE : c'est voulu, et sans cycle
    // possible puisqu'une substitution ne dépend jamais d'un rang.
    //
    // Une compétence SANS caractéristique est exclue : le jeu lui donnerait bien la caractéristique de
    // remplacement, mais elle n'a aucun chiffre à recalculer et la mention de fin d'infobulle afficherait
    // un « au lieu de Aucune » absurde. Les compétences à RANG DE TITRE, elles, sont bien dedans
    // (tranché par Philippe le 17/09/2026) : leur table s'arrête au rang 10, donc un rang de
    // caractéristique plus élevé y lit la valeur de plateau (clamp de SkillProgression.At).
    public static readonly IReadOnlyList<AttributeSubstitutionDescriptor> All = new AttributeSubstitutionDescriptor[]
    {
        // Sceau des illusions : « your next 1…3 non-Illusion spell[s] use your Illusion attribute ».
        // Effet à charges — icône allumée = la charge s'applique (règle du chantier, 14/09/2026).
        new(SignetOfIllusionsSkillId, IllusionMagic,
            s => EnergyCostBoostData.IsSpell(s) && s.Attribute != IllusionMagic && HasAttribute(s)),
        // Célérité symbolique : « your signets use your Fast Casting attribute ».
        new(SymbolicCeleritySkillId, FastCasting,
            s => NatureRitualData.IsSignet(s) && s.Attribute != FastCasting && HasAttribute(s)),
    };

    private static bool HasAttribute(Skill s) => !GwAttributeData.IsNoAttribute(s.Attribute);

    private static readonly Dictionary<int, AttributeSubstitutionDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);
    private static readonly HashSet<int> _toggleIds = All.Select(d => d.SkillId).ToHashSet();

    public static AttributeSubstitutionDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);
}
