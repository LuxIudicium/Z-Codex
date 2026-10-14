using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Compétences dont l'armure brisée tombe AVANT leurs propres dégâts : ceux-ci frappent donc déjà une armure à −20
/// (minimum 60, avant pénétration). Seules celles dont le wiki le DIT figurent ici (inventaire du 13/10/2026) — chez
/// Lightning Hammer, Thunderclap, Lightning Touch, Spirit Rift, Shell Shock et Aura Slicer, le wiki dit au contraire
/// qu'elle tombe APRÈS les dégâts ; chez Teinai's Crystals, « Finish Him! » et Alkar's Alchemical Acid il ne dit rien
/// (et leurs dégâts sans type ignorent l'armure de toute façon). L'armure brisée REÇUE d'un allié (Arme de
/// fractionnement) ne passe pas par ici : c'est <see cref="DamageBoosts.CrackedArmor"/>.
/// Chaque entrée porte la ligne d'explication de l'infobulle, qui dit QUELS dégâts l'armure brisée précède.
/// </summary>
public static class CrackedArmorBeforeDamage
{
    /// <param name="ZonesFr">Table CUMULÉE par zone (Shockwave) : une ligne par palier de distance, chacune somme les paquets
    /// que reçoit un ennemi de ce palier (le 1er paquet seul, puis 1+2, puis 1+2+3). Null = une ligne par paquet.</param>
    /// <param name="CrackedFromZone">Premier palier qui reçoit l'armure brisée : TOUS ses paquets sont alors à −20 (elle
    /// tombe avant tout dégât) ; les paliers d'avant restent sans.</param>
    public sealed record Entry(string NoteFr, string NoteEn,
                               string[]? ZonesFr = null, string[]? ZonesEn = null, int CrackedFromZone = 0)
    {
        public string Note => AppLanguage.IsFr ? NoteFr : NoteEn;
        public string[]? Zones => AppLanguage.IsFr ? ZonesFr : ZonesEn;
    }

    // −20 en violet, comme la ligne de l'Arme de fractionnement.
    private const char E = SkillProgression.MarkEffect;

    private static readonly Dictionary<string, Entry> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        // wiki/Lightning_Surge : « Cracked armor is applied before the damage is dealt. »
        ["Lightning Surge"] = new(
            $"armure brisée posée avant les dégâts : armure de la cible {E}−20{E} (minimum 60, avant pénétration)",
            $"Cracked Armor lands before the damage: target armor {E}−20{E} (minimum 60, before penetration)"),
        // wiki/Shockwave : « Cracked Armor is applied before any damage. » — seuls les ennemis PROCHES (et adjacents) la
        // reçoivent. Table cumulée par zone (décision Philippe du 14/10/2026) : zone = 1er paquet sans armure brisée ;
        // proche = paquets 1+2, adjacent = 1+2+3, TOUS à −20 puisqu'elle tombe avant tout dégât.
        ["Shockwave"] = new(
            $"les ennemis proches et adjacents reçoivent l'armure brisée avant tout dégât : armure {E}−20{E} (minimum 60)",
            $"nearby and adjacent foes get Cracked Armor before any damage: armor {E}−20{E} (minimum 60)",
            ZonesFr: ["zone", "proche", "adjacent"], ZonesEn: ["area", "nearby", "adjacent"], CrackedFromZone: 1),
        // wiki/Low_Blow : « The Cracked Armor condition is applied before any damage from this skill » — si la cible est
        // au sol. Table calculée cible au sol (les 2 paquets), décision Philippe du 13/10/2026.
        ["Low Blow"] = new(
            $"cible au sol : armure brisée posée avant les dégâts, armure {E}−20{E} (minimum 60) sur les deux paquets. "
            + "Cible debout : 1er paquet seul, sans armure brisée",
            $"Knocked-down target: Cracked Armor lands before the damage, armor {E}−20{E} (minimum 60) on both packets. "
            + "Standing target: first packet only, without Cracked Armor"),
    };

    /// <summary>L'entrée de la compétence, null si elle ne pose pas d'armure brisée avant ses propres dégâts.</summary>
    public static Entry? For(Skill skill) => ByName.GetValueOrDefault(skill.Name);
}
