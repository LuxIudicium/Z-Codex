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
    // false par défaut : l'API n'a aucun filtre « ce qui a changé », donc chaque synchronisation
    // rapatrie toute la bibliothèque visible — ce n'est pas à imposer au démarrage sans accord.
    public bool GwRankSyncOnStartup { get; set; }

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

    private static string FilePath => AppPaths.In("settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        // Volontairement TOUTES les exceptions, pas seulement JsonException : le fichier peut
        // aussi être verrouillé (IOException), inaccessible (UnauthorizedAccessException) ou sur
        // un chemin invalide. Les préférences ne valent pas un démarrage raté — on repart des
        // valeurs par défaut. Load() est appelé dans un initialiseur de champ de MainWindow :
        // ce qui échappe ici tue l'application avant même sa première fenêtre.
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings.Load: {FilePath} illisible — {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings.Save: échec écriture {FilePath} — {ex.Message}");
        }
    }
}
