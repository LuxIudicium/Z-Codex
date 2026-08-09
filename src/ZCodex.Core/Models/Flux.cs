namespace ZCodex.Core.Models;

// Les 12 flux du cycle courant (figé depuis septembre 2013) : un effet PvP mondial par mois,
// rotation le 1er du mois. La valeur = le numéro du mois (janvier=1 … décembre=12), ce qui rend
// la persistance .pn3 stable et lisible et permet un ForMonth trivial. 0/absent = aucun flux.
// Détails et descriptions : ZCodex.Core.Data.FluxData.
public enum Flux
{
    OdransRazor       = 1,  // Janvier
    AmateurHour       = 2,  // Février
    HiddenTalent      = 3,  // Mars
    ThereCanBeOnlyOne = 4,  // Avril
    MeekShallInherit  = 5,  // Mai
    JackOfAllTrades   = 6,  // Juin
    ChainCombo        = 7,  // Juillet
    XinraesRevenge    = 8,  // Août
    LikeABoss         = 9,  // Septembre
    MinionApocalypse  = 10, // Octobre
    AllIn             = 11, // Novembre
    PartingGift       = 12, // Décembre
}
