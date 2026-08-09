using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Scraper;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App.ViewModels;

// Un perso à évaluer : compétences équipées + catalogue accessible à son combo PR/SEC
// (déjà filtré par le mode de jeu PvE/PvP courant). Le bandeau agrège 1..n persos
// (1 = éditeur de build ; n = ligne unique du teambuild).
public record ConditionScope(IReadOnlyCollection<Skill> Equipped, IReadOnlyCollection<Skill> Available);

// Une icône du bandeau : condition fixe, état recalculé par ConditionBandViewModel.Refresh.
public class ConditionIndicatorViewModel : ViewModelBase
{
    // Cache statique des icônes (10 conditions × couleur/gris) — cf. [[reference_wpf_icon_cache_tooltip]] :
    // jamais de redécodage disque par binding. Une entrée n'est mise en cache qu'une fois le
    // fichier présent (le téléchargement au démarrage peut ne pas être terminé → on resonde).
    private static readonly Dictionary<string, (ImageSource Color, ImageSource Gray)> _iconCache = new();

    private ConditionCoverage _state;
    private string _inflictedBy = string.Empty;

    private readonly GwConditionData.GwCondition _condition;

    public ConditionIndicatorViewModel(GwConditionData.GwCondition condition)
    {
        Name = condition.Name;
        _condition = condition;
    }

    public string Name { get; }          // canon anglais (clé logique + nom de fichier d'icône)
    // Description + nom dans la langue affichée (le tooltip), sans toucher à la clé anglaise.
    public string Description => _condition.DisplayDescription;
    public string DisplayName => GwConditionData.DisplayName(Name);

    public ConditionCoverage State
    {
        get => _state;
        set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IconOpacity));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    // Noms des compétences équipées qui infligent la condition (agrégés sur tous les persos).
    public string InflictedBy
    {
        get => _inflictedBy;
        set { if (SetField(ref _inflictedBy, value)) OnPropertyChanged(nameof(ToolTipText)); }
    }

    // Couleur = infligée par une compétence équipée ; gris = accessible / inaccessible
    // (différenciés par l'opacité seule).
    public ImageSource? Icon => LoadIcons() is { } pair
        ? (_state == ConditionCoverage.Inflicted ? pair.Color : pair.Gray)
        : null;

    public double IconOpacity => _state switch
    {
        ConditionCoverage.Inflicted => 1.0,
        ConditionCoverage.Available => 0.55,
        _                           => 0.15,
    };

    // Infligée → liste des compétences équipées responsables ; sinon → description officielle
    // de la condition (choix produit : le survol d'une icône éteinte sert de mémo).
    public string ToolTipText => _state == ConditionCoverage.Inflicted
        ? $"{DisplayName} {(ZCodex.Core.Models.AppLanguage.IsFr ? "— infligée par : " : "— inflicted by: ")}{InflictedBy}"
        : $"{DisplayName}\n{Description}";

    private (ImageSource Color, ImageSource Gray)? LoadIcons()
    {
        if (_iconCache.TryGetValue(Name, out var cached)) return cached;

        var path = ConditionIconService.GetLocalPath(Name);
        if (!File.Exists(path)) return null;

        var color = new BitmapImage();
        color.BeginInit();
        color.CacheOption = BitmapCacheOption.OnLoad;
        color.UriSource = new Uri(path);
        color.DecodePixelWidth = 64;
        color.EndInit();
        color.Freeze();

        var gray = new FormatConvertedBitmap(color, PixelFormats.Gray8, null, 0);
        gray.Freeze();

        var pair = ((ImageSource)color, (ImageSource)gray);
        _iconCache[Name] = pair;
        return pair;
    }
}

/// <summary>
/// Bandeau des 10 conditions GW1 : couleur = infligée par une compétence équipée,
/// grisée = accessible au(x) combo(s) PR/SEC, quasi-éteinte = inaccessible.
/// La règle de transfert (auto-infligée + Plague/Grenth) est évaluée PAR PERSO
/// (une compétence de transfert d'un perso ne renvoie pas les conditions d'un autre).
/// </summary>
public class ConditionBandViewModel : ViewModelBase
{
    public ObservableCollection<ConditionIndicatorViewModel> Items { get; } =
        new(GwConditionData.All.Select(c => new ConditionIndicatorViewModel(c)));

    public void Refresh(IReadOnlyCollection<ConditionScope> scopes)
    {
        foreach (var item in Items)
        {
            var inflictedBy = new List<string>();
            bool available = false;

            foreach (var scope in scopes)
            {
                foreach (var s in GwConditionData.InflictingSkills(scope.Equipped, item.Name))
                    if (!inflictedBy.Contains(s.DisplayName)) inflictedBy.Add(s.DisplayName);
                available = available || GwConditionData.CanInflict(scope.Available, item.Name);
            }

            item.InflictedBy = string.Join(", ", inflictedBy);
            item.State = inflictedBy.Count > 0 ? ConditionCoverage.Inflicted
                       : available             ? ConditionCoverage.Available
                       :                         ConditionCoverage.None;
        }
    }
}
