namespace ZCodex.App.ViewModels;

// Onglet dont le libellé est renommable en ligne (édition inline dans la barre d'onglets).
// L'application du nom (et l'éventuel renommage de fichier) est faite par le code-behind
// selon le type concret ; l'interface ne porte que l'état transitoire de l'édition.
public interface IRenamableTab
{
    // Nom courant utilisé pour amorcer la zone de saisie (sans marqueur « * » de dirty).
    string RenameSeed { get; }

    // Tampon lié à la TextBox pendant l'édition.
    string EditName { get; set; }

    // Vrai pendant l'édition inline (bascule TextBlock ↔ TextBox).
    bool IsRenaming { get; set; }
}
