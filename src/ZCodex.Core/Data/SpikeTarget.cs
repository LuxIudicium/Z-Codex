namespace ZCodex.Core.Data;

/// <summary>
/// Cible du calcul de spike : AL de base + bonus par groupe (physique = tranchant/perforant/
/// contondant ; élémentaire = feu/froid/terre/foudre) et par type précis, cumulés comme en jeu.
/// Aucun bonus « contre ténèbres/chaos » n'existe (décision lore Philippe) ; sacré/ombre/sans
/// type ignorent l'armure et ne passent pas par ici. Cracked Armor : −20 AL plancher 60
/// (wiki/Cracked_Armor : « -20 armor (minimum 60) »), appliquée APRÈS les bonus et AVANT la
/// pénétration ; une AL déjà ≤ 60 n'est NI réduite NI remontée (une condition ne peut pas
/// améliorer l'armure). Deep Wound : min(20 % des PV max, 100) comptés comme dégâts du spike
/// (wiki/Deep_Wound : « will never reduce your maximum health by more than 100 » et « for
/// spike teams, it provides extra damage on the spike »).
/// </summary>
public sealed record SpikeTarget(int BaseArmor, int Level, int MaxHealth,
                                 int BonusPhysical, int BonusElemental,
                                 int BonusSlashing, int BonusPiercing, int BonusBlunt,
                                 int BonusFire, int BonusCold, int BonusEarth, int BonusLightning)
{
    /// <summary>AL effective contre un type armor-respecting (dark/chaos : AL de base seule).</summary>
    public int EffectiveArmor(string? damageType, bool crackedArmor)
    {
        int al = BaseArmor + damageType switch
        {
            "slashing"  => BonusPhysical + BonusSlashing,
            "piercing"  => BonusPhysical + BonusPiercing,
            "blunt"     => BonusPhysical + BonusBlunt,
            "physical"  => BonusPhysical,
            "fire"      => BonusElemental + BonusFire,
            "cold"      => BonusElemental + BonusCold,
            "earth"     => BonusElemental + BonusEarth,
            "lightning" => BonusElemental + BonusLightning,
            _ => 0,
        };
        // Plancher qui n'AUGMENTE jamais : AL 75 → 60, AL 90 → 70, AL 58 → 58 (inchangée).
        return crackedArmor ? Math.Max(Math.Min(al, 60), al - 20) : al;
    }

    /// <summary>Dégâts effectifs de la Deep Wound sur un spike, tronqués.</summary>
    public int DeepWoundDamage => (int)Math.Min(0.2 * MaxHealth, 100.0);
}
