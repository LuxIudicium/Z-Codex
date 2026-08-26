using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Navigation;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Scraper;

namespace ZCodex.App.Views;

/// <summary>
/// « Extras ▸ Importer un rapport GvG ». Lit un rapport de match publié par gvg.report et en tire
/// un .zcx par équipe — l'utilisateur choisit celles qu'il garde, une ou les deux.
/// </summary>
public partial class GvgReportImportWindow : Window
{
    private static string T(string key) => LanguageManager.T(key);

    private readonly IReadOnlyDictionary<int, Skill> _skillsById;
    private readonly CancellationTokenSource _cts = new();
    private GvgReportImport? _import;

    /// <summary>Destination retenue, à persister par l'appelant une fois l'écriture réussie.</summary>
    public string? ChosenDestination { get; private set; }

    public GvgReportImportWindow(IReadOnlyDictionary<int, Skill> skillsById, string? initialDestination)
    {
        InitializeComponent();
        _skillsById = skillsById;
        DestinationBox.Text = initialDestination ?? string.Empty;
    }

    // Changer l'URL périme le rapport déjà lu : garder les cases cochées laisserait enregistrer un
    // match qui n'est plus celui affiché dans la zone de saisie.
    private void UrlBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (MatchPanel is null) return;   // pendant InitializeComponent, les champs n'existent pas encore

        _import = null;
        MatchPanel.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        var reportId = GvgReportTeamBuilder.ReportIdFrom(UrlBox.Text);
        if (reportId is null)
        {
            Status(T("S.Gvg.BadUrl"));
            return;
        }

        SetBusy(true);
        Status(T("S.Gvg.Reading"));

        try
        {
            var import = await GvgReportService.FetchAsync(reportId, _skillsById, _cts.Token);
            if (import is null)
            {
                Status(T("S.Gvg.Unreadable"));
                return;
            }

            _import = import;
            Show(import);
        }
        catch (OperationCanceledException) { /* fenêtre fermée en cours de lecture */ }
        catch (HttpRequestException)
        {
            Status(string.Format(T("S.Gvg.NotFound"), GvgReportService.SiteUrl));
        }
        catch (Exception ex)
        {
            Status($"{T("S.Gvg.Error")}\n{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Show(GvgReportImport import)
    {
        MatchText.Text = $"{import.Date} — {import.Map}";

        FirstTeamCheck.Content = import.Teams[0].Label;
        FirstTeamBars.Text = BarsOf(import.Teams[0]);
        SecondTeamCheck.Content = import.Teams[1].Label;
        SecondTeamBars.Text = BarsOf(import.Teams[1]);

        MatchPanel.Visibility = Visibility.Visible;
        SaveButton.Visibility = Visibility.Visible;

        // Une compétence absente du catalogue est un signal pour NOUS (catalogue à mettre à jour),
        // pas une erreur de l'utilisateur : dit une fois, sans bloquer l'enregistrement.
        if (import.UnknownSkills.Count > 0)
            Status(string.Format(T("S.Gvg.Unknown"),
                import.UnknownSkills.Count, string.Join(", ", import.UnknownSkills)));
        else
            StatusText.Visibility = Visibility.Collapsed;
    }

    private static string BarsOf(GvgReportTeam team) =>
        team.IncompleteBars == 0
            ? T("S.Gvg.BarsComplete")
            : string.Format(T("S.Gvg.BarsIncomplete"), team.IncompleteBars);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = T("S.Dlg.SelectGvgDestination"),
            InitialDirectory = DestinationBox.Text,
        };
        if (dlg.ShowDialog() == true) DestinationBox.Text = dlg.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_import is null) return;

        var teams = new List<GvgReportTeam>();
        if (FirstTeamCheck.IsChecked == true) teams.Add(_import.Teams[0]);
        if (SecondTeamCheck.IsChecked == true) teams.Add(_import.Teams[1]);

        if (teams.Count == 0)
        {
            MessageBox.Show(this, T("S.Gvg.NoTeam"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destination = DestinationBox.Text.Trim();
        if (destination.Length == 0 || !Directory.Exists(destination))
        {
            MessageBox.Show(this, T("S.Gvg.NoDestination"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var report = GvgReportService.Write(teams, destination);
        if (report.FilesWritten > 0) ChosenDestination = destination;

        var message = report.FilesWritten > 0
            ? string.Format(T("S.Gvg.Done"), report.FilesWritten, destination)
            : T("S.Gvg.WriteFailed");

        if (report.Errors.Count > 0)
            message += "\n\n" + string.Join('\n', report.Errors);

        Status(message);
    }

    private void Status(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        ReadButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        UrlBox.IsEnabled = !busy;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _cts.Cancel();
        base.OnClosing(e);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
