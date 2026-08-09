namespace ZCodex.Core.Data;

// Heroic Refrain (Lot D) : seule source de boost d'attribut dont l'origine n'est PAS sur sa propre
// barre — diffusée par N'IMPORTE QUEL perso de l'équipe qui l'équipe, reçue par CHAQUE perso via
// un toggle personnel indépendant (décision Philippe, 19/07/2026). Le rang de résolution = celui
// du LANCEUR le plus fort équipé (patron Roaring Winds/Tranquility, « le plus fort gagne »), PAS
// le rang du receveur (qui peut ne même pas avoir Leadership).
public static class HeroicRefrainData
{
    public const int SkillId = 3431;
    public const string ScalingAttribute = "Leadership";

    // Bonus "+N à tous les attributs" au rang du lanceur (colonne 1 : "+1...3...3 to all
    // attributes" ; colonne 0 = durée, ignorée). Rang/progression absents → 0.
    public static int BonusAtRank(string[][]? progression, int rank) =>
        SkillProgression.IntAt(progression is { Length: > 1 } p ? p[1] : null, rank) ?? 0;
}
