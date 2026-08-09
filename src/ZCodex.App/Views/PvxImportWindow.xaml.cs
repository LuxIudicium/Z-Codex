using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using ZCodex.Core.Models;
using ZCodex.Scraper;

namespace ZCodex.App.Views;

/// <summary>
/// « Extras ▸ Télécharger les builds PvX ». Récupère les build packs de PvXwiki et les dépose
/// dans le dossier choisi, en .txt (lisibles par Guild Wars lui-même), avec un .zcx par équipe.
/// </summary>
public partial class PvxImportWindow : Window
{
    private static string T(string key) => LanguageManager.T(key);

    private readonly IReadOnlyDictionary<int, Skill> _skillsById;
    private readonly CancellationTokenSource _cts = new();
    private bool _running;

    /// <summary>Destination retenue, à persister par l'appelant une fois l'import réussi.</summary>
    public string? ChosenDestination { get; private set; }

    public PvxImportWindow(IReadOnlyDictionary<int, Skill> skillsById, string? initialDestination)
    {
        InitializeComponent();
        _skillsById = skillsById;
        DestinationBox.Text = initialDestination ?? string.Empty;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = T("S.Dlg.SelectPvxDestination"),
            InitialDirectory = DestinationBox.Text,
        };
        if (dlg.ShowDialog() == true) DestinationBox.Text = dlg.FolderName;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var packs = new List<PvxPack>();
        if (PveCheck.IsChecked == true) packs.Add(PvxPack.Pve);
        if (PvpCheck.IsChecked == true) packs.Add(PvxPack.Pvp);

        if (packs.Count == 0)
        {
            MessageBox.Show(this, T("S.Pvx.NoPack"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destination = DestinationBox.Text.Trim();
        if (destination.Length == 0 || !Directory.Exists(destination))
        {
            MessageBox.Show(this, T("S.Pvx.NoDestination"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetRunning(true);

        var progress = new Progress<PvxProgress>(p =>
        {
            var pack = PvxBuildPackService.FolderNameOf(p.Pack);
            StatusText.Text = p.Stage switch
            {
                PvxStage.Downloading     => string.Format(T("S.Pvx.Downloading"), pack),
                PvxStage.Extracting      => string.Format(T("S.Pvx.Extracting"), pack, p.Done, p.Total),
                PvxStage.ConvertingTeams => string.Format(T("S.Pvx.Converting"), p.Done, p.Total),
                _                        => string.Empty,
            };

            // Le téléchargement n'a pas de total connu → barre indéterminée le temps qu'il dure.
            ProgressBar.IsIndeterminate = p.Total == 0;
            if (p.Total > 0)
            {
                ProgressBar.Maximum = p.Total;
                ProgressBar.Value = p.Done;
            }
        });

        try
        {
            var report = await Task.Run(
                () => PvxBuildPackService.ImportAsync(packs, destination, _skillsById, progress, _cts.Token),
                _cts.Token);

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = ProgressBar.Maximum;

            // Aucun fichier écrit = les deux liens ont échoué : on renvoie l'utilisateur vers la
            // page PvX plutôt que de le laisser avec un message d'erreur réseau brut.
            if (report.FilesWritten == 0)
            {
                StatusText.Text = string.Format(T("S.Pvx.Failed"), PvxBuildPackService.BuildPacksPageUrl)
                                  + JoinErrors(report);
            }
            else
            {
                ChosenDestination = destination;
                StatusText.Text = string.Format(T("S.Pvx.Done"),
                    report.FilesWritten, report.TeamsConverted, report.TeamsTooLarge) + JoinErrors(report);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = T("S.Pvx.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{T("S.Pvx.Error")}\n{ex.Message}";
        }
        finally
        {
            SetRunning(false);
            StartButton.Content = T("S.Pvx.Restart");
        }
    }

    private static string JoinErrors(PvxImportReport report)
    {
        if (report.Errors.Count == 0) return string.Empty;
        // Un import partiel reste un succès : les erreurs sont listées, pas masquées, mais
        // limitées à quelques lignes pour ne pas faire grandir la fenêtre indéfiniment.
        var shown = report.Errors.Take(4);
        var extra = report.Errors.Count > 4 ? $"\n… (+{report.Errors.Count - 4})" : string.Empty;
        return "\n\n" + string.Join('\n', shown) + extra;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        ProgressBar.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Visible;
        StartButton.IsEnabled = !running;
        BrowseButton.IsEnabled = !running;
        PveCheck.IsEnabled = !running;
        PvpCheck.IsEnabled = !running;
        CloseButton.Content = running ? T("S.Common.Cancel") : T("S.Common.Close");
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Pendant l'import, le bouton annule au lieu de fermer : partir en laissant l'extraction
        // écrire dans le dos de l'utilisateur ne serait pas honnête.
        if (_running) { _cts.Cancel(); return; }
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_running) { e.Cancel = true; _cts.Cancel(); }
        base.OnClosing(e);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
