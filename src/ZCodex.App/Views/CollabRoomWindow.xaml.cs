using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ZCodex.Core.Collab;
using ZCodex.Core.Serialization;
using ZCodex.Core.Sync;

namespace ZCodex.App.Views;

/// <summary>
/// « Extras ▸ Partager ce teambuild en direct… » (hôte) et « Extras ▸ Rejoindre une session
/// partagée… » (invité). La fenêtre fait elle-même la connexion : elle ne se ferme qu'une fois la
/// session ouverte — et, pour un invité, le teambuild reçu — ou abandonnée. Un refus du serveur
/// (code inconnu, salon plein, clé refusée) s'affiche sur place, champs encore remplis.
/// </summary>
public partial class CollabRoomWindow : Window
{
    public enum Mode { Host, Join }

    private static string T(string key) => LanguageManager.T(key);

    /// <summary>Forme d'un code de salon (OpenAPI) : 4 + 3 caractères d'un alphabet sans 0/O/1/I.</summary>
    private static readonly Regex CodeFormat = new("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{3}$", RegexOptions.CultureInvariant);

    private readonly Mode _mode;
    private readonly string? _token;
    private readonly string? _baseUrl;
    private readonly CancellationTokenSource _cts = new();
    private bool _busy;
    private bool _accepted;

    /// <summary>Session ouverte, remise à l'appelant si <see cref="Accepted"/>. Sinon, la fenêtre
    /// l'a fermée elle-même.</summary>
    public RoomSession? Session { get; private set; }

    /// <summary>Invité : le premier teambuild reçu, dont l'onglet sera construit.</summary>
    public RoomStateEventArgs? FirstState { get; private set; }

    public bool Accepted => _accepted;
    public string Nick => NickBox.Text.Trim();

    public CollabRoomWindow(Mode mode, string? nick, string? token, string? baseUrl, string? teamName)
    {
        InitializeComponent();
        _mode = mode;
        _token = token;
        _baseUrl = baseUrl;
        NickBox.Text = nick ?? string.Empty;

        if (mode == Mode.Host)
        {
            Title = T("S.Collab.HostTitle");
            IntroText.Text = string.Format(T("S.Collab.HostIntro"), teamName ?? "");
            CodePanel.Visibility = Visibility.Collapsed;
            OkButton.Content = T("S.Collab.HostCreate");
        }
        else
        {
            Title = T("S.Collab.JoinTitle");
            IntroText.Text = T("S.Collab.JoinIntro");
            OkButton.Content = T("S.Collab.JoinGo");
        }
        Loaded += (_, _) => (NickBox.Text.Length == 0 || mode == Mode.Host ? NickBox : CodeBox).Focus();
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        // Hôte, salon déjà ouvert : ce bouton est devenu « Commencer ».
        if (Session is not null && _mode == Mode.Host)
        {
            _accepted = true;
            Close();
            return;
        }

        if (Nick.Length == 0)
        {
            ShowError(T("S.Collab.NickMissing"));
            NickBox.Focus();
            return;
        }

        string code = string.Empty;
        if (_mode == Mode.Join)
        {
            code = NormalizeCode(CodeBox.Text);
            CodeBox.Text = code;
            if (!CodeFormat.IsMatch(code))
            {
                ShowError(T("S.Collab.CodeInvalid"));
                CodeBox.Focus();
                return;
            }
        }

        SetBusy(true);
        try
        {
            if (_mode == Mode.Host) await HostAsync();
            else await JoinAsync(code);
        }
        catch (OperationCanceledException) { /* fenêtre fermée pendant l'attente */ }
        finally
        {
            if (IsLoaded) SetBusy(false);
        }
    }

    private async Task HostAsync()
    {
        ShowProgress(T("S.Collab.Creating"));
        using var client = new GwRankClient(_token, _baseUrl);
        var created = await client.CreateRoomAsync(_cts.Token);
        if (!created.IsOk)
        {
            ShowError(created.RoomsFull ? T("S.Collab.ErrRoomsFull")
                : created.Status switch
                {
                    GwRankStatus.NoToken or GwRankStatus.Unauthorized => T("S.Collab.ErrToken"),
                    GwRankStatus.NotFound => T("S.Collab.ErrNoRooms"),
                    GwRankStatus.Offline => T("S.Collab.ErrOffline"),
                    GwRankStatus.RateLimited => T("S.GwRank.FailedRateLimited"),
                    _ => string.Format(T("S.Collab.ErrOther"), created.Message ?? "?"),
                });
            return;
        }

        var room = created.Room!;
        ShowProgress(T("S.Collab.Connecting"));
        var session = await ConnectAsync(new RoomSessionOptions
        {
            WebsocketUrl = new Uri(room.WebsocketUrl),
            Origin = RoomEndpoints.SiteOrigin(_baseUrl),
            Code = room.Code,
            CreatorSecret = room.CreatorSecret,
            Nick = Nick,
            Limits = room.Limits,
            ExpiresAt = room.ExpiresAt,
            AppVersion = AppVersion(),
            FormatVersion = TeamBuildSerializer.FormatVersion,
            Context = SynchronizationContext.Current,
        }, wantState: false);
        if (session is null) return;

        Session = session;
        FormPanel.Visibility = Visibility.Collapsed;
        IntroText.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        CodeResult.Text = room.Code;
        OkButton.Content = T("S.Collab.HostStart");
        StatusText.Text = string.Empty;
        // Le code part d'office dans le presse-papier : c'est ce qu'on en fait dans 9 cas sur 10.
        if (SafeClipboard.SetText(room.Code)) ShowProgress(T("S.Collab.CodeCopied"));
    }

    private async Task JoinAsync(string code)
    {
        ShowProgress(T("S.Collab.Connecting"));
        var session = await ConnectAsync(new RoomSessionOptions
        {
            WebsocketUrl = RoomEndpoints.CableUrl(_baseUrl),
            Origin = RoomEndpoints.SiteOrigin(_baseUrl),
            Code = code,
            Nick = Nick,
            AppVersion = AppVersion(),
            FormatVersion = TeamBuildSerializer.FormatVersion,
            Context = SynchronizationContext.Current,
        }, wantState: true);
        if (session is null) return;

        ShowProgress(T("S.Collab.Receiving"));
        var first = await session.WaitForStateAsync(TimeSpan.FromSeconds(25), _cts.Token);
        if (first is null)
        {
            await session.DisposeAsync();
            ShowError(session.Status == RoomStatus.Ended ? ReasonText(session.EndReason, session.EndDetail)
                                                         : T("S.Collab.ErrNoState"));
            return;
        }
        Session = session;
        FirstState = first;
        _accepted = true;
        Close();
    }

    /// <summary>Ouvre la session ; affiche le refus et rend null en cas d'échec.</summary>
    private async Task<RoomSession?> ConnectAsync(RoomSessionOptions options, bool wantState)
    {
        try
        {
            return await RoomSession.StartAsync(options, wantState, _cts.Token);
        }
        catch (RoomConnectException ex)
        {
            Debug.WriteLine($"[Collab] connexion refusée : {ex.Info.Reason} {ex.Info.RawReason} {ex.Info.CloseCode}");
            ShowError(ReasonText(ex.Info.Reason, ex.Info.RawReason));
            return null;
        }
    }

    private static string ReasonText(RoomEndReason reason, string? raw) => reason switch
    {
        RoomEndReason.NotFound     => T("S.Collab.ErrNotFound"),
        RoomEndReason.Closed       => T("S.Collab.ErrNotFound"),
        RoomEndReason.Expired      => T("S.Collab.ErrExpired"),
        RoomEndReason.HostGone     => T("S.Collab.ErrHostGone"),
        RoomEndReason.Full         => T("S.Collab.ErrFull"),
        RoomEndReason.Unauthorized => T("S.Collab.ErrUnauthorized"),
        RoomEndReason.NetworkLost  => T("S.Collab.ErrOffline"),
        _                          => string.Format(T("S.Collab.ErrOther"), raw ?? "?"),
    };

    /// <summary>« kurz 7t4 », « KURZ7T4 », « kurz-7t4 » → « KURZ-7T4 ».</summary>
    public static string NormalizeCode(string text)
    {
        var s = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return s.Length == 7 ? $"{s[..4]}-{s[4..]}" : text.Trim().ToUpperInvariant();
    }

    private static string AppVersion()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (SafeClipboard.SetText(CodeResult.Text)) ShowProgress(T("S.Collab.CodeCopied"));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        OkButton.IsEnabled = !busy;
        NickBox.IsEnabled = !busy;
        CodeBox.IsEnabled = !busy;
    }

    private void ShowProgress(string text)
    {
        StatusText.Foreground = (Brush)FindResource("TextSecondaryBrush");
        StatusText.Text = text;
    }

    private void ShowError(string text)
    {
        StatusText.Foreground = (Brush)FindResource("ErrorTextBrush");
        StatusText.Text = text;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        // Fermée sans valider (Annuler, Échap, croix) : rien ne doit rester ouvert derrière.
        if (!_accepted && Session is { } s)
        {
            Session = null;
            _ = Task.Run(async () =>
            {
                await s.LeaveAsync();
                await s.DisposeAsync();
            });
        }
        base.OnClosed(e);
    }
}
