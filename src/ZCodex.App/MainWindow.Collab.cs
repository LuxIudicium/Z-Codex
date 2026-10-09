using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using ZCodex.App.ViewModels;
using ZCodex.App.Views;
using ZCodex.Core.Collab;
using ZCodex.Core.Models;
using ZCodex.Core.Search;
using ZCodex.Core.Serialization;

namespace ZCodex.App;

// ── Session partagée : co-édition d'un teambuild en direct ─────────────────────────────────────
//
// Une session à la fois, liée à UN onglet de teambuild. L'hôte partage l'onglet qu'il a sous les
// yeux (son fichier reste le sien) ; un invité reçoit le build dans un onglet NEUF, jamais relié à
// un fichier : rien de ce qui se passe dans la session ne peut écraser un fichier de sa
// bibliothèque, il enregistre une copie s'il veut la garder.
//
// Le réseau et la fusion vivent dans ZCodex.Core.Collab ; ici, seulement le pont écran ↔ document
// (conversions VM ↔ modèle, qui n'existent que dans MainWindow) et les gestes de l'utilisateur.
public partial class MainWindow
{
    private CollabSessionViewModel? _collab;

    private CollabBridge CreateCollabBridge() => new()
    {
        Capture = CaptureCollabDoc,
        Apply = ApplyCollabDoc,
        Skills = () => _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s),
        Canon = CollabCanon,
    };

    /// <summary>L'onglet en document .zcx, tel que la session le compare et l'envoie. Le mode de
    /// jeu n'y est PAS estampillé : c'est un réglage d'affichage de chaque participant.</summary>
    private static JsonObject CaptureCollabDoc(TeamBuildViewModel tb)
    {
        var model = ViewModelToModel(tb);
        model.UpdatedAt = default;
        model.GameMode = null;
        return (JsonObject)JsonNode.Parse(TeamBuildSerializer.Serialize(model))!;
    }

    /// <summary>
    /// Remplace le contenu de l'onglet par un état reçu — même chemin que l'annulation (Ctrl+Z),
    /// qui a fait ses preuves : l'onglet, son fichier et son activation survivent. On y ajoute ce
    /// qu'une annulation n'a pas à préserver mais qu'un état venu d'ailleurs ne doit pas défaire :
    /// les variantes dépliées ou repliées, le filtre de cadenas, le personnage choisi pour la
    /// molette, et la variante PvE/PvP des compétences selon le mode affiché chez SOI.
    /// </summary>
    private void ApplyCollabDoc(TeamBuildViewModel tb, JsonObject doc)
    {
        if (!ApplyCollabDocCore(tb, doc, _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s))) return;
        _vm.ApplyGameModeTo(tb);
        _vm.RefreshTeamConditionBand();
    }

    /// <summary>Le cœur de <see cref="ApplyCollabDoc"/>, sans dépendance à la fenêtre (le banc
    /// d'essai l'appelle tel quel, sur de vrais onglets).</summary>
    private static bool ApplyCollabDocCore(TeamBuildViewModel tb, JsonObject doc, IReadOnlyDictionary<int, Skill> skillsById)
    {
        var model = TeamBuildSerializer.Deserialize(doc.ToJsonString(), skillsById);
        if (model is null)
        {
            Debug.WriteLine($"[Collab] état illisible pour '{tb.Name}' — non appliqué");
            return false;
        }

        var expanded = tb.EnumerateTree().GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First().IsExpanded);
        int? lockIndex = tb.ActiveLockFilter?.Index;
        Guid? wheelId = tb.WheelSelection?.Id;

        tb.BeginRestore();
        try
        {
            PopulateViewModel(tb, model);
            foreach (var n in tb.EnumerateTree())
                if (expanded.TryGetValue(n.Id, out var open)) n.IsExpanded = open;
        }
        finally { tb.EndRestore(); }

        if (lockIndex is { } li && tb.Locks.FirstOrDefault(l => l.Index == li) is { } lk) tb.SetLockFilter(lk);
        if (wheelId is { } wid) tb.SelectForWheel(tb.EnumerateTree().FirstOrDefault(n => n.Id == wid));
        return true;
    }

    private Dictionary<int, int>? _canonMap;
    private int _canonMapSize = -1;

    /// <summary>
    /// Équivalence PvE/PvP pour la comparaison des documents : nulle en mode « Tout » (où
    /// l'utilisateur choisit lui-même sa variante, et où la changer EST une modification), sinon
    /// id → id de la version de base (même règle que les codes de template, cf.
    /// <see cref="SkillVariants.TemplateIdsByName"/>).
    /// </summary>
    private Func<int, int>? CollabCanon()
    {
        if (_vm.SkillPanel.SelectedGameMode == SkillGameMode.All) return null;
        var all = _vm.SkillPanel.AllSkills;
        if (_canonMap is null || _canonMapSize != all.Count)
        {
            var byName = SkillVariants.TemplateIdsByName(all);
            _canonMap = all.ToDictionary(s => s.Id, s => byName.TryGetValue(s.Name, out var b) ? b : s.Id);
            _canonMapSize = all.Count;
        }
        var map = _canonMap;
        return id => map.TryGetValue(id, out var b) ? b : id;
    }

    // ── Gestes ────────────────────────────────────────────────────────────────

    private bool CollabPreconditions()
    {
        if (_collab is { IsLive: true } live)
        {
            MessageBox.Show(string.Format(T("S.Collab.AlreadyInSession"), live.Build.Name),
                T("S.Collab.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            if (_vm.OpenTeamBuilds.Contains(live.Build)) _vm.ActiveTeamBuild = live.Build;
            return false;
        }
        if (_vm.SkillPanel.AllSkills.Count == 0)
        {
            MessageBox.Show(T("S.Msg.CatalogNotLoaded"), T("S.Msg.CatalogNotLoadedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    /// <summary>« Extras ▸ Partager ce teambuild en direct… »</summary>
    private void CollabHost_Click(object sender, RoutedEventArgs e)
    {
        if (!CollabPreconditions()) return;
        if (!_vm.IsTeamBuildActive || _vm.ActiveTeamBuild is not { } tb)
        {
            MessageBox.Show(T("S.Collab.NeedTeamBuild"), T("S.Collab.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // La création d'un salon demande un compte GWRank ; rejoindre, non.
        if (!HasGwRankToken() && !OpenGwRankSettings()) return;

        var win = new CollabRoomWindow(CollabRoomWindow.Mode.Host, _settings.CollabNickname,
                                       _settings.GwRankApiToken, _settings.GwRankBaseUrl, tb.Name) { Owner = this };
        win.ShowDialog();
        RememberNick(win.Nick);
        if (!win.Accepted || win.Session is not { } session) return;

        // Ce qui est à l'écran EST l'état de départ : l'hôte n'a rien à attendre de personne.
        var shared = SharedTeamDoc.FromLocal(session.ClientId, CaptureCollabDoc(tb));
        AttachCollab(tb, session, shared, 0, null);
    }

    /// <summary>« Extras ▸ Rejoindre une session partagée… »</summary>
    private void CollabJoin_Click(object sender, RoutedEventArgs e)
    {
        if (!CollabPreconditions()) return;

        var win = new CollabRoomWindow(CollabRoomWindow.Mode.Join, _settings.CollabNickname,
                                       null, _settings.GwRankBaseUrl, null) { Owner = this };
        win.ShowDialog();
        RememberNick(win.Nick);
        if (!win.Accepted || win.Session is not { } session || win.FirstState is not { } first) return;

        var skillsById = _vm.SkillPanel.AllSkills.ToDictionary(s => s.Id, s => s);
        var normalized = RoomCompat.Normalize(first.Doc, skillsById, out var compat);
        var model = normalized is null ? null : TeamBuildSerializer.Deserialize(normalized.ToJsonString(), skillsById);
        if (normalized is null || model is null)
        {
            _ = Task.Run(async () => { await session.LeaveAsync(); await session.DisposeAsync(); });
            MessageBox.Show(T("S.Collab.ErrUnreadable"), T("S.Collab.Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Onglet NEUF, sans fichier : la session ne peut rien écraser dans la bibliothèque de
        // l'invité. « Enregistrer » ouvrira la boîte « Enregistrer sous ».
        var tb = ModelToViewModel(model);
        _vm.ApplyGameModeTo(tb);
        tb.BeginTracking();
        _vm.OpenTeamBuilds.Add(tb);
        _vm.ActiveTeamBuild = tb;

        var shared = SharedTeamDoc.FromRemote(session.ClientId, normalized, first.Stamps, first.Order);
        AttachCollab(tb, session, shared, first.Serial, compat);
    }

    private CollabSessionViewModel AttachCollab(TeamBuildViewModel tb, RoomSession session, SharedTeamDoc shared,
                                                long serial, RoomCompatResult? compat)
    {
        var collab = new CollabSessionViewModel(tb, session, CreateCollabBridge(), shared, serial, compat);
        _collab = collab;
        tb.Collab = collab;
        return collab;
    }

    private void RememberNick(string nick)
    {
        if (nick.Length == 0 || nick == _settings.CollabNickname) return;
        _settings.CollabNickname = nick;
        _settings.Save();
    }

    /// <summary>« Quitter la session » (menu Extras et bandeau).</summary>
    private async void CollabLeave_Click(object sender, RoutedEventArgs e)
    {
        if (_collab is not { IsLive: true } collab) return;
        var answer = MessageBox.Show(
            T(collab.IsHost ? "S.Collab.LeaveConfirmHost" : "S.Collab.LeaveConfirmGuest"),
            T("S.Collab.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        await collab.LeaveAsync();
    }

    private void CollabCopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild?.Collab is { } c) SafeClipboard.SetText(c.Code);
    }

    /// <summary>Bandeau d'une session terminée : le refermer rend à l'onglet son aspect normal.</summary>
    private void CollabDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTeamBuild is { Collab: { IsEnded: true } c } tb) DetachCollab(tb, c);
    }

    /// <summary>Clic sur le badge de prise d'un personnage.</summary>
    private void CollabClaim_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CharacterSlotViewModel row && _vm.ActiveTeamBuild?.Collab is { } c)
        {
            c.ToggleClaim(row);
            e.Handled = true;
        }
    }

    private void DetachCollab(TeamBuildViewModel tb, CollabSessionViewModel collab)
    {
        collab.Detach();
        if (ReferenceEquals(tb.Collab, collab)) tb.Collab = null;
        if (ReferenceEquals(_collab, collab)) _collab = null;
    }

    /// <summary>
    /// Fermeture d'un onglet en session : on le dit, et le départ ne se fait qu'une fois la
    /// fermeture confirmée. ⚠ Pas les textes de « Quitter », qui promettent que l'onglet reste.
    /// L'invité dont l'onglet n'existe sur aucun disque se voit proposer d'en enregistrer une
    /// copie : <paramref name="saveHandled"/> dit alors que la question « enregistrer ? » est posée.
    /// </summary>
    private bool ConfirmCollabTabClose(TeamBuildViewModel tb, out bool saveHandled)
    {
        saveHandled = false;
        if (tb.Collab is not { IsLive: true } c) return true;
        if (!c.IsHost && tb.FilePath is null)
        {
            saveHandled = true;
            var answer = MessageBox.Show(T("S.Collab.CloseTabGuest"), T("S.Collab.Title"),
                                         MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return false;
            // « Enregistrer sous » abandonné : on reste dans la session plutôt que de tout perdre.
            return answer == MessageBoxResult.No || SaveTeamBuild(tb);
        }
        return MessageBox.Show(
            T(c.IsHost ? "S.Collab.CloseTabHost" : "S.Collab.CloseTabSaved"),
            T("S.Collab.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }

    /// <summary>L'onglet se ferme pour de bon : départ du salon (sans attendre) et débranchement.</summary>
    private void EndCollabForClosedTab(TeamBuildViewModel tb)
    {
        if (tb.Collab is not { } c) return;
        if (c.IsLive) _ = c.LeaveAsync();
        DetachCollab(tb, c);
    }

    /// <summary>Fermeture de l'application : un au revoir aux autres, borné à 2 s — sans lui,
    /// leurs écrans garderaient nos personnages pris encore 45 s.</summary>
    private void LeaveCollabOnExit()
    {
        if (_collab is not { } c) return;
        if (c.IsLive) c.LeaveBlocking(TimeSpan.FromSeconds(2));
        DetachCollab(c.Build, c);
    }
}
