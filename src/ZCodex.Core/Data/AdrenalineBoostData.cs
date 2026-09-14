using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

// Qui profite de l'effet : le perso qui porte la compétence (« you »), ou l'allié qu'il vise
// (« target ally », patron Heroic Refrain : icône proposée sur la carte de chaque perso dès qu'un
// membre de l'équipe la porte, lanceur compris).
public enum AdrenalineBoostScope { Self, TargetAlly }

// Compétence qui accélère le gain d'adrénaline, togglée par icône (simulation Z-Codex : la durée
// réelle est ignorée, icône allumée = effet actif). MultiplierPct = « X % more adrenaline » ;
// FixedStrikes = coups ajoutés par touche ; ScalingAttribute non null = le % se lit en
// progression[0] au rang de cette caractéristique (Focused Anger). BaseSkillId = id PvE d'une
// variante « (PvP) » : l'icône est mémorisée sur l'id de base, pour rester allumée quand le
// catalogue change de mode et que la compétence équipée change d'id.
public sealed record AdrenalineBoostDescriptor(
    int SkillId, AdrenalineBoostScope Scope, int MultiplierPct = 0, int FixedStrikes = 0,
    string? ScalingAttribute = null, int BaseSkillId = 0,
    bool IsEnchantment = false, bool NeedsUnenchanted = false)
{
    /// <summary>Id sous lequel l'icône est mémorisée (et persistée) : l'id de base.</summary>
    public int ToggleId => BaseSkillId != 0 ? BaseSkillId : SkillId;
}

public static class AdrenalineBoostData
{
    /// <summary>Weapon of Fury : seule compétence du lot 1a reçue d'un allié.</summary>
    public const int WeaponOfFurySkillId = 1749;

    /// <summary>Mod d'arme « Furious » : ce n'est pas une compétence, son icône a donc un id réservé,
    /// négatif pour ne jamais croiser un SkillId dans la liste persistée des boosts actifs.</summary>
    public const int FuriousModToggleId = -1;

    /// <summary>« Double Adrenaline on hit » : un multiplicateur de +100 %, dans le plafond.</summary>
    public const int FuriousModPercent = 100;

    // SkillId et chiffres relevés dans la base réelle (probe scratchpad du 14/09/2026).
    public static readonly IReadOnlyList<AdrenalineBoostDescriptor> All = new AdrenalineBoostDescriptor[]
    {
        new(343,  AdrenalineBoostScope.Self, MultiplierPct: 100),                           // "For Great Justice!" : +100 %
        new(2883, AdrenalineBoostScope.Self, FixedStrikes: 1, BaseSkillId: 343),            // "For Great Justice!" (PvP) : +1 coup par touche
        new(370,  AdrenalineBoostScope.Self, MultiplierPct: 100),                           // Berserker Stance
        new(317,  AdrenalineBoostScope.Self, MultiplierPct: 100),                           // Battle Rage : « double adrenaline from your attacks »
        new(1769, AdrenalineBoostScope.Self, ScalingAttribute: "Leadership"),               // Focused Anger : 0…120…150 %
        new(1770, AdrenalineBoostScope.Self, MultiplierPct: 33, NeedsUnenchanted: true),    // Natural Temper
        new(1754, AdrenalineBoostScope.Self, MultiplierPct: 25, IsEnchantment: true),       // Onslaught
        new(3365, AdrenalineBoostScope.Self, MultiplierPct: 25, IsEnchantment: true, BaseSkillId: 1754), // Onslaught (PvP)
        new(1518, AdrenalineBoostScope.Self, MultiplierPct: 25),                            // Avatar of Balthazar
        new(1773, AdrenalineBoostScope.Self, MultiplierPct: 33),                            // Soldier's Fury (sous un cri ou un chant)
        new(WeaponOfFurySkillId, AdrenalineBoostScope.TargetAlly, MultiplierPct: 100),      // Weapon of Fury
    };

    private static readonly Dictionary<int, AdrenalineBoostDescriptor> _bySkillId = All.ToDictionary(d => d.SkillId);
    private static readonly HashSet<int> _toggleIds = All.Select(d => d.ToggleId).Append(FuriousModToggleId).ToHashSet();

    public static AdrenalineBoostDescriptor? BySkillId(int skillId) => _bySkillId.GetValueOrDefault(skillId);

    /// <summary>Id d'icône reconnu (id de base d'une compétence du tableau, ou mod « Furious ») —
    /// filtre de chargement de la liste persistée des boosts actifs.</summary>
    public static bool IsToggleId(int id) => _toggleIds.Contains(id);

    /// <summary>Effet d'une compétence active. <paramref name="scalingRank"/> ne sert qu'aux
    /// compétences à caractéristique d'échelle (Focused Anger).</summary>
    public static AdrenalineGain.Effect EffectOf(AdrenalineBoostDescriptor d, Skill skill, int scalingRank) => new(
        d.ScalingAttribute is null
            ? d.MultiplierPct
            : SkillProgression.IntAt(skill.Progression is { Length: > 0 } p ? p[0] : null, scalingRank) ?? 0,
        d.FixedStrikes, d.IsEnchantment, d.NeedsUnenchanted);

    public static AdrenalineGain.Effect FuriousModEffect => new(MultiplierPct: FuriousModPercent);

    /// <summary>Vrai si ce mod d'arme est un « Furious ». Détecté par l'ancre wiki du mod, comme
    /// <see cref="EnchantmentDuration.IsEnchantingMod"/>.</summary>
    public static bool IsFuriousMod(int modId) =>
        GwEquipmentModDetails.ByModId.TryGetValue(modId, out var d) && d.WikiPath == "Furious";
}
