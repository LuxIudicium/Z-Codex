namespace ZCodex.Core.Models;

public enum Profession
{
    None = 0,
    Warrior = 1,
    Ranger = 2,
    Monk = 3,
    Necromancer = 4,
    Mesmer = 5,
    Elementalist = 6,
    Assassin = 7,
    Ritualist = 8,
    Paragon = 9,
    Dervish = 10
}

// Noms de profession pour l'AFFICHAGE. Le canon interne reste l'anglais (Profession.ToString()
// = clés .pn3, matching, codes de template) ; le FR n'est qu'une couche d'affichage pilotée par
// AppLanguage.IsFr. Cf. [[project_i18n_fr_en]].
public static class ProfessionNames
{
    private static readonly Dictionary<Profession, string> Fr = new()
    {
        [Profession.Warrior]      = "Guerrier",
        [Profession.Ranger]       = "Rôdeur",
        [Profession.Monk]         = "Moine",
        [Profession.Necromancer]  = "Nécromant",
        [Profession.Mesmer]       = "Envoûteur",
        [Profession.Elementalist] = "Élémentaliste",
        [Profession.Assassin]     = "Assassin",
        [Profession.Ritualist]    = "Ritualiste",
        [Profession.Paragon]      = "Parangon",
        [Profession.Dervish]      = "Derviche",
    };

    // Nom affiché (langue courante). EN = ToString() de l'enum (canon interne).
    public static string DisplayName(this Profession p) =>
        AppLanguage.IsFr && Fr.TryGetValue(p, out var fr) ? fr : p.ToString();

    // Nom FR brut (indépendant de la langue courante), pour les rares usages FR-only.
    public static string FrenchName(this Profession p) => Fr.GetValueOrDefault(p, p.ToString());
}
