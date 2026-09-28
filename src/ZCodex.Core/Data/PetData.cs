using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

// Familier obligatoire (TODO du 27/09, cadré avec Philippe le 28/09/2026) : en jeu, le familier ne suit le
// Rôdeur que si l'une des compétences de GrantsPet est équipée. Une compétence de RequiresPet posée sans
// elle ne peut jamais servir → avertissement rouge dans son infobulle, rien n'est bloqué.
// Arbitrages Philippe : Guérison partagée (PvP) ne donne PAS de familier ; Course unique, Saccage collectif,
// « Ensemble et unis ! », Renaissance animale et Sceau du chagrin se lancent sans familier.
public static class PetData
{
    private static readonly HashSet<int> Grants = new()
    {
        411,    // Charm Animal
        900004, // Charm Animal (Codex) — clé orpheline
        436,    // Comfort Animal
        3045,   // Comfort Animal (PvP)
        1195,   // Heal as One — PvE seulement
    };

    // Compétences qui n'agissent que sur le familier, en plus des 16 attaques de familier (type « Pet Attack »).
    private static readonly HashSet<int> PetOnly = new()
    {
        412,         // Call of Protection
        415, 2657,   // Call of Haste (+ PvP)
        423,         // Symbiotic Bond
        447, 3451,   // Otyugh's Cry (+ PvP)
        1194, 3050,  // Predatory Bond (+ PvP)
        1468,        // Strike as One
        1721,        // Rampage as One
        2141,        // Companionship
        2142,        // Feral Aggression
    };

    public static bool GrantsPet(Skill s) => Grants.Contains(s.Id);

    public static bool RequiresPet(Skill s) => s.SkillType == "Pet Attack" || PetOnly.Contains(s.Id);
}
