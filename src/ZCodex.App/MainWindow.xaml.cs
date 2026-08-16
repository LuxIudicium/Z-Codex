using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ZCodex.App.Settings;
using ZCodex.App.Undo;
using ZCodex.App.ViewModels;
using ZCodex.App.Views;
using ZCodex.Core;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Importers;
using ZCodex.Core.Search;
using ZCodex.Core.Serialization;
using ZCodex.Core.Templates;
using ZCodex.Data;
using ZCodex.Data.Repositories;
using ZCodex.Scraper;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppDbContext _db;
    private readonly Settings.AppSettings _settings = Settings.AppSettings.Load();
    private Point _dragStart;
    private Point _slotDragStartPos;
    private SkillSlotViewModel? _slotDragSource;

    // Molette d'attribut au-dessus d'un slot : on ferme l'infobulle du slot le temps de la rafale
    // (elle recouvrirait les niveaux modifiés + son recalcul est coûteux) et on la réactive 500 ms
    // après le dernier cran.
    private readonly System.Windows.Threading.DispatcherTimer _attrWheelTooltipTimer =
        new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DependencyObject? _suppressedTooltipSlot;

    // Raccourci de localisation pour les chaînes construites en C# (MessageBox, menus dynamiques).
    private static string T(string key) => LanguageManager.T(key);

    public MainWindow()
    {
        // Avant InitializeComponent : les DynamicResource résolvent la bonne palette dès le premier rendu.
        ThemeManager.Apply(_settings.DarkTheme);
        // Idem pour la langue (chaînes UI + propriétés Display* des skills, chargées après).
        LanguageManager.Apply(_settings.Language != "en");
        InitializeComponent();
        UpdateLanguageButtons(LanguageManager.IsFr);   // drapeaux visibles en permanence
        RestoreWindowBounds();
        _db = AppDbContextFactory.Create();
        _vm = new MainViewModel();
        _vm.ShowConditions = _settings.ShowConditions;
        _vm.ShowAllNatureRituals = _settings.ShowAllNatureRituals;
        _vm.ShowNatureRituals = _settings.ShowNatureRituals;
        _vm.IconSize = _settings.IconSize;
        _vm.WheelNeedsSelection = _settings.WheelNeedsSelection;
        _vm.ArmorCalc.LoadSettings(_settings);  // profils du calculateur d'armure (chantier 14)
        ArmorDamageDisplay.Enabled = _settings.ShowArmorDamage;
        // Migration de l'ancien AL custom unique vers la liste (max 8, bornée au chargement :
        // un settings.json édité à la main ne doit pas casser les formules ni la modale).
        ArmorDamageDisplay.CustomArmorLevels = _settings.CustomArmorLevels.Count > 0
            ? _settings.CustomArmorLevels.Where(al => al is >= 0 and <= 200).Distinct().Take(8).ToList()
            : _settings.CustomArmorLevel is int legacyAl ? [legacyAl] : [];
        ArmorDamageDisplay.CharacterLevel = Math.Clamp(_settings.CharacterLevel, 1, 20);
        ArmorDamageDisplay.TargetLevel = Math.Clamp(_settings.TargetLevel, 1, 40);
        DataContext = _vm;

        // Un cran de molette absorbé par le défilement natif ne remonte pas jusqu'aux handlers
        // ordinaires : handledEventsToo est le seul moyen d'apprendre, après coup, qu'il a fait
        // défiler la grille plutôt que régler une caractéristique.
        TeamScroll.AddHandler(MouseWheelEvent, new MouseWheelEventHandler(TeamScroll_MouseWheel),
                              handledEventsToo: true);

        _attrWheelTooltipTimer.Tick += (_, _) =>
        {
            _attrWheelTooltipTimer.Stop();
            if (_suppressedTooltipSlot != null) ToolTipService.SetIsEnabled(_suppressedTooltipSlot, true);
            _suppressedTooltipSlot = null;
        };

        // Undo/redo : un manager par onglet team build, attaché ici quel que soit le chemin
        // de création (File > New, Open, browser) — voir OnOpenTeamBuildsChanged.
        _vm.OpenTeamBuilds.CollectionChanged += OnOpenTeamBuildsChanged;
        _vm.OpenBuilds.CollectionChanged     += OnOpenBuildsChanged;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, Undo_Executed, UndoRedo_CanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, Redo_Executed, UndoRedo_CanExecute));
        // Ctrl+C / Ctrl+V sur le PERSONNAGE SURVOLÉ (copie du code O / import du presse-papier).
        // Même mécanique que Undo/Redo : un contrôle d'édition focusé (recherche de compétences,
        // renommage d'onglet) revendique Copy/Paste avant la fenêtre et garde son comportement
        // natif — d'où le CommandBinding de fenêtre plutôt qu'un InputBinding.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopyHovered_Executed, HoveredCharacter_CanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, PasteHovered_Executed, HoveredCharacter_CanExecute));
        // Aide : ApplicationCommands.Help route F1 (et le menu Help) vers la fenêtre d'aide.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, Help_Executed));
        // Enregistrement. ApplicationCommands.Save porte déjà Ctrl+S dans ses InputGestures : le
        // CommandBinding suffit à activer le raccourci ET à afficher « Ctrl+S » dans le menu.
        // Contrairement à Undo/Redo, aucun contrôle d'édition ne revendique Save → pas de conflit
        // avec un TextBox focusé.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, Save_Executed, Save_CanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, SaveAs_Executed, Save_CanExecute));
        // SaveAs n'a PAS de geste par défaut dans WPF : on le pose à la main (et le libellé du
        // menu est écrit en dur dans le XAML, un InputBinding de fenêtre ne le renseigne pas).
        InputBindings.Add(new KeyBinding(ApplicationCommands.SaveAs, Key.S,
                                         ModifierKeys.Control | ModifierKeys.Shift));
        // Alt appartient à Alt+molette (breakpoints de caractéristique, HandleBreakpointWheel) :
        // on neutralise le « mode menu » de WPF, sinon le relâchement d'Alt après une rafale donne
        // le focus à « Fichier ». Marquer le KeyDown traité suffit — AccessKeyManager ignore les
        // touches déjà traitées, et c'est aussi ce qui éteint le soulignement des mnémoniques.
        // Seul Alt SEUL est filtré (SystemKey = LeftAlt/RightAlt) : Alt+F4 et Alt+Espace portent
        // leur propre SystemKey et continuent d'aller à DefWindowProc. Les mnémoniques « _X » ont
        // par ailleurs été retirées des libellés (Strings.fr/en.xaml).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true;
        };
        Loaded += async (_, _) =>
        {
            InitializeBrowser();
            // Lancés AVANT le scrape du catalogue, et non après : au tout premier démarrage
            // celui-ci dure cinq minutes, et une icône qui n'arrive sur le disque qu'à la fin
            // arrive trop tard pour les bindings déjà évalués. Quelques dizaines de Ko, en
            // parallèle du scrape, non bloquants.
            _ = ProfessionIconService.DownloadAllAsync();
            _ = SkillStatIconService.DownloadAllAsync();
            _ = ConditionIconService.DownloadAllAsync();
            _ = FluxIconService.DownloadAllAsync();
            _ = LifeStealIconService.DownloadAllAsync();
            await InitializeSkillsAsync();
        };
    }

    // Le thème WPF par défaut rend TOUJOURS le bouton de débordement du ToolBar : il n'est que
    // *désactivé* quand rien ne déborde (IsEnabled lié à HasOverflowItems), au lieu d'être masqué.
    // Résultat : un rectangle blanc inerte et hors-thème collé à droite de la barre, insensible au
    // redimensionnement. Les items étant tous en OverflowMode=Never (rien ne peut déborder), on
    // masque ce chrome et on récupère la gouttière que le template lui réservait.
    private void MainToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        var toolBar = (ToolBar)sender;

        if (toolBar.Template.FindName("OverflowGrid", toolBar) is FrameworkElement overflowGrid)
            overflowGrid.Visibility = Visibility.Collapsed;

        if (toolBar.Template.FindName("MainPanelBorder", toolBar) is FrameworkElement mainPanelBorder)
            mainPanelBorder.Margin = new Thickness(0);
    }

    // Restaure la taille/position sauvegardées, si elles tiennent encore dans l'espace des écrans actuels.
    private void RestoreWindowBounds()
    {
        if (_settings.WindowWidth is double w && _settings.WindowHeight is double h && w >= MinWidth && h >= MinHeight)
        {
            Width = w;
            Height = h;
        }

        if (_settings.WindowLeft is double left && _settings.WindowTop is double top
            && left >= 0 && top >= 0
            && left + Width <= SystemParameters.VirtualScreenWidth
            && top + Height <= SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowBounds()
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.Save();
    }

    // ── Browser ───────────────────────────────────────────────────────────────

    private void InitializeBrowser()
    {
        _vm.Browser.OpenFileRequested += OpenFileFromBrowser;
        _vm.Browser.RootChanged += OnBrowserRootChanged;

        // Racine : préférence persistée si elle existe encore, sinon auto-détection.
        var root = (_settings.TemplatesRootPath != null && Directory.Exists(_settings.TemplatesRootPath))
            ? _settings.TemplatesRootPath
            : BrowserViewModel.FindGuildWarsTemplatesFolder();
        if (root != null)
        {
            _vm.Browser.SetRoot(root);
        }
        else
        {
            var result = MessageBox.Show(
                T("S.Msg.RootNotFound"),
                T("S.Msg.RootNotFoundTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var dlg = new OpenFolderDialog
                {
                    Title = T("S.Msg.PickRootFolder"),
                };
                if (dlg.ShowDialog() == true)
                    _vm.Browser.SetRoot(dlg.FolderName);
            }
        }
    }

    private void OnBrowserRootChanged(string path)
    {
        _settings.TemplatesRootPath = path;
        _settings.Save();
    }

    private void OpenFileFromBrowser(string filePath)
    {
        if (_vm.SkillPanel.AllSkills.Count == 0)
        {
            MessageBox.Show(T("S.Msg.CatalogNotLoaded"),
                T("S.Msg.CatalogNotLoadedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var skillsById = _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        // .txt ne contenant que des codes P → modale d'équipement préremplie (pas un team build).
        if (ext == SkillTemplateImporter.Extension && TryOpenEquipmentTemplate(filePath, skillsById))
            return;

        // .txt de compétences (code O) → éditeur de build SIMPLE (léger), pas un teambuild à 1 perso.
        if (ext == SkillTemplateImporter.Extension)
        {
            var tbSkill = SkillTemplateImporter.Import(filePath, skillsById);
            if (tbSkill == null || tbSkill.Characters.Count == 0)
            {
                MessageBox.Show(string.Format(T("S.Msg.CantReadTemplate"), filePath),
                    T("S.Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var buildChar = CharToVm(tbSkill.Characters[0]);
            var buildTab = _vm.OpenBuild(Path.GetFileNameWithoutExtension(filePath), buildChar, filePath);
            PrepareBuildCatalog(buildTab);
            return;
        }

        // Format natif (.zcx, ou .pn3 hérité) : le seul réécrit en place.
        bool isNative = TeamBuildSerializer.IsNativeExtension(ext);
        var unresolved = new List<int>();
        TeamBuild? model = ext switch
        {
            PwndImporter.Extension          => PwndImporter.Import(filePath, skillsById),
            SkillTemplateImporter.Extension => SkillTemplateImporter.Import(filePath, skillsById),
            _                               => TeamBuildSerializer.Load(filePath, skillsById, out unresolved),
        };

        if (model == null)
        {
            MessageBox.Show(string.Format(T("S.Msg.CantReadFile"), filePath),
                T("S.Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WarnUnresolvedSkills(unresolved);

        var tb = ModelToViewModel(model);
        if (isNative) tb.FilePath = filePath;
        tb.SourcePath = filePath;      // tracé pour le renommage d'onglet (tout format)
        RestoreSavedGameMode(model, isNative);   // .pn3 v17 : rouvre dans le mode enregistré
        _vm.ApplyGameModeTo(tb);       // puis aligne les compétences sur le mode retenu
        tb.BeginTracking();
        _vm.OpenTeamBuilds.Add(tb);
        _vm.ActiveTeamBuild = tb;
    }

    // ── Mode de jeu enregistré dans le .pn3 (v17) ─────────────────────────────

    private static Core.Models.GameMode ToCoreGameMode(SkillGameMode mode) => mode switch
    {
        SkillGameMode.PvE => Core.Models.GameMode.PvE,
        SkillGameMode.PvP => Core.Models.GameMode.PvP,
        _                 => Core.Models.GameMode.All,
    };

    private static SkillGameMode ToSkillGameMode(Core.Models.GameMode mode) => mode switch
    {
        Core.Models.GameMode.PvE => SkillGameMode.PvE,
        Core.Models.GameMode.PvP => SkillGameMode.PvP,
        _                        => SkillGameMode.All,
    };

    // Restaure le mode enregistré dans un .pn3. Réservé au format natif : les formats hérités
    // (.pwnd, .txt) ne portent pas cette information et gardent le mécanisme actuel — c'est le
    // mode courant qui s'applique alors à leurs compétences.
    // Poser le filtre re-résout aussi les onglets DÉJÀ ouverts : le filtre est global, donc ouvrir
    // un build PvP bascule toute la session en PvP. C'est la contrepartie assumée d'un réglage
    // partagé plutôt que d'un réglage par onglet.
    private void RestoreSavedGameMode(TeamBuild model, bool isNativeFormat)
    {
        if (!isNativeFormat || model.GameMode is not { } saved) return;
        _vm.SkillPanel.SelectedGameMode = ToSkillGameMode(saved);
    }

    // Compétences du fichier absentes du catalogue courant : leur slot est vide et une
    // sauvegarde les effacerait définitivement. L'utilisateur doit le savoir AVANT d'enregistrer
    // (fichier reçu d'un autre joueur, catalogue local plus ancien que le fichier…).
    private void WarnUnresolvedSkills(List<int> unresolvedIds)
    {
        if (unresolvedIds.Count == 0) return;
        var ids = string.Join(", ", unresolvedIds.Distinct().OrderBy(i => i));
        MessageBox.Show(string.Format(T("S.Msg.UnknownSkills"), unresolvedIds.Distinct().Count(), ids),
            T("S.Msg.UnknownSkillsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── Tab bar ───────────────────────────────────────────────────────────────

    private void BrowserTab_Click(object sender, RoutedEventArgs e)
        => _vm.ActivateBrowser();

    private void SearchResultsTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchResultsViewModel tab })
            _vm.ActivateSearchResults(tab);
    }

    private void SearchResultsTab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: SearchResultsViewModel tab })
        {
            _vm.CloseSearchResults(tab);
            e.Handled = true;
        }
    }

    private void CloseSearchResultsTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: SearchResultsViewModel tab })
            _vm.CloseSearchResults(tab);
    }

    // 🔍 toolbar + onglet Search : ouvre/active le constructeur de requête.
    private void SearchTemplates_Click(object sender, RoutedEventArgs e)
        => _vm.ActivateSearchBuilder();

    // Barre d'outils : cycle le mode de jeu Tout → PvE → PvP → Tout. Le filtre est global à la
    // session — il pilote le catalogue, la résolution des variantes des compétences équipées et la
    // règle des bonus sur caractéristiques secondaires. Il vit dans le panneau catalogue, donc
    // invisible depuis un onglet team build ; ce bouton le rend accessible et surtout LISIBLE
    // partout, notamment après l'ouverture d'un .pn3 qui l'a repositionné tout seul.
    private void CycleGameMode_Click(object sender, RoutedEventArgs e)
        => _vm.SkillPanel.SelectedGameMode = _vm.SkillPanel.SelectedGameMode switch
        {
            SkillGameMode.All => SkillGameMode.PvE,
            SkillGameMode.PvE => SkillGameMode.PvP,
            _                 => SkillGameMode.All,
        };

    // Sites de partage de builds GW1, aussi accessibles depuis Extras.
    private const string Gw1BuildsUrl = "https://www.gw1builds.com/";
    private const string GwPvxUrl     = "https://gwpvx.fandom.com/wiki/PvX_wiki";

    // Toolbar « Partager le build » : deux destinations → le bouton ouvre son propre menu
    // plutôt que d'en imposer une (même idiome que le clic gauche sur le joueur assigné).
    private void ShareBuild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.ContextMenu is not { } menu) return;
        menu.PlacementTarget = btn;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OpenGw1Builds_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(Gw1BuildsUrl) { UseShellExecute = true });

    private void OpenGwPvx_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(GwPvxUrl) { UseShellExecute = true });

    // Bouton « 🔍 Rechercher » de l'onglet Search : construit la requête depuis le perso-requête,
    // scanne récursivement la racine des builds (en tâche de fond → pas de gel UI) selon les
    // périmètres cochés, puis affiche les fichiers correspondants dans l'onglet Recherche.
    private async void RunSearch_Click(object sender, RoutedEventArgs e)
    {
        var sb = _vm.SearchBuilder;

        if (_vm.SkillPanel.AllSkills.Count == 0)
        {
            MessageBox.Show(T("S.Msg.CatalogNotLoaded"),
                T("S.Msg.CatalogNotLoadedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var query = sb.BuildQuery();
        if (query.IsEmpty)
        {
            MessageBox.Show(T("S.Msg.SearchNeedCriteria"),
                T("S.Msg.SearchTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunSearchAndShowResults(query, sb.Scope);
    }

    // Exécute une requête sur la racine des builds (en tâche de fond → pas de gel UI) et affiche les
    // correspondances dans un nouvel onglet « Résultats N ». Partagé par le bouton Rechercher (onglet
    // Search) et par « Parse in templates » (clic droit sur une compétence).
    private async Task RunSearchAndShowResults(BuildSearchQuery query, SearchScope scope)
    {
        var root = _vm.Browser.RootPath;
        if (root == null || !Directory.Exists(root))
        {
            MessageBox.Show(T("S.Msg.NoRootFolder"),
                T("S.Msg.SearchTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var skillsById = _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s);

        Mouse.OverrideCursor = Cursors.Wait;
        List<string> matches;
        try
        {
            matches = await Task.Run(() => BuildSearchEngine.SearchRoot(root, query, scope, skillsById));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        var tab = _vm.NewSearchResults();
        tab.SetSkillsProvider(() => _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s));
        tab.OpenFileRequested += OpenFileFromBrowser;
        tab.LoadResults(matches);
    }

    private void BuildTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TeamBuildViewModel tb })
            _vm.ActiveTeamBuild = tb;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: TeamBuildViewModel tb })
            TryCloseTab(tb);
    }

    private void BuildTab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: TeamBuildViewModel tb })
        {
            TryCloseTab(tb);
            e.Handled = true;
        }
    }

    // ── Renommage d'onglet (build ET résultats) ───────────────────────────────
    // Déclencheurs : double-clic sur l'onglet + menu contextuel « Renommer ».
    // Onglet de build → renomme aussi le fichier source sur le disque (extension conservée).
    // Onglet de résultats → renommage du libellé en mémoire seulement.

    private void Tab_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        BeginTabRename(sender);
        e.Handled = true;
    }

    private void RenameTab_MenuClick(object sender, RoutedEventArgs e) => BeginTabRename(sender);

    private void BeginTabRename(object sender)
    {
        if (sender is FrameworkElement { DataContext: IRenamableTab tab })
        {
            tab.EditName = tab.RenameSeed;
            tab.IsRenaming = true;
        }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
            box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }),
                System.Windows.Threading.DispatcherPriority.Input);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTabRename(sender);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (sender is FrameworkElement { DataContext: IRenamableTab tab })
                tab.IsRenaming = false;   // abandon : EditName ignoré
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e) => CommitTabRename(sender);

    private void CommitTabRename(object sender)
    {
        switch ((sender as FrameworkElement)?.DataContext)
        {
            case TeamBuildViewModel tb:      CommitTeamBuildRename(tb); break;
            case SearchResultsViewModel sr:  CommitSearchResultsRename(sr); break;
        }
    }

    private void CommitSearchResultsRename(SearchResultsViewModel sr)
    {
        if (!sr.IsRenaming) return;
        sr.IsRenaming = false;
        var name = (sr.EditName ?? "").Trim();
        if (name.Length > 0) sr.Title = name;
    }

    private void CommitTeamBuildRename(TeamBuildViewModel tb)
    {
        if (!tb.IsRenaming) return;
        tb.IsRenaming = false;

        var newName = (tb.EditName ?? "").Trim();
        if (newName.Length == 0 || newName == tb.Name) return;   // vide ou inchangé → no-op

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(T("S.Msg.InvalidFileChars"),
                T("S.Msg.RenameTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Renomme le fichier source sur le disque si l'onglet en a un.
        if (tb.SourcePath is { } src && File.Exists(src))
        {
            var target = Path.Combine(Path.GetDirectoryName(src)!, newName + Path.GetExtension(src));
            if (!string.Equals(target, src, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(target))
                {
                    MessageBox.Show(string.Format(T("S.Msg.FileExists"), Path.GetFileName(target)),
                        T("S.Msg.RenameTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;   // conserve l'ancien nom
                }
                try { File.Move(src, target); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(string.Format(T("S.Msg.RenameFailed"), ex.Message),
                        T("S.Msg.RenameTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.Equals(tb.FilePath, src, StringComparison.OrdinalIgnoreCase))
                    tb.FilePath = target;   // repointe la cible d'enregistrement native
                tb.SourcePath = target;
                _vm.Browser.RefreshFiles();  // reflète le renommage dans le browser
            }
        }

        tb.Name = newName;
    }

    private bool TryCloseTab(TeamBuildViewModel tb)
    {
        if (tb.IsDirty)
        {
            var result = MessageBox.Show(
                string.Format(T("S.Msg.UnsavedBuild"), tb.Name),
                T("S.Msg.UnsavedTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes && !SaveTeamBuild(tb)) return false;
        }

        CloseTab(tb);
        return true;
    }

    private void CloseTab(TeamBuildViewModel tb)
    {
        if (_spikeWindow is { } sw && sw.Build == tb) sw.Close();
        int idx = _vm.OpenTeamBuilds.IndexOf(tb);
        bool wasActive = tb.IsActive;
        _vm.OpenTeamBuilds.Remove(tb);

        if (wasActive)
        {
            if (_vm.OpenTeamBuilds.Count > 0)
                _vm.ActiveTeamBuild = _vm.OpenTeamBuilds[Math.Max(0, idx - 1)];
            else
                _vm.ActivateBrowser();
        }
    }

    // ── Skills ────────────────────────────────────────────────────────────────

    private async Task InitializeSkillsAsync()
    {
        var repo = new SkillRepository(_db);
        int count = await repo.CountAsync();

        if (count == 0)
        {
            // Première utilisation : scrapping automatique
            var win = new SkillUpdateWindow { Owner = this };
            win.ShowDialog();
        }
        else if (ScrapeInfo.Load() is { Complete: false })
        {
            // Téléchargement interrompu après la sauvegarde intermédiaire : le catalogue est là,
            // mais sans les tables de progression ni les icônes. Sans ce rattrapage l'utilisateur
            // garderait des infobulles aux plages non résolues sans savoir pourquoi.
            var result = MessageBox.Show(
                T("S.Msg.CatalogIncomplete"),
                T("S.Msg.CatalogIncompleteTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var win = new SkillUpdateWindow { Owner = this };
                win.ShowDialog();
            }
        }
        else
        {
            // Vérification des mises à jour GW en arrière-plan
            _ = CheckForGameUpdatesAsync();
        }

        await LoadSkillsFromDbAsync();

        // Nouvelle version de Z-Codex : sans rapport avec l'état du catalogue, donc hors du
        // if/else ci-dessus. En arrière-plan et sans await — rien ici ne doit retarder l'affichage.
        _ = CheckForAppUpdateAsync(manual: false);
    }

    // Une version plus récente de Z-Codex est-elle publiée sur GitHub ? On se contente de le
    // signaler et d'ouvrir la page de téléchargement : ni téléchargement ni installation
    // automatiques (cf. AppVersionChecker pour le pourquoi).
    //
    // En AUTOMATIQUE, tout est silencieux : déjà à jour, pas de réseau, aucune version publiée,
    // version écartée — rien ne s'affiche. Un démarrage ne doit jamais être interrompu par une
    // boîte de dialogue qui n'apprend rien. En MANUEL l'utilisateur a cliqué : il attend une
    // réponse dans tous les cas, y compris « tout va bien ».
    private async Task CheckForAppUpdateAsync(bool manual)
    {
        try
        {
            if (!manual)
            {
                if (_settings.LastUpdateCheckUtc?.Date == DateTime.UtcNow.Date) return;
                // Horodaté AVANT l'appel réseau, et non après : sinon un poste hors ligne, dont
                // l'appel part en exception, réessaierait à chaque démarrage sans jamais rien
                // enregistrer. Ce champ note la dernière TENTATIVE, pas le dernier succès.
                _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _settings.Save();
            }

            var current = AppVersionChecker.Normalize(
                Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));
            var latest = await AppVersionChecker.GetLatestAsync();

            if (latest == null || latest.Version <= current)
            {
                if (manual)
                    MessageBox.Show(this, string.Format(T("S.Msg.AppUpToDate"), current),
                        T("S.Msg.AppUpdateTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Version écartée d'un clic sur la case à cocher : on se tait jusqu'à ce qu'une version
            // ENCORE plus récente paraisse. Une vérification manuelle passe outre.
            if (!manual && Version.TryParse(_settings.IgnoredUpdateVersion, out var ignored)
                && latest.Version <= AppVersionChecker.Normalize(ignored))
                return;

            var dlg = new AppUpdateWindow(
                string.Format(T("S.Msg.AppUpdateDetected"), latest.Version, current)) { Owner = this };
            dlg.ShowDialog();

            if (dlg.ShouldDownload)
                Process.Start(new ProcessStartInfo(latest.PageUrl) { UseShellExecute = true });
            else if (dlg.IgnoreChecked)
            {
                _settings.IgnoredUpdateVersion = latest.Version.ToString();
                _settings.Save();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CheckForAppUpdate: {ex.Message}");
            if (manual)
                MessageBox.Show(this, T("S.Msg.AppUpdateFailed"), T("S.Msg.AppUpdateTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        => await CheckForAppUpdateAsync(manual: true);

    private async Task CheckForGameUpdatesAsync()
    {
        try
        {
            var scrapeInfo = ScrapeInfo.Load();
            if (scrapeInfo == null) return;

            var lastGwUpdate = await GameUpdateChecker.GetLastUpdateDateAsync();
            if (lastGwUpdate == null) return;

            var ignoredDate = scrapeInfo.IgnoredUpdateDate?.Date;
            var referenceDate = ignoredDate.HasValue && ignoredDate.Value > scrapeInfo.LastScrapeDate.Date
                ? ignoredDate.Value
                : scrapeInfo.LastScrapeDate.Date;

            if (lastGwUpdate.Value.Date > referenceDate)
            {
                var dlg = new GwUpdateWindow(
                    string.Format(T("S.Msg.GwUpdateDetected"),
                        $"{lastGwUpdate.Value:d MMMM yyyy}", $"{scrapeInfo.LastScrapeDate:d MMMM yyyy}"))
                { Owner = this };
                dlg.ShowDialog();

                if (dlg.ShouldUpdate)
                {
                    var win = new SkillUpdateWindow { Owner = this };
                    win.ShowDialog();
                    if (win.SkillsUpdated > 0)
                        await LoadSkillsFromDbAsync();
                }
                else if (dlg.IgnoreChecked)
                {
                    scrapeInfo.IgnoredUpdateDate = lastGwUpdate.Value.Date;
                    scrapeInfo.Save();
                }
            }
        }
        catch { /* pas bloquant */ }
    }

    private async Task LoadSkillsFromDbAsync()
    {
        var repo = new SkillRepository(_db);
        var entities = await repo.GetAllAsync();
        var skills = entities
            .Where(e => !SkillCatalogFilter.IsBuildUnusable(e.Name))
            .Select(e => new Skill
        {
            Id = e.Id,
            Name = e.Name,
            Profession = (Profession)e.ProfessionId,
            Attribute = e.Attribute,
            Description = e.Description,
            EnergyCost = e.EnergyCost,
            Adrenaline = e.Adrenaline,
            Sacrifice = e.Sacrifice,
            Overcast = e.Overcast,
            Upkeep = e.Upkeep,
            CastTime = e.CastTime,
            Recharge = e.Recharge,
            SkillType = e.SkillType,
            Campaign = e.Campaign,
            Progression = ParseProgression(e.Progression),
            Conditions = ParseConditions(e.Conditions),
            // Rebasé sur le dossier d'icônes courant : la base stocke des chemins absolus,
            // périmés dès que le dossier de données bouge (cf. AppPaths.IconFile).
            IconPath = AppPaths.IconFile(e.IconUrl),
            IconPathHd = AppPaths.IconFile(e.IconUrlHd),
            WikiUrl = e.WikiUrl,
            NameFr = e.NameFr,
            DescriptionFr = e.DescriptionFr,
            AttributeFr = e.AttributeFr,
            TypeFr = e.TypeFr,
            FrSuspect = e.FrSuspect,
        }).ToList();
        DeriveFrenchPvpNames(skills);
        _vm.SkillPanel.LoadSkills(skills);
        _vm.SearchBuilder.Catalog.LoadSkills(skills);
        _vm.ArmorCalc.LoadSkills(skills);   // attaques de référence du calculateur d'armure (Lot D)
        // Les conditions par skill viennent d'être (re)chargées → recalcul de la ligne teambuild.
        _vm.RefreshTeamConditionBand();
        // Catalogue multi-colonnes (vue Liste) : cellule assez large pour le nom le plus long
        // → noms affichés EN ENTIER sans ellipsis, colonnes de largeur uniforme.
        _vm.SearchBuilder.Catalog.CatalogItemWidth = ComputeCatalogItemWidth(skills);
        // Pré-chauffe le cache d'icônes du catalogue (taille 24) en arrière-plan → évite le storm
        // de décodages JPEG sur le thread UI quand le multi-colonnes réalise ~80 cellules d'un coup.
        var iconPaths = skills.Select(s => s.IconPath).Where(p => !string.IsNullOrEmpty(p))
                              .Distinct().ToList();
        Converters.UrlToImageConverter.Prewarm(iconPaths, 24);
        _vm.Browser.SetSkillsProvider(() => _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s));
    }

    // Variantes « (PvP) » de skills splittées : le wiki FR n'a pas de page dédiée pour beaucoup de
    // ces splits récents → leur NameFr est vide et DisplayName retombe en anglais. On dérive le nom
    // FR affiché depuis le skill de BASE (même nom sans le suffixe) + « (PvP) ». Ne touche pas les
    // variantes qui ont déjà un nom FR propre (page FR existante).
    private static void DeriveFrenchPvpNames(List<Skill> skills)
    {
        const string suffix = " (PvP)";
        var frByName = skills.Where(s => s.NameFr.Length > 0)
                             .GroupBy(s => s.Name, StringComparer.Ordinal)
                             .ToDictionary(g => g.Key, g => g.First().NameFr, StringComparer.Ordinal);
        foreach (var s in skills)
            if (s.NameFr.Length == 0 && s.Name.EndsWith(suffix, StringComparison.Ordinal)
                && frByName.TryGetValue(s.Name[..^suffix.Length], out var baseFr))
                s.NameFr = baseFr + suffix;
    }

    // Largeur d'une cellule du catalogue = icône + le NOM LE PLUS LONG (mesuré) + bloc mécaniques.
    // Mesuré une seule fois au chargement (nom stable) → colonnes uniformes sans ellipsis.
    private static double ComputeCatalogItemWidth(IEnumerable<Skill> skills)
    {
        var typeface = new Typeface("Segoe UI");
        double maxName = 0;
        foreach (var s in skills)
        {
            // Nom AFFICHÉ (langue courante) : recalculé au switch via LoadSkillsFromDbAsync.
            if (string.IsNullOrEmpty(s.DisplayName)) continue;
            var ft = new FormattedText(s.DisplayName, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, 12, Brushes.Black, 1.0);
            if (ft.Width > maxName) maxName = ft.Width;
        }
        // icône(24)+marges(2) + nom + marges nom(6) + grille mécaniques(232)+marges(10)
        // + padding item(2) + petite marge de garde(12).
        return Math.Ceiling(maxName) + 24 + 2 + 6 + 232 + 10 + 2 + 12;
    }

    // CSV de conditions (colonne Skills.Conditions) → entrées individuelles. "" = aucune.
    private static string[] ParseConditions(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Désérialise la table de progression JSON (string[][]) ; "" ou JSON invalide → null
    // (les plages de description restent alors en notation de plage, pas de crash).
    private static string[][]? ParseProgression(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<string[][]>(json); }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[Progression] désérialisation échouée : {ex.Message}");
            return null;
        }
    }

    // ── File menu ─────────────────────────────────────────────────────────────

    private void NewTeamBuild_Click(object sender, RoutedEventArgs e)
        => _vm.NewTeamBuild();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SkillPanel.AllSkills.Count == 0)
        {
            MessageBox.Show(T("S.Msg.CatalogNotLoaded"),
                T("S.Msg.CatalogNotLoadedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // L'ouverture accepte AUSSI l'ancienne extension .pn3 : sans elle, les team builds déjà
        // enregistrés n'apparaîtraient plus dans la boîte de dialogue.
        var nat  = $"*{TeamBuildSerializer.Extension};*{TeamBuildSerializer.LegacyExtension}";
        var all  = $"{nat};*{PwndImporter.Extension}";
        var natFilter  = $"{T("S.Filter.NativeBuild")} ({nat})|{nat}";
        var pwndFilter = $"paw·ned² (*{PwndImporter.Extension})|*{PwndImporter.Extension}";
        var dlg = new OpenFileDialog
        {
            Filter = $"{natFilter}|{pwndFilter}|{T("S.Filter.AllBuilds")} ({all})|{all}",
            Title  = T("S.Dlg.OpenTeamBuild"),
            FilterIndex = 3,
        };
        if (dlg.ShowDialog() != true) return;

        var skillsById = _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s);
        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();

        TeamBuild? model;
        var unresolved = new List<int>();
        bool isPwnd = ext == PwndImporter.Extension;
        if (isPwnd)
            model = PwndImporter.Import(dlg.FileName, skillsById);
        else
            model = TeamBuildSerializer.Load(dlg.FileName, skillsById, out unresolved);

        if (model == null)
        {
            MessageBox.Show(string.Format(T("S.Msg.CantReadFile"), dlg.FileName),
                T("S.Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WarnUnresolvedSkills(unresolved);

        var tb = ModelToViewModel(model);
        if (!isPwnd) tb.FilePath = dlg.FileName; // .pwnd → filePath null, save as .pn3
        tb.SourcePath = dlg.FileName;   // tracé pour le renommage d'onglet (tout format)
        RestoreSavedGameMode(model, !isPwnd);    // .pn3 v17 : rouvre dans le mode enregistré
        _vm.ApplyGameModeTo(tb);        // puis aligne les compétences sur le mode retenu
        tb.BeginTracking();
        _vm.OpenTeamBuilds.Add(tb);
        _vm.ActiveTeamBuild = tb;
    }

    // Rien à enregistrer tant qu'aucun onglet n'est actif (le navigateur de fichiers, par exemple) :
    // grise les entrées du menu au lieu de les laisser ne rien faire en silence.
    private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = (_vm.IsBuildEditorActive && _vm.ActiveBuild is not null)
                       || _vm.ActiveTeamBuild is not null;

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save_Click(sender, e);
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => SaveAs_Click(sender, e);

    // Save contextuel : selon la vue active, enregistre le build simple (template .txt) OU le teambuild.
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBuildEditorActive && _vm.ActiveBuild is { } build)
            SaveBuildTab(build);
        else if (_vm.ActiveTeamBuild is { } tb)
            SaveTeamBuild(tb);
    }

    // Save as contextuel : force le dialogue de destination, dans les deux vues.
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBuildEditorActive && _vm.ActiveBuild is { } build)
            SaveBuildTab(build, forceDialog: true);
        else if (_vm.ActiveTeamBuild is { } tb)
            SaveTeamBuild(tb, forceDialog: true);
    }

    // ── Export paw·ned² (.pwnd) ───────────────────────────────────────────────

    private void FileMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var tb = _vm.ActiveTeamBuild;
        ExportPwndMenuItem.IsEnabled = tb is not null;

        // Un item par cadenas : contrairement au menu contextuel, le menu Fichier n'a aucun
        // cadenas sous le curseur — il faut donc désigner lequel.
        ExportPwndLocksMenuItem.Items.Clear();
        if (tb is null || tb.Locks.Count == 0)
        {
            ExportPwndLocksMenuItem.Items.Add(
                new MenuItem { Header = T("S.Menu.ExportPwndNoLock"), IsEnabled = false });
            return;
        }
        foreach (var lk in tb.Locks)
        {
            var item = new MenuItem { Header = lk.MembersTooltip, Tag = lk };
            item.Click += ExportPwndLock_Click;
            ExportPwndLocksMenuItem.Items.Add(item);
        }
    }

    // Portée « tout le teambuild » : racines ET variantes, dans l'ordre d'affichage.
    private void ExportPwndAll_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild is not { } tb) return;
        ExportPwnd(tb.Characters.Select(CharToModel).SelectMany(PwndExporter.Flatten).ToList(), tb.Name);
    }

    // Portée « les 8 premiers persos » : les 8 premières RACINES, variantes ignorées — c'est le
    // gabarit exact d'un fichier paw·ned².
    private void ExportPwndFirst8_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild is not { } tb) return;
        ExportPwnd(tb.Characters.Take(8).Select(CharToModel).ToList(), tb.Name);
    }

    private void ExportPwndLock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not VariantLockViewModel lk || _vm.ActiveTeamBuild is null) return;
        ExportPwnd(lk.Members.Select(CharToModel).ToList(), LockExportName(lk));
    }

    // Demande la destination puis écrit. Le nombre d'emplacements est annoncé, et au-delà de 8 le
    // message le signale : aucun fichier paw·ned² d'origine ne dépasse 8, on sort du gabarit connu.
    private void ExportPwnd(List<CharacterBuild> characters, string defaultName)
    {
        if (characters.Count == 0)
        {
            MessageBox.Show(T("S.Msg.PwndNothing"), T("S.Msg.PwndExportTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title            = T("S.Menu.FileExportPwnd"),
            Filter           = T("S.Filter.Pwnd"),
            DefaultExt       = PwndExporter.Extension,
            AddExtension     = true,
            FileName         = SafeFileName(defaultName),
        };
        if (_vm.Browser.RootPath is { } root && Directory.Exists(root)) dlg.InitialDirectory = root;
        if (dlg.ShowDialog() != true) return;

        try
        {
            PwndExporter.Save(characters, dlg.FileName,
                SkillVariants.TemplateIdsByName(_vm.SkillPanel.AllSkills));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExportPwnd] '{dlg.FileName}': {ex}");
            MessageBox.Show(string.Format(T("S.Msg.ExportFailed"), ex.Message),
                T("S.Msg.PwndExportTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Un export de moins de 8 persos est complété par des emplacements vides côté écriture :
        // c'est ce nombre-là, et non celui des persos, qui décrit le fichier obtenu.
        int slots = Math.Max(characters.Count, 8);
        MessageBox.Show(
            string.Format(T(slots > 8 ? "S.Msg.PwndOver8" : "S.Msg.PwndExported"), dlg.FileName, slots),
            T("S.Msg.PwndExportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Pendant de SaveTeamBuild pour l'onglet Build : réécrit en place le .txt d'origine quand il y
    // en a un, sinon demande une destination. Retourne false sur annulation ou échec d'écriture —
    // ce que les appelants traduisent par « ne pas fermer ».
    private bool SaveBuildTab(BuildEditorViewModel build, bool forceDialog = false)
    {
        var path = forceDialog ? null : build.SourcePath;
        if (SaveCharacterTemplate(build.Character, path) is not { } written) return false;

        build.SourcePath = written;
        // Un « Enregistrer sous » vers un autre fichier détache l'onglet de son nom d'origine :
        // sans ça, deux onglets issus du même .txt porteraient le même titre sans qu'on sache
        // lequel pointe où.
        build.Title = Path.GetFileNameWithoutExtension(written);
        build.MarkClean();
        return true;
    }

    private bool SaveTeamBuild(TeamBuildViewModel tb, bool forceDialog = false)
    {
        var filePath = forceDialog ? null : tb.FilePath;
        if (filePath == null)
        {
            var dlg = new SaveFileDialog
            {
                // L'enregistrement, lui, ne propose que le format courant.
                Filter   = $"{T("S.Filter.NativeBuild")} (*{TeamBuildSerializer.Extension})|*{TeamBuildSerializer.Extension}",
                Title    = T("S.Dlg.SaveTeamBuild"),
                FileName = tb.Name,
            };
            // Team builds : sous-dossier "Teambuilds" de la racine Templates détectée
            // (repli sur la racine si absent). Sans rien créer.
            if (_vm.Browser.RootPath is { } root && Directory.Exists(root))
            {
                var teamBuildsDir = Path.Combine(root, "Teambuilds");
                dlg.InitialDirectory = Directory.Exists(teamBuildsDir) ? teamBuildsDir : root;
            }
            if (dlg.ShowDialog() != true) return false;
            filePath = dlg.FileName;
        }

        var model = ViewModelToModel(tb);
        // v17 — mode de jeu capturé À LA SAUVEGARDE depuis le filtre courant, et non porté par le
        // VM : basculer PvE/PvP ne doit donc pas salir le build ni polluer l'historique d'undo
        // (ViewModelToModel sert aussi aux snapshots, qui restent inchangés).
        model.GameMode = ToCoreGameMode(_vm.SkillPanel.SelectedGameMode);
        // Une écriture qui échoue (fichier en lecture seule, lecteur réseau tombé, disque plein)
        // ne doit PAS remonter : ce chemin est aussi celui de la fermeture, où une exception
        // emporterait tous les builds non enregistrés — au moment précis où l'utilisateur demande
        // à les sauver. On signale et on retourne false, ce que les appelants traduisent déjà en
        // « ne pas fermer ».
        try
        {
            TeamBuildSerializer.Save(model, filePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveTeamBuild] '{filePath}': {ex}");
            MessageBox.Show(string.Format(T("S.Msg.SaveFailed"), filePath, ex.Message),
                T("S.Msg.SaveFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        tb.FilePath = filePath;
        tb.SourcePath = filePath;   // le renommage d'onglet ciblera désormais le .pn3 enregistré
        tb.MarkClean();
        return true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ── Help ──────────────────────────────────────────────────────────────────

    // F1 ou Help > Aide et raccourcis. Fenêtre non modale (consultable en travaillant) ;
    // réutilise l'instance existante si elle est déjà ouverte plutôt que d'en empiler.
    private HelpWindow? _helpWindow;
    private void Help_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_helpWindow is { IsLoaded: true })
        {
            _helpWindow.Activate();
            return;
        }
        _helpWindow = new HelpWindow { Owner = this };
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show();
    }

    private void About_Click(object sender, RoutedEventArgs e)
        => new AboutWindow { Owner = this }.ShowDialog();

    // ── Extras ────────────────────────────────────────────────────────────────

    private async void UpdateSkills_Click(object sender, RoutedEventArgs e)
    {
        var win = new SkillUpdateWindow { Owner = this };
        win.ShowDialog();
        if (win.SkillsUpdated > 0)
            await LoadSkillsFromDbAsync();
    }

    // Extras → Vérifier les icônes. Les cinq jeux à taille fixe (stats, professions, conditions,
    // flux, vol de vie) ne sont téléchargés qu'au démarrage et AUCUN scraping du catalogue ne les
    // répare : un utilisateur dont le premier téléchargement a échoué n'avait aucun recours, pas
    // même la réinstallation — le dossier de données survit à la désinstallation. Les icônes de
    // COMPÉTENCES, elles, relèvent de la mise à jour du catalogue : on se contente de les compter
    // et de renvoyer vers l'entrée juste au-dessus.
    private async void CheckIcons_Click(object sender, RoutedEventArgs e)
    {
        IconReport report;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            // Séquentiel et non en rafale : c'est justement la concurrence des cinq services au
            // démarrage qui met le wiki en défense et provoque la panne qu'on répare ici.
            report  = await ProfessionIconService.DownloadAllAsync();
            report += await SkillStatIconService.DownloadAllAsync();
            report += await ConditionIconService.DownloadAllAsync();
            report += await FluxIconService.DownloadAllAsync();
            report += await LifeStealIconService.DownloadAllAsync();
        }
        finally { Mouse.OverrideCursor = null; }

        int missingSkillIcons = _vm.SkillPanel.AllSkills.Count(
            s => !string.IsNullOrEmpty(s.IconPath) && !File.Exists(AppPaths.IconFile(s.IconPath)));

        var lines = new List<string>();
        if (report.Repaired > 0)
            lines.Add(string.Format(T("S.Msg.IconsRepaired"), report.Repaired));
        if (report.Failed > 0)
            lines.Add(string.Format(T("S.Msg.IconsFailed"), report.Failed));
        if (report.Repaired == 0 && report.Failed == 0)
            lines.Add(string.Format(T("S.Msg.IconsAllGood"), report.Ok));
        if (missingSkillIcons > 0)
            lines.Add(string.Format(T("S.Msg.IconsSkillsMissing"), missingSkillIcons));

        MessageBox.Show(this, string.Join("\n\n", lines), T("S.Msg.IconsTitle"),
                        MessageBoxButton.OK,
                        report.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    // Extras → Calculateur d'armure + onglet Armure : ouvre/active l'onglet (pas de fenêtre).
    private void ArmorCalculator_Click(object sender, RoutedEventArgs e)
        => _vm.ActivateArmorCalc();

    // Extras → Télécharger les builds PvX. Le catalogue est indispensable : sans lui aucun code
    // template ne se décode, et les équipes ne pourraient pas être assemblées.
    private void PvxImport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SkillPanel.AllSkills.Count == 0)
        {
            MessageBox.Show(T("S.Msg.CatalogNotLoaded"),
                T("S.Msg.CatalogNotLoadedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var skillsById = _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s);
        // Défaut : la destination du dernier import, sinon la racine du navigateur — c'est-à-dire
        // le dossier Templates du jeu quand il a été détecté, exactement là où ces .txt servent.
        var initial = _settings.PvxDestinationPath ?? _settings.TemplatesRootPath;

        var win = new PvxImportWindow(skillsById, initial) { Owner = this };
        win.ShowDialog();

        if (win.ChosenDestination is { } destination)
        {
            _settings.PvxDestinationPath = destination;
            _settings.Save();

            // Le navigateur a bâti son arbre au démarrage : sans relecture, les milliers de
            // fichiers qui viennent d'atterrir n'apparaissent pas et l'import a l'air d'avoir
            // échoué. On ne relit que si la destination est DANS l'arborescence affichée, pour
            // ne pas déplacer l'utilisateur sans raison.
            var root = _vm.Browser.RootPath;
            if (root is not null && destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                _vm.Browser.SetRoot(root);
        }
    }

    private void ArmorCalculatorTab_Click(object sender, RoutedEventArgs e)
        => _vm.ActivateArmorCalc();

    // ✕ de l'onglet Armure (ou menu contextuel Fermer) : masque l'onglet, l'état est conservé.
    // e.Handled : sinon le clic remonte au bouton d'onglet qui réactiverait la vue fermée.
    private void CloseArmorCalcTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _vm.CloseArmorCalc();
    }

    // ── Character slots ───────────────────────────────────────────────────────

    private void AddCharacterSlot_Click(object sender, RoutedEventArgs e)
        => _vm.ActiveTeamBuild?.AddCharacterSlot();

    private void SaveAssignments_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild != null)
            SaveTeamBuild(_vm.ActiveTeamBuild);
    }

    // ── Edit menu : undo/redo ─────────────────────────────────────────────────

    // Attache/détache le gestionnaire d'undo de chaque onglet team build. Point unique :
    // couvre New (MainViewModel), Open et l'ouverture depuis le browser.
    private void OnOpenTeamBuildsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TeamBuildViewModel tb in e.NewItems) AttachUndo(tb);
        if (e.OldItems != null)
            foreach (TeamBuildViewModel tb in e.OldItems) { tb.Undo?.Dispose(); tb.Undo = null; }
    }

    private void AttachUndo(TeamBuildViewModel tb)
    {
        if (tb.Undo != null) return;
        tb.Undo = new UndoManager(
            capture: () =>
            {
                var model = ViewModelToModel(tb);
                model.UpdatedAt = default;   // hors snapshot : deux états identiques doivent se comparer égaux
                return TeamBuildSerializer.Serialize(model);
            },
            restore: json =>
            {
                var skillsById = _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s);
                var model = TeamBuildSerializer.Deserialize(json, skillsById);
                if (model == null)
                {
                    Debug.WriteLine($"[Undo] Snapshot illisible pour '{tb.Name}' — restauration annulée");
                    return;
                }
                RestoreTeamBuild(tb, model);
                // La restauration suspend le tracking (pas de Mutated) → recalcul explicite
                // de la ligne de conditions du teambuild.
                _vm.RefreshTeamConditionBand();
            });
        tb.Mutated += tb.Undo.OnMutated;
    }

    // Onglets Build : seule la fermeture passe par ici. L'attache, elle, se fait dans
    // PrepareBuildCatalog — l'onglet n'est ajouté à OpenBuilds qu'AVANT d'être câblé, et le
    // premier snapshot doit refléter le build fini, comme la signature de référence du dirty flag.
    private void OnOpenBuildsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (BuildEditorViewModel b in e.OldItems) { b.Undo?.Dispose(); b.Undo = null; }
    }

    // Snapshot = ContentSignature (périmètre exact du .txt). Attaché depuis MainWindow, seul
    // détenteur du catalogue de skills nécessaire pour retrouver une compétence par son id.
    private void AttachUndo(BuildEditorViewModel tab)
    {
        if (tab.Undo != null) return;
        tab.Undo = new UndoManager(
            capture: () => tab.ContentSignature,
            restore: snapshot => tab.RestoreContent(
                snapshot, _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s)));
        tab.Mutated += tab.Undo.OnMutated;
    }

    // Pile d'annulation de l'onglet ACTIF. Le garde sur la vue est explicite : ActiveTeamBuild
    // et ActiveBuild survivent au changement d'onglet, et annuler dans un build qu'on ne regarde
    // pas serait invisible.
    private UndoManager? ActiveUndo =>
        _vm.IsTeamBuildActive   ? _vm.ActiveTeamBuild?.Undo :
        _vm.IsBuildEditorActive ? _vm.ActiveBuild?.Undo     : null;

    private void UndoRedo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        var undo = ActiveUndo;
        e.CanExecute = undo != null && (e.Command == ApplicationCommands.Undo ? undo.CanUndo : undo.CanRedo);
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e) => ActiveUndo?.Undo();
    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e) => ActiveUndo?.Redo();

    // ── Edit menu : Ctrl+C / Ctrl+V sur le personnage survolé ─────────────────

    // Le perso désigné par la SOURIS, pas par le focus : un team build affiche jusqu'à 12 lignes
    // et aucune n'est « sélectionnée » au sens Windows — le pointeur est la seule désignation
    // naturelle. Un onglet Build n'a qu'un perso : survoler sa page suffit.
    private CharacterSlotViewModel? HoveredCharacter()
    {
        if (_vm.IsBuildEditorActive) return _vm.ActiveBuild?.Character;
        if (!_vm.IsTeamBuildActive)  return null;
        return GetCharFromVisualParent(Mouse.DirectlyOver as DependencyObject);
    }

    private void HoveredCharacter_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = HoveredCharacter() != null;

    private void CopyHovered_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (HoveredCharacter() is { } charVm) CopyCharacterTemplate(charVm, chat: false);
    }

    private void PasteHovered_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (HoveredCharacter() is { } charVm) ImportTemplateFromClipboard(charVm);
    }

    // ── View menu ─────────────────────────────────────────────────────────────

    private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        ShowAttributesMenuItem.IsEnabled = _vm.ActiveTeamBuild != null;
        ShowAttributesMenuItem.IsChecked = _vm.ActiveTeamBuild?.ShowAttributes ?? false;
        ShowPreviewAttributesMenuItem.IsChecked = _vm.Browser.ShowPreviewAttributes;
        IconsLargeMenuItem.IsChecked     = _vm.IconSize == IconSizeMode.Large;
        IconsMediumMenuItem.IsChecked    = _vm.IconSize == IconSizeMode.Medium;
        IconsSmallMenuItem.IsChecked     = _vm.IconSize == IconSizeMode.Small;
        ShowConditionsMenuItem.IsChecked = _vm.ShowConditions;
        WheelNeedsSelectionMenuItem.IsChecked = _vm.WheelNeedsSelection;
        ShowNatureRitualBandMenuItem.IsChecked = _vm.ShowNatureRituals;
        ShowAllNatureRitualsMenuItem.IsChecked = _vm.ShowAllNatureRituals;
        ShowArmorDamageMenuItem.IsChecked = ArmorDamageDisplay.Enabled;
        // Rappel des valeurs hors défaut dans l'entrée de menu (AL custom, niveaux ≠ 20).
        var damageParts = new List<string>();
        if (ArmorDamageDisplay.CustomArmorLevels.Count > 0)
            damageParts.Add($"AL {string.Join("/", ArmorDamageDisplay.CustomArmorLevels.OrderBy(a => a))}");
        if (ArmorDamageDisplay.CharacterLevel != 20) damageParts.Add(string.Format(T("S.Menu.DmgPartChar"), ArmorDamageDisplay.CharacterLevel));
        if (ArmorDamageDisplay.TargetLevel != 20) damageParts.Add(string.Format(T("S.Menu.DmgPartTarget"), ArmorDamageDisplay.TargetLevel));
        CustomArmorLevelMenuItem.Header = damageParts.Count > 0
            ? $"{T("S.Menu.ViewArmorDamageConfig")} ({string.Join(", ", damageParts)})"
            : T("S.Menu.ViewArmorDamageConfig");
        DarkThemeMenuItem.IsChecked      = ThemeManager.IsDark;
        LangFrMenuItem.IsChecked         = LanguageManager.IsFr;
        LangEnMenuItem.IsChecked         = !LanguageManager.IsFr;
        ScreenshotMenuItem.IsEnabled     = _vm.ActiveTeamBuild != null;
    }

    // ── Rituels de la nature ──────────────────────────────────────────────────

    // Environnement de rituels du contexte actif : le teambuild affiché, sinon le build simple.
    private NatureRitualEnvironment? ActiveNatureRitualEnvironment =>
        _vm.IsTeamBuildActive   ? _vm.ActiveTeamBuild?.NatureRituals :
        _vm.IsBuildEditorActive ? _vm.ActiveBuild?.NatureRituals :
        null;

    // (Re)construit le sous-menu Sélection > Rituels de la Nature : les 8 rituels, cochés selon
    // l'état du contexte actif. Désactivé si aucun teambuild/build n'est affiché.
    // Nom affiché d'un rituel = DisplayName de sa compétence (les rituels SONT des skills → nom FR
    // déjà en base, résolu par SkillId) ; repli sur le nom EN du descripteur si absent du catalogue.
    private string RitualDisplayName(NatureRitualData.Descriptor d) =>
        _vm.SkillPanel.AllSkills.FirstOrDefault(s => s.Id == d.SkillId)?.DisplayName ?? d.Name;

    private void BuildNatureRitualMenu()
    {
        var env = ActiveNatureRitualEnvironment;
        NatureRitualMenuItem.IsEnabled = env != null;
        NatureRitualMenuItem.Items.Clear();
        if (env == null) return;

        foreach (var d in NatureRitualData.All)
        {
            var item = new MenuItem
            {
                Header = RitualDisplayName(d),
                IsCheckable = true,
                IsChecked = env.IsActive(d.Ritual),
                ToolTip = d.DisplayTooltip,
            };
            var ritual = d.Ritual;   // capture par valeur pour la fermeture
            item.Click += (_, _) => env.Toggle(ritual);
            NatureRitualMenuItem.Items.Add(item);
        }
    }

    // Clic sur une icône du bandeau des rituels de la nature (éditeur ou teambuild) : bascule
    // l'activation dans l'environnement. Le bandeau est reconstruit via Mutated (teambuild) ou
    // NatureRituals.Changed (build simple).
    private void NatureRitualToggle_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is NatureRitualIndicatorViewModel vm)
            vm.Toggle();
    }

    // Clic sur l'icône « prolongateurs de durée » d'un perso (Blessed Aura / Extend Enchantments) :
    // bascule l'activation pour ce perso (recalcule les durées de ses enchantements Monk/Derviche).
    private void DurationBoosters_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CharacterSlotViewModel c)
            c.ToggleDurationBoosters();
        e.Handled = true;
    }

    // Clic sur une icône du bandeau local de boosts d'attribut (Lot A) : bascule ce boost pour
    // CE perso (Aura of the Lich, Awaken the Blood...).
    private void AttributeBoostToggle_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is AttributeBoostIndicatorViewModel vm)
            vm.Toggle();
        e.Handled = true;
    }

    // Molette sur une icône du bandeau : ajuste le rang de simulation (Roaring Winds / Tranquility).
    private void NatureRitualRank_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is NatureRitualIndicatorViewModel { HasRank: true } vm)
        {
            vm.AdjustRank(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    // Section « Dégâts selon l'armure » des infobulles : bascule + persistance settings.json.
    // Pas de refresh à pousser : chaque infobulle relit l'état à son ouverture (Loaded).
    private void ShowArmorDamage_Click(object sender, RoutedEventArgs e)
    {
        ArmorDamageDisplay.Enabled = ShowArmorDamageMenuItem.IsChecked;
        _settings.ShowArmorDamage = ArmorDamageDisplay.Enabled;
        _settings.Save();
    }

    private void CustomArmorLevel_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ArmorLevelWindow(ArmorDamageDisplay.CustomArmorLevels,
            ArmorDamageDisplay.CharacterLevel, ArmorDamageDisplay.TargetLevel) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        ArmorDamageDisplay.CustomArmorLevels = dlg.ArmorLevels;
        ArmorDamageDisplay.CharacterLevel = dlg.CharacterLevel;
        ArmorDamageDisplay.TargetLevel = dlg.TargetLevel;
        _settings.CustomArmorLevels = dlg.ArmorLevels;
        _settings.CustomArmorLevel = null;   // héritage pré-multi soldé
        _settings.CharacterLevel = dlg.CharacterLevel;
        _settings.TargetLevel = dlg.TargetLevel;
        _settings.Save();
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(DarkThemeMenuItem.IsChecked);
        _settings.DarkTheme = DarkThemeMenuItem.IsChecked;
        _settings.Save();
    }

    // ── Langue (Affichage ▸ Langue) ───────────────────────────────────────────
    // Swap à chaud : chaînes UI via DynamicResource, puis rechargement du catalogue pour
    // rafraîchir les noms affichés (DisplayName) et la largeur des colonnes. Les infobulles
    // relisent les propriétés Display* à chaque ouverture → rien d'autre à pousser.
    private async void LangFr_Click(object sender, RoutedEventArgs e) => await SwitchLanguage(fr: true);
    private async void LangEn_Click(object sender, RoutedEventArgs e) => await SwitchLanguage(fr: false);

    // Drapeaux du coin haut droit : la langue active est pleinement lisible et encadrée, l'autre
    // en retrait. Appelé aussi au démarrage — le menu Affichage, lui, ne se coche qu'à l'ouverture
    // du sous-menu, alors que les drapeaux sont visibles en permanence.
    private void UpdateLanguageButtons(bool fr)
    {
        if (LangFrBorder == null || LangEnBorder == null) return;   // avant InitializeComponent

        // SetResourceReference et non FindResource : l'encadré doit suivre la bascule de thème,
        // pas figer la couleur d'accent qui avait cours au moment du clic.
        static void Frame(Border b, bool active)
        {
            if (active) b.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else        b.BorderBrush = Brushes.Transparent;
        }
        Frame(LangFrBorder, fr);
        Frame(LangEnBorder, !fr);
        LangFrButton.Opacity = fr ? 1.0 : 0.45;
        LangEnButton.Opacity = fr ? 0.45 : 1.0;
    }

    private async Task SwitchLanguage(bool fr)
    {
        LangFrMenuItem.IsChecked = fr;
        LangEnMenuItem.IsChecked = !fr;
        UpdateLanguageButtons(fr);
        if (LanguageManager.IsFr == fr) return;
        LanguageManager.Apply(fr);
        _settings.Language = fr ? "fr" : "en";
        _settings.Save();
        await LoadSkillsFromDbAsync();   // rebâtit skills + attaques de référence du calculateur

        // Filtres des catalogues (Professions/Caractéristiques/PvE) : libellés bakés → rebind FR/EN.
        _vm.SkillPanel.RefreshLanguage();
        _vm.SearchBuilder.Catalog.RefreshLanguage();
        _vm.ArmorCalc.AttackCatalog.RefreshLanguage();

        // Persos de tous les teambuilds/éditeurs : noms, résumés/lignes d'attributs, noms de skills,
        // infobulles de flux (chaînes bakées, non recréées par un switch en place).
        _vm.RefreshLanguage();

        // Surfaces à chaînes « bakées » : forcer un refresh dans la nouvelle langue (demande Philippe).
        _vm.ArmorCalc.RefreshLanguage();
        // Aide : son contenu résout la langue à la lecture → rebind si la fenêtre est ouverte.
        if (_helpWindow is { IsLoaded: true }) _helpWindow.RefreshLanguage();
        // La fenêtre Spike bake ses libellés au calcul → la rebâtir depuis l'état persisté (.pn3).
        if (_spikeWindow is { } sw)
        {
            var build = sw.Build;
            sw.Close();
            OpenSpikeWindow(build);
        }
    }

    private void ShowAttributesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild is { } tb)
            tb.ShowAttributes = !tb.ShowAttributes;
    }

    private void ShowPreviewAttributesMenuItem_Click(object sender, RoutedEventArgs e)
        => _vm.Browser.ShowPreviewAttributes = !_vm.Browser.ShowPreviewAttributes;

    // Bandeaux de conditions (éditeur de build + teambuild) : bascule + persistance settings.json.
    private void ShowConditionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowConditions = !_vm.ShowConditions;
        _settings.ShowConditions = _vm.ShowConditions;
        _settings.Save();
    }

    // Molette du teambuild : n'autoriser le réglage des caractéristiques que sur le personnage
    // cliqué. Décoché = geste d'origine (paw·ned²), le verrou de salve restant actif dans les deux cas.
    private void WheelNeedsSelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _vm.WheelNeedsSelection = !_vm.WheelNeedsSelection;
        _settings.WheelNeedsSelection = _vm.WheelNeedsSelection;
        _settings.Save();
    }

    // Bandeau des rituels de la nature : « équipés seulement » ↔ « tous les 8 ». Persisté settings.json.
    private void ShowAllNatureRitualsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowAllNatureRituals = !_vm.ShowAllNatureRituals;
        _settings.ShowAllNatureRituals = _vm.ShowAllNatureRituals;
        _settings.Save();
    }

    // Afficher/masquer la BARRE des rituels de la nature (teambuild + éditeur). Persisté settings.json.
    private void ShowNatureRitualBandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowNatureRituals = !_vm.ShowNatureRituals;
        _settings.ShowNatureRituals = _vm.ShowNatureRituals;
        _settings.Save();
    }

    private void IconsLarge_Click(object sender, RoutedEventArgs e)  => SetIconSize(IconSizeMode.Large);
    private void IconsMedium_Click(object sender, RoutedEventArgs e) => SetIconSize(IconSizeMode.Medium);
    private void IconsSmall_Click(object sender, RoutedEventArgs e)  => SetIconSize(IconSizeMode.Small);

    private void SetIconSize(IconSizeMode mode)
    {
        _vm.IconSize = mode;
        _settings.IconSize = mode;
        _settings.Save();
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild == null) return;

        var bg = Application.Current.TryFindResource("ContentBackgroundBrush") as Brush ?? Brushes.White;
        if (VisualCapture.Render(CharacterGrid, bg) is not { } bmp) return;

        var dlg = new SaveFileDialog
        {
            Filter   = T("S.Filter.Png"),
            Title    = T("S.Dlg.SaveScreenshot"),
            FileName = _vm.ActiveTeamBuild.Name + ".png",
        };
        if (dlg.ShowDialog() != true) return;

        VisualCapture.SavePng(bmp, dlg.FileName);
    }

    // ── Selection menu ────────────────────────────────────────────────────────

    private void SelectionMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        OpenSelectedMenuItem.IsEnabled = _vm.IsBrowserActive && _vm.Browser.SelectedFile != null;
        BuildFluxMenu();
        BuildNatureRitualMenu();   // sous-menu « Rituels de la Nature (énergie/recharge) » — déplacé de View
    }

    private void OpenSelectedBuild_Click(object sender, RoutedEventArgs e)
        => _vm.Browser.OpenSelected();

    // ── Flux ──────────────────────────────────────────────────────────────────

    // Indicateur de flux du contexte actif : le teambuild affiché, sinon le build simple affiché.
    private FluxIndicatorViewModel? ActiveFluxIndicator =>
        _vm.IsTeamBuildActive   ? _vm.ActiveTeamBuild?.FluxIndicator :
        _vm.IsBuildEditorActive ? _vm.ActiveBuild?.FluxIndicator :
        null;

    // Clic sur l'icône de flux (teambuild ou build simple) : bascule aucun ↔ flux du mois.
    // Le DataContext du bouton EST l'indicateur (posé en XAML), donc uniforme pour les deux vues.
    private void FluxIcon_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FluxIndicatorViewModel fi)
            fi.Toggle();
    }

    // (Re)construit le sous-menu Sélection > Flux : « Aucun » + les 12 flux (mois courant marqué),
    // cochés selon l'état du contexte actif. Désactivé si aucun teambuild/build n'est affiché.
    private void BuildFluxMenu()
    {
        var fi = ActiveFluxIndicator;
        FluxMenuItem.IsEnabled = fi != null;
        FluxMenuItem.Items.Clear();
        if (fi == null) return;

        var none = new MenuItem { Header = T("S.Flux.None"), IsCheckable = true, IsChecked = !fi.IsActive };
        none.Click += (_, _) => fi.ActiveFlux = null;
        FluxMenuItem.Items.Add(none);
        FluxMenuItem.Items.Add(new Separator());

        int currentMonth = DateTime.Now.Month;
        foreach (var info in FluxData.All)
        {
            var item = new MenuItem
            {
                Header = info.Month == currentMonth ? info.DisplayName + T("S.Flux.CurrentMonthSuffix") : info.DisplayName,
                IsCheckable = true,
                IsChecked = fi.ActiveFlux == info.Flux,
                ToolTip = info.DisplayDescription,
            };
            var flux = info.Flux; // capture par valeur pour la fermeture
            item.Click += (_, _) => fi.ActiveFlux = flux;
            FluxMenuItem.Items.Add(item);
        }
    }

    // ── Window menu ───────────────────────────────────────────────────────────

    private void WindowMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu) return;
        menu.Items.Clear();

        var browserItem = new MenuItem
        {
            Header      = "Team Builds",
            IsCheckable = true,
            IsChecked   = _vm.IsBrowserActive,
        };
        browserItem.Click += (_, _) => _vm.ActivateBrowser();
        menu.Items.Add(browserItem);

        menu.Items.Add(new Separator());

        if (_vm.OpenTeamBuilds.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = T("S.WinMenu.NoBuilds"), IsEnabled = false });
            return;
        }

        foreach (var tb in _vm.OpenTeamBuilds)
        {
            var item = new MenuItem
            {
                Header      = tb.DisplayName,
                IsCheckable = true,
                IsChecked   = tb.IsActive,
            };
            var captured = tb;
            item.Click += (_, _) => _vm.ActiveTeamBuild = captured;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var closeItem = new MenuItem
        {
            Header    = T("S.WinMenu.CloseActive"),
            IsEnabled = _vm.ActiveTeamBuild != null,
        };
        closeItem.Click += (_, _) => { if (_vm.ActiveTeamBuild is { } tb) TryCloseTab(tb); };
        menu.Items.Add(closeItem);
    }

    // ── Profession picker ─────────────────────────────────────────────────────

    private static readonly Profession[] _professionPickerList =
        Enum.GetValues<Profession>().Where(p => p != Profession.None).ToArray();

    private void PrimaryProfIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not FrameworkElement el || el.DataContext is not CharacterSlotViewModel vm) return;
        OpenProfessionMenu(vm, isPrimary: true, el);
        e.Handled = true;
    }

    private void SecondaryProfIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not FrameworkElement el || el.DataContext is not CharacterSlotViewModel vm) return;
        OpenProfessionMenu(vm, isPrimary: false, el);
        e.Handled = true;
    }

    // Onglet Search : bouton ⇄ du perso-requête. Le picker de profession propose déjà Swap quand
    // les deux professions sont posées, mais le bouton dédié est plus direct.
    private void QuerySwapProf_Click(object sender, RoutedEventArgs e)
        => _vm.SearchBuilder.QueryCharacter.SwapProfessions();

    // Onglets du catalogue de recherche : ligne 1 (groupes / caractéristiques) et ligne 2 (sous-onglets).
    private void CatalogTab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CatalogTab tab)
            _vm.SearchBuilder.SelectCatalogTab(tab);
    }

    private void CatalogSubTab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CatalogTab tab)
            _vm.SearchBuilder.SelectCatalogSubTab(tab);
    }

    // ── Onglets Build (éditeur d'un build simple) ─────────────────────────────

    // Menu Fichier > New Build ET bouton toolbar : crée un onglet de build simple vierge.
    private void NewBuild_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SkillPanel.AllSkills.Count == 0)
        {
            MessageBox.Show(T("S.Msg.CatalogNotLoaded"),
                T("S.Msg.CatalogNotLoadedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var tab = _vm.NewBuild();
        PrepareBuildCatalog(tab);
    }

    // Charge le catalogue d'un onglet Build (skills + largeur de cellule) et recâble ses onglets PR/SEC.
    private void PrepareBuildCatalog(BuildEditorViewModel tab)
    {
        tab.Catalog.LoadSkills(_vm.SkillPanel.AllSkills);
        tab.Catalog.CatalogItemWidth = ComputeCatalogItemWidth(_vm.SkillPanel.AllSkills);
        tab.RebuildCatalogTabs();
        // En DERNIER : la signature de référence doit être celle du build fini de câbler. Appelé
        // ici plutôt que dans MainViewModel parce que c'est ce point de passage-là, commun au
        // nouveau build et à l'ouverture d'un .txt, qui voit l'onglet complet. L'undo se branche
        // dans la foulée, pour partir du même état de référence.
        tab.BeginTracking();
        AttachUndo(tab);
    }

    // Pendant de TryCloseTab pour les onglets Build.
    private bool TryCloseBuild(BuildEditorViewModel build)
    {
        if (build.IsDirty)
        {
            var result = MessageBox.Show(
                string.Format(T("S.Msg.UnsavedBuild"), build.Title),
                T("S.Msg.UnsavedTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes && !SaveBuildTab(build)) return false;
        }

        _vm.CloseBuild(build);
        return true;
    }

    private void BuildEditorTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BuildEditorViewModel tab })
            _vm.ActivateBuild(tab);
    }

    private void BuildEditorTab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: BuildEditorViewModel tab })
        {
            TryCloseBuild(tab);
            e.Handled = true;
        }
    }

    private void CloseBuildEditorTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: BuildEditorViewModel tab })
            TryCloseBuild(tab);
    }

    // Bouton ⇄ d'un onglet Build : le bouton est dans le bloc perso (DataContext = Character).
    // SwapProfessions() ne produit jamais PR=None ici car le bouton est désactivé si SEC=None (XAML).
    private void BuildSwapProf_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CharacterSlotViewModel character)
            character.SwapProfessions();
    }

    // Onglets du catalogue d'un onglet Build (seul l'onglet actif est visible → clics sûrs).
    private void BuildCatalogTab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CatalogTab tab)
            _vm.ActiveBuild?.SelectCatalogTab(tab);
    }

    private void BuildCatalogSubTab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CatalogTab tab)
            _vm.ActiveBuild?.SelectCatalogSubTab(tab);
    }

    // Bouton « Enregistrer le modèle » de l'onglet Build : c'est LE bouton d'enregistrement de cet
    // onglet, il passe donc par le même chemin que Ctrl+S et que la barre d'outils. Deux boutons
    // d'enregistrement au comportement différent dans la même vue seraient un piège.
    private void SaveBuild_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveBuild is { } tab)
            SaveBuildTab(tab);
    }

    // Onglet Build : copie silencieuse du code de template (code O) dans le presse-papier.
    private void CopyBuildCode_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveBuild is not { } tab) return;
        SafeClipboard.SetText(EncodeCharacterCode(tab.Character));
    }

    // Onglet Build : copie silencieuse du code de chat (format paw·ned² [1 P/S - nom;code]).
    private void CopyBuildChatCode_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveBuild is not { } tab) return;
        var charVm   = tab.Character;
        var code     = EncodeCharacterCode(charVm);
        var chatCode = GwTemplateCodec.FormatChatCode(
            1, charVm.PrimaryProfession, charVm.SecondaryProfession, charVm.Name, code);
        SafeClipboard.SetText(chatCode);
    }

    // Encode un perso en code O (template de compétences GW1), IDs résolus via le catalogue.
    private string EncodeCharacterCode(CharacterSlotViewModel charVm)
    {
        var skillIdsByName = SkillVariants.TemplateIdsByName(_vm.SkillPanel.AllSkills);
        var build = new CharacterBuild
        {
            PrimaryProfession   = charVm.PrimaryProfession,
            SecondaryProfession = charVm.SecondaryProfession,
            Skills              = charVm.SkillSlots.Select(s => s.Skill).ToArray(),
            Attributes          = GwTemplateCodec.ToAttributeDict(charVm.Attributes),
        };
        return GwTemplateCodec.Encode(build, skillIdsByName);
    }

    // L'équipement est lié à la profession principale par son armure : avant de changer la PR
    // d'un perso qui porte une armure d'une AUTRE profession, demander confirmation et vider
    // l'équipement. True = feu vert (équipement neutre/compatible, ou vidé après confirmation).
    private static bool ConfirmEquipmentReset(CharacterSlotViewModel vm, Profession newPrimary)
    {
        if (vm.Equipment is not { IsEmpty: false } eq) return true;
        var armorProf = GwEquipmentInfo.GuessProfession(eq);
        if (armorProf == Profession.None || armorProf == newPrimary) return true;

        var r = MessageBox.Show(
            string.Format(T("S.Msg.EquipProfConfirm"), armorProf, newPrimary),
            T("S.Msg.EquipProfTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return false;

        vm.Equipment = null;
        return true;
    }

    private void OpenProfessionMenu(CharacterSlotViewModel vm, bool isPrimary, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        foreach (var prof in _professionPickerList)
        {
            var p = prof;
            var item = new MenuItem { Header = prof.ToString(), Icon = ProfessionMenuIcon(prof) };
            // Cliquer la primaire dans le picker SEC = swap PR/SEC (géré dans SetSecondaryProfession).
            item.Click += (_, _) =>
            {
                if (isPrimary)
                {
                    if (p != vm.PrimaryProfession && ConfirmEquipmentReset(vm, p))
                        vm.SetPrimaryProfession(p);
                }
                else
                {
                    // Choisir la PR actuelle dans le picker SEC = swap → la PR change aussi.
                    bool isSwap = p != Profession.None && p == vm.PrimaryProfession
                                  && vm.SecondaryProfession != Profession.None;
                    if (!isSwap || ConfirmEquipmentReset(vm, vm.SecondaryProfession))
                        vm.SetSecondaryProfession(p);
                }
            };
            menu.Items.Add(item);
        }

        // "None" : SEC toujours ; PR uniquement pour le perso-requête de recherche (« toute PR »).
        bool isQuery = ReferenceEquals(vm, _vm.SearchBuilder.QueryCharacter);
        if (!isPrimary || isQuery)
        {
            menu.Items.Add(new Separator());
            var noneItem = new MenuItem { Header = T("S.Prof.None") };
            if (isPrimary) noneItem.Click += (_, _) => vm.ClearPrimaryProfession();
            else           noneItem.Click += (_, _) => vm.SetSecondaryProfession(Profession.None);
            menu.Items.Add(noneItem);
        }

        if (vm.PrimaryProfession != Profession.None && vm.SecondaryProfession != Profession.None)
        {
            menu.Items.Add(new Separator());
            var swapItem = new MenuItem { Header = T("S.Prof.Swap") };
            swapItem.Click += (_, _) =>
            {
                if (ConfirmEquipmentReset(vm, vm.SecondaryProfession))
                    vm.SwapProfessions();
            };
            menu.Items.Add(swapItem);
        }

        menu.IsOpen = true;
    }

    // Icône 16px d'une profession pour les MenuItem du picker (null si introuvable).
    private static Image? ProfessionMenuIcon(Profession p)
    {
        var path = ProfessionIconService.GetLocalPath(p);
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path);
            img.DecodePixelWidth = 16;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return new Image { Source = img, Width = 16, Height = 16 };
        }
        catch { return null; }
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────────

    private void SkillList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStart = e.GetPosition(null);

    private void SkillList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        // Généralisé : la compétence draggée = sélection de CETTE liste (éditeur ou catalogue de
        // recherche), pas d'un panneau codé en dur → les mêmes handlers servent les deux listes.
        if (sender is not ListView list || list.SelectedItem is not Skill skill) return;

        var pos  = e.GetPosition(null);
        var diff = pos - _dragStart;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(list, skill, DragDropEffects.Copy);
    }

    private void SkillSlot_DragEnter(object sender, DragEventArgs e) => UpdateSlotDragFeedback(sender, e);

    private void SkillSlot_DragOver(object sender, DragEventArgs e) => UpdateSlotDragFeedback(sender, e);

    // Reflète le mode réel sur le curseur et la bordure : copie (vert) si source du catalogue,
    // ou Shift/Ctrl depuis un slot ; sinon déplacement/swap (bleu).
    private void UpdateSlotDragFeedback(object sender, DragEventArgs e)
    {
        bool fromCatalog = e.Data.GetDataPresent(typeof(Skill));
        bool fromSlot    = e.Data.GetDataPresent(typeof(SkillSlotViewModel));
        if ((fromCatalog || fromSlot) && sender is Border border)
        {
            bool ctrl  = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool copy  = fromCatalog || ctrl || shift;

            e.Effects              = copy ? DragDropEffects.Copy : DragDropEffects.Move;
            border.BorderBrush     = copy ? Brushes.LimeGreen : Brushes.DodgerBlue;
            border.BorderThickness = new Thickness(2);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void SkillSlot_DragLeave(object sender, DragEventArgs e)
        => RestoreSlotStyle(sender as Border);

    private void SkillSlot_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border && border.DataContext is SkillSlotViewModel targetSlot)
        {
            if (e.Data.GetData(typeof(Skill)) is Skill skill)
            {
                targetSlot.Skill = skill;
                var charVm = FindCharacterVm(border);
                charVm?.EnforcePveCap(targetSlot);
                WaMoEasterEgg.OnSkillAdded(charVm, skill, _vm.IsTeamBuildActive);
            }
            else if (e.Data.GetData(typeof(SkillSlotViewModel)) is SkillSlotViewModel sourceSlot
                     && !ReferenceEquals(sourceSlot, targetSlot))
            {
                bool ctrl  = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);
                bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                if (ctrl || shift)
                {
                    // Shift ou Ctrl = copier le skill (la source reste). Ctrl+Shift = copier aussi le niveau de carac.
                    targetSlot.Skill = sourceSlot.Skill;
                    if (ctrl && shift) CopyAttributeLevel(sourceSlot, targetSlot);
                }
                else
                {
                    (targetSlot.Skill, sourceSlot.Skill) = (sourceSlot.Skill, targetSlot.Skill); // swap
                }
                e.Effects = DragDropEffects.Copy; // signal : traité, ne pas effacer la source
            }
        }
        RestoreSlotStyle(sender as Border);
        e.Handled = true;
    }

    // Copie le niveau investi de la caractéristique du skill (cible) du perso source vers le perso cible.
    private void CopyAttributeLevel(SkillSlotViewModel source, SkillSlotViewModel target)
    {
        var chars = _vm.ActiveTeamBuild?.Characters;
        if (chars == null || target.Skill?.Attribute is not string attrName) return;

        var gw = GwAttributeData.ByName(attrName);
        if (gw == null) return;

        var srcChar = chars.FirstOrDefault(c => c.SkillSlots.Contains(source));
        var dstChar = chars.FirstOrDefault(c => c.SkillSlots.Contains(target));
        if (srcChar == null || dstChar == null) return;

        var srcRow = srcChar.PrimaryAttributeRows.Concat(srcChar.SecondaryAttributeRows)
            .FirstOrDefault(r => r.AttributeId == gw.Id);
        var dstRow = dstChar.PrimaryAttributeRows.Concat(dstChar.SecondaryAttributeRows)
            .FirstOrDefault(r => r.AttributeId == gw.Id);

        // Si le perso cible n'a pas cette caractéristique (professions différentes), on copie juste le skill.
        if (srcRow != null && dstRow != null)
            dstRow.Points = srcRow.Points;
    }

    private void SkillSlot_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is Border { DataContext: SkillSlotViewModel slot } && slot.HasSkill)
        {
            _slotDragStartPos = e.GetPosition(null);
            _slotDragSource   = slot;
        }
        (sender as UIElement)?.Focus();
    }

    private void SkillSlot_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _slotDragSource == null) return;
        var diff = e.GetPosition(null) - _slotDragStartPos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var source = _slotDragSource;
        _slotDragSource = null;

        // Le mode (déplacement/swap, copie via Shift ou Ctrl, copie + niveau de carac via Ctrl+Shift)
        // est déterminé dans SkillSlot_Drop selon les modificateurs au moment du drop.
        var effects = DragDrop.DoDragDrop((DependencyObject)sender, source, DragDropEffects.Move | DragDropEffects.Copy);
        if (effects == DragDropEffects.None)
            source.Skill = null; // drag hors de la barre → suppression
    }

    private void SkillSlot_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && sender is Border { DataContext: SkillSlotViewModel slot })
        {
            slot.Skill = null;
            e.Handled  = true;
        }
    }

    private void ClearSkillSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu cm } &&
            cm.DataContext is SkillSlotViewModel slot)
            slot.Skill = null;
    }

    // Compétence ciblée par un menu contextuel de skill : slot d'une ligne perso si présent,
    // sinon sélection de la ListView sous le menu (catalogue éditeur OU recherche), enfin fallback.
    private Skill? ContextMenuSkill(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu cm })
        {
            if (cm.DataContext is SkillSlotViewModel slot) return slot.Skill;
            if (cm.PlacementTarget is ListView { SelectedItem: Skill s }) return s;
        }
        return _vm.SkillPanel.SelectedSkill;
    }

    // Clic droit sur une ligne du catalogue : sélectionne la ligne sous le curseur pour que le menu
    // contextuel cible la bonne compétence (sinon il retomberait sur la sélection précédente).
    private void SkillList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView list && e.OriginalSource is DependencyObject d
            && ItemsControl.ContainerFromElement(list, d) is ListViewItem item)
            item.IsSelected = true;
    }

    private static void RestoreSlotStyle(Border? border)
    {
        if (border == null) return;
        border.ClearValue(Border.BorderBrushProperty);
        border.ClearValue(Border.BorderThicknessProperty);
    }

    // ── Skill list layout + sort ──────────────────────────────────────────────

    private void SkillListHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Content: string col } || col.Length == 0)
            return;
        // Le panneau ciblé = Tag de la ListView (SkillPanel de l'éditeur ou Catalog de la recherche).
        if ((sender as FrameworkElement)?.Tag is not SkillPanelViewModel panel) return;
        bool asc = panel.SortColumn == col ? !panel.SortAscending : true;
        panel.SetSort(col, asc);
    }

    private void SkillList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ListView list || list.View is not GridView gv) return;
        // Colonnes fixes : icône(74) + 7 colonnes de mécaniques 48px (E/A/S/O/U/C/R, cf. SkillPanelViewModel.StatColumnWidth) + scrollbar + bordures
        double fixedWidth = 74 + 7 * 48 + SystemParameters.VerticalScrollBarWidth + 4;
        gv.Columns[1].Width = Math.Max(60, list.ActualWidth - fixedWidth);
    }

    // ── Skill context menu ────────────────────────────────────────────────────

    private void OpenSkillArticle_Click(object sender, RoutedEventArgs e)
    {
        var skill = ContextMenuSkill(sender);
        if (skill == null) return;
        Process.Start(new ProcessStartInfo(SkillArticleUrl(skill)) { UseShellExecute = true });
    }

    private void OpenSkillDiscussion_Click(object sender, RoutedEventArgs e)
    {
        var skill = ContextMenuSkill(sender);
        if (skill == null) return;
        // Onglet discussion du wiki = page "Talk:<nom>" → on préfixe le segment /wiki/.
        var article = SkillArticleUrl(skill);
        var talk = article.Contains("/wiki/")
            ? article.Replace("/wiki/", "/wiki/Talk:")
            : article;
        Process.Start(new ProcessStartInfo(talk) { UseShellExecute = true });
    }

    // Clic droit « Parse in templates » sur une compétence = lancer une recherche PR/SEC = None/None
    // avec cette SEULE compétence comme critère → nouvel onglet « Résultats » (templates + teambuilds).
    private async void ParseSkillInTemplates_Click(object sender, RoutedEventArgs e)
    {
        var skill = ContextMenuSkill(sender);
        if (skill == null) return;
        if (_vm.SkillPanel.AllSkills.Count == 0)
        {
            MessageBox.Show(T("S.Msg.CatalogNotLoaded"),
                T("S.Msg.CatalogNotLoadedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var query = new BuildSearchQuery();
        query.RequiredSkillGroups.Add(
            SkillVariants.ResolveGroup(skill, SkillVariantMode.AllVersions, _vm.SkillPanel.AllSkills));
        await RunSearchAndShowResults(query, SearchScope.SkillTemplates | SearchScope.TeamBuilds);
    }

    private static string SkillArticleUrl(Skill skill)
        => string.IsNullOrEmpty(skill.WikiUrl)
            ? $"https://wiki.guildwars.com/wiki/{Uri.EscapeDataString(skill.Name)}"
            : skill.WikiUrl;

    // ── Template import / export ──────────────────────────────────────────────
    //
    // Les anciennes entrées « Importer un template de compétences (O)... » et « ...d'équipement
    // (P)... » (saisie d'un code dans une modale) ont été retirées des menus le 01/08/2026 :
    // l'import presse-papier/fichier couvre les deux, code brut comme code de chat.

    private void CopyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveMenuCharacter(sender) is { } charVm) CopyCharacterTemplate(charVm, chat: false);
    }

    private void CopyChatCode_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveMenuCharacter(sender) is { } charVm) CopyCharacterTemplate(charVm, chat: true);
    }

    // Édition ▸ Copier : seul un onglet Build a UN perso courant évident (un team build en a
    // jusqu'à 12 → la copie y passe par le clic droit sur la ligne, son icône ⧉, ou Ctrl+C
    // sur la ligne survolée).
    private void CopyTemplateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBuildEditorActive && _vm.ActiveBuild is { } build)
            CopyCharacterTemplate(build.Character, chat: false);
    }

    private void CopyChatCodeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBuildEditorActive && _vm.ActiveBuild is { } build)
            CopyCharacterTemplate(build.Character, chat: true);
    }

    // Copie le code O du perso — brut, ou au format chat « [1 Mo/N - nom;code] » collable en
    // jeu — en confirmant visuellement CE QUI a été mis dans le presse-papier.
    private void CopyCharacterTemplate(CharacterSlotViewModel charVm, bool chat)
    {
        var code = EncodeCharacterCode(charVm);
        if (chat)
        {
            // Index = position dans le teambuild, comme l'icône ⧉ (0 pour une variante, qui ne
            // figure pas dans Characters). Un onglet Build n'a qu'un perso → toujours 1.
            int index = _vm.IsTeamBuildActive
                ? (_vm.ActiveTeamBuild?.Characters.IndexOf(charVm) ?? -1) + 1
                : 1;
            code = GwTemplateCodec.FormatChatCode(
                index, charVm.PrimaryProfession, charVm.SecondaryProfession, charVm.Name, code);
        }
        SafeClipboard.SetText(code);
        MessageBox.Show(string.Format(T("S.Msg.TemplateCopied"), code), T("S.Msg.TemplateGw1Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Clic sur l'étoile : enregistre le code template du personnage dans un .txt
    // (par défaut dans Documents\GUILD WARS\Templates\Skills), format importable par GW1.
    private void SaveTemplate_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is CharacterSlotViewModel charVm)
            SaveCharacterTemplate(charVm);
    }

    // Enregistre un personnage comme template de compétences GW1 (.txt, code O) : partagé par
    // l'étoile des lignes de perso (éditeur teambuild) et le bouton « Enregistrer le modèle »
    // de l'onglet Build. Par défaut dans Documents\GUILD WARS\Templates\Skills.
    // existingPath non nul = réécriture EN PLACE, sans dialogue (Enregistrer sur un onglet Build
    // déjà lié à un fichier). Retourne le chemin réellement écrit, ou null si l'utilisateur a
    // annulé ou si l'écriture a échoué — l'appelant s'en sert pour ne pas marquer l'onglet propre.
    private string? SaveCharacterTemplate(CharacterSlotViewModel charVm, string? existingPath = null)
    {
        var code = EncodeCharacterCode(charVm);

        if (existingPath is { } inPlace) return WriteTemplateFile(inPlace, code) ? inPlace : null;

        var safeName = string.Concat(charVm.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog
        {
            Filter     = T("S.Filter.SkillTplSave"),
            Title      = T("S.Dlg.SaveBuildTpl"),
            FileName   = string.IsNullOrWhiteSpace(safeName) ? "build" : safeName,
            DefaultExt = ".txt",
        };
        // Skill templates : sous-dossier "Skills" de la racine Templates détectée
        // (repli sur la racine si le sous-dossier n'existe pas). Sans rien créer.
        if (_vm.Browser.RootPath is { } root && Directory.Exists(root))
        {
            var skillsDir = Path.Combine(root, "Skills");
            dlg.InitialDirectory = Directory.Exists(skillsDir) ? skillsDir : root;
        }

        if (dlg.ShowDialog() != true) return null;
        return WriteTemplateFile(dlg.FileName, code) ? dlg.FileName : null;
    }

    // Écriture d'un template .txt. Comme SaveTeamBuild, une erreur d'écriture ne remonte PAS :
    // ce chemin est aussi celui de la fermeture, où une exception emporterait le build non
    // enregistré au moment précis où l'utilisateur demande à le sauver.
    private bool WriteTemplateFile(string filePath, string code)
    {
        try
        {
            File.WriteAllText(filePath, code);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveCharacterTemplate] '{filePath}': {ex}");
            MessageBox.Show(string.Format(T("S.Msg.SaveFailed"), filePath, ex.Message),
                T("S.Msg.SaveFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    // ── Equipment templates (fichiers .txt de l'arborescence) ────────────────

    // Dossier des templates d'équipement (sous-dossier "Equipment" de la racine GW,
    // repli sur la racine). Null si la racine n'est pas définie.
    private string? EquipmentTemplatesDir()
    {
        if (_vm.Browser.RootPath is not { } root || !Directory.Exists(root)) return null;
        var dir = Path.Combine(root, "Equipment");
        return Directory.Exists(dir) ? dir : root;
    }

    private void NewEquipmentTemplate_Click(object sender, RoutedEventArgs e)
        => EditEquipmentTemplate(null, T("S.Dlg.NewEquipTpl"), null);

    // Fichier .txt ne contenant que des codes P → modale d'équipement préremplie. Retourne
    // false pour laisser la voie normale (team build / skill template) si le fichier est
    // mixte ou sans code d'équipement.
    private bool TryOpenEquipmentTemplate(string filePath, IReadOnlyDictionary<int, Skill> skillsById)
    {
        string text;
        try { text = File.ReadAllText(filePath); }
        catch { return false; }

        var builds = GwEquipmentCodec.DecodeLines(text);
        if (builds.Count == 0) return false;

        bool hasSkillCode = text.Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0)
            .Any(l => GwTemplateCodec.Decode(l, skillsById) != null);
        if (hasSkillCode) return false;

        var initial = builds.Count == 1 ? builds[0] : EquipmentBuild.Combine(builds);
        EditEquipmentTemplate(initial,
            string.Format(T("S.Equip.TitleFor"), Path.GetFileNameWithoutExtension(filePath)), filePath);
        return true;
    }

    // Ouvre la modale d'équipement en mode template autonome ; à la validation, écrit le
    // fichier (un code P par ligne, un par set — un seul set = fichier compatible jeu).
    // filePath null = nouveau template → SaveFileDialog dans le dossier Equipment.
    private void EditEquipmentTemplate(EquipmentBuild? initial, string title, string? filePath)
    {
        var profession = initial == null ? Profession.None : GwEquipmentInfo.GuessProfession(initial);
        var dialog = new EquipmentEditorWindow(initial, profession, EquipmentTemplatesDir(), title)
        { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            // "Sauvegarder comme template..." a pu écrire malgré l'annulation.
            _vm.Browser.RefreshFiles();
            return;
        }

        if (dialog.OutputEquipment is not { } eq)
        {
            if (filePath != null)
                MessageBox.Show(T("S.Msg.EquipEmpty"),
                    T("S.Msg.EquipTemplateTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (filePath == null)
        {
            var dlg = new SaveFileDialog
            {
                Title  = T("S.Dlg.SaveEquipTpl"),
                Filter = T("S.Filter.EquipTplSave"),
            };
            if (EquipmentTemplatesDir() is { } dir) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog() != true) return;
            filePath = dlg.FileName;
        }

        try
        {
            File.WriteAllText(filePath, GwEquipmentCodec.EncodeFileText(eq));
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(T("S.Msg.CantSaveTemplate"), ex.Message),
                T("S.Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _vm.Browser.RefreshFiles();
    }

    // ── Equipment import / export ─────────────────────────────────────────────
    // TODO(equipment): templates d'équipement GW1 sont PvP-only en jeu.
    //                  Désactiver ou avertir l'import pour les builds marqués PvE.
    // L'import d'un code P saisi à la main vit désormais dans Propriétés du personnage ; depuis
    // les menus, le code P passe par l'import presse-papier/fichier commun.

    private void CopyEquipment_Click(object sender, RoutedEventArgs e)
    {
        var charVm = ResolveMenuCharacter(sender);
        if (charVm == null) return;

        if (charVm.Equipment is not { IsEmpty: false } eq)
        {
            MessageBox.Show(T("S.Msg.NoEquipDefined"), T("S.Msg.EquipExportTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Un code P par set : un seul set → code brut (collable en jeu) ; plusieurs sets →
        // une ligne "F1 : Pxxx" par set non vide.
        var text = GwEquipmentCodec.EncodeAllSets(eq);
        SafeClipboard.SetText(text);
        MessageBox.Show(string.Format(T("S.Msg.EquipCopied"), text), T("S.Msg.EquipGw1Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Character slot context menu (panneau gauche) ─────────────────────────

    private CharacterSlotViewModel? GetCharFromLeftPanelMenu(object sender)
    {
        DependencyObject? current = sender as DependencyObject;
        while (current != null)
        {
            if (current is ContextMenu cm)
                return (cm.PlacementTarget as FrameworkElement)?.Tag as CharacterSlotViewModel;
            current = LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static MenuItem? FindMenuItemByTag(ContextMenu menu, string tag)
        => menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Tag as string == tag);

    private void CharLeftPanel_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.ContextMenu is not ContextMenu menu) return;
        var charVm = fe.Tag as CharacterSlotViewModel;
        if (charVm == null || _vm.ActiveTeamBuild == null) return;
        PopulateCharContextMenu(menu, charVm, _vm.ActiveTeamBuild.Characters);
    }

    private void PopulateCharContextMenu(ContextMenu menu, CharacterSlotViewModel charVm,
        ObservableCollection<CharacterSlotViewModel> chars)
    {
        int selfIdx = chars.IndexOf(charVm);

        // ── Assigner joueur ──────────────────────────────────────────────────
        var assignItem = FindMenuItemByTag(menu, "assign");
        if (assignItem != null)
        {
            assignItem.Items.Clear();

            void AddAssign(string header, string value)
            {
                var item = new MenuItem { Header = header, IsCheckable = true,
                                         IsChecked = charVm.Assignment == value };
                item.Click += (_, _) => charVm.Assignment = value;
                assignItem.Items.Add(item);
            }

            AddAssign(T("S.Assign.Unassigned"), "(unassigned)");

            foreach (var name in chars
                .Where(c => c != charVm && c.Assignment != "(unassigned)")
                .Select(c => c.Assignment).Distinct().OrderBy(a => a))
                AddAssign(name, name);

            assignItem.Items.Add(new Separator());
            var custom = new MenuItem { Header = T("S.Assign.Custom") };
            custom.Click += (_, _) => AssignCustom(charVm);
            assignItem.Items.Add(custom);
        }

        // ── Échanger avec ────────────────────────────────────────────────────
        var swapItem = FindMenuItemByTag(menu, "swap");
        if (swapItem != null)
        {
            swapItem.Items.Clear();
            for (int i = 0; i < chars.Count; i++)
            {
                if (i == selfIdx) continue;
                var other = chars[i];
                var item = new MenuItem { Header = $"Slot {i + 1} – {other.Name}" };
                item.Click += (_, _) => _vm.ActiveTeamBuild?.SwapCharacters(charVm, other);
                swapItem.Items.Add(item);
            }
            swapItem.IsEnabled = chars.Count > 1;
        }

        // ── Déplacer vers ────────────────────────────────────────────────────
        var moveItem = FindMenuItemByTag(menu, "move");
        if (moveItem != null)
        {
            moveItem.Items.Clear();
            for (int i = 0; i < chars.Count; i++)
            {
                if (i == selfIdx) continue;
                var captI = i;
                var item = new MenuItem { Header = $"Position {i + 1}" };
                item.Click += (_, _) => _vm.ActiveTeamBuild?.MoveCharacter(charVm, captI);
                moveItem.Items.Add(item);
            }
            moveItem.IsEnabled = chars.Count > 1;
        }

        // ── Copier vers ──────────────────────────────────────────────────────
        var copyItem = FindMenuItemByTag(menu, "copy");
        if (copyItem != null)
        {
            bool canCopy = chars.Count < 12;
            copyItem.IsEnabled = canCopy;
            copyItem.Items.Clear();
            if (canCopy)
            {
                for (int i = 0; i < chars.Count; i++)
                {
                    var captI = i;
                    var header = i == selfIdx
                        ? string.Format(T("S.Ctx2.CopyAfterSelf"), i + 2)
                        : string.Format(T("S.Ctx2.CopyAfter"), i + 1, chars[i].Name);
                    var item = new MenuItem { Header = header };
                    item.Click += (_, _) => _vm.ActiveTeamBuild?.InsertCopyAt(charVm, captI);
                    copyItem.Items.Add(item);
                }
            }
        }
    }

    private void AssignCustom(CharacterSlotViewModel charVm)
    {
        var current = charVm.Assignment == "(unassigned)" ? "" : charVm.Assignment;
        var dialog = new RenameWindow(current) { Title = T("S.Dlg.AssignPlayer"), Owner = this };
        if (dialog.ShowDialog() == true)
            charVm.Assignment = string.IsNullOrWhiteSpace(dialog.NewName)
                ? "(unassigned)"
                : dialog.NewName;
    }

    private void CharacterProperties_Click(object sender, RoutedEventArgs e)
    {
        var charVm = GetCharFromLeftPanelMenu(sender);
        if (charVm == null) return;
        OpenCharacterProperties(charVm);
    }

    // Icône crayon (panneau gauche) → propriétés du personnage.
    private void EditCharacter_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CharacterSlotViewModel charVm) return;
        OpenCharacterProperties(charVm);
        e.Handled = true;
    }

    private void OpenCharacterProperties(CharacterSlotViewModel charVm)
    {
        var dialog = new CharacterPropertiesWindow(charVm) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            charVm.Name       = dialog.OutputName;
            charVm.Notes      = dialog.OutputNotes;
            charVm.Assignment = dialog.OutputAssignment;
            charVm.Equipment  = dialog.OutputEquipment;
        }
    }

    // Icône copier (panneau gauche) → modale de copie (code brut / format chat).
    private void CopyCharacter_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CharacterSlotViewModel charVm) return;
        e.Handled = true;

        var skillIdsByName = SkillVariants.TemplateIdsByName(_vm.SkillPanel.AllSkills);
        var build = new CharacterBuild
        {
            PrimaryProfession   = charVm.PrimaryProfession,
            SecondaryProfession = charVm.SecondaryProfession,
            Skills              = charVm.SkillSlots.Select(s => s.Skill).ToArray(),
            Attributes          = GwTemplateCodec.ToAttributeDict(charVm.Attributes),
        };
        var code = GwTemplateCodec.Encode(build, skillIdsByName);

        int index = (_vm.ActiveTeamBuild?.Characters.IndexOf(charVm) ?? -1) + 1;
        var chatCode = GwTemplateCodec.FormatChatCode(
            index, charVm.PrimaryProfession, charVm.SecondaryProfession, charVm.Name, code);

        new CopyTemplateWindow(code, chatCode) { Owner = this }.ShowDialog();
    }

    // Icône import (panneau gauche, builds vides).
    private void ImportTemplateIcon_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is CharacterSlotViewModel charVm)
            ImportTemplateInto(charVm);
    }

    // Cible d'une entrée de menu qui agit sur un perso : le Tag (menu contextuel du teambuild,
    // posé par PlacementTarget.Tag) ou, à défaut, le DataContext (menu de la page d'un onglet
    // Build, dont le ContextMenu prend le perso édité pour DataContext).
    private static CharacterSlotViewModel? ResolveMenuCharacter(object sender)
        => sender is not MenuItem mi
            ? null
            : mi.Tag as CharacterSlotViewModel ?? mi.DataContext as CharacterSlotViewModel;

    // Menus contextuels (ligne de perso du teambuild, page d'un onglet Build).
    private void ImportClipboardAny_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveMenuCharacter(sender) is { } charVm) ImportTemplateFromClipboard(charVm);
    }

    private void ImportFileAny_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveMenuCharacter(sender) is { } charVm) ImportTemplateFromFile(charVm);
    }

    private void ImportClipboardMenu_Click(object sender, RoutedEventArgs e)
        => ImportFromEditMenu(ImportTemplateFromClipboard);

    private void ImportFileMenu_Click(object sender, RoutedEventArgs e)
        => ImportFromEditMenu(ImportTemplateFromFile);

    // Édition ▸ imports : la cible dépend de la vue active.
    //   • onglet Build     → le perso édité (il n'y en a qu'un) ;
    //   • onglet teambuild → le 1er perso VIDE (celui qui porte justement l'icône d'import),
    //                        sinon une nouvelle ligne, retirée si l'import n'aboutit pas.
    private void ImportFromEditMenu(Func<CharacterSlotViewModel, bool> import)
    {
        if (_vm.IsBuildEditorActive && _vm.ActiveBuild is { } build)
        {
            import(build.Character);
            return;
        }

        if (!_vm.IsTeamBuildActive || _vm.ActiveTeamBuild is not { } team) return;

        if (team.Characters.FirstOrDefault(c => c.IsEmptyBuild) is { } empty)
        {
            import(empty);
            return;
        }

        if (team.Characters.Count >= 12)
        {
            MessageBox.Show(T("S.Msg.ImportNoFreeSlot"),
                T("S.Msg.ImportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        team.AddCharacterSlot();
        var added = team.Characters[^1];
        if (!import(added)) team.DeleteRow(added);
    }

    // Import depuis le presse-papier : code ou code chat, de compétences (O) et/ou d'équipement
    // (P). Action EXPLICITE → un presse-papier sans code se dit, il ne se devine pas.
    private bool ImportTemplateFromClipboard(CharacterSlotViewModel charVm)
    {
        if (TryApplyTemplateCodes(charVm, GwTemplateCodec.ExtractCodes(SafeClipboard.GetText())))
            return true;

        MessageBox.Show(T("S.Msg.ClipboardNotTemplate"),
            T("S.Msg.ImportTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    // Import depuis un fichier .txt de template (dossier des templates par défaut).
    private bool ImportTemplateFromFile(CharacterSlotViewModel charVm)
    {
        var dlg = new OpenFileDialog
        {
            Title  = T("S.Dlg.ImportAnyTpl"),
            Filter = T("S.Filter.AnyTpl"),
        };
        if (_vm.Browser.RootPath is { } root && Directory.Exists(root))
            dlg.InitialDirectory = root;

        if (dlg.ShowDialog() != true) return false;

        string content;
        try { content = File.ReadAllText(dlg.FileName); } catch { return false; }
        if (TryApplyTemplateCodes(charVm, GwTemplateCodec.ExtractCodes(content))) return true;

        MessageBox.Show(T("S.Msg.FileNotTemplate"),
            T("S.Msg.ImportTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    // Icône ⤓ : un seul geste, donc pas de choix à offrir — presse-papier en priorité (SANS
    // message si rien à y prendre), repli sur le fichier. Les menus, eux, exposent les deux
    // chemins séparément : un presse-papier chargé ne doit pas rendre le fichier inatteignable.
    private bool ImportTemplateInto(CharacterSlotViewModel charVm)
        => TryApplyTemplateCodes(charVm, GwTemplateCodec.ExtractCodes(SafeClipboard.GetText()))
           || ImportTemplateFromFile(charVm);

    // Édition : import et copie ne visent un perso que depuis un onglet Build ou teambuild.
    private void EditMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        bool onCharacter = _vm.IsBuildEditorActive || _vm.IsTeamBuildActive;
        ImportClipboardMenuItem.IsEnabled = onCharacter;
        ImportFileMenuItem.IsEnabled      = onCharacter;
        CopyTemplateMenuItem.IsEnabled    = _vm.IsBuildEditorActive;
        CopyChatCodeMenuItem.IsEnabled    = _vm.IsBuildEditorActive;
    }

    // Applique des codes GW1 (skill ET/OU équipement) au personnage. Les codes P multiples
    // (ligne .txt "un code par set") sont fusionnés : armure du premier, armes en F1, F2, ...
    // Retourne false si aucun code n'est reconnu — sans rien modifier.
    private bool TryApplyTemplateCodes(CharacterSlotViewModel charVm, IReadOnlyList<string> codes)
    {
        var skillsById = _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s);

        // 1re passe : décoder (le dernier code skills gagne, les codes P se cumulent en sets).
        CharacterBuild? skillBuild = null;
        var equips = new List<EquipmentBuild>();
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code)) continue;
            if (GwTemplateCodec.Decode(code, skillsById) is { } sb) skillBuild = sb;
            else if (GwEquipmentCodec.Decode(code) is { } eb)       equips.Add(eb);
        }
        if (skillBuild == null && equips.Count == 0) return false;

        // Le code skills change la PR sans apporter d'équipement → équipement existant d'une
        // autre profession = confirmation + vidage (sinon l'équipement importé remplace tout).
        if (skillBuild != null && equips.Count == 0
            && !ConfirmEquipmentReset(charVm, skillBuild.PrimaryProfession))
            return false;

        if (skillBuild != null)
        {
            charVm.PrimaryProfession   = skillBuild.PrimaryProfession;
            charVm.SecondaryProfession = skillBuild.SecondaryProfession;
            charVm.Attributes          = GwTemplateCodec.ToAttributesBuild(skillBuild.Attributes);
            for (int i = 0; i < 8; i++)
                charVm.SkillSlots[i].Skill = skillBuild.Skills[i];
        }

        if (equips.Count > 0)
            charVm.Equipment = equips.Count == 1 ? equips[0] : EquipmentBuild.Combine(equips);

        return true;
    }

    private void CharEquipment_Click(object sender, RoutedEventArgs e)
    {
        var charVm = GetCharFromLeftPanelMenu(sender);
        if (charVm == null) return;
        OpenEquipmentEditor(charVm);
    }

    private void EquipmentLabel_Click(object sender, MouseButtonEventArgs e)
    {
        var charVm = GetCharFromVisualParent(sender as DependencyObject);
        if (charVm == null) return;
        OpenEquipmentEditor(charVm);
        e.Handled = true;
    }

    private void OpenEquipmentEditor(CharacterSlotViewModel charVm)
    {
        var dialog = new EquipmentEditorWindow(charVm, EquipmentTemplatesDir()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            charVm.Equipment = dialog.OutputEquipment;
            charVm.Gender = dialog.OutputGender;
        }
        // "Sauvegarder comme template..." a pu écrire dans le dossier affiché par le browser.
        _vm.Browser.RefreshFiles();
    }

    private static CharacterSlotViewModel? GetCharFromVisualParent(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is FrameworkElement { Tag: CharacterSlotViewModel cvm })
                return cvm;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void AssignmentLabel_Click(object sender, MouseButtonEventArgs e)
    {
        var current = VisualTreeHelper.GetParent(sender as DependencyObject);
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.ContextMenu != null)
            {
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.IsOpen = true;
                e.Handled = true;
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void RemoveCharacterSlot_Click(object sender, RoutedEventArgs e)
    {
        var charVm = GetCharFromLeftPanelMenu(sender);
        if (charVm == null || _vm.ActiveTeamBuild == null) return;
        if (charVm.HasVariants)
        {
            var r = MessageBox.Show(
                T("S.Msg.RemoveCharVariants"),
                T("S.Msg.RemoveCharTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        _vm.ActiveTeamBuild.DeleteRow(charVm);
    }

    // ── Vidage du build ───────────────────────────────────────────────────────
    // Ligne de teambuild ET page d'un onglet Build (ResolveMenuCharacter couvre les deux).
    // Pas de confirmation : le vidage passe par les propriétés suivies, donc Ctrl+Z le défait
    // en UN SEUL pas (la rafale est coalescée par UndoManager).
    // ⚠ Le niveau BONUS (rune/coiffe/conso) est hors snapshot d'undo dans les deux onglets
    // (.pn3 comme ContentSignature ne le portent pas) : l'annulation rend les compétences et les
    // points de base, jamais le « +3 ». Philippe le sait — décision du 14/08/2026 de le vider
    // quand même, un bonus se repose d'un coup de molette.

    private void ClearCharSkills_Click(object sender, RoutedEventArgs e)
        => ResolveMenuCharacter(sender)?.ClearSkills();

    private void ClearCharAttributes_Click(object sender, RoutedEventArgs e)
        => ResolveMenuCharacter(sender)?.ClearAttributePoints();

    private void ClearCharBuild_Click(object sender, RoutedEventArgs e)
        => ResolveMenuCharacter(sender)?.ClearBuild();

    // ── Variantes ─────────────────────────────────────────────────────────────

    private void CreateVariant_Click(object sender, RoutedEventArgs e)
    {
        var charVm = GetCharFromLeftPanelMenu(sender);
        if (charVm != null) _vm.ActiveTeamBuild?.CreateVariant(charVm);
    }

    private void ToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CharacterSlotViewModel vm)
            _vm.ActiveTeamBuild?.ToggleExpanded(vm);
    }

    private void VariantUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CharacterSlotViewModel vm)
            _vm.ActiveTeamBuild?.MoveVariantUp(vm);
    }

    private void VariantDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CharacterSlotViewModel vm)
            _vm.ActiveTeamBuild?.MoveVariantDown(vm);
    }

    private void VariantSwap_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CharacterSlotViewModel vm)
            _vm.ActiveTeamBuild?.SwapWithParent(vm);
    }

    // ── Cadenas (tuples de variantes) ─────────────────────────────────────────

    private void LockVariants_Click(object sender, RoutedEventArgs e)
        => _vm.ActiveTeamBuild?.EnterLockSelectionMode();

    private void ConfirmLock_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild is not { } tb) return;
        bool wasSpike = tb.IsSelectingSpike;
        tb.ConfirmLockSelection();
        // Validation d'un roster spike passée (le mode s'est fermé) → (r)ouvrir la fenêtre.
        if (wasSpike && !tb.IsLockSelectionMode && tb.SpikeMembers.Count > 0)
            OpenSpikeWindow(tb);
    }

    // ── Spike damage calculus ─────────────────────────────────────────────────

    private Views.SpikeWindow? _spikeWindow;

    private void SpikeCalc_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild is not { } tb) return;
        if (tb.SpikeMembers.Count == 0) tb.EnterSpikeSelectionMode();
        else OpenSpikeWindow(tb);
    }

    // Fenêtre unique, non modale (le build reste éditable derrière : recalcul à chaud) ;
    // refaite si elle ciblait un autre build. Ré-appuyer sur ⚡ Spike alors qu'elle est déjà
    // ouverte pour ce build la ramène au premier plan (bouton "Teambuild" symétrique côté fenêtre
    // Spike, cf. SelectRosterRequested) au lieu d'en rouvrir une seconde.
    private void OpenSpikeWindow(TeamBuildViewModel tb)
    {
        if (_spikeWindow is { } open && open.Build == tb) { BringToFront(open); return; }
        _spikeWindow?.Close();
        // PAS d'Owner : une fenêtre owned reste TOUJOURS au-dessus de son owner (Z-order WPF),
        // ce qui empêcherait le bouton « Teambuild » de ramener la fenêtre principale devant.
        // On la traite en fenêtre sœur indépendante (entrée taskbar propre) et on récupère à la
        // main le cycle de vie (fermée avec l'onglet/l'app) et le centrage.
        var win = new Views.SpikeWindow(tb, _settings);
        win.SelectRosterRequested += (_, _) =>
        {
            _vm.ActiveTeamBuild = tb;   // l'onglet visé peut ne plus être l'onglet actif
            BringToFront(this);
            tb.EnterSpikeSelectionMode();
        };
        win.Closed += (_, _) => { if (_spikeWindow == win) _spikeWindow = null; };
        _spikeWindow = win;
        win.Show();
    }

    // Activate() seul ne garantit pas le passage au premier plan devant une autre fenêtre du
    // même process (Z-order) — bascule Topmost un instant pour le forcer, restaure si réduite.
    private static void BringToFront(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true;
        w.Topmost = false;
    }

    private void CancelLock_Click(object sender, RoutedEventArgs e)
        => _vm.ActiveTeamBuild?.CancelLockSelection();

    private void RemoveLock_Click(object sender, RoutedEventArgs e)
    {
        var lk = GetLockFromMenu(sender);
        if (lk != null) _vm.ActiveTeamBuild?.RemoveLock(lk);
    }

    private void EditLock_Click(object sender, RoutedEventArgs e)
    {
        var lk = GetLockFromMenu(sender);
        if (lk != null) _vm.ActiveTeamBuild?.EditLock(lk);
    }

    // ── Export d'un verrouillage (membres seuls) ──────────────────────────────

    private static string SafeFileName(string s) => string.Concat(s.Split(Path.GetInvalidFileNameChars()));

    // Vers le dossier Teambuild, format choisi via le sous-menu (Tag = "pn3"/"png"/"txt").
    private void ExportLock_Click(object sender, RoutedEventArgs e)
    {
        var lk = GetLockFromMenu(sender);
        var fmt = (sender as MenuItem)?.Tag as string;
        if (lk == null || fmt == null || _vm.ActiveTeamBuild == null) return;

        var folder = (_vm.Browser.RootPath is { } r && Directory.Exists(r))
            ? r : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(folder, SafeFileName(LockExportName(lk)) + "." + fmt);
        DoExportLock(lk, fmt, path);
    }

    // Nom par défaut d'un export de cadenas, dans la langue courante : sert de nom de FICHIER
    // (les 2 chemins d'export) et de nom du teambuild écrit dans un .pn3 de cadenas.
    private string LockExportName(VariantLockViewModel lk) =>
        string.Format(T("S.Lock.ExportName"), _vm.ActiveTeamBuild!.Name, lk.Index);

    // Choix du chemin ET du format par l'utilisateur.
    private void ExportLockAs_Click(object sender, RoutedEventArgs e)
    {
        var lk = GetLockFromMenu(sender);
        if (lk == null || _vm.ActiveTeamBuild == null) return;

        var dlg = new SaveFileDialog
        {
            Title    = T("S.Dlg.ExportLockAs"),
            Filter   = T("S.Filter.LockExport"),
            FileName = SafeFileName(LockExportName(lk)),
        };
        if (_vm.Browser.RootPath is { } r && Directory.Exists(r)) dlg.InitialDirectory = r;
        if (dlg.ShowDialog() != true) return;

        var fmt = Path.GetExtension(dlg.FileName).TrimStart('.').ToLowerInvariant();
        if (fmt is not ("pn3" or "png" or "txt" or "pwnd"))
            fmt = dlg.FilterIndex switch { 2 => "png", 3 => "txt", 4 => "pwnd", _ => "pn3" };
        DoExportLock(lk, fmt, dlg.FileName);
    }

    private void DoExportLock(VariantLockViewModel lk, string fmt, string path)
    {
        try
        {
            switch (fmt)
            {
                case "pn3":  ExportLockPn3(lk, path); break;
                case "txt":  ExportLockTxt(lk, path); break;
                case "png":  ExportLockPng(lk, path); break;
                case "pwnd": ExportLockPwnd(lk, path); break;
                default: return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExportLock] {fmt} '{path}': {ex}");
            MessageBox.Show(string.Format(T("S.Msg.ExportFailed"), ex.Message), T("S.Msg.LockExportTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(string.Format(T("S.Msg.LockExported"), path), T("S.Msg.LockExportTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // .pn3 : un team build des seuls membres (variantes aplaties).
    private void ExportLockPn3(VariantLockViewModel lk, string path)
    {
        var model = new TeamBuild
        {
            Name = LockExportName(lk),
            Characters = lk.Members.Select(m => { var cb = CharToModel(m); cb.Variants.Clear(); return cb; }).ToList(),
        };
        TeamBuildSerializer.Save(model, path);
    }

    // .txt : une ligne par membre, format "[N prof - nom;codeO]" (+ ";codeP" si équipement).
    private void ExportLockTxt(VariantLockViewModel lk, string path)
    {
        var skillIdsByName = SkillVariants.TemplateIdsByName(_vm.SkillPanel.AllSkills);
        var lines = new List<string>();
        int n = 1;
        foreach (var m in lk.Members)
        {
            var build = new CharacterBuild
            {
                PrimaryProfession   = m.PrimaryProfession,
                SecondaryProfession = m.SecondaryProfession,
                Skills              = m.SkillSlots.Select(s => s.Skill).ToArray(),
                Attributes          = GwTemplateCodec.ToAttributeDict(m.Attributes),
            };
            var profile = m.PrimaryProfession.Profile(m.SecondaryProfession);
            var name = string.IsNullOrWhiteSpace(m.Name) || m.Name == "(unnamed)" ? T("S.Misc.UnnamedChar") : m.Name;
            var line = $"[{n} {profile} - {name};{GwTemplateCodec.Encode(build, skillIdsByName)}";
            if (m.Equipment is { IsEmpty: false } eq)
                foreach (var k in eq.ExportSets()) // un code P par set
                    line += $";{GwEquipmentCodec.Encode(eq, k)}";
            lines.Add(line + "]");
            n++;
        }
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
    }

    // .pwnd : les seuls membres, variantes aplaties. Le format ne porte que professions +
    // attributs + 8 compétences — noms, équipement et notes ne suivent pas (cf. PwndExporter).
    private void ExportLockPwnd(VariantLockViewModel lk, string path)
        => PwndExporter.Save(lk.Members.Select(CharToModel).ToList(), path,
                             SkillVariants.TemplateIdsByName(_vm.SkillPanel.AllSkills));

    // .png : filtre la vue sur ce cadenas et rend la grille (taille de contenu complète via la ScrollViewer Auto).
    private void ExportLockPng(VariantLockViewModel lk, string path)
    {
        var prev = _vm.ActiveTeamBuild!.ActiveLockFilter;
        _vm.ActiveTeamBuild.SetLockFilter(lk);
        try
        {
            CharacterGrid.UpdateLayout();
            var bg = Application.Current.TryFindResource("ContentBackgroundBrush") as Brush ?? Brushes.White;
            if (VisualCapture.Render(CharacterGrid, bg) is not { } rtb)
                throw new InvalidOperationException(T("S.Msg.NothingToRender"));

            VisualCapture.SavePng(rtb, path);
        }
        finally
        {
            _vm.ActiveTeamBuild.SetLockFilter(prev);
        }
    }

    private void FilterByLock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is VariantLockViewModel lk)
            _vm.ActiveTeamBuild?.SetLockFilter(lk);
    }

    private void ShowAllLocks_Click(object sender, RoutedEventArgs e)
        => _vm.ActiveTeamBuild?.ClearLockFilter();

    private VariantLockViewModel? GetLockFromMenu(object sender)
    {
        DependencyObject? current = sender as DependencyObject;
        while (current != null)
        {
            if (current is ContextMenu cm)
                return (cm.PlacementTarget as FrameworkElement)?.Tag as VariantLockViewModel;
            current = LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    // ── Mapping VM ↔ Model (.pn3) ─────────────────────────────────────────────

    private static TeamBuildViewModel ModelToViewModel(TeamBuild model)
    {
        var vm = new TeamBuildViewModel { Id = model.Id, CreatedAt = model.CreatedAt };
        PopulateViewModel(vm, model);
        return vm;
    }

    // Peuple Name/Tags/Characters/Locks depuis le modèle. Partagé entre la construction
    // initiale (ModelToViewModel) et la restauration d'un snapshot d'undo (RestoreTeamBuild).
    private static void PopulateViewModel(TeamBuildViewModel vm, TeamBuild model)
    {
        vm.Name = model.Name;
        vm.ActiveFlux = model.ActiveFlux;
        vm.NatureRituals.LoadFromSkillIds(model.ActiveNatureRituals);
        vm.NatureRituals.LoadRoaringWindsRank(model.RoaringWindsRitualRank);
        vm.NatureRituals.LoadTranquilityRank(model.TranquilityRitualRank);
        vm.VampiricHits3 = Math.Clamp(model.VampiricHits3, 0, 25);
        vm.VampiricHits5 = Math.Clamp(model.VampiricHits5, 0, 25);
        vm.Tags.Clear();
        foreach (var tag in model.Tags) vm.Tags.Add(tag);

        vm.Characters.Clear();
        foreach (var c in model.Characters)
            vm.Characters.Add(CharToVm(c));

        // Cadenas : résolus une fois l'arbre construit (MemberIds → lignes par Id).
        vm.Locks.Clear();
        var byId = vm.EnumerateTree().GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var lm in model.Locks)
        {
            var lk = new VariantLockViewModel(lm.Index, string.IsNullOrEmpty(lm.Color) ? "#888888" : lm.Color);
            foreach (var id in lm.MemberIds)
                if (byId.TryGetValue(id, out var member)) lk.Members.Add(member);
            if (lk.Members.Count > 0) vm.Locks.Add(lk);
        }

        // Roster spike (v6) : mêmes références que l'arbre, cadres verts recochés par slot.
        vm.SpikeMembers.Clear();
        foreach (var sm in model.Spike)
        {
            if (vm.SpikeMembers.Count >= TeamBuildViewModel.MaxSpikeMembers) break;
            if (!byId.TryGetValue(sm.CharacterId, out var member)) continue;
            vm.SpikeMembers.Add(member);
            foreach (var sk in sm.Skills)
            {
                var slot = member.SkillSlots[sk.Slot];
                slot.IsSpikeSelected = true;
                slot.SpikeWeaponDamageType = sk.WeaponDamageType ?? string.Empty;
                slot.SpikeTicks = Math.Clamp(sk.Ticks, 1, 30);
                slot.SpikeOrder = sk.Order;
                slot.SpikeWeaponKind = sk.WeaponKind ?? string.Empty;
                slot.SpikeProcs = Math.Clamp(sk.Procs, 0, 25);
                slot.SpikeConditional = sk.Conditional;
                slot.SpikeThreshold = Math.Clamp(sk.Threshold, -1, 99);
                slot.SpikeCasterCurrentHp = Math.Clamp(sk.CasterCurrentHp, 1, 2000);
                slot.SpikeCasterMaxHp = Math.Clamp(sk.CasterMaxHp, 1, 2000);
                // Mods d'arme (v18) : le mod AVANT la case du proc — son setter la remet à false.
                slot.SpikeWeaponModKey = sk.WeaponMod ?? string.Empty;
                slot.SpikeSunderingProc = sk.SunderingProc;
                slot.SpikeHornbow = sk.Hornbow;
            }
            // Buffs d'arme actifs du membre (v11) — instances fraîches, pas de purge préalable.
            foreach (var key in sm.Buffs) member.SetSpikeBuff(key, true);
        }
        // Normalise l'ordre de cast en 1..N (contigu ; anciens fichiers sans Order → ordre d'itération).
        vm.CompactSpikeOrder();
    }

    // Repeuple un TeamBuildViewModel existant depuis un snapshot d'undo SANS changer d'instance :
    // l'onglet, son activation et FilePath/SourcePath survivent à la restauration.
    private static void RestoreTeamBuild(TeamBuildViewModel tb, TeamBuild model)
    {
        tb.BeginRestore();
        try { PopulateViewModel(tb, model); }
        finally { tb.EndRestore(); }
    }

    // Construit récursivement un CharacterSlotViewModel (+ son sous-arbre de variantes) depuis le modèle.
    private static CharacterSlotViewModel CharToVm(CharacterBuild c)
    {
        var charVm = new CharacterSlotViewModel
        {
            Id                  = c.Id,
            Name                = c.Name,
            Notes               = c.Notes,
            PrimaryProfession   = c.PrimaryProfession,
            SecondaryProfession = c.SecondaryProfession,
            IsFavorite          = c.IsFavorite,
            Assignment          = c.Assignment,
            Gender              = c.Gender,
            Equipment           = c.Equipment,
            DurationBoostersEnabled = c.DurationBoostersEnabled,
        };

        var attrs = GwTemplateCodec.ToAttributesBuild(c.Attributes);
        attrs.TitleRanks = new Dictionary<string, int>(c.TitleRanks);
        charVm.Attributes = attrs.Allocations.Count > 0 || attrs.TitleRanks.Count > 0 ? attrs : null;

        for (int i = 0; i < 8; i++)
            charVm.SkillSlots[i].Skill = c.Skills[i];

        // Boosts d'attribut actifs (v16) — instances fraîches, pas de purge préalable.
        foreach (var id in c.ActiveAttributeBoosts) charVm.SetAttributeBoost(id, true);

        foreach (var v in c.Variants)
            charVm.Variants.Add(CharToVm(v));   // Parent/Depth câblés ensuite par RefreshTree

        return charVm;
    }

    private static TeamBuild ViewModelToModel(TeamBuildViewModel vm) => new()
    {
        Id        = vm.Id,
        Name      = vm.Name,
        Tags      = vm.Tags.ToList(),
        CreatedAt = vm.CreatedAt,
        UpdatedAt = DateTime.UtcNow,
        ActiveFlux = vm.ActiveFlux,
        ActiveNatureRituals = vm.NatureRituals.ToSkillIds(),
        RoaringWindsRitualRank = vm.NatureRituals.RoaringWindsRank,
        TranquilityRitualRank = vm.NatureRituals.TranquilityRank,
        VampiricHits3 = vm.VampiricHits3,
        VampiricHits5 = vm.VampiricHits5,
        Characters = vm.Characters.Select(CharToModel).ToList(),
        Locks = vm.Locks.Select(l => new VariantLock
        {
            Index = l.Index,
            Color = l.ColorHex,
            MemberIds = l.Members.Select(m => m.Id).ToList(),
        }).ToList(),
        Spike = vm.SpikeMembers.Select(m => new SpikeMember
        {
            CharacterId = m.Id,
            Skills = m.SkillSlots.Where(s => s.IsSpikeSelected).Select(s => new SpikeSkill
            {
                Slot = s.SlotIndex,
                WeaponDamageType = string.IsNullOrEmpty(s.SpikeWeaponDamageType) ? null : s.SpikeWeaponDamageType,
                Ticks = s.SpikeTicks,
                Order = s.SpikeOrder,
                WeaponKind = string.IsNullOrEmpty(s.SpikeWeaponKind) ? null : s.SpikeWeaponKind,
                Procs = s.SpikeProcs,
                Conditional = s.SpikeConditional,
                Threshold = s.SpikeThreshold,
                CasterCurrentHp = s.SpikeCasterCurrentHp,
                CasterMaxHp = s.SpikeCasterMaxHp,
                WeaponMod = string.IsNullOrEmpty(s.SpikeWeaponModKey) ? null : s.SpikeWeaponModKey,
                SunderingProc = s.SpikeSunderingProc,
                Hornbow = s.SpikeHornbow,
            }).ToList(),
            // Ordre stable (HashSet non déterministe) : les snapshots d'undo comparent le JSON.
            Buffs = m.SpikeActiveBuffs.OrderBy(k => k, StringComparer.Ordinal).ToList(),
        }).ToList(),
    };

    // Sérialise récursivement un CharacterSlotViewModel (+ ses variantes) vers le modèle.
    private static CharacterBuild CharToModel(CharacterSlotViewModel c) => new()
    {
        Id                  = c.Id,
        Name                = c.Name,
        Notes               = c.Notes,
        PrimaryProfession   = c.PrimaryProfession,
        SecondaryProfession = c.SecondaryProfession,
        IsFavorite          = c.IsFavorite,
        Assignment          = c.Assignment,
        Gender              = c.Gender,
        Skills              = c.SkillSlots.Select(s => s.Skill).ToArray(),
        Attributes          = GwTemplateCodec.ToAttributeDict(c.Attributes),
        TitleRanks          = c.Attributes is null ? new() : new Dictionary<string, int>(c.Attributes.TitleRanks),
        Equipment           = c.Equipment,
        DurationBoostersEnabled = c.DurationBoostersEnabled,
        // Ordre stable (HashSet non déterministe) : les snapshots d'undo comparent le JSON.
        ActiveAttributeBoosts = c.ActiveAttributeBoosts.OrderBy(id => id).ToList(),
        Variants            = c.Variants.Select(CharToModel).ToList(),
    };

    // ── Attribute editor ──────────────────────────────────────────────────────

    private void AttrDecrement_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AttributeRowViewModel row)
            row.Decrement();
    }

    private void AttrIncrement_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AttributeRowViewModel row)
            row.Increment();
    }

    // Spinner de niveau bonus — cadre d'attributs de l'éditeur de build uniquement (rune, coiffe,
    // consommables : hors budget). AdjustBonus borne à EffectiveMaxBonus, donc sans effet là où le
    // bonus est interdit (rang de titre, carac SEC sous filtre PvP) : pas de garde à ajouter ici.
    private void AttrBonusDecrement_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AttributeRowViewModel row)
            row.AdjustBonus(-1);
    }

    private void AttrBonusIncrement_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AttributeRowViewModel row)
            row.AdjustBonus(1);
    }

    // Bascule ≥ / ≤ d'un seuil de recherche (grille de seuils uniquement).
    private void AttrToggleComparison_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AttributeRowViewModel row)
            row.ToggleComparison();
    }

    // ── Molette : défiler ou régler ? ─────────────────────────────────────────────────────
    // Dans la grille du teambuild, le contenu défile SOUS un curseur immobile : après un cran,
    // une compétence se retrouve souvent sous la souris et le cran suivant réglerait sa
    // caractéristique sans qu'on l'ait demandé. Le PREMIER cran d'une salve tranche donc pour
    // toute la salve, tant que les crans s'enchaînent à moins de WheelGestureMs d'intervalle.
    // Symétriquement, régler un niveau jusqu'à 0 puis dépasser d'UN cran ne doit pas faire
    // décrocher la grille : un réglage effectif ferme la molette au défilement pour
    // WheelGestureMs (cf. SettleWheel).
    //
    // ⚠ Les deux verrous ne peuvent PAS partager le même état. WPF ne promeut PreviewMouseWheel en
    // MouseWheel que si le tunnel n'a pas traité le cran (MouseDevice.PostProcessInput) : un cran
    // converti en réglage n'atteint donc JAMAIS la remontée ci-dessous. La salve ne s'y acquiert
    // qu'au défilement ; le verrou inverse doit vivre sur son propre horodatage.
    private const int WheelGestureMs = 400;
    private int  _wheelStamp = -1;   // horodatage du cran en cours de traversée de la grille
    private int  _wheelTicks;        // date du dernier cran vu dans la grille (fin de salve)
    private bool _wheelScrolls;      // la salve en cours est acquise au défilement
    private long _wheelAdjustTicks = long.MinValue / 2;  // date du dernier cran qui a VRAIMENT réglé

    // Tunnel du ScrollViewer : il précède les slots et les lignes d'attributs, et voit donc AUSSI
    // les crans qu'aucun d'eux ne recevra (curseur sur une marge, sur le panneau du personnage…).
    private void TeamScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _wheelStamp = e.Timestamp;
        if (Environment.TickCount - _wheelTicks >= WheelGestureMs) _wheelScrolls = false;
        _wheelTicks = Environment.TickCount;
    }

    // Remontée sur le même ScrollViewer, posée en handledEventsToo (cf. constructeur) : le
    // défilement natif marque l'événement traité avant nous. Seuls les crans que personne n'a
    // convertis en réglage arrivent ici (cf. la promotion conditionnelle plus haut) : celui-là
    // fixe la salve en défilement, et les compétences qui passeront ensuite sous la souris ne
    // seront plus touchées.
    private void TeamScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Timestamp == _wheelStamp) _wheelScrolls = true;
    }

    // Garde en tête de tout handler de molette qui modifie une caractéristique. Hors grille du
    // teambuild (éditeur de build, grille de recherche) le tunnel n'a pas horodaté ce cran :
    // le comportement d'origine y est conservé tel quel.
    private bool WheelMayAdjust(MouseWheelEventArgs e, object sender)
    {
        if (e.Timestamp != _wheelStamp) return true;
        if (_wheelScrolls) return false;
        return !_vm.WheelNeedsSelection || IsArmedCard(sender);
    }

    // Carte « armée » = le personnage désigné par le dernier clic (cf. SelectForWheel). Le focus
    // clavier a été essayé d'abord et ne convient pas : il n'atterrit pas dans la carte selon
    // l'endroit cliqué, la molette ne réglait alors plus jamais rien.
    private bool IsArmedCard(object sender) =>
        FindCharacterVm(sender as DependencyObject) is { } card
        && ReferenceEquals(card, _vm.ActiveTeamBuild?.WheelSelection);

    // Applique un réglage et dit s'il a changé quelque chose : un cran sans effet (plafond ou
    // plancher déjà atteint) doit repartir au défilement plutôt que d'être avalé en silence.
    private static bool Changes(AttributeRowViewModel row, Action<AttributeRowViewModel> adjust)
    {
        int points = row.Points, bonus = row.BonusPoints;
        adjust(row);
        return row.Points != points || row.BonusPoints != bonus;
    }

    // Verdict d'un cran arrivé sur un slot ou une ligne d'attribut, une fois le réglage tenté.
    //
    // Cran effectif → il consomme la molette et (r)ouvre une fenêtre de WheelGestureMs.
    // Cran sans effet (0 atteint, plafond, skill sans caractéristique) → il dépend de cette fenêtre :
    //   • encore ouverte : on vient de régler et la main est toujours sur la grille — l'avaler.
    //     C'est le cran de trop après être descendu à 0, qui sinon fait décrocher la grille.
    //   • fermée : la main insiste sur un cran qui ne fait rien, elle veut défiler. Le laisser
    //     passer ET acquérir la salve au défilement, sans quoi le contenu glisserait sous le
    //     curseur et la compétence amenée là se ferait régler au cran suivant.
    // Hors grille du teambuild (éditeur de build, grille de seuils), rien à arbitrer.
    private void SettleWheel(MouseWheelEventArgs e, bool changed)
    {
        if (changed)
        {
            e.Handled = true;
            _wheelAdjustTicks = Environment.TickCount64;
            return;
        }
        if (e.Timestamp != _wheelStamp) return;

        if (Environment.TickCount64 - _wheelAdjustTicks < WheelGestureMs) e.Handled = true;
        else _wheelScrolls = true;
    }

    // Clic n'importe où dans la carte : ce personnage devient celui que la molette peut régler.
    // En tunnel (Preview) — les boutons et icônes du panneau consomment le clic avant la remontée,
    // cliquer dessus doit malgré tout sélectionner le personnage.
    private void CharacterCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CharacterSlotViewModel character)
            _vm.ActiveTeamBuild?.SelectForWheel(character);
    }

    private void AttrRow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!WheelMayAdjust(e, sender)) return;
        SettleWheel(e, ((FrameworkElement)sender).Tag is AttributeRowViewModel row
                       && Changes(row, r => r.Adjust(e.Delta > 0 ? 1 : -1)));
    }

    // Idem, cadre d'attributs de l'éditeur de build : molette nue = niveau de base, Shift+molette =
    // niveau bonus (même convention que sur les slots de compétence). Handler distinct
    // d'AttrRow_MouseWheel : ailleurs la molette ne règle que la base, Shift compris — comportement
    // du teambuild et de la grille de seuils inchangé (demande Philippe : « dans build uniquement »).
    private void EditorAttrRow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!WheelMayAdjust(e, sender)) return;
        if (((FrameworkElement)sender).Tag is not AttributeRowViewModel row) return;

        int  delta = e.Delta > 0 ? 1 : -1;
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        SettleWheel(e, Changes(row, r => { if (shift) r.AdjustBonus(delta); else r.Adjust(delta); }));
    }

    private void SkillSlot_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!WheelMayAdjust(e, sender)) return;
        SkillSlotWheelAdjust(sender, e);
        SettleWheel(e, e.Handled);
    }

    private void SkillSlotWheelAdjust(object sender, MouseWheelEventArgs e)
    {
        // La molette ajuste le niveau de caractéristique même quand le panneau d'attributs est replié.
        var character = FindCharacterVm(sender as DependencyObject);
        if (character == null) return;
        if ((sender as FrameworkElement)?.DataContext is not SkillSlotViewModel slot) return;

        SuppressSlotTooltipDuringWheel(sender);

        int  delta = e.Delta > 0 ? 1 : -1;
        bool ctrl  = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        bool alt   = Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt);

        if (alt)
        {
            // Alt : saute au hard breakpoint suivant/précédent de la caractéristique de la skill
            // survolée. Alt seul = niveau de base (0–12) ; Alt+Shift = niveau effectif (base+bonus).
            // Rang de titre = niveau 0–10, sans mode effectif (pas de bonus).
            e.Handled = HandleBreakpointWheel(character, slot, delta, shift);
            return;
        }

        if (ctrl && shift)
        {
            // Ctrl+Shift : bonus du niveau de l'attribut primaire
            var row = character.PrimaryAttributeRows.FirstOrDefault(r => r.IsPrimary);
            if (row != null && Changes(row, r => r.AdjustBonus(delta))) e.Handled = true;
        }
        else if (ctrl)
        {
            // Ctrl : niveau de l'attribut primaire
            var row = character.PrimaryAttributeRows.FirstOrDefault(r => r.IsPrimary);
            if (row != null && Changes(row, r => AdjustLevelWithOverflow(r, delta))) e.Handled = true;
        }
        else if (shift && slot.Skill?.Attribute is string attrNameBonus)
        {
            // Shift : bonus du niveau de la caractéristique associée à la skill survolée
            // (rang de titre = MaxBonus 0 → no-op, cohérent : pas de rune sur un titre).
            var row = character.FindAttributeRow(attrNameBonus);
            if (row != null && Changes(row, r => r.AdjustBonus(delta))) e.Handled = true;
        }
        else if (slot.Skill?.Attribute is string attrName)
        {
            // Sans modificateur : niveau de la caractéristique associée à la skill survolée
            var row = character.FindAttributeRow(attrName);
            if (row != null && Changes(row, r => AdjustLevelWithOverflow(r, delta))) e.Handled = true;
        }
    }

    // Ferme et bloque l'infobulle du slot survolé pendant la molette d'attribut (elle recouvrirait
    // les niveaux de caractéristiques qu'on modifie, et son recalcul « Dégâts selon l'armure » à
    // chaque cran est coûteux). Réactivée 500 ms après le dernier cran → réapparaît à jour au survol.
    private void SuppressSlotTooltipDuringWheel(object? sender)
    {
        if (sender is not DependencyObject slot) return;
        if (!ReferenceEquals(_suppressedTooltipSlot, slot))
        {
            if (_suppressedTooltipSlot != null) ToolTipService.SetIsEnabled(_suppressedTooltipSlot, true);
            _suppressedTooltipSlot = slot;
            ToolTipService.SetIsEnabled(slot, false);   // ferme l'infobulle ouverte + bloque la réouverture
        }
        _attrWheelTooltipTimer.Stop();
        _attrWheelTooltipTimer.Start();
    }

    // Alt+molette : aligne le niveau de la caractéristique de la skill sur son hard breakpoint
    // suivant (delta>0) ou précédent (delta<0). Sans breakpoint exploitable → ne fait rien.
    private static bool HandleBreakpointWheel(CharacterSlotViewModel character, SkillSlotViewModel slot, int delta, bool shift)
    {
        if (slot.Skill is not { } skill || skill.Attribute is not string attr) return false;
        var row = character.FindAttributeRow(attr);
        if (row is null) return false;

        bool isTitle = GwAttributeData.IsTitleRank(attr);
        bool effective = shift && !isTitle;              // Alt+Shift = niveau effectif (hors titres)
        // Titre : cap dérivé des données (10 EotN/Sunspear, 12 Allegiance). Attribut : 12 base / 20 effectif.
        int snapMax = isTitle ? SkillBreakpoints.RankMax(skill.Progression) : (effective ? 20 : 12);

        var bps = SkillBreakpoints.Compute(skill.Progression, snapMax, BreakpointOverrides.For(skill.Id));
        if (bps.Count == 0) return false;

        int current = effective ? row.EffectiveLevel : row.Points;
        if (SkillBreakpoints.Snap(bps, current, delta) is not int target) return false;

        return Changes(row, r =>
        {
            if (effective) SetEffectiveLevel(r, target);
            else           r.Points = target;           // setter borne à [0, MaxPoints]
        });
    }

    // Pose un niveau effectif visé en remplissant d'abord la base puis le bonus (modèle de débordement).
    private static void SetEffectiveLevel(AttributeRowViewModel row, int level)
    {
        row.Points      = Math.Min(level, row.MaxPoints);
        row.BonusPoints = Math.Max(0, level - row.MaxPoints);
    }

    // Molette sur le niveau de base : déborde sur le niveau bonus aux extrêmes.
    // Montée : base 0→Max puis bonus (Max+1, Max+2…). Descente : base Max→0 puis bonus.
    // Pas de débordement quand le bonus est interdit (EffectiveMaxBonus 0) : rang de titre
    // (pas de rune sur un titre) ou caractéristique SEC sous filtre PvP.
    private static void AdjustLevelWithOverflow(AttributeRowViewModel row, int delta)
    {
        if (delta > 0)
        {
            if (row.Points < row.MaxPoints)   row.Adjust(1);
            else if (row.EffectiveMaxBonus > 0) row.AdjustBonus(1);
        }
        else if (delta < 0)
        {
            if (row.Points > 0)               row.Adjust(-1);
            else if (row.EffectiveMaxBonus > 0) row.AdjustBonus(-1);
        }
    }

    private static CharacterSlotViewModel? FindCharacterVm(DependencyObject? element)
    {
        var current = element is null ? null : VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            if (current is FrameworkElement { DataContext: CharacterSlotViewModel vm })
                return vm;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Les DEUX sortes d'onglets : un build simple modifié se perdait silencieusement ici.
        var dirty      = _vm.OpenTeamBuilds.Where(tb => tb.IsDirty).ToList();
        var dirtyBuild = _vm.OpenBuilds.Where(b => b.IsDirty).ToList();
        if (dirty.Count > 0 || dirtyBuild.Count > 0)
        {
            var names = string.Join("\n",
                dirty.Select(tb => $"  • {tb.Name}")
                     .Concat(dirtyBuild.Select(b => $"  • {b.Title}")));
            var result = MessageBox.Show(
                string.Format(T("S.Msg.UnsavedBuilds"), names),
                T("S.Msg.UnsavedTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
            else if (result == MessageBoxResult.Yes)
            {
                foreach (var tb in dirty)
                {
                    if (!SaveTeamBuild(tb))
                    {
                        e.Cancel = true;
                        break;
                    }
                }
                // Un build simple sans fichier d'origine ouvre un SaveFileDialog : annuler
                // l'un d'eux annule la fermeture, comme pour les team builds.
                if (!e.Cancel)
                    foreach (var b in dirtyBuild)
                    {
                        if (!SaveBuildTab(b))
                        {
                            e.Cancel = true;
                            break;
                        }
                    }
            }
        }

        if (!e.Cancel)
        {
            SaveWindowBounds();
            base.OnClosing(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // La fenêtre Spike n'est plus owned : la fermer explicitement, sinon (ShutdownMode
        // OnLastWindowClose) elle maintiendrait le process en vie après fermeture du teambuild.
        _spikeWindow?.Close();
        _db.Dispose();
        base.OnClosed(e);
    }
}
