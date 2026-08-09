namespace ZCodex.Core.Models;

public static class ProfessionExtensions
{
    // Abréviation GW1 (W, R, Mo, N, Me, E, A, Rt, P, D).
    public static string Abbr(this Profession p) => p switch
    {
        Profession.Warrior      => "W",
        Profession.Ranger       => "R",
        Profession.Monk         => "Mo",
        Profession.Necromancer  => "N",
        Profession.Mesmer       => "Me",
        Profession.Elementalist => "E",
        Profession.Assassin     => "A",
        Profession.Ritualist    => "Rt",
        Profession.Paragon      => "P",
        Profession.Dervish      => "D",
        _                       => "?",
    };

    // Abréviations du client FR (validées Philippe 28/07/2026). Seules 2 diffèrent de l'anglais :
    // Guerrier → G et Envoûteur → En ; les 8 autres coïncident, mais la table est complète pour
    // qu'un changement futur n'ait qu'un seul endroit à toucher.
    private static readonly Dictionary<Profession, string> AbbrFr = new()
    {
        [Profession.Warrior]      = "G",
        [Profession.Ranger]       = "R",
        [Profession.Monk]         = "Mo",
        [Profession.Necromancer]  = "N",
        [Profession.Mesmer]       = "En",
        [Profession.Elementalist] = "E",
        [Profession.Assassin]     = "A",
        [Profession.Ritualist]    = "Rt",
        [Profession.Paragon]      = "P",
        [Profession.Dervish]      = "D",
    };

    // Abréviation AFFICHÉE (langue courante). À ne pas confondre avec Abbr(), qui reste
    // l'abréviation ANGLAISE : celle-ci est une clé d'export figée (en-tête des codes de
    // template/chat « [1 W/Mo - Nom;code] », lus par le jeu et par les autres outils).
    public static string DisplayAbbr(this Profession p) =>
        AppLanguage.IsFr && AbbrFr.TryGetValue(p, out var fr) ? fr : p.Abbr();

    // Profil PR/SEC abrégé pour l'EXPORT (ex. "W/Mo"), toujours en anglais. Secondaire masquée
    // si None (ex. "W").
    public static string Profile(this Profession primary, Profession secondary) =>
        secondary == Profession.None ? primary.Abbr() : $"{primary.Abbr()}/{secondary.Abbr()}";
}
