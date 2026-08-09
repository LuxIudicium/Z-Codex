using System.IO;

namespace ZCodex.Core;

/// <summary>
/// Emplacement unique des données de l'application (<c>%AppData%\Z-Codex</c>) : base de
/// compétences, réglages, icônes, journaux. Tous les projets passent par ici — aucun littéral
/// de dossier ne doit subsister ailleurs.
///
/// Assure aussi la MIGRATION depuis le nom hérité (constantes <c>Legacy*</c> ci-dessous) : au
/// premier accès, si le nouveau dossier n'existe pas encore et que l'ancien est là, on le renomme
/// (avec son contenu — base, réglages, milliers d'icônes). L'opération n'a lieu qu'une fois.
///
/// Principe de sûreté, appliqué au dossier ET à la base : si un renommage échoue (verrou,
/// droits refusés), on CONTINUE SUR L'ANCIEN CHEMIN plutôt que de repartir sur du vide.
/// L'utilisateur garde son catalogue et ses réglages ; rien n'est perdu, et la migration
/// sera retentée au démarrage suivant.
/// </summary>
public static class AppPaths
{
    // Les deux littéraux « Legacy » sont les noms RÉELS sur disque d'avant le renommage : ce sont
    // des données, pas l'identité de l'application. Les aligner sur le nom courant casserait la
    // migration d'une installation jamais mise à jour.
    private const string FolderName       = "Z-Codex";
    private const string LegacyFolderName = "PawNed3";

    private const string DbFileName       = "zcodex.db";
    private const string LegacyDbFileName = "pawned3.db";

    /// <summary>Dossier de données, créé au besoin. Migration effectuée au premier accès.</summary>
    public static string Root { get; } = ResolveRoot();

    /// <summary>Fichier de la base de compétences, sous son nom courant ou hérité.</summary>
    public static string DbPath { get; } = ResolveDb();

    /// <summary>Chemin dans le dossier de données (ex. <c>In("icons")</c>).</summary>
    public static string In(params string[] parts) =>
        Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>
    /// Rebase un chemin d'icône LU EN BASE sur le dossier d'icônes courant.
    ///
    /// Le scraping enregistre des chemins ABSOLUS : toute la colonne devient morte dès que le
    /// dossier de données bouge — renommage de l'application, profil utilisateur différent, base
    /// recopiée d'une machine à l'autre. Le catalogue s'affichait alors sans la moindre icône,
    /// alors que les fichiers étaient tous là.
    ///
    /// On ne conserve donc QUE le nom du fichier, recollé sur l'emplacement actuel. Fonctionne
    /// aussi bien sur un chemin absolu hérité que sur un simple nom de fichier, et préserve le
    /// fragment « #top » / « #bottom » des icônes doubles Kurzick/Luxon (le « # » n'est pas un
    /// séparateur de chemin, GetFileName le garde).
    /// </summary>
    public static string IconFile(string? storedPath) =>
        string.IsNullOrWhiteSpace(storedPath)
            ? string.Empty
            : Path.Combine(In("icons"), Path.GetFileName(storedPath));

    private static string ResolveRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root    = Path.Combine(appData, FolderName);
        var legacy  = Path.Combine(appData, LegacyFolderName);

        if (!Directory.Exists(root) && Directory.Exists(legacy))
        {
            // Échec → on reste sur l'ancien dossier, contenu intact.
            try { Directory.Move(legacy, root); }
            catch { return legacy; }
        }

        Directory.CreateDirectory(root);
        return root;
    }

    // Appelé APRÈS ResolveRoot (dépendance via Root) : on est donc déjà dans le bon dossier,
    // migré ou hérité. La base suit le renommage pour qu'une installation migrée et une
    // installation neuve soient identiques — sinon les deux fichiers coexisteraient.
    private static string ResolveDb()
    {
        var db       = Path.Combine(Root, DbFileName);
        var legacyDb = Path.Combine(Root, LegacyDbFileName);

        if (!File.Exists(db) && File.Exists(legacyDb))
        {
            // Échec → on continue sur l'ancienne base plutôt que d'en créer une vide.
            try { File.Move(legacyDb, db); }
            catch { return legacyDb; }
        }

        return db;
    }
}
