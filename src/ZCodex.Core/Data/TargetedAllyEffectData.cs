using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Effets posés sur un ALLIÉ qui profitent aux compétences qui le VISENT (chantier infobulle, lot 7a, cadrage
/// tranché par Philippe le 28/09/2026). L'icône vit sur la carte de l'allié qui a REÇU l'effet ; allumée = « on vise
/// cet allié » (idiome du chantier). Ce qu'elle change, et chez qui :
///  • « A l'aide ! » (1594, PvP 3036) : les sorts des AUTRES membres qui visent cet allié s'incantent 50 % plus vite
///    (wiki long : « other allies' spells targeting that ally ») — ses propres sorts, non. Seul, il atteint déjà le
///    plafond de 50 % d'incantation du lot 3 ; seule Incantation rapide va au-delà (Q4).
///  • Atmosphère enchanteresse (1115) : TOUT enchantement lancé sur cet allié coûte 10 de moins (minimum 1), y
///    compris ceux qu'il se lance à lui-même (confirmé en jeu par Philippe) ; seul son lanceur ne peut pas la recevoir.
/// Harmonie persistante n'est PAS ici : elle allonge les cris et chants que LANCE l'allié affecté, pas ceux qui le
/// visent (précision de Philippe) — c'est un effet reçu de <see cref="SkillDurationBoostData"/>.
/// </summary>
public static class TargetedAllyEffectData
{
    public const int HelpSkillId = 1594;
    public const int HelpPvpSkillId = 3036;
    public const int AirOfEnchantmentSkillId = 1115;

    /// <summary>« A l'aide ! » : −50 % d'incantation aux sorts qui visent un allié VIVANT non esprit. Reçu d'un allié,
    /// jamais une icône personnelle ; les deux variantes partagent l'icône de la version PvE.</summary>
    public static readonly SkillSpeedBoostDescriptor HelpCast = new(
        HelpSkillId, TargetsLivingAlly, Cast: SpeedEffectKind.Percent, CastValue: 50, Received: true);

    /// <summary>Atmosphère enchanteresse : −10 d'énergie, minimum 1. Qui en profite se décide chez l'appelant
    /// (<see cref="AirOfEnchantmentReaches"/>) : ce descripteur ne sert qu'à la cascade du lot 2.</summary>
    public static readonly EnergyCostBoostDescriptor AirOfEnchantmentCost = new(
        AirOfEnchantmentSkillId, EnergyCostKind.Flat, NatureRitualData.IsEnchantment, FixedValue: 10, Minimum: 1);

    /// <summary>Id d'icône reconnu — filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => id is HelpSkillId or AirOfEnchantmentSkillId;

    /// <summary>La compétence porte-t-elle l'un de ces effets ? Rend l'id d'icône (id de base), null sinon.</summary>
    public static int? ToggleIdOf(int skillId) => skillId switch
    {
        HelpSkillId or HelpPvpSkillId => HelpSkillId,
        AirOfEnchantmentSkillId       => AirOfEnchantmentSkillId,
        _                             => null,
    };

    /// <summary>« Cannot self-target » : seule Atmosphère enchanteresse ne peut pas venir de son receveur.</summary>
    public static bool CannotSelfTarget(int toggleId) => toggleId == AirOfEnchantmentSkillId;

    // Les sorts de AllyTargetSpells dont le champ « target » du wiki ne désigne PAS un allié vivant non esprit
    // (relevé du 28/09/2026 sur les 172, API wiki.guildwars.com) : ni « A l'aide ! » ni Atmosphère enchanteresse ne
    // peuvent être posés sur un mort, un esprit, un serviteur ou une créature invoquée, donc ces sorts ne visent
    // jamais un allié qui les porte. Les « allies or foes » (Rafale, Gaine de pierre, Miroir de glace…) restent.
    private static readonly HashSet<string> NotLivingAlly = new(StringComparer.Ordinal)
    {
        // membre du groupe mort (9)
        "Rebirth", "Renew Life", "Restore Life", "Resurrect", "Resurrection Chant", "Unyielding Aura (PvP)",
        "Vengeance", "Flesh of My Flesh", "Flesh of My Flesh (PvP)",
        // esprit allié (4)
        "Draw Spirit", "Rupture Soul", "Spirit to Flesh", "Spirit Walk",
        // serviteur (5) et créature invoquée (1)
        "Feast for the Dead", "Jagged Bones", "Putrid Flesh", "Taste of Death", "Verata's Gaze", "Swap",
        // cadavre le plus proche de la cible (1)
        "Putrid Explosion",
    };

    /// <summary>Sort qui peut viser un allié vivant non esprit (152 des 172 de <see cref="AllyTargetSpells"/>).</summary>
    public static bool TargetsLivingAlly(Skill s) =>
        EnergyCostBoostData.IsSpell(s) && AllyTargetSpells.Names.Contains(s.Name) && !NotLivingAlly.Contains(s.Name);

    /// <summary>
    /// Atmosphère enchanteresse réduit-elle <paramref name="target"/> ? <paramref name="onMe"/> = elle est allumée
    /// sur le perso qui LANCE (ses enchantements qu'il peut se lancer à lui-même) ; <paramref name="onOther"/> = elle
    /// est allumée sur un AUTRE membre (ses enchantements qui visent un allié vivant). Les enchantements « autre allié
    /// seulement » (Life Barrier…) ne profitent donc jamais de l'Atmosphère posée sur leur propre lanceur.
    /// </summary>
    /// Note du wiki apportée par Philippe (28/09/2026) : les enchantements SANS cible (Égide, Ordres) comptent chez le
    /// receveur, les « Flash » et les Harmonisations aussi ; mais un enchantement DOUBLE (« you and target ally ») ne
    /// compte que si c'est la CIBLE qui porte l'Atmosphère, jamais le lanceur, bien qu'il soit enchanté lui aussi.
    public static bool AirOfEnchantmentReaches(Skill target, bool onMe, bool onOther) =>
        NatureRitualData.IsEnchantment(target)
        && ((onMe && SkillSpeedBoostData.IsSelfTargetableEnchantment(target) && !Doublecast.Contains(target.Name))
            || (onOther && TargetsLivingAlly(target)));

    // Les 4 enchantements « you and target ally » de la base (Élémentaliste), recoupés avec la recherche du wiki.
    private static readonly HashSet<string> Doublecast = new(StringComparer.Ordinal)
        { "Energy Boon", "Gust", "Double Dragon", "Stone Sheath" };
}
