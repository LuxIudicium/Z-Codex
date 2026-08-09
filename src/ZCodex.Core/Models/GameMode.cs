namespace ZCodex.Core.Models;

/// <summary>
/// Contexte de jeu d'un build : décide quelle variante d'une compétence splittée s'applique
/// (« Heal Party » ou « Heal Party (PvP) »), et donc les valeurs affichées et calculées.
///
/// Doublon assumé de SkillGameMode (couche App) : le mode est persisté dans le .pn3 (v17), donc
/// le modèle Core doit pouvoir le porter sans dépendre de la couche de présentation. La
/// conversion entre les deux est triviale et localisée dans MainWindow.
/// </summary>
public enum GameMode
{
    All,
    PvE,
    PvP,
}
