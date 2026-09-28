using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Effets posés sur un ENNEMI qui profitent aux compétences qui le VISENT (chantier infobulle, lot 7b, cadrage tranché
/// par Philippe le 28/09/2026). L'icône vit sur la carte du LANCEUR (l'ennemi n'a pas de carte) ; allumée = « on vise
/// l'ennemi qui porte ce maléfice ». Ce qu'elle change, et chez qui :
///  • Vol de vitesse (880) : les sorts qui visent l'ennemi maudit s'incantent 50 % plus vite, ceux du lanceur ET de ses
///    alliés (wiki long : « cast by you or your allies »). Variante PvP (3187) : ceux du lanceur seulement (« Spells you
///    cast »). Seul, il atteint déjà le plafond de 50 % d'incantation du lot 3.
///  • Vents glaciaux (1368) : le prochain maléfice de Magie de l'eau qui vise l'ennemi dure 25…100 % de plus (Magie de
///    l'air du lanceur), qu'il vienne du lanceur ou de n'importe quel membre (test de Philippe, lecture « b »).
/// Les deux parts « lanceur » sont des descripteurs PERSONNELS ordinaires des lots 3 et 4a, déclarés dans leurs tables
/// (<see cref="SkillSpeedBoostData"/>, <see cref="SkillDurationBoostData"/> : icône et effet chez le porteur) ; la part
/// « alliés » est ajoutée chez les AUTRES membres par l'appelant, patron « A l'aide ! » du lot 7a.
/// </summary>
public static class TargetedFoeEffectData
{
    public const int StolenSpeedSkillId = 880;
    public const int StolenSpeedPvpSkillId = 3187;
    public const int ChillingWindsSkillId = 1368;
    public const int ShadowyBurdenSkillId = 950;

    /// <summary>Sort qui peut viser un ennemi (351 sorts, <see cref="FoeTargetSpells"/>).</summary>
    public static bool TargetsFoe(Skill s) => EnergyCostBoostData.IsSpell(s) && FoeTargetSpells.Names.Contains(s.Name);

    /// <summary>Maléfice de Magie de l'eau qui VISE un ennemi : Explosion glaciale (sans cible) n'en profite pas
    /// (arbitrage de Philippe), Miroir de glace (qui vise un allié) non plus.</summary>
    public static bool IsWaterHexOnFoe(Skill s) =>
        NatureRitualData.IsHex(s) && s.Attribute == "Water Magic" && TargetsFoe(s);

    /// <summary>
    /// Fardeau nébuleux (lot 7b-2) : « 20…28…30 less armor against YOUR attacks », tant que la cible n'a pas d'autre
    /// maléfice (icône allumée = c'est le cas). Seulement les attaques du LANCEUR — pas celles de son familier — et
    /// dans la catégorie « Special » d'<see cref="ArmorCalculator"/> : après la pénétration, SANS le plancher de 60
    /// (la seule compétence qui y descend, raison de son entrée au lot 7, Q1). Icône personnelle, pas de variante PvP.
    /// </summary>
    public static bool ShadowyBurdenReaches(Skill s) => NatureRitualData.IsAttack(s) && s.SkillType != "Pet Attack";

    /// <summary>Armure retirée par Fardeau nébuleux au rang <paramref name="shadowArts"/> (progression[1]).</summary>
    public static int ShadowyBurdenArmor(Skill source, int shadowArts) =>
        SkillProgression.IntAt(source.Progression is { Length: > 1 } p ? p[1] : null, shadowArts) ?? 0;

    /// <summary>Icônes dont l'allumage change les infobulles des AUTRES membres (rafraîchissement d'équipe).</summary>
    public static bool IsTeamToggleId(int id) => id is StolenSpeedSkillId or ChillingWindsSkillId;

    /// <summary>La compétence équipée <paramref name="skillId"/> fait-elle profiter les AUTRES membres de l'icône
    /// <paramref name="toggleId"/> ? Vol de vitesse PvP non : ses sorts à lui seulement.</summary>
    public static bool SharesWithAllies(int skillId, int toggleId) => (skillId, toggleId) switch
    {
        (StolenSpeedSkillId, StolenSpeedSkillId)     => true,
        (ChillingWindsSkillId, ChillingWindsSkillId) => true,
        _                                            => false,
    };
}
