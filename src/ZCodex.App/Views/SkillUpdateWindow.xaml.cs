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
        string counter = string.Empty;   // « n / total », rétabli quand l'avis de nouvel essai s'efface
        var progress = new Progress<(int done, int total, string current)>(p =>
        {
            ProgressBar.Maximum = p.total;
            ProgressBar.Value = p.done;
            StatusText.Text = string.Format(T("S.Upd.Fetching"), p.current);
            DetailText.Text = counter = $"{p.done} / {p.total}";
        });

        // Page obligatoire en échec : pourquoi, puis le compte à rebours du nouvel essai.
        var retryNotice = new Progress<WikiRetryNotice?>(n =>
        {
            if (n is null) { DetailText.Text = counter; return; }
            var reason = n.Failure switch
            {
                WikiFailure.Timeout     => T("S.Upd.FailTimeout"),
                WikiFailure.HttpError   => string.Format(T("S.Upd.FailHttp"), n.StatusCode),
                WikiFailure.NotWikiPage => T("S.Upd.FailNotWiki"),
                WikiFailure.NoData      => T("S.Upd.FailNoData"),
                _                       => T("S.Upd.FailUnreachable"),
            };
            var next = n.SecondsLeft switch
            {
                0 => string.Format(T("S.Upd.RetryNow"), n.Retry, n.Retries),
                1 => string.Format(T("S.Upd.RetryIn1"), n.Retry, n.Retries),
                _ => string.Format(T("S.Upd.RetryIn"), n.SecondsLeft, n.Retry, n.Retries),
            };
            DetailText.Text = $"{reason}\n{next}";
        });

        // L'ouverture de la base est DANS le try : c'est le premier écran d'un poste neuf,
        // une base inaccessible doit s'afficher ici et non remonter en exception non gérée.
        AppDbContext? db = null;
        try
        {
            db = AppDbContextFactory.Create();
            var scraper = new WikiSkillScraper(NullLogger<WikiSkillScraper>.Instance) { RetryNotice = retryNotice };
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
        catch (WikiUnavailableException ex)
        {
            // Wiki en panne ou pages troubles : rien d'incomplet n'a été écrit. La page en cause
            // (ou les compétences qui auraient disparu) sert au support quand un joueur signale.
            var cause = ex.WouldLose.Count > 0
                ? string.Format(T("S.Upd.WikiDownLoss"), ex.WouldLose.Count, string.Join(", ", ex.WouldLose.Take(3)))
                : string.Format(T("S.Upd.WikiDownPage"), ex.Page);
            StatusText.Text = T("S.Upd.WikiDown");
            DetailText.Text = $"{T("S.Upd.WikiDownDetail")}\n\n{cause}\n\n{T("S.Upd.Retry")}";
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
