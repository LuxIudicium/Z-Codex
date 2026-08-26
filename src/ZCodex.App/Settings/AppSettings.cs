using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZCodex.App.ViewModels;
using ZCodex.Core;

namespace ZCodex.App.Settings;

// Préférences applicatives persistées dans %AppData%\Z-Codex\settings.json.
// Même patron que ScrapeInfo (Load/Save statiques, catch JsonException).
public class AppSettings
{
    // Dossier racine du browser, choisi par l'utilisateur. Null = auto-détection au démarrage.
    public string? TemplatesRootPath { get; set; }

    // Destination du dernier import des build packs PvX (Extras ▸ Télécharger les builds PvX).
    // Null = jamais importé ; la fenêtre propose alors TemplatesRootPath.
    public string? PvxDestinationPath { get; set; }

    // ── GWRank (Extras ▸ Envoyer sur GWRank) ────────────────────────────────
    // Jeton d'API personnel, créé par l'utilisateur sur sa page de profil GWRank. Null = aucune
    // synchronisation configurée : le menu propose alors d'ouvrir les réglages.
    // ⚠ Stocké en clair, comme le reste du fichier : ce n'est PAS un coffre-fort. C'est un jeton
    // de bibliothèque de builds, révocable côté GWRank — pas un mot de passe de compte.
    public string? GwRankApiToken { get; set; }

    // Serveur visé. Null = production (GwRankClient.DefaultBaseUrl) ; le champ existe pour viser
    // une instance de test sans recompiler.
    public string? GwRankBaseUrl { get; set; }

    // Dépôt public par défaut. false = privé, le choix prudent : un build ne devient visible des
    // autres joueurs que sur décision explicite.
    public bool GwRankPublicByDefault { get; set; }

    // Rafraîchir le miroir GWRank du navigateur au lancement (Extras ▸ Synchroniser au démarrage).
    // false par défaut : c'est un accès réseau au lancement, il se demande.
    public bool GwRankSyncOnStartup { get; set; }

    // Dossiers cochés lors du dernier envoi de bibliothèque, en chemins complets. Vide = rien de
    // choisi, l'utilisateur désignera. Rien n'est coché d'office : la bibliothèque de référence
    // contient des milliers de gabarits venus de packs téléchargés, qui n'ont pas à partir sur
    // GWRank parce qu'ils se trouvaient sous la même racine.
    public List<string> GwRankBulkFolders { get; set; } = [];

    // Fichiers écartés à la main dans ces dossiers, en chemins complets. On retient les
    // EXCEPTIONS et non la sélection : cocher un dossier coche tout son contenu, et les exceptions
    // se comptent en dizaines là où la sélection peut se compter en milliers. Un chemin disparu
    // est simplement ignoré au chargement suivant.
    public List<string> GwRankBulkExcluded { get; set; } = [];

    // Géométrie de MainWindow, sauvegardée à la fermeture. Null = valeurs par défaut du XAML.
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }

    // Thème sombre (menu Affichage). false = thème clair d'origine.
    public bool DarkTheme { get; set; }

    // Langue d'affichage ("fr"/"en", menu Affichage ▸ Langue). UI + textes des compétences.
    public string Language { get; set; } = "fr";

    // Cran de taille des icônes (menu Affichage ▸ Taille des icônes). Pilote aussi la hauteur des
    // lignes de perso. Grande par défaut. Sérialisé en chaîne ("Large"/"Medium"/"Small").
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IconSizeMode IconSize { get; set; } = IconSizeMode.Large;

    // Bandeaux de conditions infligeables (menu Affichage) : sous le build (éditeur) et
    // en ligne unique au-dessus du teambuild. Visible par défaut.
    public bool ShowConditions { get; set; } = true;

    // Colonnes « Types » et « Mechanics » du catalogue (cases à cocher de la barre de recherche =
    // menu Affichage). Masquées par défaut ; masquer une colonne remet SON filtre sur « All ».
    // Deux réglages séparés depuis le 20/08/2026 — l'ancien booléen unique est relu une dernière
    // fois pour ne pas faire disparaître les colonnes de ceux qui les avaient affichées.
    public bool ShowTypeColumn { get; set; } = false;
    public bool ShowMechanicColumn { get; set; } = false;

    /// <summary>Ancien réglage unique (≤ 1.1.0). Nul dans un fichier écrit depuis la séparation.</summary>
    public bool? ShowCategoryColumns { get; set; }

    // Ligne de puces « Skill Types » des écrans Build et Recherche : dépliée, ou réduite au filtre
    // actif. Repliée par défaut. (Les mécaniques n'ont plus de ligne de puces : elles sont un rail.)
    public bool TypeChipsExpanded { get; set; } = false;

    // Bandeau des rituels de la nature : afficher TOUS les rituels (cliquables même non équipés)
    // au lieu des seuls équipés. Masqué par défaut.
    public bool ShowAllNatureRituals { get; set; } = false;

    // Afficher la BARRE des rituels de la nature (teambuild + éditeur). Visible par défaut.
    public bool ShowNatureRituals { get; set; } = true;

    // Section « Dégâts selon l'armure » dans l'infobulle de skill (menu Affichage). Visible par défaut.
    public bool ShowArmorDamage { get; set; } = true;

    // Molette dans la grille du teambuild : exiger un clic sur le personnage avant de régler ses
    // caractéristiques (menu Affichage). Décoché par défaut → geste d'origine, celui de paw·ned².
    // Indépendant du verrou de salve, lui toujours actif (cf. WheelMayAdjust dans MainWindow).
    public bool WheelNeedsSelection { get; set; } = false;

    // Niveau d'armure personnalisé unique (héritage pré-multi) : migré dans CustomArmorLevels
    // au chargement, remis à null à la prochaine sauvegarde de la modale.
    public int? CustomArmorLevel { get; set; }

    // Niveaux d'armure personnalisés ajoutés aux colonnes fixes 60/80/100/120 (max 8).
    public List<int> CustomArmorLevels { get; set; } = new();

    // Niveaux pour le calcul des dégâts : personnage (lanceur, 1–20) et cible (1–40, taux de
    // critique). 20/20 = comportement niveau max du jeu.
    public int CharacterLevel { get; set; } = 20;
    public int TargetLevel { get; set; } = 20;

    // Profils de cible nommés du « Spike damage calculus » (fenêtre Spike des teambuilds).
    public List<SpikeTargetProfile> SpikeTargets { get; set; } = new();

    // Profils nommés du calculateur d'armure (chantier 14, onglet Extras → Calculateur d'armure).
    public List<ArmorCalcProfile> ArmorProfiles { get; set; } = new();

    // Date (UTC) de la dernière interrogation des Releases GitHub. Une vérification par jour
    // suffit largement — Z-Codex ne sort pas trois versions dans la même journée, et c'est le
    // démarrage de l'application qu'on ne veut pas ralentir. Aide ▸ Rechercher les mises à jour
    // passe outre : un clic explicite vaut demande.
    public DateTime? LastUpdateCheckUtc { get; set; }

    // Version écartée par l'utilisateur via la case à cocher de la modale de mise à jour. Une
    // version PLUS RÉCENTE que celle-ci redonne de la voix — sinon un seul refus rendrait
    // l'application définitivement muette. Même principe que ScrapeInfo.IgnoredUpdateDate.
    public string? IgnoredUpdateVersion { get; set; }

    /// <summary>Fichier des préférences. Public pour que le message d'échec d'écriture montre le
    /// chemin exact sans le réinventer de son côté.</summary>
    public static string FilePath => AppPaths.In("settings.json");

    // ── Sûreté de la clé d'API GWRank ────────────────────────────────────────
    // Elle est le SEUL réglage que l'utilisateur ne peut pas reconstituer de tête : perdre une
    // case à cocher se recoche, perdre la clé oblige à retourner sur GWRank. Les deux drapeaux
    // ci-dessous existent pour qu'elle ne soit saisie qu'une fois — jamais deux.

    // Un fichier de réglages EXISTAIT mais n'a pas pu être relu (JSON tronqué, verrou, droits) :
    // cette instance n'est alors qu'un jeu de valeurs par défaut. L'écrire remplacerait les vrais
    // réglages par du vide — clé comprise. On préfère perdre les préférences de la séance.
    private bool _unreadableFile;

    // La clé a été posée ou effacée PENDANT cette séance. Tant que ce n'est pas le cas, cette
    // instance n'a aucune autorité sur elle : rien n'interdit deux Z-Codex ouverts en même temps
    // (aucun verrou d'instance unique), et notre instantané périmé effacerait la clé que l'autre
    // vient d'enregistrer. Le fichier fait alors foi, pas nous.
    private bool _gwRankTokenTouched;

    // Copie de secours de la SEULE clé d'API, dans un fichier à part. Mesuré le 25/08/2026 sur le
    // poste de Philippe : une version ANCIENNE de Z-Codex ouverte en même temps (la 1.2.0
    // installée, antérieure à GWRank) réécrit settings.json depuis un modèle qui ignore ces
    // réglages — les quatre propriétés GwRank* disparaissent purement et simplement du fichier
    // quand elle se ferme, et la clé avec. Aucun code de la version courante ne peut l'en
    // empêcher : cette version-là est déjà installée. En revanche, elle ne connaît pas ce
    // fichier-ci et n'y touchera jamais — la clé s'y retrouve, et n'est pas à ressaisir.
    private static string TokenBackupPath => AppPaths.In("gwrank_key.txt");

    /// <summary>Pose (ou efface, avec null) la clé d'API GWRank. Passer par ici plutôt que par la
    /// propriété : c'est ce geste qui donne à cette instance le droit de l'écrire sur le disque,
    /// et qui tient la copie de secours à jour.</summary>
    public void SetGwRankToken(string? token)
    {
        GwRankApiToken      = string.IsNullOrWhiteSpace(token) ? null : token;
        _gwRankTokenTouched = true;
        // Effacer la clé efface AUSSI la copie : un retrait volontaire ne doit pas ressusciter.
        WriteTokenBackup(GwRankApiToken);
    }

    /// <summary>Relit la clé sur le disque et l'adopte si on n'en a pas. À appeler avant de
    /// conclure « aucune clé configurée » : une autre fenêtre de Z-Codex a pu l'enregistrer
    /// depuis notre démarrage, et redemander sa clé à quelqu'un qui vient de la saisir est le
    /// meilleur moyen de lui faire croire qu'elle n'a pas été retenue.</summary>
    public void ReloadGwRankToken()
    {
        if (_gwRankTokenTouched) return;   // la clé effacée en séance ne doit pas ressusciter
        if (TryEffectiveDiskToken(out var onDisk) && !string.IsNullOrWhiteSpace(onDisk))
            GwRankApiToken = onDisk;
    }

    /// <summary>La clé enregistrée, vue du disque : celle de settings.json, ou à défaut la copie
    /// de secours. Renvoie false quand settings.json est là mais illisible — on ne sait alors
    /// rien, ce qui n'est pas la même chose que « aucune clé ».</summary>
    private static bool TryEffectiveDiskToken(out string? token)
    {
        if (!TryReadTokenOnDisk(out token)) return false;
        if (string.IsNullOrWhiteSpace(token)) token = ReadTokenBackup();
        return true;
    }

    private static string? ReadTokenBackup()
    {
        try
        {
            if (!File.Exists(TokenBackupPath)) return null;
            var token = File.ReadAllText(TokenBackupPath).Trim();
            return token.Length == 0 ? null : token;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings: copie de secours illisible {TokenBackupPath} — {ex.Message}");
            return null;
        }
    }

    private static void WriteTokenBackup(string? token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                File.Delete(TokenBackupPath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(TokenBackupPath)!);
            File.WriteAllText(TokenBackupPath, token);
        }
        // Sans conséquence immédiate : la clé reste dans settings.json, c'est seulement le filet
        // qui manque. Rien à annoncer à l'utilisateur, qui n'a rien demandé de ce côté.
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings: copie de secours non écrite {TokenBackupPath} — {ex.Message}");
        }
    }

    /// <summary>Lit la seule clé GWRank du fichier. Renvoie false quand le fichier est là mais
    /// illisible — cas où l'on ne sait RIEN de la clé enregistrée, à ne pas confondre avec
    /// « aucune clé » : c'est cette confusion qui l'effacerait.</summary>
    private static bool TryReadTokenOnDisk(out string? token)
    {
        token = null;
        if (!File.Exists(FilePath)) return true;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (doc.RootElement.TryGetProperty(nameof(GwRankApiToken), out var el) &&
                el.ValueKind == JsonValueKind.String)
                token = el.GetString();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings: clé GWRank illisible dans {FilePath} — {ex.Message}");
            return false;
        }
    }

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return Recovered(new AppSettings());
        try
        {
            return Recovered(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings());
        }
        // Volontairement TOUTES les exceptions, pas seulement JsonException : le fichier peut
        // aussi être verrouillé (IOException), inaccessible (UnauthorizedAccessException) ou sur
        // un chemin invalide. Les préférences ne valent pas un démarrage raté — on repart des
        // valeurs par défaut. Load() est appelé dans un initialiseur de champ de MainWindow :
        // ce qui échappe ici tue l'application avant même sa première fenêtre.
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings.Load: {FilePath} illisible — {ex.Message}");
            return Recovered(new AppSettings { _unreadableFile = true });
        }
    }

    /// <summary>Rend les réglages avec leur clé d'API même si le fichier vient de la perdre : une
    /// version ancienne de Z-Codex ouverte en parallèle la fait disparaître de settings.json en se
    /// fermant (cf. <see cref="TokenBackupPath"/>). La copie de secours la remet en place sans que
    /// l'utilisateur ait à la ressaisir, et la prochaine sauvegarde la réinscrit dans le fichier.</summary>
    private static AppSettings Recovered(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GwRankApiToken))
            settings.GwRankApiToken = ReadTokenBackup();
        return settings;
    }

    /// <summary>Écrit les réglages. Renvoie false si RIEN n'a été écrit : l'appelant qui vient de
    /// recevoir une saisie de l'utilisateur doit alors le lui dire, sinon il la croira enregistrée
    /// jusqu'au prochain lancement.</summary>
    public bool Save()
    {
        if (_unreadableFile)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings.Save: {FilePath} illisible au démarrage — écriture refusée");
            return false;
        }

        // La clé du disque fait foi tant que cette séance n'y a pas touché (cf. _gwRankTokenTouched).
        if (!_gwRankTokenTouched)
        {
            if (!TryEffectiveDiskToken(out var onDisk)) return false;   // clé inconnue : ne rien écrire
            GwRankApiToken = onDisk;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            // Écriture en deux temps : une écriture directe interrompue (plantage, coupure,
            // antivirus) laisse un settings.json tronqué que le lancement suivant ne sait plus
            // relire — et tous les réglages repartent de zéro. Le remplacement, lui, est atomique.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
            // Le filet est tenu à jour même pour une clé qui n'est pas passée par SetGwRankToken :
            // sinon il ne protégerait que ceux qui ont saisi leur clé APRÈS cette version.
            if (!string.IsNullOrWhiteSpace(GwRankApiToken) && ReadTokenBackup() != GwRankApiToken)
                WriteTokenBackup(GwRankApiToken);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings.Save: échec écriture {FilePath} — {ex.Message}");
            // Ne pas laisser traîner un settings.json.tmp à moitié écrit dans le dossier de données.
            try { File.Delete(FilePath + ".tmp"); } catch { /* rien de plus à tenter */ }
            return false;
        }
    }
}
