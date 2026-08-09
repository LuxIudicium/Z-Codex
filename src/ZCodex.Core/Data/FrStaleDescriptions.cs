namespace ZCodex.Core.Data;

/// <summary>
/// Compétences dont la description FR de gwiki est incomplète ou périmée alors que ses PLAGES
/// DE VALEURS s'apparient correctement — le détecteur automatique <c>FrSuspect</c> (calculé par
/// le scraper) ne les voit donc pas, et elles s'afficheraient en français avec un contenu faux.
///
/// Repérées en comparant le NOMBRE de plages entre le texte anglais et le texte français : quand
/// il en manque côté FR, c'est qu'une clause entière a sauté (relevé 27/07/2026, 21 cas sur
/// 1253 descriptions FR affichées). Liste validée avec Philippe.
///
/// Effet : ces compétences retombent sur la description ANGLAISE et affichent l'avertissement
/// correspondant dans l'infobulle (cf. <c>Skill.DescriptionFallback</c>). Mieux vaut un anglais
/// juste qu'un français faux.
///
/// À réviser si gwiki est mis à jour : re-lancer le détecteur (scratchpad <c>stale_text.py</c>).
/// </summary>
public static class FrStaleDescriptions
{
    // Motif indiqué pour chaque entrée = ce qui MANQUE (ou diverge) dans le texte français.
    private static readonly HashSet<int> Ids =
    [
        11,   // Distortion — texte FR écourté
        2,    // Resurrection Signet — omet que le sceau ne se recharge qu'au gain de moral
        49,   // Mind Wrack — omet les dégâts par point d'énergie perdu
        87,   // Verata's Gaze — omet le soin 60...80 ; seuil d'échec faux (4 au lieu de 5)
        126,  // Life Transfer — omet le maléfice sur les ennemis adjacents
        172,  // Stone Daggers — omet le Saignement en cas de Surcharge
        204,  // Rust — omet l'interruption/désactivation des sceaux si Surcharge
        205,  // Lightning Surge — omet Armure brisée et la pénétration d'armure de 25 %
        261,  // Shield of Regeneration — bonus d'armure figé à +40 au lieu de +20...45
        391,  // Hunter's Shot — omet la condition « si la cible est touchée »
        405,  // Oath Shot — durée figée à 10 s ; seuil d'Expertise faux (7 au lieu de 8)
        865,  // Lightning Hammer — omet Armure brisée
        871,  // Shadowsong — omet les dégâts des attaques de l'esprit
        892,  // Quivering Blade — dégâts figés à +10 au lieu de +10...40
        937,  // Shockwave — 3 paliers (zone/proximité/adjacent) réduits à une phrase vague
        1095, // Star Burst — omet la Brûlure ; annonce une PERTE d'énergie au lieu d'un GAIN
        1531, // Intimidating Aura — décrit une tout autre mécanique (retrait d'enchantement)
        1536, // Wounding Strike — omet le retrait d'enchantement de Derviche
        2001, // Ward of Weakness — omet la durée de la protection elle-même
        3006, // Shadowsong (PvP) — idem 871
    ];

    /// <summary>true si la description FR de cette compétence est connue pour être fausse.</summary>
    public static bool IsStale(int skillId) => Ids.Contains(skillId);

    public static int Count => Ids.Count;
}
