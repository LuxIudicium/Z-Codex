using System.IO;
using System.Windows;
using System.Windows.Controls;
using ZCodex.Core.Sync;

namespace ZCodex.App.Views;

/// <summary>
/// Envoi de toute une bibliothèque sur GWRank.
///
/// La fenêtre ne connaît ni le réseau ni l'index : elle reçoit deux délégués, l'un qui analyse,
/// l'autre qui dépose. Elle ne décide que d'une chose — ce que l'utilisateur voit avant de
/// s'engager, et c'est là tout l'enjeu à cette échelle.
///
/// Cocher un dossier coche TOUT ce qu'il contient ; l'utilisateur le déplie ensuite pour écarter
/// ce qu'il ne veut pas. Un dossier n'est donc jamais un « tout ou rien ».
/// </summary>
public partial class GwRankBulkWindow : Window
{
    private static string T(string key) => LanguageManager.T(key);

    /// <summary>Au-delà, on ne détaille pas : construire des milliers de cases figerait la
    /// fenêtre, et un dossier de cette taille se décoche en entier, pas fichier par fichier.
    /// (Mesuré sur la bibliothèque de référence : 2 322 fichiers dans un seul pack.)</summary>
    private const int DetailLimit = 400;

    private sealed class FolderRow
    {
        public required string Path { get; init; }
        public required CheckBox Box { get; init; }
        public required Button Toggle { get; init; }
        public required StackPanel FileHost { get; init; }
        public required int Count { get; init; }
        public List<FileRow>? Files { get; set; }
    }

    private sealed class FileRow
    {
        public required string Path { get; init; }
        public required CheckBox Box { get; init; }
        public required TextBlock Label { get; init; }
        public required string BaseText { get; init; }
    }

    private readonly Func<IReadOnlyList<string>, Task<List<GwRankBulkItem>>> _analyze;
    private readonly Func<IReadOnlyList<GwRankBulkItem>, IProgress<GwRankBulkItem>,
                          CancellationToken, Task<GwRankBulkReport>> _upload;

    private readonly List<FolderRow> _folders = [];
    /// <summary>Fichiers explicitement DÉcochés sous un dossier coché. On retient les exceptions,
    /// pas la sélection : elle se compte en dizaines quand la sélection se compte en milliers.</summary>
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    private List<GwRankBulkItem> _items = [];
    private HashSet<string> _analyzed = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private bool _sending;
    private bool _suspend;

    /// <summary>Dossiers cochés, même partiellement, à retenir pour la prochaine fois.</summary>
    public List<string> SelectedFolders =>
        _folders.Where(f => f.Box.IsChecked != false).Select(f => f.Path).ToList();

    /// <summary>Fichiers écartés à la main, à retenir eux aussi : les redécocher à chaque envoi
    /// serait le plus sûr moyen d'en oublier un.</summary>
    public List<string> ExcludedFiles => _excluded.ToList();

    /// <summary>Vrai si au moins un build a été déposé : l'appelant doit alors rafraîchir le miroir.</summary>
    public bool AnythingSent { get; private set; }

    public GwRankBulkWindow(string root,
                            IReadOnlyCollection<string> rememberedFolders,
                            IReadOnlyCollection<string> rememberedExclusions,
                            Func<IReadOnlyList<string>, Task<List<GwRankBulkItem>>> analyze,
                            Func<IReadOnlyList<GwRankBulkItem>, IProgress<GwRankBulkItem>,
                                 CancellationToken, Task<GwRankBulkReport>> upload)
    {
        InitializeComponent();
        _analyze = analyze;
        _upload = upload;
        foreach (var x in rememberedExclusions) _excluded.Add(x);
        BuildFolderList(root, rememberedFolders);
    }

    // ── Arborescence de sélection ─────────────────────────────────────────────

    private void BuildFolderList(string root, IReadOnlyCollection<string> remembered)
    {
        var known = new HashSet<string>(remembered, StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Subfolders(root))
        {
            int count = CountBuilds(dir);
            var host = new StackPanel { Margin = new Thickness(26, 0, 0, 4),
                                        Visibility = Visibility.Collapsed };

            var toggle = new Button
            {
                Content = "▸",                        // ▸
                Width = 20, Padding = new Thickness(0), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = count > 0,
            };

            var box = new CheckBox
            {
                IsChecked = known.Contains(dir),
                IsEnabled = count > 0,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Content = new TextBlock
                {
                    Text = $"{Path.GetFileName(dir)}   —   {string.Format(T("S.GwRank.BulkFolderCount"), count)}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = FontWeights.SemiBold,
                },
            };

            var row = new FolderRow { Path = dir, Box = box, Toggle = toggle,
                                      FileHost = host, Count = count };
            toggle.Click += (_, _) => ToggleFolder(row);
            box.Checked += (_, _) => FolderToggled(row, true);
            box.Unchecked += (_, _) => FolderToggled(row, false);

            var header = new StackPanel { Orientation = Orientation.Horizontal,
                                          Margin = new Thickness(0, 0, 0, 3) };
            header.Children.Add(toggle);
            header.Children.Add(box);

            _folders.Add(row);
            FolderList.Children.Add(header);
            FolderList.Children.Add(host);
        }

        if (_folders.Count == 0)
            FolderList.Children.Add(new TextBlock
            {
                Text = T("S.GwRank.BulkNoFolderFound"),
                TextWrapping = TextWrapping.Wrap, FontSize = 11,
            });
        else
            RefreshFolderStates();
    }

    private static IEnumerable<string> Subfolders(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        try
        {
            return Directory.GetDirectories(root)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase);
        }
        catch (Exception) { return []; }
    }

    /// <summary>Compte sans rien lire : on énumère des noms de fichiers, pas des documents.</summary>
    private static int CountBuilds(string folder)
    {
        try { return GwRankBulkUpload.EnumerateBuilds(folder).Count(); }
        catch (Exception) { return 0; }
    }

    private void ToggleFolder(FolderRow row)
    {
        bool opening = row.FileHost.Visibility != Visibility.Visible;
        if (opening && row.Files is null) MaterializeFiles(row);

        row.FileHost.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        row.Toggle.Content = opening ? "▾" : "▸";     // ▾ / ▸
        SizeToContent = SizeToContent.Height;
    }

    /// <summary>Construit les cases des fichiers, à la première ouverture seulement.</summary>
    private void MaterializeFiles(FolderRow row)
    {
        row.Files = [];

        if (row.Count > DetailLimit)
        {
            row.FileHost.Children.Add(new TextBlock
            {
                Text = string.Format(T("S.GwRank.BulkTooManyToDetail"), row.Count, DetailLimit),
                TextWrapping = TextWrapping.Wrap, FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            });
            return;
        }

        bool folderOn = row.Box.IsChecked != false;
        foreach (var file in GwRankBulkUpload.EnumerateBuilds(row.Path)
                                             .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
        {
            // Chemin relatif : dans un dossier à sous-dossiers, le seul nom de fichier ne suffit
            // pas à distinguer deux builds homonymes — et ils sont la norme ici.
            var label = Path.GetRelativePath(row.Path, file);
            var text = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis,
                                       FontSize = 12 };
            var box = new CheckBox
            {
                IsChecked = folderOn && !_excluded.Contains(file),
                Margin = new Thickness(0, 0, 0, 2),
                Content = text,
            };

            var fileRow = new FileRow { Path = file, Box = box, Label = text, BaseText = label };
            box.Checked   += (_, _) => FileToggled(row, fileRow, true);
            box.Unchecked += (_, _) => FileToggled(row, fileRow, false);

            row.Files.Add(fileRow);
            row.FileHost.Children.Add(box);
            ShowVerdictOn(fileRow);
        }
    }

    private void FolderToggled(FolderRow row, bool on)
    {
        if (_suspend) return;

        // Cocher un dossier coche TOUT ce qu'il contient : c'est le geste attendu, et il efface
        // les exceptions posées avant. Le décocher les efface aussi — sans quoi elles
        // ressusciteraient silencieusement au prochain cochage.
        _excluded.RemoveWhere(f => IsUnder(f, row.Path));

        _suspend = true;
        foreach (var f in row.Files ?? []) f.Box.IsChecked = on;
        _suspend = false;

        SelectionChanged();
    }

    private void FileToggled(FolderRow row, FileRow file, bool on)
    {
        if (_suspend) return;

        if (on) _excluded.Remove(file.Path);
        else _excluded.Add(file.Path);

        // Le dossier suit ses fichiers : tout coché, rien de coché, ou état intermédiaire.
        // (La liste existe forcément : ces événements viennent des cases qu'elle contient.)
        var files = row.Files ?? [];
        int ticked = files.Count(f => f.Box.IsChecked == true);
        _suspend = true;
        row.Box.IsChecked = ticked == files.Count ? true : ticked == 0 ? false : null;
        _suspend = false;

        SelectionChanged();
    }

    private void RefreshFolderStates()
    {
        _suspend = true;
        foreach (var row in _folders.Where(r => r.Box.IsChecked != false && r.Files is null))
        {
            // Dossier coché mais jamais déplié : son état intermédiaire se déduit des exceptions
            // retenues, sans avoir à construire une seule case.
            bool anyExcluded = GwRankBulkUpload.EnumerateBuilds(row.Path).Any(_excluded.Contains);
            if (anyExcluded) row.Box.IsChecked = null;
        }
        _suspend = false;
        UpdateSendCount();
    }

    /// <summary>Les fichiers réellement retenus. Se calcule sans dépendre de ce qui est déplié :
    /// un dossier coché mais replié compte tout de même tout son contenu.</summary>
    private List<string> SelectedFiles()
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _folders.Where(r => r.Box.IsChecked != false))
            foreach (var f in GwRankBulkUpload.EnumerateBuilds(row.Path))
                if (!_excluded.Contains(f) && seen.Add(f)) files.Add(f);
        return files;
    }

    private static bool IsUnder(string path, string folder)
    {
        try
        {
            var root = Path.GetFullPath(folder).TrimEnd('\\', '/');
            return Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar,
                                                     StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Une sélection qui RÉTRÉCIT n'invalide pas l'analyse : les verdicts déjà calculés restent
    /// vrais, seul le compte change. Une sélection qui s'ÉLARGIT, elle, porte des fichiers que
    /// personne n'a examinés — envoyer sur cette base serait déposer à l'aveugle.
    /// </summary>
    private void SelectionChanged()
    {
        if (_sending) return;

        if (_items.Count > 0 && SelectedFiles().Any(f => !_analyzed.Contains(f)))
        {
            ResetAnalysis();
            return;
        }

        UpdateSendCount();
    }

    private void ResetAnalysis()
    {
        _items = [];
        _analyzed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SummaryText.Visibility = Visibility.Collapsed;
        AttentionPanel.Visibility = Visibility.Collapsed;
        ProgressText.Visibility = Visibility.Collapsed;
        SendButton.Visibility = Visibility.Collapsed;
        AnalyzeButton.Visibility = Visibility.Visible;
        foreach (var row in _folders)
            foreach (var f in row.Files ?? []) f.Label.Text = f.BaseText;
        SizeToContent = SizeToContent.Height;
    }

    // ── Analyse ───────────────────────────────────────────────────────────────

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        var files = SelectedFiles();
        if (files.Count == 0)
        {
            MessageBox.Show(this, T("S.GwRank.BulkPickFolder"), T("S.GwRank.BulkTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AnalyzeButton.IsEnabled = false;
        ProgressText.Text = T("S.GwRank.BulkAnalyzing");
        ProgressText.Visibility = Visibility.Visible;
        try { _items = await _analyze(files); }
        finally { AnalyzeButton.IsEnabled = true; }

        _analyzed = new HashSet<string>(_items.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
        ProgressText.Visibility = Visibility.Collapsed;
        ShowAnalysis();
    }

    private void ShowAnalysis()
    {
        var report = GwRankBulkUpload.Summarize(_items);
        int fresh   = _items.Count(i => i.Verdict == GwRankBulkVerdict.New);
        int changed = _items.Count(i => i.Verdict == GwRankBulkVerdict.Changed);

        SummaryText.Text = string.Format(T("S.GwRank.BulkSummary"), fresh, changed, report.Unchanged);
        SummaryText.Visibility = Visibility.Visible;

        ShowAttention(report.NeedsAttention.ToList());

        // Chaque fichier déplié porte désormais son verdict : décocher devient un choix éclairé.
        foreach (var row in _folders)
            foreach (var f in row.Files ?? []) ShowVerdictOn(f);

        AnalyzeButton.Visibility = Visibility.Collapsed;
        SendButton.Visibility = Visibility.Visible;
        UpdateSendCount();
        SizeToContent = SizeToContent.Height;
    }

    private void ShowAttention(List<GwRankBulkItem> attention)
    {
        if (attention.Count == 0) { AttentionPanel.Visibility = Visibility.Collapsed; return; }

        AttentionTitle.Text = string.Format(T("S.GwRank.BulkAttention"), attention.Count);
        AttentionList.ItemsSource = attention.Select(Describe).ToList();
        AttentionPanel.Visibility = Visibility.Visible;
    }

    private void ShowVerdictOn(FileRow file)
    {
        var item = _items.FirstOrDefault(i =>
            string.Equals(i.FilePath, file.Path, StringComparison.OrdinalIgnoreCase));
        file.Label.Text = item is null ? file.BaseText : $"{file.BaseText}   —   {VerdictLabel(item)}";
    }

    private static string VerdictLabel(GwRankBulkItem item) => item.Verdict switch
    {
        GwRankBulkVerdict.New               => T("S.GwRank.BulkVerdictNew"),
        GwRankBulkVerdict.Changed           => T("S.GwRank.BulkVerdictChanged"),
        GwRankBulkVerdict.Unchanged         => T("S.GwRank.BulkVerdictUnchanged"),
        GwRankBulkVerdict.IdentityCollision => T("S.GwRank.BulkVerdictCollision"),
        GwRankBulkVerdict.UnknownSkills     => T("S.GwRank.BulkVerdictUnknownSkills"),
        _                                   => T("S.GwRank.BulkVerdictUnreadable"),
    };

    /// <summary>Ce qui partirait vraiment : analysé, à envoyer, et toujours coché.</summary>
    private List<GwRankBulkItem> Pending()
    {
        var kept = new HashSet<string>(SelectedFiles(), StringComparer.OrdinalIgnoreCase);
        return _items.Where(i => i.WillBeSent && kept.Contains(i.FilePath)).ToList();
    }

    private void UpdateSendCount()
    {
        if (SendButton.Visibility != Visibility.Visible) return;
        int n = Pending().Count;
        SendButton.IsEnabled = n > 0;
        SendButton.Content = n > 0
            ? string.Format(T("S.GwRank.BulkSend"), n)
            : T("S.GwRank.BulkNothing");
    }

    /// <summary>Une ligne du panneau d'avertissement : ce qui bloque, et pourquoi.</summary>
    private static string Describe(GwRankBulkItem item)
    {
        var name = Path.GetFileName(item.FilePath);
        if (item.Result is { } r && r != GwRankStatus.Ok)
            return r == GwRankStatus.Conflict
                ? string.Format(T("S.GwRank.BulkItemConflict"), name)
                : string.Format(T("S.GwRank.BulkItemFailed"), name, item.Message ?? "?");

        return item.Verdict switch
        {
            GwRankBulkVerdict.IdentityCollision =>
                string.Format(T("S.GwRank.BulkItemCollision"), name,
                              Path.GetFileName(item.ConflictingPath ?? "")),
            GwRankBulkVerdict.UnknownSkills =>
                string.Format(T("S.GwRank.BulkItemUnknownSkills"), name),
            _ => string.Format(T("S.GwRank.BulkItemUnreadable"), name),
        };
    }

    // ── Envoi ─────────────────────────────────────────────────────────────────

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        // On dépose ce qui est ENCORE coché : décocher après l'analyse doit retirer le fichier du
        // lot, pas seulement du décompte affiché.
        var pending = Pending();
        if (pending.Count == 0) return;

        _sending = true;
        _cts = new CancellationTokenSource();
        SendButton.IsEnabled = false;
        CloseButton.Content = T("S.GwRank.BulkStop");
        SetSelectionEnabled(false);

        int done = 0, total = pending.Count;
        ProgressText.Text = string.Format(T("S.GwRank.BulkProgress"), 0, total);
        ProgressText.Visibility = Visibility.Visible;
        var progress = new Progress<GwRankBulkItem>(_ =>
            ProgressText.Text = string.Format(T("S.GwRank.BulkProgress"), ++done, total));

        GwRankBulkReport report;
        try { report = await _upload(pending, progress, _cts.Token); }
        catch (OperationCanceledException)
        {
            // Ce qui est déjà parti l'est vraiment : l'index a été enregistré, une reprise ne
            // redéposera que le reste.
            ProgressText.Text = string.Format(T("S.GwRank.BulkStopped"), done, total);
            AnythingSent = done > 0;
            Finish();
            return;
        }

        AnythingSent = report.Created + report.Updated > 0;
        ProgressText.Visibility = Visibility.Collapsed;

        var text = string.Format(T("S.GwRank.BulkDone"), report.Created, report.Updated);
        int untouched = _items.Count(i => i.Verdict == GwRankBulkVerdict.Unchanged);
        if (untouched > 0)
            text += "\n" + string.Format(T("S.GwRank.BulkDoneUnchanged"), untouched);

        // Un lot interrompu n'est pas un lot raté : ce qui est parti est acquis, et le relancer
        // ne reprendra que le reste. Le dire évite qu'on croie devoir tout recommencer.
        if (report.WasInterrupted)
            text += "\n\n" + string.Format(
                T(report.StoppedBy == GwRankStatus.RateLimited
                  ? "S.GwRank.BulkStoppedRateLimited" : "S.GwRank.BulkStoppedOffline"),
                report.Created + report.Updated, report.Planned, report.NotAttempted);

        SummaryText.Text = text;
        SummaryText.Visibility = Visibility.Visible;
        // Le bilan reprend TOUT ce qui réclame une décision : les écartés d'avance comme les
        // refus survenus pendant le lot.
        ShowAttention(GwRankBulkUpload.Summarize(_items).NeedsAttention
                      .Concat(report.NeedsAttention).Distinct().ToList());
        Finish();
    }

    private void SetSelectionEnabled(bool on)
    {
        foreach (var row in _folders)
        {
            row.Box.IsEnabled = on && row.Count > 0;
            row.Toggle.IsEnabled = on && row.Count > 0;
            foreach (var f in row.Files ?? []) f.Box.IsEnabled = on;
        }
    }

    private void Finish()
    {
        _sending = false;
        _cts?.Dispose();
        _cts = null;
        SendButton.Visibility = Visibility.Collapsed;
        CloseButton.Content = T("S.Common.Close");
        SizeToContent = SizeToContent.Height;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Pendant un lot, ce bouton ARRÊTE l'envoi au lieu de fermer : refermer la fenêtre
        // laisserait l'utilisateur sans aucune idée de ce qui est parti.
        if (_sending) { _cts?.Cancel(); return; }
        Close();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_sending) return;
        e.Cancel = true;
        _cts?.Cancel();
    }
}
