namespace ZCodex.Core.Data;

/// <summary>
/// Coups nécessaires pour charger une compétence d'adrénaline (chantier infobulle, lot 1). Une
/// touche rapporte 1 coup (25 points), modifié par les effets actifs sur le perso.
///
/// Règle de cumul (page wiki « Adrenaline », texte brut relu le 14/09/2026) :
///  • les multiplicateurs (« X % more adrenaline ») s'additionnent, somme PLAFONNÉE à +100 % ;
///  • sauf si UN effet dépasse seul +100 % (Focused Anger au-delà du rang 10) : il s'applique à sa
///    valeur et les autres multiplicateurs sont ignorés ;
///  • les coups fixes par touche (« For Great Justice! » PvP, Dark Fury, Mark of Fury) s'ajoutent au
///    coup de base, hors plafond, et le multiplicateur retenu s'applique à TOUT le gain de la touche,
///    coups fixes compris : gain = (1 + coups fixes) × (1 + multiplicateur). Décision Philippe du
///    14/09/2026 (« tous les pourcentages ») — le wiki ne l'affirme que pour « For Great Justice! »,
///    Focused Anger et le mod « Furious » ;
///  • Natural Temper est sans effet sur un perso enchanté — l'app l'ignore dès qu'un effet actif
///    l'enchante (Onslaught, Dark Fury), décision Philippe 14/09/2026.
/// Calcul en ENTIERS (centièmes de coup) : un 4,000001 ne devient jamais 5.
/// </summary>
public static class AdrenalineGain
{
    /// <summary>Un effet actif sur le perso. <paramref name="Enchants"/> : l'effet est un
    /// enchantement posé sur lui. <paramref name="NeedsUnenchanted"/> : l'effet disparaît quand le
    /// perso est enchanté (Natural Temper).</summary>
    public readonly record struct Effect(
        int MultiplierPct = 0, int FixedStrikes = 0, bool Enchants = false, bool NeedsUnenchanted = false);

    /// <summary>Effets venus du bandeau d'équipe (lot 1b), à ajouter à ceux du perso.
    /// <paramref name="Slowed"/> : Soothing, lancé par l'ennemi, divise le gain total par deux.</summary>
    public readonly record struct TeamEffects(IReadOnlyList<Effect> Effects, bool Slowed)
    {
        public static TeamEffects None { get; } = new(Array.Empty<Effect>(), false);

        /// <summary>Un effet du bandeau pèse-t-il sur l'adrénaline ?</summary>
        public bool Any => Effects.Count > 0 || Slowed;
    }

    /// <summary>Gain par touche en centièmes de coup : 100 = un coup, aucun effet.</summary>
    public static int GainPerHit(IEnumerable<Effect> effects)
    {
        var all = effects as IReadOnlyCollection<Effect> ?? effects.ToList();
        bool enchanted = all.Any(e => e.Enchants);
        int sum = 0, top = 0, strikes = 0;
        foreach (var e in all)
        {
            if (e.NeedsUnenchanted && enchanted) continue;
            sum += e.MultiplierPct;
            top = Math.Max(top, e.MultiplierPct);
            strikes += e.FixedStrikes;
        }
        int multiplier = top > 100 ? top : Math.Min(sum, 100);
        return (1 + strikes) * (100 + multiplier);
    }

    /// <summary>Coups nécessaires pour un coût de <paramref name="cost"/> coups, arrondis au
    /// supérieur. <paramref name="slowed"/> (Soothing) divise le gain TOTAL par deux, coups fixes
    /// compris. Coût nul ou gain invalide → coût renvoyé tel quel.</summary>
    public static int StrikesNeeded(int cost, int gainPerHit, bool slowed = false)
    {
        if (cost <= 0 || gainPerHit <= 0) return cost;
        int numerator = cost * (slowed ? 200 : 100);
        return (numerator + gainPerHit - 1) / gainPerHit;
    }
}
