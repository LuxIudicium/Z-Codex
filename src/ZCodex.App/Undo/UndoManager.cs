using System.Windows.Input;
using System.Windows.Threading;

namespace ZCodex.App.Undo;

// Undo/redo par snapshots JSON (.pn3 sérialisé en mémoire) d'un team build.
// Coalescence : une rafale de mutations espacées de moins de 500 ms = un seul pas d'undo
// (ex : ticks de molette sur un attribut). Un manager par onglet, piles mortes avec lui.
public sealed class UndoManager : IDisposable
{
    private static readonly TimeSpan CoalesceDelay = TimeSpan.FromMilliseconds(500);

    private readonly Func<string> _capture;
    private readonly Action<string> _restore;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly DispatcherTimer _timer;

    // Dernier état stable connu = pré-état de la rafale en cours, et état courant hors rafale.
    private string _baseline;
    private bool _burst;      // une rafale de mutations attend son commit
    private bool _restoring;  // ignore les Mutated ré-entrants pendant une restauration

    // Partie ENREGISTRÉE d'un instantané (ce qui va dans le fichier : pas les points bonus, par exemple).
    private readonly Func<string, string> _persisted;
    // Partie enregistrée de l'état tel qu'il est sur disque ; null = inconnu (ouvert déjà modifié).
    private string? _saved;

    public UndoManager(Func<string> capture, Action<string> restore,
                       Func<string, string>? persisted = null, bool startsSaved = false)
    {
        _capture = capture;
        _restore = restore;
        _persisted = persisted ?? (s => s);
        _baseline = capture();
        if (startsSaved) _saved = _persisted(_baseline);
        _timer = new DispatcherTimer { Interval = CoalesceDelay };
        _timer.Tick += (_, _) => CommitPending();
    }

    // L'état courant vient d'être enregistré : c'est lui que Ctrl+Z / Ctrl+Y devront retrouver pour que le
    // build redevienne « non modifié ».
    public void MarkSaved()
    {
        CommitPending();
        _saved = _persisted(_baseline);
    }

    // Vrai quand l'état courant (hors rafale en cours) est exactement celui du fichier. Sans cela, revenir au
    // départ à coups de Ctrl+Z laissait l'étoile « modifié » et la demande d'enregistrement à la fermeture.
    public bool IsAtSavedState => _saved is not null && !_burst && _persisted(_baseline) == _saved;

    public bool CanUndo => _undo.Count > 0 || _burst;
    public bool CanRedo => _redo.Count > 0;

    // À brancher sur TeamBuildViewModel.Mutated. Chaque mutation relance la fenêtre de coalescence.
    public void OnMutated()
    {
        if (_restoring) return;
        _burst = true;
        _timer.Stop();
        _timer.Start();
    }

    // Clôt la rafale en cours : l'état pré-rafale part sur la pile undo.
    private void CommitPending()
    {
        _timer.Stop();
        if (!_burst) return;
        _burst = false;
        var current = _capture();
        if (current == _baseline) return;   // état inchangé (prop non persistée) → pas de pas d'undo
        _undo.Push(_baseline);
        _redo.Clear();
        _baseline = current;
        CommandManager.InvalidateRequerySuggested();
    }

    // Session partagée : un état reçu d'un autre participant vient de remplacer l'écran. Les
    // instantanés antérieurs ne décrivent plus que le passé du build ; les restaurer défairait
    // le travail des autres, puis le leur renverrait. L'historique repart donc de l'état reçu.
    public void Reset()
    {
        _timer.Stop();
        _burst = false;
        _undo.Clear();
        _redo.Clear();
        _baseline = _capture();
        CommandManager.InvalidateRequerySuggested();
    }

    public void Undo()
    {
        CommitPending();
        if (_undo.Count == 0) return;
        _redo.Push(_baseline);
        _baseline = _undo.Pop();
        Restore(_baseline);
    }

    public void Redo()
    {
        CommitPending();
        if (_redo.Count == 0) return;
        _undo.Push(_baseline);
        _baseline = _redo.Pop();
        Restore(_baseline);
    }

    private void Restore(string state)
    {
        _restoring = true;
        try { _restore(state); }
        finally { _restoring = false; }
        CommandManager.InvalidateRequerySuggested();
    }

    public void Dispose() => _timer.Stop();
}
