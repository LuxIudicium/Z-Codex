using ZCodex.Core.Models;

namespace ZCodex.Core.Data;

/// <summary>
/// Moteur de calcul de l'armure effective (wiki/Armor_calculation, 4 étapes STRICTES) :
/// 1. Core (armure de base + insignes + Mysticisme + base du bouclier + inscriptions) ;
/// 2. Bonus net (mods d'armes, inhérents, effets non-skill, presque toutes les skills,
///    Cracked Armor) avec la règle des 25 et le plancher 60 ;
/// 3. pénétration d'armure (pourcentage) ;
/// 4. Special (IAU!, Illusionary Weaponry, Mantra of Signets ; malus crits, Barbed Arrows,
///    Healing Signet, Shadowy Burden, flux Massive Damage) — APRÈS la pénétration, sans plancher.
/// Moteur PUR : il reçoit des contributions déjà composées et ne sait pas ce qu'est un insigne
/// ou une compétence. Le tri par type de dégâts / localisation / état du perso est la
/// responsabilité de l'appelant (une contribution passée ici est une contribution appliquée).
/// Oracle : les 3 exemples chiffrés du wiki (Parangon 85, Envoûteur 65, Élémentaliste 98).
/// </summary>
public static class ArmorCalculator
{
    /// <summary>Catégorie wiki d'une contribution (List_of_armor_effects_by_category).</summary>
    public enum Category { Core, Bonus, Special }

    /// <summary>Une contribution résolue : libellé (détail dépliable de l'UI), catégorie,
    /// valeur SIGNÉE (un malus est négatif : Cracked Armor = −20).</summary>
    public sealed record Contribution(string Label, Category Category, int Value);

    /// <summary>Résultat détaillé : tous les intermédiaires des 4 étapes sont exposés pour que
    /// l'UI puisse montrer le calcul (et pour que le harnais puisse le vérifier étape par étape).
    /// Invariant : <see cref="BeforePenetration"/> == <see cref="Core"/> + <see cref="BonusApplied"/>.</summary>
    public sealed record Result(
        int Core,               // étape 1
        int BonusNet,           // somme algébrique de la catégorie Bonus (peut être négative)
        int BonusApplied,       // ce qui est RÉELLEMENT ajouté (règle des 25 / plancher 60)
        int BeforePenetration,  // fin de l'étape 2
        int AfterPenetration,   // étape 3, arrondi au plus proche
        int Effective,          // étape 4 — PEUT être négatif (flux Massive Damage, cf. wiki)
        IReadOnlyList<Contribution> Contributions);

    /// <summary>Déroule les 4 étapes sur un jeu de contributions déjà filtrées par l'appelant.</summary>
    public static Result Compute(IReadOnlyList<Contribution> contributions,
                                 int armorPenetrationPercent = 0)
    {
        // Étape 1 — Core : simple somme.
        int core = contributions.Where(c => c.Category == Category.Core).Sum(c => c.Value);

        // Étape 2 — Bonus net, puis les trois branches du wiki.
        var bonuses = contributions.Where(c => c.Category == Category.Bonus).ToList();
        int net = bonuses.Sum(c => c.Value);

        int beforePen;
        if (net >= 26)
        {
            // Bug du jeu : au-delà de 25 net, on n'ajoute QUE 25 — ou le plus gros bonus
            // UNITAIRE s'il dépasse 25, auquel cas les malus sont purement ignorés
            // (exemple 3 : +43 +5 −20 = net 28 → on applique 43, pas 28, et le −20 disparaît).
            int largestSingle = bonuses.Where(c => c.Value > 0)
                                       .Select(c => c.Value)
                                       .DefaultIfEmpty(0)
                                       .Max();
            beforePen = core + Math.Max(25, largestSingle);
        }
        else if (net >= 0)
        {
            beforePen = core + net;                     // 0 ≤ net ≤ 25 : tel quel.
        }
        else
        {
            // Net négatif : les malus ne peuvent faire descendre l'armure que jusqu'à 60 —
            // ou jusqu'au Core si celui-ci est déjà sous 60 (exemple 2 : Core 40, net −17 → 40).
            beforePen = Math.Max(core + net, Math.Min(60, core));
        }
        int applied = beforePen - core;                 // maintient l'invariant du Result.

        // Étape 3 — Pénétration : AL × (1 − p/100), arrondi au plus proche. AwayFromZero est
        // obligatoire (le banker's rounding de .NET donnerait 60,5 → 60) ; il reproduit les
        // exemples wiki (60,75 → 61 ; 98,25 → 98). Note wiki : en jeu le résultat peut valoir
        // 1 de moins (mécanique exacte inconnue) — on suit l'arrondi du wiki.
        double penetrated = beforePen * (1 - armorPenetrationPercent / 100.0);
        int afterPen = (int)Math.Round(penetrated, MidpointRounding.AwayFromZero);

        // Étape 4 — Special, APRÈS la pénétration. Aucun plancher : depuis le flux Massive
        // Damage, l'armure effective peut passer sous 0 (wiki/Armor_calculation, Notes).
        int special = contributions.Where(c => c.Category == Category.Special).Sum(c => c.Value);

        return new Result(core, net, applied, beforePen, afterPen, afterPen + special, contributions);
    }

    // ── Bases par profession (armure max, niveau 20) ──────────────────────────────────────

    /// <summary>AR max de base par profession (table wiki/Armor, validée par Philippe le
    /// 04/07/2026). Les inhérents du Basic armor (Guerrier +20 vs physique, Rôdeur +30 vs
    /// élémentaire) sont CONDITIONNELS au type de dégâts : ils sont du Core, mais c'est à
    /// l'appelant de les ajouter en Contribution quand la colonne de dégâts s'y prête.</summary>
    public static int BaseArmor(Profession p) => p switch
    {
        Profession.Warrior => 80,
        Profession.Paragon => 80,
        Profession.Ranger or Profession.Assassin or Profession.Dervish => 70,
        _ => 60, // Monk, Necromancer, Mesmer, Elementalist, Ritualist (et None)
    };

    // ── Localisations et espérance pondérée ───────────────────────────────────────────────

    /// <summary>Les 5 emplacements d'armure touchables.</summary>
    public enum HitLocation { Head, Chest, Arms, Legs, Feet }

    /// <summary>Ordre canonique des localisations (lignes de la grille de résultats).</summary>
    public static readonly IReadOnlyList<HitLocation> Locations =
        [HitLocation.Head, HitLocation.Chest, HitLocation.Arms, HitLocation.Legs, HitLocation.Feet];

    /// <summary>Probabilité qu'une attaque touche cette pièce (loi du jeu : torse 3/8,
    /// jambes 2/8, tête/bras/pieds 1/8 chacun). Somme = 1.</summary>
    public static double HitProbability(HitLocation loc) => loc switch
    {
        HitLocation.Chest => 3 / 8.0,
        HitLocation.Legs  => 2 / 8.0,
        _                 => 1 / 8.0,   // tête, bras, pieds
    };

    /// <summary>Multiplicateur de dégâts subis pour une AL donnée : 2^((3×niveau du lanceur − AL)/40)
    /// (= 1 à AL 60 pour un lanceur niveau 20). Même formule que <see cref="SkillDamage.DamageAt"/>,
    /// chantier 10 — ici en continu (pas de troncature), car elle sert au calcul d'espérance.</summary>
    public static double DamageMultiplier(int al, int casterLevel = 20) =>
        Math.Pow(2, (3 * casterLevel - al) / 40.0);

    /// <summary>Inverse de <see cref="DamageMultiplier"/> : l'AL qui subirait exactement ce
    /// multiplicateur de dégâts. AL = 3×niveau − 40×log2(E).</summary>
    public static double EquivalentArmor(double expectedMultiplier, int casterLevel = 20) =>
        3 * casterLevel - 40 * Math.Log2(expectedMultiplier);

    /// <summary>
    /// AL équivalente d'un jeu d'AL par pièce, pondéré par les probabilités de toucher.
    /// L'armure ne se MOYENNE PAS (les dégâts sont exponentiels en AL) : on fait l'espérance
    /// des MULTIPLICATEURS de dégâts, puis on revient à une AL.
    /// Contrôle : base 60 + insignes +10 partout sauf la tête → 68,65 (et non 68,75, moyenne naïve).
    /// </summary>
    public static double EquivalentArmor(IReadOnlyDictionary<HitLocation, int> armorByLocation,
                                         int casterLevel = 20)
    {
        double expected = Locations.Sum(loc =>
            HitProbability(loc) * DamageMultiplier(armorByLocation[loc], casterLevel));
        return EquivalentArmor(expected, casterLevel);
    }
}
