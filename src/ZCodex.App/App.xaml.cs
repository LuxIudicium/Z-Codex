using System.IO;
using System.Windows;
using System.Windows.Threading;
using ZCodex.Core;

namespace ZCodex.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Filet de sécurité global. En développement, une exception non gérée se voit dans le
    // débogueur ; sur un poste utilisateur elle ferme l'application sans un mot et sans trace,
    // avec le travail en cours. On journalise donc systématiquement dans
    // %AppData%\Z-Codex\crash.log, et on garde l'application en vie quand l'erreur vient du fil
    // d'interface (l'utilisateur doit toujours pouvoir enregistrer ses builds avant de quitter).
    private static string LogPath => Path.Combine(DataFolder, "crash.log");

    private const long MaxLogBytes = 1024 * 1024;   // au-delà, on repart d'un journal vierge

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Fil d'interface : récupérable — on prévient et on continue.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Autres fils : le CLR termine le process juste après, on ne peut que tracer.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        // Task jamais attendue (les _ = XxxAsync() du démarrage) : silencieux, journalisé.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // La fenêtre est créée ICI plutôt que par StartupUri : son constructeur ouvre la base
        // (AppDbContextFactory.Create) et relit les préférences, deux opérations qui dépendent
        // de %AppData% et peuvent échouer sur un poste neuf — disque plein, dossier en lecture
        // seule, base corrompue. Via StartupUri, cet échec ne laissait qu'une boîte « crash »
        // suivie d'une application sans fenêtre, ni vivante ni fermée. On veut un message qui
        // dise quoi faire, puis une sortie propre.
        try
        {
            MainWindow = new MainWindow();
            // Les liaisons posées par InitializeComponent ne sont branchées qu'au prochain passage du
            // moteur de liaison de WPF, qu'il poste lui-même en priorité DataBind pour passer AVANT la
            // mise en page. Or Show() met la fenêtre en page tout de suite, dans cette même opération :
            // sans ce passage, chaque vue masquée par une liaison de Visibility (calculateur d'armure,
            // onglet Recherche, teambuild) est encore « Visible » au premier affichage, construite en
            // entier, puis masquée. Mesuré le 15/10/2026 : 7 341 éléments construits au lieu de 536, et
            // ~3 s de démarrage en plus. Ces vues se construisent désormais à leur première ouverture.
            Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            Log("Startup", ex);
            MessageBox.Show(
                string.Format(LanguageManager.T("S.Msg.StartupFailed"), ex.Message, DataFolder, LogPath),
                LanguageManager.T("S.Msg.StartupFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Environment.Exit et non Shutdown() : Shutdown() poste sa demande sur le
            // dispatcher, or celui-ci n'est pas encore démarré ici (Run() ne lance sa boucle
            // qu'après OnStartup). Sans fenêtre à fermer ni état à sauvegarder à ce stade,
            // on sort franchement — un process fantôme sans interface serait pire que tout.
            Environment.Exit(1);
        }
    }

    private static string DataFolder => AppPaths.Root;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("UI", e.Exception);
        e.Handled = true;   // AVANT le MessageBox : une erreur d'affichage ne doit pas boucler

        MessageBox.Show(
            string.Format(LanguageManager.T("S.Msg.Crash"), e.Exception.Message, LogPath),
            LanguageManager.T("S.Msg.CrashTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Log("Fatal", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log("Task", e.Exception);
        e.SetObserved();
    }

    private static void Log(string origin, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                File.Delete(LogPath);

            File.AppendAllText(LogPath,
                $"""

                ───────────────────────────────────────────────────────────
                {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{origin}] {ex.GetType().Name}
                {ex}

                """);
        }
        catch
        {
            // Journaliser l'échec de journalisation n'a nulle part où aller : on abandonne
            // silencieusement plutôt que de masquer l'erreur d'origine par une seconde.
        }
    }
}
