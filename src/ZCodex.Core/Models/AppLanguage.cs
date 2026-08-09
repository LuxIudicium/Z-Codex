namespace ZCodex.Core.Models;

/// <summary>
/// Langue d'affichage de l'application (français par défaut). Le canon INTERNE reste
/// l'anglais : tout le moteur (regex de descriptions, matching par nom, clés .pn3,
/// codes de template) travaille sur les champs EN. <see cref="IsFr"/> ne pilote que
/// les propriétés Display* (Skill) et l'habillage UI — jamais un calcul.
/// </summary>
public static class AppLanguage
{
    public static bool IsFr { get; set; } = true;
}
