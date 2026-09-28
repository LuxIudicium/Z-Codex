using ZCodex.App.Settings;
using ZCodex.App.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZCodex.App.Views;

// Fenêtre NON modale « Spike damage calculus » (chantier 11) : le teambuild reste éditable
// derrière et le VM recalcule à chaud sur chaque mutation. Une seule instance à la fois,
// gérée par MainWindow (rouvre/replace selon le build ciblé, fermée avec son onglet).
public partial class SpikeWindow : Window
{
    private readonly SpikeViewModel _vm;

    public TeamBuildViewModel Build => _vm.Build;

    // MainWindow bascule le teambuild en mode sélection de roster (mêmes cases que les cadenas).
    public event EventHandler? SelectRosterRequested;

    public SpikeWindow(TeamBuildViewModel build, AppSettings settings)
    {
        InitializeComponent();
        _vm = new SpikeViewModel(build, settings);
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
    }

    private void SelectRoster_Click(object sender, RoutedEventArgs e)
        => SelectRosterRequested?.Invoke(this, EventArgs.Empty);

    private void SkillSlot_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SkillSlotViewModel slot && slot.HasSkill)
            Build.ToggleSpikeSlot(slot);   // (dé)sélection + ordre de cast (indices 1..N)
    }

    // Icône de buff d'arme de la carte membre (chantier 14) : bascule l'état PAR PERSO
    // (SetSpikeBuff → Mutated → recalcul, persisté .pn3 v11).
    private void BuffToggle_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SpikeBuffToggleViewModel toggle)
            toggle.IsActive = !toggle.IsActive;
    }

    // Clic sur une icône du bandeau des effets d'ÉQUIPE (28/09/2026) : la MÊME bascule que dans la vue
    // Teambuild — l'état vit dans Build.NatureRituals, donc allumer ici allume partout, infobulles
    // comprises. Le recalcul suit : le toggle mute le build, et Mutated reconstruit la fenêtre.
    private void TeamEffectToggle_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NatureRitualIndicatorViewModel vm)
            vm.Toggle();
        e.Handled = true;
    }

    // Molette sur une icône du bandeau : rang de SIMULATION de l'effet (mêmes crans que la vue
    // Teambuild). ⚠ e.Handled dès que le cran a servi, sinon le ScrollViewer du roster défilerait en
    // même temps (arbitrage molette du chantier : défiler OU modifier, jamais les deux).
    private void TeamEffectRank_Wheel(object sender, MouseWheelEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NatureRitualIndicatorViewModel { CanAdjustRank: true } vm)
        {
            vm.AdjustRank(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    // Clic sur une icône d'effet de dégâts de la carte (lot 6e) : la MÊME bascule que dans la vue
    // Build — allumer ici allume partout, il n'y a qu'un seul état par perso. Le recalcul suit tout
    // seul : Toggle() notifie, le perso est muté, et la fenêtre se reconstruit.
    private void BoostToggle_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AttributeBoostIndicatorViewModel vm)
            vm.Toggle();
        e.Handled = true;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e) => _vm.SaveProfile();
    private void DeleteProfile_Click(object sender, RoutedEventArgs e) => _vm.DeleteProfile();

    // Copie une capture de la fenêtre entière dans le presse-papiers (collable telle quelle).
    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        string feedback;
        try
        {
            var bg = TryFindResource("DialogBackgroundBrush") as Brush ?? Brushes.White;
            var bmp = VisualCapture.Render(CaptureRoot, bg);
            feedback = bmp != null && SafeClipboard.SetImage(bmp)
                ? LanguageManager.T("S.Spike.CaptureOk")
                : LanguageManager.T("S.Spike.CaptureFailed");
        }
        catch (Exception)
        {
            // occupé par une autre appli : réessayer
            feedback = LanguageManager.T("S.Spike.CaptureFailed");
        }
        CaptureButton.Content = feedback;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { CaptureButton.Content = LanguageManager.T("S.Spike.CaptureImage"); timer.Stop(); };
        timer.Start();
    }
}
