using System.Collections.ObjectModel;
using System.IO;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Core.Search;
using ZCodex.Core.Serialization;
using ZCodex.Core.Templates;

namespace ZCodex.App.ViewModels;

public class BrowserViewModel : ViewModelBase
{
    private FolderTreeItemViewModel? _selectedFolder;
    private BuildFileViewModel? _selectedFile;
    private TeamBuild? _previewBuild;
    private EquipmentPreviewViewModel? _equipmentPreview;
    private string? _sortColumn;
    private bool _sortDescending;
    private bool _isSkillTemplateView;
    private bool _isResultsMode;
    private bool _showPreviewAttributes;
    private string? _rootPath;
    private string? _currentFolderPath;
    private Func<IReadOnlyDictionary<int, Skill>>? _skillsProvider;

    public ObservableCollection<FolderTreeItemViewModel> RootItems { get; } = new();
    public ObservableCollection<BuildFileViewModel> Files { get; } = new();

    // Dossier racine actuel (détecté ou choisi par l'utilisateur). Null si non défini.
    public string? RootPath
    {
        get => _rootPath;
        private set { if (SetField(ref _rootPath, value)) OnPropertyChanged(nameof(RootPathDisplay)); }
    }

    // Chemin affiché dans le bandeau, avec repli localisé quand aucune racine n'est définie.
    public string RootPathDisplay =>
        string.IsNullOrEmpty(_rootPath) ? ZCodex.App.LanguageManager.T("S.Browser.NoFolder") : _rootPath;

    // Bascule de langue : libellés affichés des fichiers (format, professions, carac, élite) +
    // repli du chemin, puis re-tri (l'ordre suit le texte affiché).
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(RootPathDisplay));
        foreach (var f in Files) f.RefreshLanguage();
        ApplySort();

        // Panneau de prévisualisation : c'est un objet Core (TeamBuild/CharacterBuild) figé au
        // chargement, dont les libellés (nom de build, placeholder, noms de compétences) ne se
        // réévaluent pas — les POCO Core ne notifient pas. On le recharge : PreviewBuild repasse
        // par null, donc l'arbre visuel est rebâti dans la langue courante. Coût = le seul
        // fichier sélectionné.
        LoadPreview(SelectedFile);
    }

    // Levé quand la racine change (auto-détection, prompt ou Browse) → persistance par MainWindow.
    public event Action<string>? RootChanged;

    // Vrai quand le dossier courant ne contient que des skill templates (.txt) :
    // le browser affiche alors les colonnes façon paw·ned² (Profession, Elite, etc.).
    public bool IsSkillTemplateView
    {
        get => _isSkillTemplateView;
        private set => SetField(ref _isSkillTemplateView, value);
    }

    // Mode résultats de recherche : liste plate (sans arbre ni Browse), alimentée par LoadResults.
    // La vue masque la colonne arbre et le bandeau racine quand ce drapeau est vrai.
    public bool IsResultsMode
    {
        get => _isResultsMode;
        set => SetField(ref _isResultsMode, value);
    }

    // Affiche la ligne d'attributs dans la preview des team builds (toggle menu View).
    // Masquée par défaut ; les skill templates l'affichent toujours (cf. IsSkillTemplateView).
    public bool ShowPreviewAttributes
    {
        get => _showPreviewAttributes;
        set => SetField(ref _showPreviewAttributes, value);
    }

    // Raised when the user wants to open a file in a tab
    public event Action<string>? OpenFileRequested;

    public FolderTreeItemViewModel? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetField(ref _selectedFolder, value))
                LoadFiles(value?.Path);
        }
    }

    public BuildFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetField(ref _selectedFile, value))
                LoadPreview(value);
        }
    }

    // Preview build: n×4×8 layout — only main characters (no variants)
    public TeamBuild? PreviewBuild
    {
        get => _previewBuild;
        private set
        {
            if (SetField(ref _previewBuild, value))
                OnPropertyChanged(nameof(PreviewGroups));
        }
    }

    // Groups of up to 4 characters for the n×4×8 preview (n = ⌈chars/4⌉)
    public IReadOnlyList<CharacterBuild[]>? PreviewGroups =>
        _previewBuild?.Characters.Chunk(4).ToArray();

    // Aperçu d'un template d'équipement. Exclusif de PreviewBuild : les deux occupent le même
    // panneau, un fichier ne peut pas être les deux à la fois.
    public EquipmentPreviewViewModel? EquipmentPreview
    {
        get => _equipmentPreview;
        private set => SetField(ref _equipmentPreview, value);
    }

    public void SetSkillsProvider(Func<IReadOnlyDictionary<int, Skill>> provider)
        => _skillsProvider = provider;

    // Ids de template pour encoder une barre de la preview. Construits sur le CATALOGUE complet et
    // non sur les seules compétences du build : une variante « (PvP) » encode l'id de sa version de
    // base, qui n'est pas forcément équipée. Vide tant que le catalogue n'est pas chargé.
    public Dictionary<string, int> TemplateIdsByName()
        => SkillVariants.TemplateIdsByName(
            _skillsProvider?.Invoke().Values ?? Enumerable.Empty<Skill>());

    public void SetRoot(string path)
    {
        RootItems.Clear();
        if (!Directory.Exists(path)) return;
        RootPath = path;
        var root = new FolderTreeItemViewModel(path);
        root.IsExpanded = true;
        RootItems.Add(root);
        AddGwRankNode();
        LoadFiles(path);
        RootChanged?.Invoke(path);
    }

    // ── GWRank ────────────────────────────────────────────────────────────────
    // Le miroir GWRank est une SECONDE racine de l'arbre, à côté du dossier de templates, et non
    // un sous-dossier de celui-ci : ce sont les builds du serveur (dont ceux d'autres joueurs),
    // pas la bibliothèque de l'utilisateur. Les mélanger fausserait la recherche, les sauvegardes
    // et la détection de doublons d'identité.
    private void AddGwRankNode()
    {
        if (!Directory.Exists(ZCodex.Core.Sync.GwRankBrowserCache.Root)) return;

        // Deplie d'office : le noeud est RECREE a chaque synchro, et le retrouver ferme apres
        // chaque envoi donnerait l'impression que rien n'est arrive.
        var node = new FolderTreeItemViewModel(ZCodex.Core.Sync.GwRankBrowserCache.Root)
        {
            IsExpanded = true,
        };
        RootItems.Add(node);
    }

    /// <summary>Reconstruit le nœud GWRank après une synchronisation. Le nœud est RECRÉÉ et non
    /// mis à jour : ses enfants sont chargés une seule fois, à la première expansion, donc un
    /// nœud conservé continuerait d'afficher l'arborescence d'avant la synchro.</summary>
    public void RefreshGwRankNode()
    {
        for (int i = RootItems.Count - 1; i >= 0; i--)
            if (ZCodex.Core.Sync.GwRankBrowserCache.IsInCache(RootItems[i].Path)
                || string.Equals(RootItems[i].Path, ZCodex.Core.Sync.GwRankBrowserCache.Root,
                                 StringComparison.OrdinalIgnoreCase))
                RootItems.RemoveAt(i);

        AddGwRankNode();

        // La liste de droite peut montrer un dossier du miroir qui vient d'être remplacé.
        if (ZCodex.Core.Sync.GwRankBrowserCache.IsInCache(_currentFolderPath))
            LoadFiles(Directory.Exists(_currentFolderPath) ? _currentFolderPath : null);
    }

    public void OpenSelected()
    {
        if (SelectedFile != null)
            OpenFileRequested?.Invoke(SelectedFile.FilePath);
    }

    // Renomme le fichier sélectionné (extension conservée). False si collision ou échec.
    public bool RenameSelectedTo(string newBaseName)
    {
        var file = SelectedFile;
        var dir = file == null ? null : Path.GetDirectoryName(file.FilePath);
        if (file == null || dir == null) return false;

        var target = Path.Combine(dir, newBaseName + Path.GetExtension(file.FilePath));
        if (string.Equals(target, file.FilePath, StringComparison.OrdinalIgnoreCase)) return true;
        if (File.Exists(target)) return false;

        try { File.Move(file.FilePath, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Rename '{file.FilePath}' → '{target}': {ex.Message}");
            return false;
        }

        RefreshFiles();
        SelectedFile = Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, target, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    // Envoie le fichier sélectionné à la corbeille (récupérable), puis rafraîchit.
    public void DeleteSelected()
    {
        var file = SelectedFile;
        if (file == null) return;
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                file.FilePath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete '{file.FilePath}': {ex.Message}");
            return;
        }
        RefreshFiles();
    }

    // Importe des fichiers externes dans le dossier affiché (copie non-destructive).
    public void ImportFiles(IEnumerable<string> sourcePaths)
    {
        if (_currentFolderPath == null || !Directory.Exists(_currentFolderPath)) return;
        string? last = null;
        foreach (var src in sourcePaths)
        {
            try
            {
                var dest = UniqueDestination(_currentFolderPath, Path.GetFileName(src));
                File.Copy(src, dest);
                last = dest;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Import '{src}': {ex.Message}");
            }
        }
        if (last == null) return;
        RefreshFiles();
        SelectedFile = Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, last, StringComparison.OrdinalIgnoreCase));
    }

    // Exporte le fichier sélectionné vers un dossier externe (copie non-destructive).
    public bool ExportSelectedTo(string targetDir)
    {
        var file = SelectedFile;
        if (file == null || !Directory.Exists(targetDir)) return false;
        try
        {
            File.Copy(file.FilePath, UniqueDestination(targetDir, Path.GetFileName(file.FilePath)));
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export '{file.FilePath}' → '{targetDir}': {ex.Message}");
            return false;
        }
    }

    // Chemin libre dans dir : ajoute " - Copie", " - Copie (2)"… si le nom existe déjà.
    private static string UniqueDestination(string dir, string fileName)
    {
        var dest = Path.Combine(dir, fileName);
        if (!File.Exists(dest)) return dest;
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 1; ; i++)
        {
            var suffix = i == 1 ? " - Copie" : $" - Copie ({i})";
            dest = Path.Combine(dir, baseName + suffix + ext);
            if (!File.Exists(dest)) return dest;
        }
    }

    public void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }
        ApplySort();
    }

    // Recharge la liste du dossier affiché (après une opération fichier).
    public void RefreshFiles() => LoadFiles(_currentFolderPath);

    // Alimente la liste à partir d'un ensemble explicite de chemins (résultats de recherche,
    // potentiellement issus de plusieurs dossiers). Colonnes team-build (résultats mixtes :
    // la colonne Format distingue .txt / .pn3 / .pwnd).
    public void LoadResults(IEnumerable<string> filePaths)
    {
        _currentFolderPath = null;
        Files.Clear();
        SelectedFile = null;
        foreach (var path in filePaths)
            Files.Add(new BuildFileViewModel(path, _skillsProvider));
        IsSkillTemplateView = false;
    }

    private void LoadFiles(string? folderPath)
    {
        _currentFolderPath = folderPath;
        Files.Clear();
        SelectedFile = null;
        if (folderPath == null || !Directory.Exists(folderPath)) return;
        try
        {
            var files = Directory.EnumerateFiles(folderPath)
                .Where(f => TeamBuildSerializer.IsNativeExtension(Path.GetExtension(f))
                         || f.EndsWith(PwndImporter.Extension, StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(SkillTemplateImporter.Extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => new BuildFileViewModel(f, _skillsProvider));
            foreach (var f in files) Files.Add(f);
        }
        catch { }
        IsSkillTemplateView = Files.Count > 0 && Files.All(f =>
            f.FilePath.EndsWith(SkillTemplateImporter.Extension, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplySort()
    {
        var sorted = (_sortColumn, _sortDescending) switch
        {
            ("Name",     false) => Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
            ("Name",     true)  => Files.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase),
            ("Format",   false) => Files.OrderBy(f => f.Format),
            ("Format",   true)  => Files.OrderByDescending(f => f.Format),
            ("Players",  false) => Files.OrderBy(f => f.PlayerCount),
            ("Players",  true)  => Files.OrderByDescending(f => f.PlayerCount),
            ("Campaigns", false) => Files.OrderBy(f => f.Campaigns, StringComparer.OrdinalIgnoreCase),
            ("Campaigns", true)  => Files.OrderByDescending(f => f.Campaigns, StringComparer.OrdinalIgnoreCase),
            // Tri sur les libellés AFFICHÉS : l'ordre doit suivre le texte qu'on lit à l'écran.
            ("Profession", false) => Files.OrderBy(f => f.PrimaryProfessionName, StringComparer.CurrentCulture),
            ("Profession", true)  => Files.OrderByDescending(f => f.PrimaryProfessionName, StringComparer.CurrentCulture),
            ("Secondary",  false) => Files.OrderBy(f => f.SecondaryProfessionName, StringComparer.CurrentCulture),
            ("Secondary",  true)  => Files.OrderByDescending(f => f.SecondaryProfessionName, StringComparer.CurrentCulture),
            ("Attribute",  false) => Files.OrderBy(f => f.HighestSecondaryAttributeDisplay, StringComparer.CurrentCulture),
            ("Attribute",  true)  => Files.OrderByDescending(f => f.HighestSecondaryAttributeDisplay, StringComparer.CurrentCulture),
            ("Elite",      false) => Files.OrderBy(f => f.EliteSkillDisplay, StringComparer.CurrentCulture),
            ("Elite",      true)  => Files.OrderByDescending(f => f.EliteSkillDisplay, StringComparer.CurrentCulture),
            ("Modified", false) => Files.OrderBy(f => f.Modified),
            ("Modified", true)  => Files.OrderByDescending(f => f.Modified),
            _                   => Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
        };

        var list = sorted.ToList();
        Files.Clear();
        foreach (var f in list) Files.Add(f);
    }

    private void LoadPreview(BuildFileViewModel? file)
    {
        PreviewBuild = null;
        EquipmentPreview = null;
        if (file == null) return;

        // Un template d'équipement n'a pas de barre de compétences : aperçu dédié, et surtout
        // pas besoin du catalogue — il s'affiche donc même au tout premier démarrage, pendant
        // le scraping.
        if (file.IsEquipmentTemplate)
        {
            EquipmentPreview = LoadEquipmentPreview(file.FilePath);
            return;
        }

        if (_skillsProvider == null) return;
        var skillsById = _skillsProvider();
        if (skillsById.Count == 0) return;

        try
        {
            PreviewBuild = Path.GetExtension(file.FilePath).ToLowerInvariant() switch
            {
                ".pwnd" => PwndImporter.Import(file.FilePath, skillsById),
                ".txt"  => SkillTemplateImporter.Import(file.FilePath, skillsById),
                _       => TeamBuildSerializer.Load(file.FilePath, skillsById),
            };
        }
        catch { }
    }

    // Plusieurs codes P dans le fichier = un par set d'armes : on les recombine en un seul
    // équipement, comme à l'ouverture (cf. MainWindow.TryOpenEquipmentTemplate).
    private static EquipmentPreviewViewModel? LoadEquipmentPreview(string filePath)
    {
        try
        {
            var builds = GwEquipmentCodec.DecodeLines(File.ReadAllText(filePath));
            if (builds.Count == 0) return null;
            return new EquipmentPreviewViewModel(
                builds.Count == 1 ? builds[0] : EquipmentBuild.Combine(builds));
        }
        catch { return null; }
    }

    // Searches Documents for the GW templates folder
    public static string? FindGuildWarsTemplatesFolder()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var variant in new[] { "GUILD WARS", "Guild Wars", "GuildWars" })
        {
            var path = Path.Combine(docs, variant, "Templates");
            if (Directory.Exists(path)) return path;
        }
        return null;
    }
}
