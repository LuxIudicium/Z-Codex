namespace ZCodex.Core.Data;

// Un boost d'attribut personnel : compétence équipée, togglée par le joueur (simulation
// Z-Codex — la durée réelle du buff en jeu est ignorée, comme les rituels de la nature et les
// prolongateurs de durée : actif ou non). FixedValue null → la valeur dépend du rang de la
// PROPRE caractéristique de la compétence, résolue via sa Progression — colonne ProgressionIndex
// (les plages de la description concise sont dans l'ORDRE D'APPARITION : la durée vient souvent
// AVANT le bonus, ex. Trapper's Focus "(12...31...36 seconds.) ... +0...3...4" → colonne 1).
// TargetsAllAttributes (Lot B, Shadow Theft) : s'applique à TOUTES les vraies caractéristiques
// (PR + SEC) DU PERSO, quelles qu'elles soient — pas une liste figée (dépend des professions du
// perso). Hors rangs de titre, même convention que le flux Meek Shall Inherit. TargetAttributes
// est alors ignoré (laissé vide).
// IsOverride (Lot C, Master of Magic) : la valeur REMPLACE le niveau de base au lieu de s'y
// ADDITIONNER (« Your elemental attributes are SET TO 8...13...14 », pas « +N ») ; les autres
// bonus additifs actifs (flux, boosts non-override) continuent de s'appliquer PAR-DESSUS.
public sealed record AttributeBoostDescriptor(
    int SkillId, IReadOnlyList<string> TargetAttributes, int? FixedValue,
    int ProgressionIndex = 0, bool TargetsAllAttributes = false, bool IsOverride = false);

public static class AttributeBoostData
{
    private static readonly string[] ElementalAttributes = { "Air Magic", "Earth Magic", "Fire Magic", "Water Magic" };
    private static readonly string[] RitualistAttributes = { "Communing", "Restoration Magic", "Channeling Magic", "Spawning Power" };
    // "Weapon attributes" (Seven Weapons Stance) = les masteries d'arme seulement (confirmé Philippe
    // 19/07/2026), PAS Critical Strikes.
    private static readonly string[] WeaponAttributes =
        { "Axe Mastery", "Hammer Mastery", "Swordsmanship", "Marksmanship", "Spear Mastery", "Scythe Mastery", "Dagger Mastery" };

    public static readonly IReadOnlyList<AttributeBoostDescriptor> All = new AttributeBoostDescriptor[]
    {
        // Lot A — buffs personnels à 1-2 attributs nommés (cadrage validé Philippe, 19/07/2026).
        new(114,  new[] { "Death Magic" },                 1),                        // Aura of the Lich
        new(111,  new[] { "Blood Magic", "Curses" },       2),                        // Awaken the Blood
        new(2139, new[] { "Death Magic", "Soul Reaping" }, 2),                        // Masochism
        new(1724, new[] { "Marksmanship" },                2),                        // Expert's Dexterity
        new(2959, new[] { "Marksmanship" },                1),                        // Expert's Dexterity (PvP)
        new(946,  new[] { "Wilderness Survival" },         null, ProgressionIndex: 1), // Trapper's Focus (scale via Expertise)

        // Lot B — groupes d'attributs (cadrage validé Philippe, 19/07/2026).
        new(164,  ElementalAttributes, null, ProgressionIndex: 1), // Elemental Attunement (scale via Energy Storage)
        new(198,  ElementalAttributes, 2),                         // Glyph of Elemental Power (fixe, pas de Progression)
        new(199,  ElementalAttributes, null, ProgressionIndex: 2), // Glyph of Energy (scale via Energy Storage)
        new(2094, ElementalAttributes, 1),                         // Elemental Lord (Kurzick) — "boosted by 1", fixe
        new(1951, ElementalAttributes, 1),                         // Elemental Lord (Luxon) — idem
        new(1217, RitualistAttributes, null, ProgressionIndex: 1), // Ritual Lord (scale via Spawning Power — sa PROPRE cible : lu en RAW, pas de cycle)
        new(3428, Array.Empty<string>(), null, ProgressionIndex: 1, TargetsAllAttributes: true), // Shadow Theft (scale via Critical Strikes)
        new(3426, WeaponAttributes, null, ProgressionIndex: 1), // Seven Weapons Stance (scale via Strength)

        // Lot C — Master of Magic : FIXE (n'additionne pas) les attributs élémentaires (cadrage
        // validé Philippe, 19/07/2026).
        new(1378, ElementalAttributes, null, ProgressionIndex: 1, IsOverride: true), // scale via Energy Storage
    };

    private static readonly Dictionary<int, AttributeBoostDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);

    public static AttributeBoostDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);
}
