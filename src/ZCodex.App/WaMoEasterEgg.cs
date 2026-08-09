using ZCodex.App.ViewModels;
using ZCodex.Core.Models;
using ZCodex.Core.Search;
using System.Windows;

namespace ZCodex.App;

/// <summary>
/// Clin d'œil au W/Mo à Rénovation — l'archétype du débutant de Guild Wars 1.
///
/// Conditions (toutes requises) : le personnage est Guerrier/Moine DANS CET ORDRE, sa barre
/// porte Rénovation, et la compétence qu'on vient d'y poser relève de « Prières de guérison ».
/// Le message monte d'un cran à chaque soin supplémentaire (jusqu'aux 7 slots restants).
///
/// Deux registres : en éditeur de build simple l'application s'adresse au joueur, en team build
/// c'est le W/Mo lui-même qui parle — c'est là qu'il a un groupe à convaincre.
///
/// Déclenché uniquement par le dépôt d'une compétence depuis le catalogue : ni l'ouverture d'un
/// fichier, ni la résolution PvE/PvP, ni une copie de barre ne doivent le faire apparaître.
/// </summary>
internal static class WaMoEasterEgg
{
    private const string MendingName = "Mending";
    private const string HealingPrayers = "Healing Prayers";

    // 7 slots restants une fois Rénovation posée → 7 paliers.
    private const int MaxSteps = 7;

    /// <summary>
    /// À appeler après avoir posé <paramref name="added"/> dans la barre de <paramref name="character"/>.
    /// Ne fait rien si les conditions ne sont pas réunies.
    /// </summary>
    public static void OnSkillAdded(CharacterSlotViewModel? character, Skill? added, bool isTeamBuild)
    {
        if (character is null || added is null) return;
        if (character.PrimaryProfession != Profession.Warrior) return;
        if (character.SecondaryProfession != Profession.Monk) return;
        if (!IsHealingPrayers(added)) return;

        var bar = character.SkillSlots
            .Select(s => s.Skill)
            .Where(s => s is not null)
            .ToList();

        if (!bar.Any(s => IsMending(s!))) return;

        // Rénovation ne se compte pas elle-même : on mesure les soins EN PLUS.
        int extraHeals = bar.Count(s => IsHealingPrayers(s!) && !IsMending(s!));
        if (extraHeals < 1) return;

        int step = Math.Min(extraHeals, MaxSteps);
        string key = $"S.Egg.{(isTeamBuild ? "Wamo" : "Player")}{step}";

        MessageBox.Show(
            LanguageManager.T(key),
            LanguageManager.T("S.Egg.Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // Le canon interne est l'anglais (cf. AppLanguage) : on matche sur les champs EN.
    // BaseName neutralise un éventuel suffixe « (PvP) ».
    private static bool IsMending(Skill s) =>
        string.Equals(SkillVariants.BaseName(s.Name), MendingName, StringComparison.OrdinalIgnoreCase);

    private static bool IsHealingPrayers(Skill s) =>
        string.Equals(s.Attribute, HealingPrayers, StringComparison.OrdinalIgnoreCase);
}
