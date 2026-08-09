using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZCodex.App.ViewModels;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;

namespace ZCodex.App.Views;

public partial class BrowserView : UserControl
{
    private static string T(string key) => LanguageManager.T(key);

    public BrowserView()
    {
        InitializeComponent();
    }

    private BrowserViewModel? Vm => DataContext as BrowserViewModel;

    // En mode résultats de recherche : liste plate → on masque l'arbre, son splitter et le
    // bandeau racine, et on récupère leur largeur (colonnes à 0).
    private void BrowserView_Loaded(object sender, RoutedEventArgs e)
    {
        if (Vm?.IsResultsMode != true) return;
        BrowseBar.Visibility = Visibility.Collapsed;
        FolderTree.Visibility = Visibility.Collapsed;
        LeftSplitter.Visibility = Visibility.Collapsed;
        LeftColumn.MinWidth = 0;
        LeftColumn.Width = new GridLength(0);
        SplitterCol.Width = new GridLength(0);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm != null && e.NewValue is FolderTreeItemViewModel folder)
            Vm.SelectedFolder = folder;
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => Vm?.OpenSelected();

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
            Vm?.OpenSelected();
    }

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader { Tag: string column })
            Vm?.SortBy(column);
    }

    // Le clic droit ne sélectionne pas la ligne par défaut : on le force avant le menu.
    private void FileItem_PreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item) item.IsSelected = true;
    }

    private void RenameFile_Click(object sender, RoutedEventArgs e)
    {
        var file = Vm?.SelectedFile;
        if (file == null) return;
        var dlg = new RenameWindow(file.Name) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && !Vm!.RenameSelectedTo(dlg.NewName))
            MessageBox.Show(T("S.Msg.RenameConflict"),
                T("S.Msg.RenameTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // Bouton copier d'une ligne de la preview : encode la barre du personnage en
    // chat code paw·ned² et le place dans le presse-papier (silencieux).
    private void CopyBar_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CharacterBuild build) return;

        // Ids pris sur le catalogue complet : une variante « (PvP) » doit encoder l'id de sa
        // version de base, qui n'est pas nécessairement équipée sur cette barre.
        var code = GwTemplateCodec.Encode(build, Vm?.TemplateIdsByName() ?? []);
        int index = (Vm?.PreviewBuild?.Characters.IndexOf(build) ?? -1) + 1;
        var chat  = GwTemplateCodec.FormatChatCode(
            index, build.PrimaryProfession, build.SecondaryProfession, build.Name, code);

        SafeClipboard.SetText(chat);
    }

    private void DeleteFile_Click(object sender, RoutedEventArgs e) => Vm?.DeleteSelected();

    private void CopyFile_Click(object sender, RoutedEventArgs e)
    {
        var file = Vm?.SelectedFile;
        if (file == null) return;
        SafeClipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { file.FilePath });
    }

    private void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dlg = new OpenFileDialog
        {
            Title = T("S.Dlg.ImportBuilds"),
            Multiselect = true,
            Filter = T("S.Filter.Builds"),
        };
        if (dlg.ShowDialog() == true)
            Vm.ImportFiles(dlg.FileNames);
    }

    private void ExportFile_Click(object sender, RoutedEventArgs e)
    {
        var file = Vm?.SelectedFile;
        if (file == null) return;
        var dlg = new OpenFolderDialog { Title = T("S.Dlg.ExportToFolder") };
        if (dlg.ShowDialog() == true && !Vm!.ExportSelectedTo(dlg.FolderName))
            MessageBox.Show(T("S.Msg.ExportFileFailed"),
                T("S.Msg.ExportTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dlg = new OpenFolderDialog
        {
            Title = T("S.Dlg.SelectBuildsRoot"),
            InitialDirectory = Vm.RootPath ?? "",
        };
        if (dlg.ShowDialog() == true)
            Vm.SetRoot(dlg.FolderName); // déclenche RootChanged → persistance
    }
}
