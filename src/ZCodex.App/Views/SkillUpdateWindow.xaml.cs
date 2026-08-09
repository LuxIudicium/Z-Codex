using Microsoft.Extensions.Logging.Abstractions;
using ZCodex.Data;
using ZCodex.Scraper;
using System.Windows;

namespace ZCodex.App.Views;

public partial class SkillUpdateWindow : Window
{
    private static string T(string key) => LanguageManager.T(key);

    private readonly CancellationTokenSource _cts = new();
    private bool _done;

    public int SkillsUpdated { get; private set; }

    public SkillUpdateWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var progress = new Progress<(int done, int total, string current)>(p =>
        {
            ProgressBar.Maximum = p.total;
            ProgressBar.Value = p.done;
            StatusText.Text = string.Format(T("S.Upd.Fetching"), p.current);
            DetailText.Text = $"{p.done} / {p.total}";
        });

        // L'ouverture de la base est DANS le try : c'est le premier écran d'un poste neuf,
        // une base inaccessible doit s'afficher ici et non remonter en exception non gérée.
        AppDbContext? db = null;
        try
        {
            db = AppDbContextFactory.Create();
            var scraper = new WikiSkillScraper(NullLogger<WikiSkillScraper>.Instance);
            var service = new SkillUpdateService(scraper, db, NullLogger<SkillUpdateService>.Instance);

            CancelButton.IsEnabled = true;
            SkillsUpdated = await service.UpdateSkillsAsync(progress, _cts.Token);
            StatusText.Text = string.Format(T("S.Upd.Done"), SkillsUpdated);
            DetailText.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = T("S.Upd.Cancelled");
            DetailText.Text = T("S.Upd.Retry");
        }
        catch (Exception ex)
        {
            // Le message d'exception seul ne dit pas quoi faire — et sur un poste neuf il laisse
            // l'utilisateur devant un catalogue vide. On indique explicitement le rattrapage.
            StatusText.Text = T("S.Upd.Error");
            DetailText.Text = $"{ex.Message}\n\n{T("S.Upd.Retry")}";
        }
        finally
        {
            db?.Dispose();
            _done = true;
            CancelButton.Content = T("S.Common.Close");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_done)
            Close();
        else
            _cts.Cancel();
    }
}
