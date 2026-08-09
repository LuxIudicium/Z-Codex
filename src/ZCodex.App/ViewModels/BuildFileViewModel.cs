using System.IO;
using System.Text.Json;
using ZCodex.Core.Data;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;
using ZCodex.Core.Serialization;

namespace ZCodex.App.ViewModels;

public class BuildFileViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyDictionary<int, Skill>>? _skillsProvider;
    // Professions primaires de l'équipe (clé logique) : Players/Campaigns en dérivent à la lecture,
    // pour que les abréviations suivent la langue courante sans relire le fichier.
    private List<int>? _primaries;
    private Profession _primaryProfession;
    private Profession _secondaryProfession;
    private string? _highestSecondaryAttribute;
    private Skill? _eliteSkillObj;
    private int _playerCount;
    private bool _metaLoaded;
    private bool _loadingMeta;
    private bool _isEquipmentTemplate;

    public string FilePath { get; }
    public string Name { get; }
    public DateTime Modified { get; }

    // Extension normalisée = clé logique ; le libellé de format affiché suit la langue courante.
    // Le .txt couvre DEUX formats du jeu (compétences et équipement) : seul le décodage les
    // distingue, d'où le chargement des métadonnées avant de trancher.
    private readonly string _ext;
    public string Format
    {
        get
        {
            if (_ext == ".txt") EnsureMetaLoaded();
            return _ext switch
            {
                ".zcx"  => ZCodex.App.LanguageManager.T("S.Browser.FormatPn3"),
                ".pn3"  => ZCodex.App.LanguageManager.T("S.Browser.FormatLegacyPn3"),
                ".pwnd" => ZCodex.App.LanguageManager.T("S.Browser.FormatPwnd"),
                ".txt"  => ZCodex.App.LanguageManager.T(
                               _isEquipmentTemplate ? "S.Browser.FormatEquip" : "S.Browser.FormatTxt"),
                _       => _ext,
            };
        }
    }

    // EN « 8 – Mo, Me, A, Rt » / FR « 8 – M, En, A, Rt » : effectif + abréviations uniques triées.
    public string? Players
    {
        get { EnsureMetaLoaded(); return _primaries is { Count: > 0 } p ? FormatPlayers(p) : null; }
    }

    // "F", "N", "F, N" or "" for core-only (lettres identiques dans les deux langues)
    public string? Campaigns
    {
        get { EnsureMetaLoaded(); return _primaries is { Count: > 0 } p ? FormatCampaigns(p) : null; }
    }

    // Vrai pour un .txt qui porte des codes d'ÉQUIPEMENT (P) et non de compétences (O).
    // Classification unique du navigateur : la liste ET l'aperçu s'y réfèrent, aucun des deux
    // ne redécode le fichier pour se faire son propre avis.
    public bool IsEquipmentTemplate
    {
        get { EnsureMetaLoaded(); return _isEquipmentTemplate; }
    }

    // Nombre de personnages de l'équipe (clé de tri de la colonne Players/Professions).
    public int PlayerCount
    {
        get { EnsureMetaLoaded(); return _playerCount; }
        private set => SetField(ref _playerCount, value);
    }

    // Skill-template columns (single-character .txt). Empty/None for multi-char builds.
    public Profession PrimaryProfession
    {
        get { EnsureMetaLoaded(); return _primaryProfession; }
        private set => SetField(ref _primaryProfession, value);
    }

    public Profession SecondaryProfession
    {
        get { EnsureMetaLoaded(); return _secondaryProfession; }
        private set => SetField(ref _secondaryProfession, value);
    }

    public string? HighestSecondaryAttribute
    {
        get { EnsureMetaLoaded(); return _highestSecondaryAttribute; }
        private set => SetField(ref _highestSecondaryAttribute, value);
    }

    // Nom EN de l'élite (clé logique) ; l'affichage passe par EliteSkillDisplay.
    public string EliteSkill { get { EnsureMetaLoaded(); return _eliteSkillObj?.Name ?? string.Empty; } }

    // ── Libellés AFFICHÉS (langue courante). Les propriétés ci-dessus restent les clés EN. ──
    public string EliteSkillDisplay { get { EnsureMetaLoaded(); return _eliteSkillObj?.DisplayName ?? string.Empty; } }

    public string PrimaryProfessionName =>
        PrimaryProfession == Profession.None ? string.Empty : PrimaryProfession.DisplayName();

    public string SecondaryProfessionName =>
        SecondaryProfession == Profession.None ? string.Empty : SecondaryProfession.DisplayName();

    public string HighestSecondaryAttributeDisplay =>
        HighestSecondaryAttribute is { Length: > 0 } a ? GwAttributeData.DisplayName(a) : string.Empty;

    // Bascule de langue : seuls les libellés affichés changent (aucune relecture de fichier).
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Format));
        OnPropertyChanged(nameof(Players));
        OnPropertyChanged(nameof(PrimaryProfessionName));
        OnPropertyChanged(nameof(SecondaryProfessionName));
        OnPropertyChanged(nameof(HighestSecondaryAttributeDisplay));
        OnPropertyChanged(nameof(EliteSkillDisplay));
    }

    public BuildFileViewModel(string filePath, Func<IReadOnlyDictionary<int, Skill>>? skillsProvider = null)
    {
        _skillsProvider = skillsProvider;
        FilePath = filePath;
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        _ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        Modified = File.GetLastWriteTime(filePath);
    }

    private void EnsureMetaLoaded()
    {
        if (_metaLoaded) return;

        // Le chargement notifie (professions, élite, format) ; une liaison WPF peut relire la
        // propriété dans la foulée, avant que _metaLoaded ne soit posé. Sans ce verrou on
        // repartirait pour un tour de décodage à chaque notification.
        if (_loadingMeta) return;
        _loadingMeta = true;
        try { LoadMeta(); }
        finally { _loadingMeta = false; }
    }

    private void LoadMeta()
    {
        if (System.IO.Path.GetExtension(FilePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            // Retry tant que le catalogue n'est pas prêt (Elite Skill a besoin des noms).
            _metaLoaded = LoadTxtMeta();
            return;
        }

        _metaLoaded = true;
        var profs = TeamBuildSerializer.IsNativeExtension(System.IO.Path.GetExtension(FilePath))
            ? ParsePn3Primaries()
            : ParsePwndPrimaries();
        _playerCount = profs.Count;
        if (profs.Count == 0) return;
        _primaries = profs;
    }

    // Returns the list of primary profession IDs (one per character)
    private List<int> ParsePn3Primaries()
    {
        var result = new List<int>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (!doc.RootElement.TryGetProperty("characters", out var chars)) return result;
            foreach (var c in chars.EnumerateArray())
            {
                if (c.TryGetProperty("primaryProfession", out var p))
                    result.Add(p.GetInt32());
            }
        }
        catch { }
        return result;
    }

    private List<int> ParsePwndPrimaries()
    {
        var result = new List<int>();
        try
        {
            var tb = PwndImporter.Parse(
                File.ReadAllText(FilePath),
                System.IO.Path.GetFileNameWithoutExtension(FilePath),
                new Dictionary<int, Skill>());
            if (tb != null)
                result.AddRange(tb.Characters.Select(c => (int)c.PrimaryProfession));
        }
        catch { }
        return result;
    }

    // Skill template .txt = un seul code de template GW1 → un personnage.
    // Décode professions, attributs et skill élite. Renvoie false si le catalogue
    // n'était pas encore chargé (Elite Skill non résolu) → recalcul ultérieur.
    private bool LoadTxtMeta()
    {
        string text;
        try { text = File.ReadAllText(FilePath); }
        catch { return true; }

        var skills = _skillsProvider?.Invoke() ?? new Dictionary<int, Skill>();
        CharacterBuild? cb;
        try { cb = GwTemplateCodec.Decode(text.Trim(), skills); }
        catch { cb = null; }
        // Pas un code de compétences (O) : template d'équipement (codes P) ?
        if (cb == null) return LoadEquipmentMeta(text);

        PrimaryProfession         = cb.PrimaryProfession;
        SecondaryProfession       = cb.SecondaryProfession;
        HighestSecondaryAttribute = HighestNonPrimaryAttribute(cb);
        // On garde la COMPÉTENCE (pas son nom) : le libellé affiché suit alors la langue courante.
        _eliteSkillObj            = cb.Skills.FirstOrDefault(s => s?.IsElite == true);
        OnPropertyChanged(nameof(EliteSkill));
        OnPropertyChanged(nameof(EliteSkillDisplay));

        _playerCount = 1; // un .txt = un personnage
        _primaries = new List<int> { (int)cb.PrimaryProfession };

        return skills.Count > 0;
    }

    // Template d'ÉQUIPEMENT (un code P par ligne, un par set d'armes). Le format n'encode pas la
    // profession, mais l'armure GW1 est par profession : on la déduit des pièces — exactement la
    // même déduction que celle qui présélectionne la profession dans l'éditeur d'équipement, pour
    // que la liste et la modale ne se contredisent jamais.
    //
    // Renvoie toujours true : contrairement aux compétences, rien ici ne dépend du catalogue,
    // donc une seconde tentative ne donnerait jamais un résultat différent.
    private bool LoadEquipmentMeta(string text)
    {
        List<EquipmentBuild> builds;
        try { builds = GwEquipmentCodec.DecodeLines(text); }
        catch { return true; }
        if (builds.Count == 0) return true; // ni build, ni équipement : .txt étranger

        _isEquipmentTemplate = true;
        var build = builds.Count == 1 ? builds[0] : EquipmentBuild.Combine(builds);
        var prof = GwEquipmentInfo.GuessProfession(build);
        PrimaryProfession = prof;
        _playerCount = 1;
        _primaries = new List<int> { (int)prof };
        OnPropertyChanged(nameof(Format));
        return true;
    }

    // Attribut investi le plus haut, hors attribut primaire de la profession primaire.
    private static string HighestNonPrimaryAttribute(CharacterBuild cb)
    {
        int primaryAttrId = GwAttributeData.PrimaryFor(cb.PrimaryProfession)?.Id ?? -1;
        (int Points, string Name)? best = null;
        foreach (var kv in cb.Attributes)
        {
            if (kv.Value <= 0) continue;
            if (!kv.Key.StartsWith("attr_", StringComparison.Ordinal)
                || !int.TryParse(kv.Key.AsSpan(5), out int id)) continue;
            if (id == primaryAttrId) continue;
            var name = GwAttributeData.ById(id)?.Name;
            if (name == null) continue;
            if (best == null || kv.Value > best.Value.Points)
                best = (kv.Value, name);
        }
        return best?.Name ?? "";
    }

    // "8 – Mo, Me, A, Rt"
    private static string FormatPlayers(List<int> primaries)
    {
        var unique = primaries
            .Where(p => p != 0)
            .Select(ProfAbbr)
            .Distinct()
            .Order();
        return $"{primaries.Count} – {string.Join(", ", unique)}";
    }

    private static string FormatCampaigns(List<int> primaries)
    {
        var campaigns = new SortedSet<string>();
        foreach (var p in primaries)
        {
            if (p is 7 or 8) campaigns.Add("F");  // Assassin, Ritualist
            if (p is 9 or 10) campaigns.Add("N"); // Paragon, Dervish
        }
        return campaigns.Count > 0 ? string.Join(", ", campaigns) : "";
    }

    // Abréviations de profession du JEU (canon GW1, fournies par Philippe) :
    //  EN  W  R  Mo N  Me E  A Rt P D
    //  FR  G  R  M  N  En El A Rt P D
    // Rôdeur/Ritualiste (R/Rt) et Envoûteur/Élémentaliste (En/El) sont désambiguïsés en deux
    // lettres — en FR AUCUN des deux « E » ne garde la lettre simple.
    private static string ProfAbbr(int id) => AppLanguage.IsFr
        ? id switch
        {
            1  => "G",
            2  => "R",
            3  => "M",
            4  => "N",
            5  => "En",
            6  => "El",
            7  => "A",
            8  => "Rt",
            9  => "P",
            10 => "D",
            _  => "?",
        }
        : id switch
        {
            1  => "W",
            2  => "R",
            3  => "Mo",
            4  => "N",
            5  => "Me",
            6  => "E",
            7  => "A",
            8  => "Rt",
            9  => "P",
            10 => "D",
            _  => "?",
        };
}
