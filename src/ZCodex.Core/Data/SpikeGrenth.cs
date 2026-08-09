namespace ZCodex.Core.Data;

/// <summary>
/// Cas particuliers Grenth du calcul de spike (chantier 15). Deux compétences que le moteur de
/// détection générique ne sait pas chiffrer seul :
///
/// - <b>Grenth's Balance</b> (« No Attribute », aucun paquet fixe) : transfert de PV selon la
///   différence de vie. Mécanique « deux gobelets » confirmée par Philippe — le lanceur vole la
///   cible quand il a MOINS de PV, mais jamais plus que sa capacité restante (PV manquants). Le
///   dégât porté au spike = ce vol (perte de PV de la cible, ignore l'armure).
///
/// - <b>Grenth's Aura</b> : DEUX vols de vie de même valeur — « steal X Health when you hit with
///   a scythe » (PAR COUP → compteur Procs) et « Initial effect: steal X Health » (one-time).
///   Traité en dual comme Mind Wrack (le proc multiplierait sinon les DEUX paquets).
/// </summary>
public static class SpikeGrenth
{
    /// <summary>
    /// Dégât (perte de PV) infligé à la cible par Grenth's Balance. Formule Philippe :
    /// transfert = min( (PVcible − PVlanceur)/2 , PVlanceur_max − PVlanceur ), plancher 0.
    /// Nul si le lanceur a autant/plus de PV que la cible, ou s'il est déjà à son maximum.
    /// Ex. lanceur 400/500, cible 1000/1000 → (1000−400)/2 = 300, plafonné par 500−400 = 100.
    /// </summary>
    public static int BalanceDamage(int casterCurrentHp, int casterMaxHp, int targetCurrentHp)
    {
        int half = (targetCurrentHp - casterCurrentHp) / 2;   // troncature entière (GW1)
        if (half <= 0) return 0;
        int capacity = Math.Max(0, casterMaxHp - casterCurrentHp);
        return Math.Min(half, capacity);
    }

    /// <summary>Vrai pour Grenth's Balance (calcul sur mesure à partir des PV saisis).</summary>
    public static bool IsBalance(string skillName)
        => string.Equals(skillName, "Grenth's Balance", StringComparison.OrdinalIgnoreCase);

    /// <summary>Vrai pour Grenth's Aura (2 vols de vie : par coup [Procs] + initial one-time).</summary>
    public static bool IsAuraDual(string skillName)
        => string.Equals(skillName, "Grenth's Aura", StringComparison.OrdinalIgnoreCase);
}
