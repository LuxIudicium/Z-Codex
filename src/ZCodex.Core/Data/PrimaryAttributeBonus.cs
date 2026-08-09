using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Texte du bonus d'attribut primaire affiché en bas de l'infobulle.
/// Formules d'après wiki.guildwars.com/wiki/Primary_attribute (validées par Philippe).
/// <para>Si <c>rank</c> a une valeur (ligne de build) → valeurs résolues à ce rang.
/// Si <c>rank</c> est null (catalogue, hors perso) → notation de plage "0...12...15"
/// (rangs 0 / 12 / 15, comme le wiki), rendue en vert/gras par SkillMarkup.</para>
/// </summary>
public static class PrimaryAttributeBonus
{
    /// <summary>Texte du bonus pour la profession, résolu au rang (ou en plage si null),
    /// dans la langue affichée (<see cref="AppLanguage.IsFr"/>).</summary>
    public static string? Describe(Profession p, int? rank)
    {
        int? r = rank.HasValue ? Math.Max(0, rank.Value) : null;
        return AppLanguage.IsFr ? DescribeFr(p, r) : DescribeEn(p, r);
    }

    private static string? DescribeEn(Profession p, int? r) => p switch
    {
        Profession.Warrior =>
            $"Your attack skills gain {V(x => x, r)}% armor penetration.",
        Profession.Ranger =>
            $"Reduces the Energy cost of your attack skills, rituals, touch skills and Ranger skills by {V(x => x * 4, r)}%.",
        Profession.Monk =>
            $"Your Monk spells that target an ally heal for an additional {V(x => (int)(x * 3.2), r)} Health.",
        Profession.Necromancer =>
            $"You gain {V(x => x, r)} Energy whenever a non-Spirit creature dies near you (max 3 times per 15 seconds).",
        Profession.Mesmer =>
            $"Reduces the activation time of your spells, and the activation time of your signets by {V(x => x * 3, r)}%.",
        Profession.Elementalist =>
            $"Increases your maximum Energy by {V(x => x * 3, r)}.",
        Profession.Assassin =>
            $"Critical hit chance is increased by {V(x => x, r)}%. Gain {V(CriticalStrikesEnergy, r)} energy when you land a critical hit.",
        Profession.Ritualist =>
            $"Increases the Health of your creatures and the duration of your weapon spells by {V(x => x * 4, r)}%.",
        Profession.Paragon =>
            $"You gain 2 Energy for each ally affected by your shouts and chants, up to {V(x => x / 2, r)} Energy.",
        Profession.Dervish =>
            $"Reduces the cost of your Dervish enchantments by {V(x => x * 4, r)}%.",
        _ => null,
    };

    // Textes français validés par Philippe (20/07/2026). « signet » = « sceau ».
    private static string? DescribeFr(Profession p, int? r) => p switch
    {
        Profession.Warrior =>
            $"Vos compétences d'attaque bénéficient de {V(x => x, r)} % de pénétration d'armure.",
        Profession.Ranger =>
            $"Réduit le coût en énergie de vos compétences d'attaque, rituels, compétences de toucher et compétences de Rôdeur de {V(x => x * 4, r)} %.",
        Profession.Monk =>
            $"Vos sorts de Moine qui ciblent un allié soignent {V(x => (int)(x * 3.2), r)} points de vie supplémentaires.",
        Profession.Necromancer =>
            $"Vous gagnez {V(x => x, r)} énergie chaque fois qu'une créature autre qu'un esprit meurt près de vous (max. 3 fois par 15 secondes).",
        Profession.Mesmer =>
            $"Réduit le temps d'activation de vos sorts, et le temps d'activation de vos sceaux, de {V(x => x * 3, r)} %.",
        Profession.Elementalist =>
            $"Augmente votre énergie maximale de {V(x => x * 3, r)}.",
        Profession.Assassin =>
            $"Votre taux de coup critique augmente de {V(x => x, r)} %. Vous gagnez {V(CriticalStrikesEnergy, r)} énergie à chaque coup critique réussi.",
        Profession.Ritualist =>
            $"Augmente les PV de vos créatures et la durée de vos sorts d'arme de {V(x => x * 4, r)} %.",
        Profession.Paragon =>
            $"Vous gagnez 2 énergie par allié affecté par vos cris et chants, jusqu'à {V(x => x / 2, r)} énergie.",
        Profession.Dervish =>
            $"Réduit le coût de vos enchantements de Derviche de {V(x => x * 4, r)} %.",
        _ => null,
    };

    // Valeur unique au rang donné, ou plage "v0...v12...v15" si rang null.
    private static string V(Func<int, int> f, int? rank)
        => rank.HasValue ? f(rank.Value).ToString() : $"{f(0)}...{f(12)}...{f(15)}";

    // Énergie par coup critique : paliers 3-7→1, 8-12→2, 13-17→3, 18+→4.
    private static int CriticalStrikesEnergy(int rank) => rank switch
    {
        >= 18 => 4,
        >= 13 => 3,
        >= 8 => 2,
        >= 3 => 1,
        _ => 0,
    };
}
