using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Scraper;

namespace ZCodex.App.ViewModels;

// Indicateur de flux partagé par le teambuild (état persisté .pn3) et le build simple (volatil).
// L'icône montre le flux ACTIF s'il y en a un, sinon le flux du MOIS courant en grisé.
// Le propriétaire s'abonne à Changed pour persister / marquer le build « modifié ».
public class FluxIndicatorViewModel : ViewModelBase
{
    // Flux du mois courant, figé à la création du VM (date système).
    public FluxData.FluxInfo MonthFlux { get; } = FluxData.Current;

    private Flux? _activeFlux;
    public Flux? ActiveFlux
    {
        get => _activeFlux;
        set
        {
            if (!SetField(ref _activeFlux, value)) return;
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(DisplayedFlux));
            OnPropertyChanged(nameof(Tooltip));
            Changed?.Invoke();
        }
    }

    // Levé à chaque changement de flux (le teambuild répercute en dirty/undo ; le build simple ignore).
    public event Action? Changed;

    public bool IsActive => _activeFlux is not null;

    // Icône du wiki, téléchargée au démarrage comme les icônes de professions ou de conditions
    // (elle n'est plus embarquée : cf. FluxIconService). Null tant qu'elle n'est pas sur le
    // disque → l'Image reste vide plutôt que de lever un binding sur un fichier absent.
    public string? IconPath => FluxIconService.GetLocalPath();

    // Flux affiché par l'icône : l'actif, sinon celui du mois (rendu grisé via IsActive=false).
    public FluxData.FluxInfo DisplayedFlux => _activeFlux is { } f ? FluxData.Get(f) : MonthFlux;

    public string Tooltip => IsActive
        ? $"{T("S.Flux.ActiveLabel")}{DisplayedFlux.DisplayName}\n{DisplayedFlux.DisplayDescription}\n\n{T("S.Flux.ClickDeactivate")}"
        : $"{T("S.Flux.MonthLabel")}{MonthFlux.DisplayName}\n{MonthFlux.DisplayDescription}\n\n{T("S.Flux.ClickActivate")}";

    // Clic sur l'icône : bascule entre « aucun » et le flux du mois courant.
    public void Toggle() => ActiveFlux = IsActive ? null : MonthFlux.Flux;

    // Bascule de langue : l'infobulle (nom + description) est calculée → rebind au prochain rendu.
    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(Tooltip));

    private static string T(string key) => ZCodex.App.LanguageManager.T(key);
}
