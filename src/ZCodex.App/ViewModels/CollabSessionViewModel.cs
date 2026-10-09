using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ZCodex.Core.Collab;
using ZCodex.Core.Models;

namespace ZCodex.App.ViewModels;

/// <summary>Ce que la fenêtre principale prête à la session : le pont entre l'écran et le
/// document <c>.zcx</c>, qui ne vit que dans MainWindow (seul détenteur du catalogue et des
/// conversions VM ↔ modèle).</summary>
public sealed class CollabBridge
{
    /// <summary>Le teambuild affiché, en document <c>.zcx</c> (horodatage de modification neutralisé).</summary>
    public required Func<TeamBuildViewModel, JsonObject> Capture { get; init; }
    /// <summary>Remplace le contenu de l'onglet par ce document, sans changer d'onglet.</summary>
    public required Action<TeamBuildViewModel, JsonObject> Apply { get; init; }
    public required Func<IReadOnlyDictionary<int, Skill>> Skills { get; init; }
    /// <summary>Équivalence PvE/PvP des compétences selon le mode de jeu affiché (null en « Tout »).</summary>
    public required Func<Func<int, int>?> Canon { get; init; }
}

/// <summary>Un participant, tel que le bandeau l'affiche.</summary>
public sealed record CollabParticipant(string Nick, Brush Brush, bool IsMe, bool IsHost, bool ReadOnly)
{
    public string Label
    {
        get
        {
            var tags = new List<string>();
            if (IsMe) tags.Add(LanguageManager.T("S.Collab.You"));
            if (IsHost) tags.Add(LanguageManager.T("S.Collab.HostTag"));
            if (ReadOnly) tags.Add(LanguageManager.T("S.Collab.ReadOnlyTag"));
            return tags.Count == 0 ? Nick : $"{Nick} ({string.Join(", ", tags)})";
        }
    }
}

/// <summary>
/// Une session partagée branchée sur un onglet de teambuild : c'est le chef d'orchestre côté
/// écran, et ce que lit le bandeau au-dessus de la grille.
///
/// <list type="bullet">
/// <item><b>Envoi</b> — chaque rafale de modifications (même fenêtre de 500 ms que l'annulation)
///   est enregistrée dans le document partagé (<see cref="SharedTeamDoc.CommitLocal"/>), qui
///   annule au passage toute retouche d'un personnage pris par un autre, puis envoyée.</item>
/// <item><b>Réception</b> — l'état reçu est relu par le sérialiseur de ce poste (compatibilité),
///   fusionné, et l'onglet n'est repeint que si quelque chose a VRAIMENT changé. L'application est
///   différée tant qu'une boîte de dialogue est ouverte, qu'un bouton de souris est enfoncé ou
///   qu'un menu est déroulé : remplacer les personnages sous une fenêtre d'équipement ouverte
///   ferait atterrir ses modifications sur un personnage qui n'existe plus.</item>
/// <item><b>Lecture seule</b> — catalogue ou application trop anciens pour le build reçu : on
///   regarde, on n'envoie rien (sinon on effacerait ce qu'on ne connaît pas).</item>
/// </list>
/// </summary>
public sealed class CollabSessionViewModel : ViewModelBase
{
    private static string T(string key) => LanguageManager.T(key);

    private static readonly string[] Palette =
        ["#1E88E5", "#E53935", "#43A047", "#FB8C00", "#8E24AA", "#00ACC1", "#C0CA33", "#6D4C41"];

    private readonly TeamBuildViewModel _tb;
    private readonly RoomSession _session;
    private readonly CollabBridge _bridge;
    private readonly SharedTeamDoc _shared;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _deferTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly Queue<DateTime> _republishes = new();
    private RoomStateEventArgs? _pendingRemote;
    private long _lastSerial;
    private bool _detached;
    private bool _readOnlyNoticeShown;

    private string _statusText = string.Empty;
    private string _noticeText = string.Empty;
    private string _readOnlyText = string.Empty;

    public string Code => _session.Code;
    public bool IsHost => _session.IsHost;
    public RoomStatus Status => _session.Status;
    public bool IsEnded => _session.Status == RoomStatus.Ended;
    public bool IsLive => !IsEnded;
    public bool IsReadOnly => _readOnlyText.Length > 0;
    public string ReadOnlyText => _readOnlyText;
    public string StatusText => _statusText;
    public string NoticeText => _noticeText;
    public bool HasNotice => _noticeText.Length > 0;
    public ObservableCollection<CollabParticipant> Participants { get; } = new();

    public string ExpiresText => _session.ExpiresAt is { } exp && !IsEnded
        ? string.Format(T("S.Collab.Expires"), exp.ToLocalTime().ToString("t"))
        : string.Empty;

    /// <summary>Connexions présentes qui ne se sont pas encore annoncées (arrivant, ou participant
    /// qui revient d'une coupure) : le serveur les compte, on n'a pas encore leur pseudo.</summary>
    public string UnknownText => !IsEnded && _session.UnknownParticipants is > 0 and var n
        ? string.Format(T("S.Collab.Unknown"), n)
        : string.Empty;

    public bool HasUnknown => UnknownText.Length > 0;

    /// <summary>Levé quand la session s'arrête (départ, fermeture du salon, refus définitif).</summary>
    public event Action? Ended;

    public TeamBuildViewModel Build => _tb;

    public CollabSessionViewModel(TeamBuildViewModel tb, RoomSession session, CollabBridge bridge,
                                  SharedTeamDoc shared, long initialSerial, RoomCompatResult? initialCompat)
    {
        _tb = tb;
        _session = session;
        _bridge = bridge;
        _shared = shared;
        _lastSerial = initialSerial;

        _debounce.Tick += (_, _) =>
        {
            // Une modification faite DANS une boîte de dialogue (propriétés, équipement) part à sa
            // fermeture : si elle visait le personnage d'un autre, la rétablir remplacerait les
            // personnages sous la boîte encore ouverte, et ce qu'on y saisit ensuite serait perdu.
            if (MustDefer()) return;
            _debounce.Stop();
            CommitLocalNow();
        };
        _deferTimer.Tick += (_, _) => TryApplyPending();
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); SetNotice(string.Empty); };

        _tb.Mutated += OnMutated;
        _session.StatusChanged += OnStatusChanged;
        _session.PeersChanged += OnPeersChanged;
        _session.StateReceived += OnStateReceived;
        _session.Notice += OnNotice;

        if (initialCompat is not null) UpdateReadOnly(initialCompat);
        // Hôte comme invité, on s'annonce par un état complet : pour l'hôte c'est l'état de départ
        // (que le serveur gardera pour les arrivants), pour l'invité c'est ce qui dit aux autres
        // qui il est. Un poste en lecture seule s'est déjà annoncé sans build (UpdateReadOnly).
        if (IsReadOnly) _session.SetShared(_shared);
        else _session.Publish(_shared);
        RefreshStatus();
        OnPeersChanged();

        // Un état arrivé entre la connexion et cet abonnement (cf. RoomSession.LatestState).
        if (_session.LatestState is { } latest && latest.Serial > _lastSerial) OnStateReceived(latest);
    }

    // ── Envoi ─────────────────────────────────────────────────────────────────

    private void OnMutated()
    {
        if (_detached) return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Enregistre ce qui est à l'écran et l'envoie s'il y a du neuf. Aussi appelé avant
    /// chaque fusion, pour qu'une modification pas encore partie ne soit pas prise pour du vieux.</summary>
    private void CommitLocalNow()
    {
        if (_detached || IsEnded) return;
        if (IsReadOnly)
        {
            if (!_readOnlyNoticeShown)
            {
                _readOnlyNoticeShown = true;
                SetNotice(T("S.Collab.NoticeReadOnlyEdit"));
            }
            return;
        }

        var outcome = _shared.CommitLocal(_bridge.Capture(_tb), _session.OwnerOf, _bridge.Canon());
        if (outcome.RevertedRoots.Count > 0)
        {
            // Retouche d'un personnage pris par un autre : on remet sa version à l'écran et on
            // dit pourquoi. L'historique d'annulation repart d'ici — sinon Ctrl+Z referait la
            // retouche refusée.
            ApplyToScreen(outcome.Document);
            var owner = OwnerNickOf(outcome.RevertedRoots[0]);
            SetNotice(string.Format(T("S.Collab.NoticeReverted"), owner));
        }
        if (outcome.Changed) _session.Publish(_shared);
        else _session.SetShared(_shared);
        _session.ReleaseMissing(SharedTeamDoc.AllNodeIds(_shared.Doc));
        RefreshClaims();
    }

    // ── Réception ─────────────────────────────────────────────────────────────

    private void OnStateReceived(RoomStateEventArgs e)
    {
        if (_detached || e.Serial <= _lastSerial) return;
        _lastSerial = e.Serial;
        if (MustDefer())
        {
            _pendingRemote = e;
            _deferTimer.Start();
            return;
        }
        ProcessRemote(e);
    }

    /// <summary>
    /// Vrai tant qu'appliquer un état reçu risquerait de défaire un geste en cours : boîte de
    /// dialogue ouverte (équipement, propriétés du personnage…), bouton de souris enfoncé
    /// (glisser-déposer d'une compétence), menu déroulé (son action viserait un personnage
    /// remplacé entre-temps), sélection de cadenas en cours.
    /// </summary>
    private bool MustDefer()
        => ComponentDispatcher.IsThreadModal
           || Mouse.LeftButton == MouseButtonState.Pressed
           || Mouse.RightButton == MouseButtonState.Pressed
           || Keyboard.FocusedElement is System.Windows.Controls.MenuItem or System.Windows.Controls.ContextMenu
           || _tb.IsLockSelectionMode;

    private void TryApplyPending()
    {
        if (_pendingRemote is null || _detached) { _deferTimer.Stop(); return; }
        if (MustDefer()) return;
        _deferTimer.Stop();
        var e = _pendingRemote;
        _pendingRemote = null;
        ProcessRemote(e);
    }

    /// <summary>
    /// Le document vient d'un autre poste — donc potentiellement d'une version de Z-Codex qui
    /// n'existe pas encore, ou d'un programme qui n'est pas Z-Codex. Un paquet qu'on ne sait pas
    /// relire est ignoré (journal de débogage) : il ne doit ni faire planter l'application, ni
    /// afficher la boîte « erreur inattendue » au milieu d'une session.
    /// </summary>
    private void ProcessRemote(RoomStateEventArgs e)
    {
        try { ProcessRemoteCore(e); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine($"[Collab] état reçu de {e.FromNick} ignoré : {ex}");
        }
    }

    private void ProcessRemoteCore(RoomStateEventArgs e)
    {
        var normalized = RoomCompat.Normalize(e.Doc, _bridge.Skills(), out var compat);
        if (normalized is null)
        {
            Debug.WriteLine($"[Collab] état illisible reçu de {e.FromNick}");
            return;
        }
        UpdateReadOnly(compat);

        // Une rafale pas encore partie : on l'enregistre d'abord, sinon la fusion la prendrait
        // pour un état périmé et l'écraserait.
        if (_debounce.IsEnabled)
        {
            _debounce.Stop();
            CommitLocalNow();
        }

        var canon = _bridge.Canon();
        var merged = _shared.MergeRemote(normalized, e.Stamps, e.Order, _session.OwnerOf, canon);
        if (!SharedTeamDoc.SameDocument(_bridge.Capture(_tb), merged.Document, canon))
            ApplyToScreen(merged.Document);

        if (merged.ShouldRepublish && !IsReadOnly && RepublishAllowed()) _session.Publish(_shared);
        else _session.SetShared(_shared);
        RefreshClaims();
    }

    /// <summary>
    /// Garde-fou contre une boucle de renvois : la fusion converge (banc d'essai : 1 000 parties
    /// aléatoires), mais une version de Z-Codex future qui fusionnerait autrement pourrait se
    /// renvoyer indéfiniment la balle avec celle-ci. Au-delà de huit renvois en dix secondes, on
    /// cesse de renvoyer : nos modifications, elles, continuent de partir normalement.
    /// </summary>
    private bool RepublishAllowed()
    {
        var now = DateTime.UtcNow;
        while (_republishes.Count > 0 && now - _republishes.Peek() > TimeSpan.FromSeconds(10)) _republishes.Dequeue();
        if (_republishes.Count >= 8)
        {
            Debug.WriteLine("[Collab] renvois suspendus : trop de renvois consécutifs");
            return false;
        }
        _republishes.Enqueue(now);
        return true;
    }

    private void ApplyToScreen(JsonObject doc)
    {
        _bridge.Apply(_tb, doc);
        _tb.Undo?.Reset();
    }

    private void UpdateReadOnly(RoomCompatResult compat)
    {
        string text = compat.NewerFormat ? T("S.Collab.ReadOnlyNewerApp")
                    : compat.CatalogOutdated ? string.Format(T("S.Collab.ReadOnlyCatalog"), compat.MissingSkillIds.Count)
                    : string.Empty;
        if (text == _readOnlyText) return;
        _readOnlyText = text;
        _session.ReadOnly = text.Length > 0;
        OnPropertyChanged(nameof(ReadOnlyText));
        OnPropertyChanged(nameof(IsReadOnly));
        RefreshClaims();
    }

    // ── Personnages pris ──────────────────────────────────────────────────────

    /// <summary>Clic sur le badge d'un personnage : le prendre s'il est libre, le rendre s'il est à moi.</summary>
    public void ToggleClaim(CharacterSlotViewModel row)
    {
        if (_detached || IsEnded || IsReadOnly) return;
        var root = row;
        while (root.Parent is { } p) root = p;
        var subtree = Subtree(root).Select(n => n.Id).ToList();

        var owner = subtree.Select(_session.OwnerOf).FirstOrDefault(o => o is not null);
        if (owner == _session.ClientId)
        {
            foreach (var id in subtree.Where(_session.MyClaims.Contains).ToList()) _session.Release(id);
        }
        else if (owner is null)
        {
            // Ce qui est à l'écran part d'abord : prendre un personnage ne doit pas figer une
            // modification encore en attente d'envoi dans un état que les autres n'ont pas.
            if (_debounce.IsEnabled) { _debounce.Stop(); CommitLocalNow(); }
            if (!_session.TryClaim(root.Id, out var by))
                SetNotice(string.Format(T("S.Collab.NoticeAlreadyTaken"), by ?? "?"));
        }
        else
        {
            SetNotice(string.Format(T("S.Collab.NoticeAlreadyTaken"), _session.PeerOf(owner)?.Nick ?? "?"));
        }
        RefreshClaims();
    }

    private static IEnumerable<CharacterSlotViewModel> Subtree(CharacterSlotViewModel n)
    {
        yield return n;
        foreach (var v in n.Variants)
            foreach (var d in Subtree(v))
                yield return d;
    }

    /// <summary>Repeint l'état de prise de chaque ligne (les lignes sont recréées à chaque état
    /// reçu : il faut le refaire après chaque application).</summary>
    public void RefreshClaims()
    {
        bool active = !_detached && !IsEnded;
        foreach (var root in _tb.Characters)
        {
            var nodes = Subtree(root).ToList();
            var owner = active ? nodes.Select(n => _session.OwnerOf(n.Id)).FirstOrDefault(o => o is not null) : null;
            var peer = owner is null ? null : _session.PeerOf(owner);
            var state = !active ? CollabClaimState.None
                      : owner is null ? CollabClaimState.Free
                      : owner == _session.ClientId ? CollabClaimState.Mine
                      : CollabClaimState.Other;
            var brush = owner is null ? null : BrushFor(owner);
            foreach (var n in nodes)
            {
                n.CollabIsRoot = ReferenceEquals(n, root);
                n.CollabClaim = state;
                n.CollabOwner = peer?.Nick ?? string.Empty;
                n.CollabBrush = brush;
            }
        }
    }

    private string OwnerNickOf(Guid rootId)
    {
        var owner = _session.OwnerOf(rootId);
        return owner is null ? "?" : _session.PeerOf(owner)?.Nick ?? "?";
    }

    private static readonly Brush[] PaletteBrushes = Palette.Select(hex =>
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return (Brush)b;
    }).ToArray();

    private readonly Dictionary<string, Brush> _colors = new(StringComparer.Ordinal);

    /// <summary>
    /// Attribue les couleurs des participants présents : chacun prend la couleur tirée de son
    /// identifiant, ou la suivante libre si un autre l'a déjà — en parcourant les participants dans
    /// l'ordre de leurs identifiants. Deux participants n'ont donc jamais la même couleur (8 au plus,
    /// autant que de couleurs), et tous les postes, qui voient les mêmes participants, font le
    /// même tirage. (Premier jet : couleur tirée seule, et deux joueurs sur trois se retrouvaient
    /// de la même couleur dès le premier rendu d'essai.)
    /// </summary>
    private void AssignColors(IEnumerable<string> clientIds)
    {
        _colors.Clear();
        var taken = new HashSet<int>();
        foreach (var id in clientIds.OrderBy(x => x, StringComparer.Ordinal))
        {
            int h = 0;
            foreach (var c in id) h = unchecked(h * 31 + c);
            int slot = (h & 0x7fffffff) % PaletteBrushes.Length;
            for (int k = 0; k < PaletteBrushes.Length && !taken.Add(slot); k++) slot = (slot + 1) % PaletteBrushes.Length;
            _colors[id] = PaletteBrushes[slot];
        }
    }

    /// <summary>Couleur d'un participant. Toujours la même INSTANCE pour une couleur donnée : les
    /// prises sont repeintes à chaque signe de vie, et un pinceau neuf à chaque fois lèverait
    /// autant de notifications pour rien.</summary>
    private Brush BrushFor(string clientId)
    {
        if (_colors.TryGetValue(clientId, out var b)) return b;
        int h = 0;
        foreach (var c in clientId) h = unchecked(h * 31 + c);
        return PaletteBrushes[(h & 0x7fffffff) % PaletteBrushes.Length];
    }

    // ── Bandeau ───────────────────────────────────────────────────────────────

    private void OnPeersChanged()
    {
        if (_detached) return;
        var peers = _session.Peers;
        AssignColors(peers.Select(p => p.ClientId));
        Participants.Clear();
        foreach (var p in peers)
            Participants.Add(new CollabParticipant(p.Nick, BrushFor(p.ClientId), p.IsMe, p.IsHost, p.ReadOnly));
        OnPropertyChanged(nameof(ExpiresText));
        OnPropertyChanged(nameof(UnknownText));
        OnPropertyChanged(nameof(HasUnknown));
        RefreshClaims();
    }

    private void OnNotice(RoomNotice n)
    {
        if (_detached) return;
        var text = n.Kind switch
        {
            RoomNoticeKind.PeerJoined => string.Format(T("S.Collab.NoticeJoined"), n.Nick ?? "?"),
            RoomNoticeKind.PeerLeft   => string.Format(T("S.Collab.NoticeLeft"), n.Nick ?? "?"),
            RoomNoticeKind.ClaimLost  => string.Format(T("S.Collab.NoticeClaimLost"), n.Nick ?? "?"),
            _                         => string.Empty,
        };
        if (text.Length > 0) SetNotice(text);
    }

    private void OnStatusChanged()
    {
        if (_detached) return;
        RefreshStatus();
        if (IsEnded)
        {
            _debounce.Stop();
            _deferTimer.Stop();
            _tb.Mutated -= OnMutated;
            RefreshClaims();
            Ended?.Invoke();
        }
    }

    private void RefreshStatus()
    {
        _statusText = _session.Status switch
        {
            RoomStatus.Connecting   => T("S.Collab.StatusConnecting"),
            RoomStatus.Connected    => T("S.Collab.StatusConnected"),
            RoomStatus.Reconnecting => T("S.Collab.StatusReconnecting"),
            _                       => EndedText(),
        };
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsEnded));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(ExpiresText));
    }

    private string EndedText()
    {
        var why = _session.EndReason switch
        {
            RoomEndReason.Left         => T("S.Collab.EndLeft"),
            RoomEndReason.Closed       => T("S.Collab.EndClosed"),
            RoomEndReason.NotFound     => T("S.Collab.EndClosed"),
            RoomEndReason.Expired      => T("S.Collab.EndExpired"),
            RoomEndReason.HostGone     => T(IsHost ? "S.Collab.EndHostGoneSelf" : "S.Collab.EndHostGone"),
            RoomEndReason.Full         => T("S.Collab.EndFull"),
            RoomEndReason.Unauthorized => T("S.Collab.EndUnauthorized"),
            RoomEndReason.NetworkLost  => T("S.Collab.EndNetwork"),
            _                          => string.Format(T("S.Collab.EndOther"), _session.EndDetail ?? "?"),
        };
        return string.Format(T(IsHost ? "S.Collab.EndedHost" : "S.Collab.EndedGuest"), why);
    }

    private void SetNotice(string text)
    {
        _noticeText = text;
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(HasNotice));
        _noticeTimer.Stop();
        if (text.Length > 0) _noticeTimer.Start();
    }

    // ── Fin ───────────────────────────────────────────────────────────────────

    /// <summary>Envoie tout de suite une rafale encore en attente. Vrai s'il y en avait une.</summary>
    private bool FlushPending()
    {
        if (!_debounce.IsEnabled || IsEnded || _detached) return false;
        _debounce.Stop();
        CommitLocalNow();
        return true;
    }

    private Task? _leaving;

    /// <summary>Départ volontaire. Une modification encore en attente part d'abord.</summary>
    public Task LeaveAsync() => _leaving ??= LeaveCoreAsync();

    private async Task LeaveCoreAsync()
    {
        // Laisser au dernier état le temps de partir avant l'au revoir (il est en file d'envoi).
        if (FlushPending()) await Task.Delay(300);
        await Task.Run(_session.LeaveAsync);
    }

    /// <summary>
    /// Départ BLOQUANT, pour la fermeture de l'application. Le réseau part sur un fil à part :
    /// attendre ici une tâche qui voudrait revenir sur le fil de l'interface le bloquerait.
    /// </summary>
    public void LeaveBlocking(TimeSpan timeout)
    {
        bool flushed = FlushPending();
        var session = _session;
        try
        {
            Task.Run(async () =>
            {
                if (flushed) await Task.Delay(300);
                await session.LeaveAsync();
            }).Wait(timeout);
        }
        catch (AggregateException ex) { Debug.WriteLine($"[Collab] départ : {ex.InnerException?.Message}"); }
    }

    /// <summary>Débranche la session de l'onglet (bandeau fermé, onglet fermé). Ne quitte pas le
    /// salon : appeler <see cref="LeaveAsync"/> avant.</summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;
        _debounce.Stop();
        _deferTimer.Stop();
        _noticeTimer.Stop();
        _tb.Mutated -= OnMutated;
        _session.StatusChanged -= OnStatusChanged;
        _session.PeersChanged -= OnPeersChanged;
        _session.StateReceived -= OnStateReceived;
        _session.Notice -= OnNotice;
        foreach (var n in _tb.EnumerateTree())
        {
            n.CollabClaim = CollabClaimState.None;
            n.CollabOwner = string.Empty;
            n.CollabBrush = null;
        }
        // Libérer la connexion APRÈS le départ en cours s'il y en a un : la couper tout de suite
        // (onglet fermé juste après « Quitter ») empêcherait l'au revoir de partir, et les autres
        // garderaient nos personnages pris jusqu'à la fin du délai de silence.
        var leaving = _leaving;
        var session = _session;
        _ = Task.Run(async () =>
        {
            if (leaving is not null)
                try { await leaving; }
                catch (Exception ex) { Debug.WriteLine($"[Collab] départ : {ex.Message}"); }
            await session.DisposeAsync();
        });
    }
}
