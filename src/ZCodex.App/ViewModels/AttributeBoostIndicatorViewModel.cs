using ZCodex.Core.Data;
using ZCodex.Core.Models;

namespace ZCodex.App.ViewModels;

// Une icône du bandeau local de boosts d'attribut : soit une compétence qualifiante équipée sur CE
// perso (Lot A/B/C, Descriptor non null), soit Heroic Refrain diffusé par un COÉQUIPIER (Lot D,
// Descriptor null — le perso ne l'équipe pas forcément lui-même). Togglable dans les deux cas (clic
// = active/désactive le boost, ou reçoit/arrête de recevoir la diffusion). Cadre vert = actif, même
// idiome que le bandeau des rituels de la nature — mais local au perso, pas à l'équipe.
public class AttributeBoostIndicatorViewModel : ViewModelBase
{
    private readonly CharacterSlotViewModel _owner;

    public AttributeBoostIndicatorViewModel(CharacterSlotViewModel owner, Skill skill, AttributeBoostDescriptor? descriptor)
    {
        _owner = owner;
        Skill = skill;
        Descriptor = descriptor;
        bool active = owner.IsAttributeBoostActive(skill.Id);
        // Diffusion reçue (Heroic Refrain d'un coéquipier) → « recevoir » ; boost propre → « activer ».
        string on  = T(descriptor is null ? "S.Boost.Receive"        : "S.Boost.Activate");
        string off = T(descriptor is null ? "S.Boost.StopReceiving"  : "S.Boost.Deactivate");
        ClickNote = string.Format(T("S.Boost.ClickTo"), active ? off : on);
    }

    private static string T(string key) => ZCodex.App.LanguageManager.T(key);

    public Skill Skill { get; }
    public AttributeBoostDescriptor? Descriptor { get; }
    // Note band-only, affichée sous le vrai tooltip de la compétence.
    public string ClickNote { get; }

    public bool IsActive => _owner.IsAttributeBoostActive(Skill.Id);

    // Le bandeau est reconstruit à chaque changement de barre de compétences, donc IsActive est
    // relu à neuf — pas besoin de notifier ici.
    public void Toggle() => _owner.SetAttributeBoost(Skill.Id, !IsActive);
}
