namespace ZCodex.Core.Models;

// TODO(attributes): no mapping exists between the codec's int attribute IDs (attr_N) and
//                   display names ("Strength", "Soul Reaping", etc.). When building the attributes
//                   UI, create GwAttributeData.cs (~45 entries, format: id → name + owning profession).
//
// TODO(attributes): each profession has one exclusive primary attribute (Warrior→Strength,
//                   Necromancer→Soul Reaping, Paragon→Leadership, etc.). UI presentation should
//                   identify it for highlighting and restrict editing to the character's PR/SEC.
//
// TODO(attributes): stores only template-invested points. PvE runtime bonuses (mods of the
//                   profession +5, consumables +1 to all, elite skills like SY!) are NOT persisted
//                   here — they are presentation/simulation concerns.
public record AttributeAllocation(int AttributeId, int Points);

public class AttributesBuild
{
    public List<AttributeAllocation> Allocations { get; set; } = [];

    // Rangs de titre PvE (Sunspear, Asura, Allegiance...), keyés par nom (pas d'ID GwAttribute :
    // hors du format de template GW1, jamais encodés dans un code de chat). Simulation Z-Codex
    // uniquement, persistée à part des Allocations pour ne jamais transiter par le codec réel.
    public Dictionary<string, int> TitleRanks { get; set; } = new();
}
