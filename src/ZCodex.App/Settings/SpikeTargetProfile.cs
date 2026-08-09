namespace ZCodex.App.Settings;

// Profil de cible nommé du « Spike damage calculus » : AL de base, niveau (taux de critique),
// PV max (Deep Wound) et bonus d'AL par groupe (physique = tranchant/perforant/contondant,
// élémentaire = feu/froid/terre/foudre) et par type précis, cumulés comme en jeu. Pas de bonus
// « contre ténèbres/chaos » (décision lore Philippe) ; sacré/ombre ignorent l'armure.
// Persisté dans settings.json (AppSettings.SpikeTargets).
public class SpikeTargetProfile
{
    public string Name { get; set; } = string.Empty;
    public int BaseArmor { get; set; } = 60;
    public int Level { get; set; } = 20;
    public int MaxHealth { get; set; } = 480;
    public int BonusPhysical { get; set; }
    public int BonusElemental { get; set; }
    public int BonusSlashing { get; set; }
    public int BonusPiercing { get; set; }
    public int BonusBlunt { get; set; }
    public int BonusFire { get; set; }
    public int BonusCold { get; set; }
    public int BonusEarth { get; set; }
    public int BonusLightning { get; set; }
}
