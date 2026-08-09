using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Les 12 flux du cycle courant de Guild Wars (https://wiki.guildwars.com/wiki/Flux).
/// Le cycle est figé depuis septembre 2013 : chaque mois de l'année a un flux fixe, la table
/// ne bougera plus (le jeu n'évolue plus) → données EN DUR, aucun scrape.
/// Nom en anglais (terme du jeu, comme les compétences). Description bilingue :
/// <see cref="FluxInfo.Description"/> (FR, résumé UI) et <see cref="FluxInfo.DescriptionEn"/>
/// (texte concis officiel du wiki EN) ; <see cref="FluxInfo.DisplayDescription"/> choisit
/// selon <see cref="AppLanguage.IsFr"/>.
/// </summary>
public static class FluxData
{
    // Month = numéro du mois (1-12) = valeur de l'enum Flux (bijection mois ↔ flux).
    // Name (EN) = clé interne (logique/matching) ; NameFr = affichage. Décision Philippe 21/07 :
    // traduire tous les noms SAUF Jack of All Trades / Minion Apocalypse / All In (gardés EN → NameFr
    // = le nom anglais). Noms FR = brouillons à confirmer Philippe (Case A).
    public sealed record FluxInfo(Flux Flux, int Month, string Name, string NameFr, string Description, string DescriptionEn)
    {
        /// <summary>Nom dans la langue affichée. Name (EN) reste la clé interne.</summary>
        public string DisplayName => AppLanguage.IsFr ? NameFr : Name;

        /// <summary>Description dans la langue affichée (FR résumé / EN officiel du wiki).</summary>
        public string DisplayDescription => AppLanguage.IsFr ? Description : DescriptionEn;
    }

    public static readonly IReadOnlyList<FluxInfo> All =
    [
        new(Flux.OdransRazor, 1, "Odran's Razor", "Rasoir d'Odran",
            "Le combat JcJ n'est pas modifié.",
            "PvP combat is unmodified."),
        new(Flux.AmateurHour, 2, "Amateur Hour", "L'heure des amateurs",
            "Les compétences de votre profession secondaire infligent 30 % de dégâts supplémentaires aux ennemis dont la profession primaire correspond à cette profession secondaire.",
            "Your secondary profession skills deal 30% more damage to foes with that primary profession."),
        new(Flux.HiddenTalent, 3, "Hidden Talent", "Talent caché",
            "Vous bénéficiez d'un bonus de +2 à toutes les caractéristiques secondaires de votre profession secondaire.",
            "You have a +2 bonus to all of the secondary attributes of your secondary profession."),
        new(Flux.ThereCanBeOnlyOne, 4, "There Can Be Only One", "Il ne peut en rester qu'un",
            "Vous infligez +30 % de dégâts aux ennemis de même profession primaire. Chaque fois que vous en tuez un, vous récupérez toute votre santé et votre énergie et gagnez 5 % de moral.",
            "You deal +30% damage to foes of the same primary profession. Each time you kill one of these foes, you regain all Health and Energy and receive a 5% morale boost."),
        new(Flux.MeekShallInherit, 5, "Meek Shall Inherit", "Les humbles hériteront",
            "Si aucune compétence élite n'est équipée : +2 à toutes vos caractéristiques, +2 régénération de santé et +1 régénération d'énergie.",
            "If you do not equip an elite skill, you have +2 to all attributes, +2 Health regeneration, and +1 Energy regeneration."),
        new(Flux.JackOfAllTrades, 6, "Jack of All Trades", "Jack of All Trades",
            "Si toutes vos caractéristiques sont comprises entre 8 et 11 rangs (bonus non comptés), vos compétences infligent 15 % de dégâts en plus, s'activent 25 % plus vite et coûtent 20 % d'énergie en moins.",
            "If your attributes are all between 8-11 before buffs, your skills deal 15% additional damage, activate 25% faster, and cost 20% less Energy."),
        new(Flux.ChainCombo, 7, "Chain Combo", "Combo enchaîné",
            "Chaque fois que vous utilisez une compétence d'une caractéristique différente de la précédente, vous gagnez un bonus de dégâts cumulatif de 5 % (maximum 30 %). Ce bonus est réinitialisé si vous utilisez une compétence de la même caractéristique que la précédente.",
            "Gain a stacking 5% damage bonus (max 30%) whenever you use a skill of a different attribute than the last skill used. Bonus resets if your next skill has the same attribute."),
        new(Flux.XinraesRevenge, 8, "Xinrae's Revenge", "La vengeance de Xinrae",
            "Chaque fois que vous activez une compétence, elle est désactivée pendant 3 secondes pour tous les alliés et ennemis proches qui la possèdent sur leur barre.",
            "Whenever you successfully activate a skill, it is disabled (3 seconds) for all party and opposing party members in the area who have it on their skill bars."),
        new(Flux.LikeABoss, 9, "Like a Boss", "Comme un boss",
            "Si personne n'est le boss, tuer un joueur vous rend boss : -20 armure, mais +33 % de vitesse d'attaque, +33 % de vitesse de déplacement, -33 % de temps d'activation, +3 régénération de santé et +1 régénération d'énergie. Si vous mourez, vous cessez d'être le boss (votre tueur le devient).",
            "Kill the boss (or any player if there is no boss); now you're the boss: -20 armor, +33% attack speed, +33% movement speed, -33% skill activation time, +3 Health regeneration, and +1 Energy regeneration. If you die, you're not the boss anymore. If a player killed you, they're the boss now."),
        new(Flux.MinionApocalypse, 10, "Minion Apocalypse", "Minion Apocalypse",
            "Chaque fois qu'un joueur meurt, toutes les créatures proches subissent 50 dégâts et une horreur d'os de niveau 20 sans maître apparaît.",
            "Each player death deals 50 damage to all nearby creatures and spawns a masterless bone horror (level 20)."),
        new(Flux.AllIn, 11, "All In", "All In",
            "Si toutes les compétences de votre barre utilisent la même caractéristique, vous gagnez +3 régénération de santé, +100 santé maximale, et vos compétences coûtent 25 % d'énergie en moins.",
            "If all your skills use one attribute, gain +3 Health regeneration and +100 max Health; your skills also cost 25% less Energy."),
        new(Flux.PartingGift, 12, "Parting Gift", "Cadeau d'adieu",
            "Si vous mourez, vous laissez tomber au sol un paquet (« Gift of Battle ») qui octroie des bonus à celui qui le ramasse.",
            "If you die, you drop a bundle on the ground that grants bonuses to whoever picks it up."),
    ];

    public static FluxInfo Get(Flux flux) => All[(int)flux - 1];

    // Flux du mois donné (1-12).
    public static FluxInfo ForMonth(int month) => All[Math.Clamp(month, 1, 12) - 1];

    // Flux du mois courant (date système).
    public static FluxInfo Current => ForMonth(DateTime.Now.Month);
}
