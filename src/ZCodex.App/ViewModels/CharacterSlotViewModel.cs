using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ZCodex.App.ViewModels;

public class CharacterSlotViewModel : ViewModelBase
{
    private string _name = "(unnamed)";
    private string _notes = string.Empty;
    private Profession _primaryProfession = Profession.None;
    private Profession _secondaryProfession = Profession.None;
    private bool _isFavorite;
    private string _assignment = "(unassigned)";
    private EquipmentBuild? _equipment;
    private AttributesBuild? _attributes;
    private bool _showAttributeEditor;

    public CharacterSlotViewModel()
    {
        for (int i = 0; i < SkillSlots.Count; i++)
        {
            var slot = SkillSlots[i];
            slot.Owner = this;
            slot.SlotIndex = i;
            slot.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SkillSlotViewModel.Skill))
                {
                    OnPropertyChanged(nameof(IsEmptyBuild));
                    OnPropertyChanged(nameof(HasAnySkill));
                    OnPropertyChanged(nameof(HasBuildContent));
                    // L'icône de toggle « prolongateurs de durée » apparaît/disparaît selon la
                    // présence de Blessed Aura / Extend Enchantments dans la barre.
                    OnPropertyChanged(nameof(HasDurationBooster));
                    // Le bandeau des boosts d'attribut (Lot A) suit les compétences qualifiantes
                    // équipées (Aura of the Lich, Awaken the Blood...).
                    OnPropertyChanged(nameof(AttributeBoostToggles));
                    OnPropertyChanged(nameof(HasAttributeBoostToggles));
                    RefreshTitleRankRows();
                    // Poser/retirer une élite bascule la condition de Meek Shall Inherit (+2 si
                    // aucune élite) → toutes les infobulles peuvent changer, pas seulement ce slot.
                    NotifyTooltipsChanged();
                    // Diff vivante : mes variantes recalculent le barré de leur slot homologue.
                    int idx = ((SkillSlotViewModel)s!).SlotIndex;
                    foreach (var child in Variants) child.SkillSlots[idx].RaiseBarredChanged();
                }
            };
        }
        Variants.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasVariants));
    }

    // ── Arbre de variantes ────────────────────────────────────────────────────
    // Une variante = copie totale et indépendante de son parent à la création (cf. plan).
    // Parent/Depth/OwnerBuild sont (re)câblés par TeamBuildViewModel.RefreshTree.

    private CharacterSlotViewModel? _parent;
    private int _depth;
    private bool _isExpanded = true;

    public CharacterSlotViewModel? Parent
    {
        get => _parent;
        set
        {
            if (SetField(ref _parent, value))
            {
                OnPropertyChanged(nameof(IsVariant));
                foreach (var s in SkillSlots) s.RaiseBarredChanged();
            }
        }
    }

    // Référence au team build propriétaire (pour le toggle ShowInheritedAsBarred).
    public TeamBuildViewModel? OwnerBuild { get; set; }

    public int Depth { get => _depth; set => SetField(ref _depth, value); }
    public bool IsVariant => _parent is not null;

    public ObservableCollection<CharacterSlotViewModel> Variants { get; } = new();
    public bool HasVariants => Variants.Count > 0;

    // Repli/déploiement du sous-arbre (UI seule, non persisté, hors dirty tracking).
    public bool IsExpanded { get => _isExpanded; set => SetField(ref _isExpanded, value); }

    // ── Cadenas (tuples de variantes) ─────────────────────────────────────────
    // Id stable round-trippé avec CharacterBuild.Id : sert de référence dans les cadenas.
    public Guid Id { get; set; } = Guid.NewGuid();

    private bool _isSelectedForLock;
    // Coché pendant le mode verrouillage (transient, hors dirty/persistance).
    public bool IsSelectedForLock { get => _isSelectedForLock; set => SetField(ref _isSelectedForLock, value); }

    // Sélection « molette » (Affichage ▸ Molette : sélectionner le personnage d'abord) : seul le
    // personnage cliqué accepte le réglage de ses caractéristiques à la molette. Exclusive, pilotée
    // par TeamBuildViewModel.SelectForWheel, et tenue HORS du dirty tracking (cf. OnChildChanged) :
    // un simple clic ne doit ni salir le build ni entrer dans l'historique d'undo.
    private bool _isWheelSelected;
    public bool IsWheelSelected { get => _isWheelSelected; set => SetField(ref _isWheelSelected, value); }

    // Une cellule par cadenas du build (rempli par TeamBuildViewModel.RebuildLockCells), pour aligner
    // verticalement les barres de même indice. Membre = barre colorée, non-membre = espace vide.
    public ObservableCollection<LockCellViewModel> LockCells { get; } = new();

    // ── Buffs d'arme du spike (chantier 14) ───────────────────────────────────
    // Clés (SpikeWeaponBuffs.Descriptor.Key) des buffs d'arme actifs sur CE perso — état PAR
    // PERSO (décision Philippe), persisté .pn3 v11 avec le roster spike. Le toggle lève
    // PropertyChanged(SpikeActiveBuffs) → dirty/Mutated → recalcul de la fenêtre Spike.
    private readonly HashSet<string> _spikeActiveBuffs = new(StringComparer.Ordinal);
    public IReadOnlyCollection<string> SpikeActiveBuffs => _spikeActiveBuffs;

    public bool IsSpikeBuffActive(string key) => _spikeActiveBuffs.Contains(key);

    public void SetSpikeBuff(string key, bool active)
    {
        if (active ? _spikeActiveBuffs.Add(key) : _spikeActiveBuffs.Remove(key))
            OnPropertyChanged(nameof(SpikeActiveBuffs));
    }

    // Rangée d'icônes de la carte membre (fenêtre Spike) : buffs PROPOSABLES à ce perso
    // (= équipés sur un membre du roster ; DWG sur son seul porteur). Resynchronisée par
    // SpikeViewModel à chaque recalcul — HasSpikeBuffToggles est UI-only, FILTRÉ du dirty
    // tracking (levé depuis Recalculate, sinon boucle Mutated → recalcul → Mutated).
    public ObservableCollection<SpikeBuffToggleViewModel> SpikeBuffToggles { get; } = new();
    public bool HasSpikeBuffToggles => SpikeBuffToggles.Count > 0;
    public void RaiseSpikeBuffTogglesChanged() => OnPropertyChanged(nameof(HasSpikeBuffToggles));

    // ── Mod "of the profession" (simulation only, non persisté) ────────────────
    // Force l'attribut PRIMAIRE de la profession choisie à max(investi, 5). Un seul mod
    // actif à la fois. Permet p.ex. à un N/W d'avoir Strength = 5 (sinon non éditable).
    private bool _ofProfessionModEnabled;
    private Profession _ofProfessionModProfession = Profession.None;

    public IReadOnlyList<Profession> ModProfessions { get; } =
        Enum.GetValues<Profession>().Where(p => p != Profession.None).ToList();

    public bool OfProfessionModEnabled
    {
        get => _ofProfessionModEnabled;
        set { if (SetField(ref _ofProfessionModEnabled, value)) OnModChanged(); }
    }

    public Profession OfProfessionModProfession
    {
        get => _ofProfessionModProfession;
        set { if (SetField(ref _ofProfessionModProfession, value)) OnModChanged(); }
    }

    private Profession ActiveModProfession =>
        _ofProfessionModEnabled ? _ofProfessionModProfession : Profession.None;

    // Caractéristique (attribut primaire) forcée à 5 par le mod, affichée dans l'overlay
    // mais HORS budget. Null si pas de mod, ou si le mod cible la PR (déjà une ligne éditable).
    private GwAttribute? ModForcedAttribute()
    {
        var mod = ActiveModProfession;
        if (mod == Profession.None || mod == _primaryProfession) return null;
        return GwAttributeData.PrimaryFor(mod);
    }

    private void OnModChanged()
    {
        RaiseAttributeDisplayChanged();
        NotifyTooltipsChanged();
    }

    // ── Flux « attributs » (Chantier 13, Lot C) ───────────────────────────────
    // Un seul flux actif à la fois : celui du teambuild propriétaire, ou (build simple) celui
    // poussé par l'éditeur via ActiveFluxProvider (volatil, non persisté). Null = aucun.
    public Func<Flux?>? ActiveFluxProvider { get; set; }
    private Flux? ActiveFlux => ActiveFluxProvider?.Invoke() ?? OwnerBuild?.ActiveFlux;

    // ── Rituels de la nature (environnement global) ───────────────────────────
    // Ensemble des rituels actifs : celui du teambuild propriétaire (OwnerBuild), ou (build simple)
    // celui poussé par l'éditeur via provider volatil. Le surcoût Roaring Winds est résolu au rang
    // du lanceur, identique pour tout le build.
    public static readonly IReadOnlySet<NatureRitualData.Ritual> NoRituals = new HashSet<NatureRitualData.Ritual>();
    public Func<IReadOnlySet<NatureRitualData.Ritual>>? ActiveNatureRitualsProvider { get; set; }
    public Func<int>? RoaringWindsBonusProvider { get; set; }

    public IReadOnlySet<NatureRitualData.Ritual> ActiveNatureRituals =>
        ActiveNatureRitualsProvider?.Invoke() ?? OwnerBuild?.NatureRituals.Active ?? NoRituals;
    public int RoaringWindsBonus =>
        RoaringWindsBonusProvider?.Invoke() ?? OwnerBuild?.RoaringWindsBonus ?? 0;

    // Bonus Roaring Winds pour un ensemble de persos. Rang effectif = celui du/des PORTEUR(S)
    // équipé(s) — le plus fort (décision Philippe : « le plus fort gagne ») — ou, si personne ne
    // l'équipe, le rang de SIMULATION saisi dans le bandeau. 0 si le rituel n'est pas actif.
    public static int RoaringWindsBonusFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters, int simRank)
    {
        if (!active.Contains(NatureRitualData.Ritual.RoaringWinds)) return 0;
        int rwId = NatureRitualData.SkillIdOf(NatureRitualData.Ritual.RoaringWinds);
        int? bestRank = null;
        foreach (var c in characters)
            foreach (var slot in c.SkillSlots)
                if (slot.Skill is { } rw && rw.Id == rwId)
                    bestRank = Math.Max(bestRank ?? 0, c.AttributeLevel(NatureRitualData.RoaringWindsAttribute) ?? 0);
        return NatureRitualData.RoaringWindsBonusAtRank(bestRank ?? simRank);
    }

    // ── Tranquility : durée d'enchantement (Lot D) ────────────────────────────

    public Func<int>? TranquilityPercentProvider { get; set; }
    // Pourcentage « expire plus vite » de Tranquility applicable à ce perso (0 si inactif).
    public int TranquilityPercent =>
        TranquilityPercentProvider?.Invoke() ?? OwnerBuild?.TranquilityPercent ?? 0;

    // % Tranquility pour un ensemble de persos. Même règle que Roaring Winds (même attribut, Survie
    // en pleine nature) : rang effectif = MAX des PORTEUR(S) équipé(s), sinon le rang de SIMULATION.
    // 0 si Tranquility n'est pas actif.
    public static int TranquilityPercentFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters, int simRank)
    {
        if (!active.Contains(NatureRitualData.Ritual.Tranquility)) return 0;
        int tId = NatureRitualData.SkillIdOf(NatureRitualData.Ritual.Tranquility);
        int? bestRank = null;
        foreach (var c in characters)
            foreach (var slot in c.SkillSlots)
                if (slot.Skill is { } t && t.Id == tId)
                    bestRank = Math.Max(bestRank ?? 0, c.AttributeLevel(NatureRitualData.RoaringWindsAttribute) ?? 0);
        return NatureRitualData.TranquilityPercentAtRank(bestRank ?? simRank);
    }

    // ── Prolongateurs de durée d'enchantement par perso (Lot D) ───────────────

    // Le perso porte-t-il un prolongateur (Blessed Aura / Extend Enchantments) → l'icône de toggle
    // apparaît. Re-notifié quand une skill change (cf. constructeur).
    public bool HasDurationBooster =>
        SkillSlots.Any(s => s.Skill?.Id is EnchantmentDuration.BlessedAuraSkillId
                                        or EnchantmentDuration.ExtendEnchantmentsSkillId);

    // Toggle « prolongateurs de durée » de ce perso (Blessed Aura maintenu / Extend appliqué au
    // prochain enchantement). Off → aucun effet ; on → durées Monk/Derviche rallongées (ambre) dans
    // l'infobulle. Persisté .pn3 v15. La PropertyChanged marque le teambuild dirty (OnChildChanged).
    private bool _durationBoostersEnabled;
    public bool DurationBoostersEnabled
    {
        get => _durationBoostersEnabled;
        set { if (SetField(ref _durationBoostersEnabled, value)) RefreshSkillTooltips(); }
    }

    public void ToggleDurationBoosters() => DurationBoostersEnabled = !_durationBoostersEnabled;

    // +20 % « of Enchanting » si le set d'armes ACTIF du perso porte ce mod (sinon 0). S'applique à
    // TOUT enchantement, indépendamment du toggle par perso (c'est de l'équipement, toujours actif).
    public int EnchantingModPercent
    {
        get
        {
            var eq = _equipment;
            if (eq is null || eq.WeaponSets.Count == 0) return 0;
            int set = Math.Clamp(eq.ActiveSet, 0, eq.WeaponSets.Count - 1);
            return eq.WeaponSets[set].Items.Any(i => i.ModifierIds.Any(EnchantmentDuration.IsEnchantingMod))
                ? EnchantmentDuration.EnchantingModPercent : 0;
        }
    }

    // Prolongateur PERSONNEL applicable à cette compétence (0 si toggle éteint ou skill hors cible) :
    // Blessed Aura % (rang Faveur divine) sur enchantement Monk, OU Extend Enchantments % (rang
    // Mystique) sur enchantement Derviche. % résolu depuis la progression de la skill équipée.
    public int ExtenderPercentFor(Skill skill)
    {
        if (!_durationBoostersEnabled) return 0;
        if (EnchantmentDuration.IsMonkEnchantment(skill)
            && FindEquippedSkill(EnchantmentDuration.BlessedAuraSkillId) is { } ba)
            return SkillProgression.IntAt(ba.Progression is { Length: > 0 } p ? p[0] : null,
                                          AttributeLevel("Divine Favor") ?? 0) ?? 0;
        if (EnchantmentDuration.IsDervishEnchantment(skill)
            && FindEquippedSkill(EnchantmentDuration.ExtendEnchantmentsSkillId) is { } ext)
            return SkillProgression.IntAt(ext.Progression is { Length: > 0 } q ? q[0] : null,
                                          AttributeLevel("Mysticism") ?? 0) ?? 0;
        return 0;
    }

    private Skill? FindEquippedSkill(int skillId) =>
        SkillSlots.FirstOrDefault(s => s.Skill?.Id == skillId)?.Skill;

    // ── Boosts d'attribut de compétences équipées (Lot A) ─────────────────────
    // Compétences personnelles donnant un bonus fixe (ou dépendant de leur propre rang) à 1-2
    // caractéristiques quand actives (Aura of the Lich, Awaken the Blood, Masochism, Expert's
    // Dexterity, Trapper's Focus). État PAR PERSO (comme les buffs d'arme du spike), persisté
    // .pn3 v16. La durée réelle du buff est ignorée (simulation Z-Codex : actif ou non, comme les
    // rituels de la nature / prolongateurs de durée).
    private readonly HashSet<int> _activeAttributeBoosts = new();
    public IReadOnlyCollection<int> ActiveAttributeBoosts => _activeAttributeBoosts;

    public bool IsAttributeBoostActive(int skillId) => _activeAttributeBoosts.Contains(skillId);

    public void SetAttributeBoost(int skillId, bool active)
    {
        if (active ? _activeAttributeBoosts.Add(skillId) : _activeAttributeBoosts.Remove(skillId))
        {
            NotifyTooltipsChanged();
            // AttributeBoostToggles construit des instances FRAÎCHES à chaque lecture (IsActive lu au
            // constructeur) : sans cette notification, l'ItemsControl du bandeau garde ses anciens
            // conteneurs et le cadre vert ne bascule jamais visuellement (skills ≠ changées, donc le
            // handler du constructeur ne se déclenche pas ici).
            OnPropertyChanged(nameof(AttributeBoostToggles));
        }
    }

    // Bandeau local : une icône par compétence qualifiante ÉQUIPÉE (bascule indépendante par
    // compétence — un perso peut porter plusieurs boosts à la fois, ex. Aura of the Lich +
    // Masochism sur un même Necro) + Heroic Refrain (Lot D) si diffusé par N'IMPORTE QUEL perso de
    // l'équipe — y compris le lanceur lui-même : Heroic Refrain n'est délibérément PAS dans
    // AttributeBoostData (sa résolution est inter-perso, pas auto-résolue par son propre porteur),
    // donc le scan "équipée" ci-dessous ne le fait jamais remonter, même chez le lanceur.
    public IEnumerable<AttributeBoostIndicatorViewModel> AttributeBoostToggles
    {
        get
        {
            var items = SkillSlots.Select(s => s.Skill)
                .Where(sk => sk != null)
                .Select(sk => (Skill: sk!, Descriptor: AttributeBoostData.BySkillId(sk!.Id)))
                .Where(t => t.Descriptor != null)
                .Select(t => new AttributeBoostIndicatorViewModel(this, t.Skill, t.Descriptor));

            var (hrSkill, _) = HeroicRefrain;
            if (hrSkill != null)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, hrSkill, null));

            return items;
        }
    }

    public bool HasAttributeBoostToggles => AttributeBoostToggles.Any();

    // Rafraîchit UNIQUEMENT le bandeau (pas les tooltips) : utilisé par le teambuild quand la
    // diffusion Heroic Refrain d'un COÉQUIPIER change (équipement/retrait/rang), un événement
    // externe à CE perso que son propre handler de slot ne peut pas détecter.
    public void RefreshAttributeBoostBand()
    {
        OnPropertyChanged(nameof(AttributeBoostToggles));
        OnPropertyChanged(nameof(HasAttributeBoostToggles));
    }

    // Boosts ACTIFS ciblant l'attribut nommé (compétence + valeur résolue). Valeur fixe, ou résolue
    // via la Progression de LA COMPÉTENCE au rang de SA PROPRE caractéristique — lecture DIRECTE de
    // la ligne (FindAttributeRow, sans repasser par AttributeLevel) pour ne jamais créer de cycle
    // quand une cible inclut l'attribut de résolution lui-même (ex. Ritual Lord boostant Spawning
    // Power, Shadow Theft boostant Critical Strikes).
    private IEnumerable<(AttributeBoostDescriptor Descriptor, int Value)> ActiveBoostsFor(string? attributeName)
    {
        if (attributeName is null) yield break;
        foreach (var slot in SkillSlots)
        {
            var sk = slot.Skill;
            if (sk is null || !IsAttributeBoostActive(sk.Id)) continue;
            var d = AttributeBoostData.BySkillId(sk.Id);
            if (d is null) continue;
            bool targeted = d.TargetsAllAttributes
                ? PrimaryAttributeRows.Concat(SecondaryAttributeRows).Any(r => r.Name == attributeName)
                : d.TargetAttributes.Contains(attributeName);
            if (!targeted) continue;
            int value = d.FixedValue ?? SkillProgression.IntAt(
                sk.Progression is { } p && d.ProgressionIndex < p.Length ? p[d.ProgressionIndex] : null,
                FindAttributeRow(sk.Attribute)?.EffectiveLevel ?? 0) ?? 0;
            yield return (d, value);
        }
    }

    // Somme des boosts ADDITIFS actifs (exclut les boosts « override », cf. AttributeOverrideValue)
    // + Heroic Refrain reçu (Lot D, même canal violet — aucune raison visuelle de le distinguer).
    private int AttributeBoostBonus(string? attributeName) =>
        ActiveBoostsFor(attributeName).Where(t => !t.Descriptor.IsOverride).Sum(t => t.Value)
        + HeroicRefrainBonus(attributeName);

    // ── Heroic Refrain (Lot D) : diffusion inter-perso ────────────────────────
    // Seul boost dont la source n'est pas sur SA PROPRE barre : équipé par N'IMPORTE QUEL perso de
    // l'équipe (rang du LANCEUR le plus fort, patron Roaring Winds), reçu par CHAQUE perso via un
    // toggle personnel indépendant (même stockage que les autres boosts : IsAttributeBoostActive/
    // SetAttributeBoost sur le SkillId 3431, actif même si CE perso ne l'a pas équipé).

    // Scan d'équipe : renvoie la compétence trouvée (pour l'icône/tooltip du bandeau) + le bonus
    // résolu au rang du porteur le plus fort. (null, 0) si personne ne l'équipe.
    // ⚠️ Rang lu en RAW (FindAttributeRow), PAS via AttributeLevel : AttributeLevel du LANCEUR
    // repasserait par HeroicRefrainBonus → HeroicRefrain → CE MÊME scan → cycle infini/débordement
    // de pile (même piège que Ritual Lord/Shadow Theft, ici entre deux persos au lieu d'un seul).
    public static (Skill? Skill, int Bonus) HeroicRefrainFor(IEnumerable<CharacterSlotViewModel> characters)
    {
        Skill? found = null;
        int? bestRank = null;
        foreach (var c in characters)
            foreach (var slot in c.SkillSlots)
                if (slot.Skill is { } sk && sk.Id == HeroicRefrainData.SkillId)
                {
                    found ??= sk;
                    bestRank = Math.Max(bestRank ?? 0, c.FindAttributeRow(HeroicRefrainData.ScalingAttribute)?.EffectiveLevel ?? 0);
                }
        return found is null ? (null, 0) : (found, HeroicRefrainData.BonusAtRank(found.Progression, bestRank ?? 0));
    }

    // Provider explicite pour le build simple (pas d'OwnerBuild → patron ActiveNatureRitualsProvider).
    public Func<(Skill? Skill, int Bonus)>? HeroicRefrainProvider { get; set; }

    public (Skill? Skill, int Bonus) HeroicRefrain =>
        HeroicRefrainProvider?.Invoke() ?? OwnerBuild?.HeroicRefrain ?? (null, 0);

    // Bonus effectif SUR CE PERSO : seulement si diffusé ET que CE perso a activé la réception,
    // et seulement sur ses vraies caractéristiques (PR + SEC, comme Shadow Theft).
    private int HeroicRefrainBonus(string? attributeName)
    {
        if (attributeName is null || !IsAttributeBoostActive(HeroicRefrainData.SkillId)) return 0;
        if (!PrimaryAttributeRows.Concat(SecondaryAttributeRows).Any(r => r.Name == attributeName)) return 0;
        return HeroicRefrain.Bonus;
    }

    // Boost « override » actif (Lot C, Master of Magic) : remplace le niveau de base au lieu de s'y
    // additionner. Plusieurs sources actives (improbable) → la plus forte gagne. Null = aucun.
    private int? AttributeOverrideValue(string? attributeName)
    {
        int? result = null;
        foreach (var (d, value) in ActiveBoostsFor(attributeName))
            if (d.IsOverride) result = Math.Max(result ?? 0, value);
        return result;
    }

    // Une élite équipée dans la barre désactive Meek Shall Inherit.
    private bool HasEliteEquipped => SkillSlots.Any(s => s.Skill?.IsElite == true);

    // Bonus de flux (+2) au niveau effectif d'un attribut NOMMÉ, plafonné en aval à 20 :
    //  • Hidden Talent    → caractéristiques (investissables) de la profession secondaire ;
    //  • Meek Shall Inherit (si aucune élite) → toutes les vraies caractéristiques (PR + SEC),
    //    hors rangs de titre. Les deux excluent l'attribut primaire non investi de la SEC
    //    (absent des lignes) et les pistes de titre. 0 sinon.
    private int FluxAttributeBonus(string? attributeName)
    {
        if (attributeName is null) return 0;
        switch (ActiveFlux)
        {
            case Flux.HiddenTalent:
                return SecondaryAttributeRows.Any(r => r.Name == attributeName) ? 2 : 0;
            case Flux.MeekShallInherit when !HasEliteEquipped:
                return PrimaryAttributeRows.Concat(SecondaryAttributeRows)
                    .Any(r => r.Name == attributeName) ? 2 : 0;
            default:
                return 0;
        }
    }

    // ── Flux « énergie » (Chantier 13, Lot C2) ────────────────────────────────
    // Jack of All Trades : actif si TOUTES les caractéristiques (niveau EFFECTIF, base + runes/
    // casque) valent 0 ou 8–11 (12 disqualifie ; rangs de titre non comptés). Même règle qu'au
    // Lot B (fenêtre Spike) — centralisée ici, SpikeViewModel délègue.
    public bool MeetsJackOfAllTrades
    {
        get
        {
            foreach (var row in PrimaryAttributeRows.Concat(SecondaryAttributeRows))
            {
                int lvl = row.EffectiveLevel;
                if (lvl != 0 && lvl is < 8 or > 11) return false;
            }
            return true;
        }
    }

    // All In : actif si toutes les compétences de la barre AYANT une vraie caractéristique
    // partagent la même. Les compétences sans vraie carac (signet de résurrection, PvE, pistes de
    // titre → ByName null) sont IGNORÉES (décision Philippe). Requiert ≥ 1 compétence à vraie carac.
    public bool MeetsAllIn
    {
        get
        {
            string? shared = null;
            foreach (var slot in SkillSlots)
            {
                if (slot.Skill is not { } skill || GwAttributeData.ByName(skill.Attribute) is null)
                    continue;
                if (shared is null) shared = skill.Attribute;
                else if (!string.Equals(shared, skill.Attribute, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return shared != null;
        }
    }

    // Réduction d'énergie du flux actif applicable à ce perso (0/20/25), IDENTIQUE pour toutes ses
    // skills (JoT/All In portent sur toute la barre). Appliquée APRÈS Expertise (SkillTooltipControl).
    public int FluxEnergyPercent => ActiveFlux switch
    {
        Flux.JackOfAllTrades when MeetsJackOfAllTrades => 20,
        Flux.AllIn when MeetsAllIn => 25,
        _ => 0,
    };

    // Jack of All Trades actif et qualifié → +15 % dégâts (table d'armure) et 25 % d'activation en
    // moins (temps × 0,75). Les deux ne concernent QUE JoT (0 sinon). Portée = toutes les skills.
    private bool JackActive => ActiveFlux == Flux.JackOfAllTrades && MeetsJackOfAllTrades;
    public int FluxDamagePercent => JackActive ? 15 : 0;
    public int FluxCastPercent   => JackActive ? 25 : 0;

    // Niveau effectif du perso dans l'attribut PRIMAIRE de `prof` : la ligne éditable
    // (attribut primaire de la profession primaire) si elle existe, sinon 0 (attribut
    // primaire d'une SEC, non éditable) ; relevé à 5 si le mod cible cette profession, puis
    // relevé du bonus de flux éventuel (Meek le touche, plafond 20).
    public int PrimaryAttributeRankFor(Profession prof)
    {
        var primAttr = GwAttributeData.PrimaryFor(prof);
        int invested = primAttr == null ? 0
            : PrimaryAttributeRows.FirstOrDefault(r => r.AttributeId == primAttr.Id)?.EffectiveLevel ?? 0;
        int level = ActiveModProfession == prof ? Math.Max(invested, 5) : invested;
        int fluxBonus = primAttr == null ? 0 : FluxAttributeBonus(primAttr.Name);
        return fluxBonus > 0 ? Math.Min(level + fluxBonus, 20) : level;
    }

    // Footer d'attribut primaire : IDENTIQUE pour toutes les skills (indépendant de leur
    // profession). Règle = bonus du/des attribut(s) primaire(s) AYANT des points : l'attribut
    // primaire de la PR s'il est investi, + celui forcé à 5 par le mod s'il est actif.
    // Aucun point → aucun footer.
    public string ComputeFooter()
    {
        var lines = new List<string>();
        // Rang primaire relevé du bonus de flux (Meek) → le footer s'affiche même si l'attribut
        // primaire n'est investi qu'à 0 mais boosté à 2.
        if (_primaryProfession != Profession.None && PrimaryAttributeRankFor(_primaryProfession) > 0)
            AddFooterLine(lines, _primaryProfession);

        var mod = ActiveModProfession;
        if (mod != Profession.None && mod != _primaryProfession)
            AddFooterLine(lines, mod);

        var fluxLine = FluxFooterLine();
        if (fluxLine != null) lines.Add(fluxLine);

        return string.Join("\n", lines);
    }

    // Ligne de footer signalant le flux « attributs » actif et son effet sur ce perso (Lot C).
    // Null si le flux actif n'est pas un flux d'attribut, ou n'a rien à booster ici.
    private string? FluxFooterLine() => ActiveFlux switch
    {
        Flux.HiddenTalent when _secondaryProfession != Profession.None
            => ZCodex.App.LanguageManager.T("S.Flux.FootHiddenTalent"),
        Flux.MeekShallInherit when !HasEliteEquipped
            => ZCodex.App.LanguageManager.T("S.Flux.FootMeek"),
        Flux.MeekShallInherit
            => ZCodex.App.LanguageManager.T("S.Flux.FootMeekInactive"),
        Flux.JackOfAllTrades when MeetsJackOfAllTrades
            => ZCodex.App.LanguageManager.T("S.Flux.FootJack"),
        Flux.AllIn when MeetsAllIn
            => ZCodex.App.LanguageManager.T("S.Flux.FootAllIn"),
        _ => null,
    };

    private void AddFooterLine(List<string> lines, Profession prof)
    {
        var text = PrimaryAttributeBonus.Describe(prof, PrimaryAttributeRankFor(prof));
        if (!string.IsNullOrEmpty(text)) lines.Add($"({text})");
    }

    // Niveau effectif du perso dans l'attribut nommé (ligne PR, SEC ou rang de titre), relevé à 5
    // si le mod "of the profession" cible cet attribut — Y COMPRIS quand le mod cible la PR
    // (Guerrier Force 3 + mod Warrior → Force 5, comme en jeu ; cohérent avec le footer, seul
    // l'overlay garde l'exclusion via ModForcedAttribute pour ne pas dupliquer la ligne éditable).
    // Null si l'attribut n'est ni édité ni forcé (ex: attribut d'une profession absente → plage).
    public int? AttributeLevel(string attributeName)
    {
        int? rowLevel = PrimaryAttributeRows.Concat(SecondaryAttributeRows).Concat(TitleRankRows)
            .FirstOrDefault(r => r.Name == attributeName)?.EffectiveLevel;
        var mod = ActiveModProfession;
        if (mod != Profession.None && GwAttributeData.PrimaryFor(mod) is { } modAttr
            && modAttr.Name == attributeName)
            rowLevel = Math.Max(rowLevel ?? 0, 5);
        // Override (Lot C, Master of Magic) : remplace la base AVANT les bonus additifs, qui
        // continuent de s'appliquer PAR-DESSUS (cumul jeu réel : "set to X" + un bonus séparé).
        if (AttributeOverrideValue(attributeName) is { } ov) rowLevel = ov;
        // Bonus de flux (Hidden Talent / Meek) + boost de compétence équipée (Aura of the Lich...) :
        // plafonnés ensemble à 20, sur les seuls attributs portant une ligne (les deux helpers
        // garantissent rowLevel non null quand ils rendent > 0).
        int fluxBonus = FluxAttributeBonus(attributeName);
        int boostBonus = AttributeBoostBonus(attributeName);
        int totalBonus = fluxBonus + boostBonus;
        if (totalBonus > 0 && rowLevel is { } lvl)
            return Math.Min(lvl + totalBonus, 20);
        return rowLevel;
    }

    // Description de la skill : phrase de type retirée + variables résolues au rang de l'attribut
    // de la skill (Skill.Attribute). Plages non résolues (pas de progression scrapée, ou attribut
    // non édité) laissées en notation de plage verte.
    public string ResolveDescription(Skill skill)
    {
        var body = SkillText.ConciseBody(skill.Description, skill.SkillType);
        // Un flux qui relève l'attribut de la skill → valeurs marquées dans la couleur du flux
        // (toute la description scale sur ce seul attribut : marquage uniforme).
        bool fluxBoosted = FluxAttributeBonus(skill.Attribute) > 0;
        return SkillProgression.Resolve(body, skill.Progression, AttributeLevel(skill.Attribute), fluxBoosted);
    }

    // Description AFFICHÉE en mode FR : le texte gwiki résolu au même rang (ancres 0/15).
    // Null = langue EN, pas de texte FR, ou page FR suspecte → l'infobulle affiche la version
    // EN (ResolveDescription). Les parseurs (dégâts, durées) restent branchés sur l'EN.
    public string? ResolveDisplayDescription(Skill skill)
    {
        // PIÈGE : DescriptionFallback vaut None pour DEUX raisons opposées — « on est en anglais »
        // ET « le français est bon » (cf. sa première ligne, !AppLanguage.IsFr ? None). Il ne peut
        // donc pas servir de garde à lui seul : sans le test de langue ci-dessous, une compétence
        // équipée affichait sa description EN FRANÇAIS en mode anglais, alors que le catalogue —
        // qui passe par Skill.DisplayDescriptionBody, lui bien conditionné à AppLanguage.IsFr —
        // affichait correctement l'anglais dans la même fenêtre.
        if (!AppLanguage.IsFr) return null;
        // Même condition que Skill.DisplayDescriptionBody / DescriptionFallback : le repli doit
        // être identique dans les deux contextes, sinon l'infobulle avertirait « affiché en
        // anglais » tout en montrant du français.
        if (skill.DescriptionFallback != Skill.FrFallback.None) return null;
        bool fluxBoosted = FluxAttributeBonus(skill.Attribute) > 0;
        return SkillProgression.Resolve(skill.DescriptionFr, skill.Progression,
            AttributeLevel(skill.Attribute), fluxBoosted, frAnchors: true);
    }

    // Pousse la mise à jour live des infobulles (footer + description) vers tous les slots,
    // réactif au survol quand on change un niveau d'attribut, le mod, ou les professions.
    // Rafraîchit aussi le +2 de flux affiché DANS chaque ligne d'attribut : mêmes déclencheurs
    // (flux, professions, élite équipée pour Meek) que les infobulles → resté synchrone ici.
    private void NotifyTooltipsChanged()
    {
        foreach (var r in PrimaryAttributeRows)   { r.FluxBonus = FluxAttributeBonus(r.Name); r.SkillBoost = AttributeBoostBonus(r.Name); }
        foreach (var r in SecondaryAttributeRows) { r.FluxBonus = FluxAttributeBonus(r.Name); r.SkillBoost = AttributeBoostBonus(r.Name); }
        foreach (var s in SkillSlots) s.RaiseTooltipChanged();
        // Le +2 de flux se répercute aussi dans l'overlay des attributs (teambuild).
        RaiseAttributeDisplayChanged();
    }

    // Rafraîchit les infobulles de tous les slots. Public : appelé quand le flux actif change
    // (le teambuild ou l'éditeur pousse la mise à jour, cf. ActiveFluxProvider / OwnerBuild).
    public void RefreshTooltips() => NotifyTooltipsChanged();

    // Rafraîchit UNIQUEMENT les infobulles de skills, sans toucher aux attributs. Pour les
    // changements qui n'affectent QUE les skills (rituels de la nature : énergie/recharge/cast) →
    // évite le coûteux RaiseAttributeDisplayChanged (re-parse SkillMarkup de l'overlay de CHAQUE
    // perso de l'arbre) et le recalcul des FluxBonus, sources du ralentissement au toggle.
    public void RefreshSkillTooltips()
    {
        foreach (var s in SkillSlots) s.RaiseTooltipChanged();
    }

    public string Name
    {
        get => _name;
        set { if (SetField(ref _name, value)) { OnPropertyChanged(nameof(IsEmptyBuild)); OnPropertyChanged(nameof(DisplayName)); } }
    }

    // Nom AFFICHÉ : le nom réel, ou un placeholder localisé quand le build n'est pas nommé.
    // « (unnamed) » reste la sentinelle interne (IsEmptyBuild, .pn3) ; seul l'affichage change.
    public string DisplayName =>
        string.IsNullOrWhiteSpace(_name) || _name == "(unnamed)"
            ? ZCodex.App.LanguageManager.T("S.Misc.BuildNamePlaceholder")
            : _name;

    // Build "vierge" du point de vue du chat code : une profession principale seule ne compte
    // pas (état de base des templates pré-remplis). Vide = pas de secondaire, nom par défaut,
    // aucune skill, aucun point d'attribut ni bonus. Détermine l'affichage du bouton
    // import (vide) vs copier (dès la moindre modif impactant le code : SEC, skill, attribut, nom).
    public bool IsEmptyBuild =>
        _secondaryProfession == Profession.None &&
        (string.IsNullOrWhiteSpace(_name) || _name == "(unnamed)") &&
        SkillSlots.All(s => s.Skill == null) &&
        PrimaryAttributeRows.All(r => r.Points == 0 && r.BonusPoints == 0) &&
        SecondaryAttributeRows.All(r => r.Points == 0 && r.BonusPoints == 0);

    // ── Vidage du build (menus contextuels) ───────────────────────────────────
    // « Vider » n'est PAS « supprimer le personnage » : la ligne, son nom, ses professions, ses
    // notes, son équipement et son joueur assigné restent en place — seul le contenu du template
    // s'en va. Les trois entrées se grisent quand il n'y a déjà plus rien à retirer.

    public bool HasAnySkill => SkillSlots.Any(s => s.Skill != null);

    // Points POSÉS À LA MAIN : la base (budget de 200) et le niveau bonus hors budget
    // (rune/coiffe/conso, réglé à la molette ou au spinner). Les rangs de titre n'en font pas
    // partie : ce ne sont pas des points de caractéristique (décision Philippe 14/08/2026).
    public bool HasInvestedAttributes =>
        PrimaryAttributeRows.Concat(SecondaryAttributeRows).Any(r => r.Points > 0 || r.BonusPoints > 0);

    public bool HasBuildContent => HasAnySkill || HasInvestedAttributes;

    public void ClearSkills()
    {
        foreach (var s in SkillSlots) s.Skill = null;
    }

    // Ce qui est DÉRIVÉ n'a rien à faire ici et se recalcule tout seul : mod « of the profession »
    // de l'équipement, bonus de flux, boost d'une compétence équipée. Rien de tout cela n'est
    // stocké dans les lignes.
    public void ClearAttributePoints()
    {
        foreach (var r in PrimaryAttributeRows.Concat(SecondaryAttributeRows))
        {
            r.Points      = 0;
            r.BonusPoints = 0;
        }
    }

    public void ClearBuild()
    {
        ClearSkills();
        ClearAttributePoints();
    }

    public string Notes
    {
        get => _notes;
        set => SetField(ref _notes, value);
    }

    public Profession PrimaryProfession
    {
        get => _primaryProfession;
        set
        {
            SetField(ref _primaryProfession, value);
            RaiseProfessionDisplayChanged();
            OnPropertyChanged(nameof(HasPrimaryProfession));
            RefreshAttributeRows();
        }
    }

    public bool HasPrimaryProfession => _primaryProfession != Profession.None;

    public Profession SecondaryProfession
    {
        get => _secondaryProfession;
        set
        {
            if (SetField(ref _secondaryProfession, value))
            {
                RaiseProfessionDisplayChanged();
                OnPropertyChanged(nameof(HasSecondaryProfession));
                RefreshAttributeRows();
            }
        }
    }

    public bool HasSecondaryProfession => _secondaryProfession != Profession.None;

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public string Assignment
    {
        get => _assignment;
        set { if (SetField(ref _assignment, value)) OnPropertyChanged(nameof(IsAssigned)); }
    }

    // Masque le libellé d'assignation quand le perso n'a personne d'assigné (le clic droit /
    // menu contextuel restent disponibles pour assigner, cf. MainWindow.xaml).
    public bool IsAssigned => _assignment != "(unassigned)";

    public string ProfessionDisplay =>
        _primaryProfession == Profession.None ? "?" :
        _secondaryProfession == Profession.None ? _primaryProfession.DisplayName() :
        $"{_primaryProfession.DisplayName()}/{_secondaryProfession.DisplayName()}";

    // Noms localisés des professions PR/SEC, pour les infobulles des icônes de profession
    // (teambuild + éditeur). Vide si None → l'icône est masquée, pas d'infobulle « None ».
    public string PrimaryProfessionName =>
        _primaryProfession == Profession.None ? string.Empty : _primaryProfession.DisplayName();
    public string SecondaryProfessionName =>
        _secondaryProfession == Profession.None ? string.Empty : _secondaryProfession.DisplayName();

    // Re-notifie l'affichage textuel des professions (display + noms d'infobulle) après un
    // changement de profession OU de langue, sans reconstruire les lignes d'attributs.
    private void RaiseProfessionDisplayChanged()
    {
        OnPropertyChanged(nameof(ProfessionDisplay));
        OnPropertyChanged(nameof(PrimaryProfessionName));
        OnPropertyChanged(nameof(SecondaryProfessionName));
    }

    // TODO(equipment): single EquipmentBuild for now.
    //                  Future: List<EquipmentBuild> bounded to 4 weapon sets (slots 0-1 vary, armor shared).
    public EquipmentBuild? Equipment
    {
        get => _equipment;
        set { SetField(ref _equipment, value); OnPropertyChanged(nameof(HasEquipment)); OnPropertyChanged(nameof(EquipmentSummary)); }
    }

    public bool HasEquipment => _equipment != null && !_equipment.IsEmpty;

    // Genre du perso (skin d'armure H/F). Édité depuis l'éditeur d'équipement ; persisté .pn3 v5.
    private Gender _gender = Gender.Male;
    public Gender Gender
    {
        get => _gender;
        set => SetField(ref _gender, value);
    }

    // Stores template-invested points only (PvE runtime bonuses are not persisted here).
    public AttributesBuild? Attributes
    {
        get => _attributes;
        set { SetField(ref _attributes, value); OnPropertyChanged(nameof(HasAttributes)); RefreshAttributeRows(); }
    }

    public bool HasAttributes => _attributes?.Allocations.Count > 0 || _attributes?.TitleRanks.Count > 0;

    public bool ShowAttributeEditor
    {
        get => _showAttributeEditor;
        set => SetField(ref _showAttributeEditor, value);
    }

    public ObservableCollection<AttributeRowViewModel> PrimaryAttributeRows   { get; } = new();
    public ObservableCollection<AttributeRowViewModel> SecondaryAttributeRows { get; } = new();

    // Rangs de titre PvE (0–10, hors budget, sans bonus) — uniquement les pistes référencées par
    // au moins une skill posée. Persistés en .pn3 via AttributesBuild.TitleRanks (round-trip),
    // mais jamais dans un code de chat GW1 (le format de template réel n'en a pas la notion).
    public ObservableCollection<AttributeRowViewModel> TitleRankRows { get; } = new();
    public bool HasTitleRanks => TitleRankRows.Count > 0;

    // Ligne d'attribut (PR, SEC ou rang de titre) portant ce nom, ou null. Sert au survol/molette.
    public AttributeRowViewModel? FindAttributeRow(string attributeName) =>
        PrimaryAttributeRows.Concat(SecondaryAttributeRows).Concat(TitleRankRows)
            .FirstOrDefault(r => r.Name == attributeName);

    private void RefreshTitleRankRows()
    {
        var used = SkillSlots.Select(s => s.Skill?.Attribute)
            .Where(GwAttributeData.IsTitleRank)
            .Select(a => a!)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        // Mêmes pistes qu'avant → on garde les lignes (préserve les niveaux saisis).
        if (TitleRankRows.Select(r => r.Name).SequenceEqual(used)) return;

        var prevLevels = TitleRankRows.ToDictionary(r => r.Name, r => r.Points);
        foreach (var r in TitleRankRows) r.PropertyChanged -= OnTitleRankRowChanged;
        TitleRankRows.Clear();

        foreach (var name in used)
        {
            // Cap du rang dérivé des données de la piste (10 EotN/Sunspear, 12 Allegiance Kurzick/Luxon).
            int max = SkillSlots.Select(s => s.Skill)
                .Where(sk => sk != null && sk.Attribute == name)
                .Select(sk => SkillBreakpoints.RankMax(sk!.Progression))
                .DefaultIfEmpty(10).Max();
            var row = new AttributeRowViewModel(0, name, isPrimary: false) { MaxPoints = max, MaxBonus = 0 };
            if (prevLevels.TryGetValue(name, out var lvl)) row.Points = lvl;
            else if (_attributes?.TitleRanks.TryGetValue(name, out var saved) == true) row.Points = saved;
            row.PropertyChanged += OnTitleRankRowChanged;
            TitleRankRows.Add(row);
        }

        OnPropertyChanged(nameof(HasTitleRanks));
        RaiseAttributeDisplayChanged();
        NotifyTooltipsChanged();
    }

    private void OnTitleRankRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AttributeRowViewModel row) return;

        if (e.PropertyName == nameof(AttributeRowViewModel.Points))
        {
            _attributes ??= new AttributesBuild();
            if (row.Points > 0) _attributes.TitleRanks[row.Name] = row.Points;
            else _attributes.TitleRanks.Remove(row.Name);

            RaiseAttributeDisplayChanged();
            NotifyTooltipsChanged();
        }
    }

    // Coût cumulé en attribute points pour atteindre chaque niveau (0–12).
    // Progression : niv.1=1, 2=2, 3=3, 4=4, 5=5, 6=6, 7=7, 8=9, 9=11, 10=13, 11=16, 12=20 (marginal).
    private static readonly int[] _attrPointCostTable = [0, 1, 3, 6, 10, 15, 21, 28, 37, 48, 61, 77, 97];

    private static int AttributePointCost(int level)
        => level >= 0 && level < _attrPointCostTable.Length ? _attrPointCostTable[level] : 97;

    public int  TotalAttributePoints  => PrimaryAttributeRows.Sum(r => AttributePointCost(r.Points))
                                       + SecondaryAttributeRows.Sum(r => AttributePointCost(r.Points));
    public bool IsOverAttributeBudget => TotalAttributePoints > 200;

    // Résumé "Nom Niveau" des attributs investis, trié alpha (ex: "Death Magic 12+4, Soul
    // Reaping 12+1"). PointsDisplay inclut le bonus de rune/casque. withBonuses → ajoute le +2 de
    // flux ET le boost de compétence équipée aux lignes déjà listées (jamais aux caracs à 0 SAUF
    // celles d'une compétence ÉQUIPÉE portant un bonus/override actif, sinon Meek listerait tout
    // ni un override sur une carac non investie ne serait jamais visible) ; marked → chaque bonus
    // est entouré de son marqueur pour être coloré par SkillMarkup (flux / violet du boost / ambre
    // de l'override). Un override (Master of Magic) REMPLACE l'affichage "Nom Points" par
    // "Nom Fixé à X" — les bonus additifs éventuels restent ajoutés PAR-DESSUS.
    private string BuildAttributeSummary(bool withBonuses, bool marked)
    {
        var equippedAttrs = SkillSlots.Select(s => s.Skill?.Attribute).Where(a => a != null).ToHashSet();
        var parts = PrimaryAttributeRows.Concat(SecondaryAttributeRows).Concat(TitleRankRows)
            .Where(r => r.Points > 0 || r.BonusPoints > 0
                     || (withBonuses && equippedAttrs.Contains(r.Name)
                         && (r.FluxBonus > 0 || r.SkillBoost > 0 || AttributeOverrideValue(r.Name) is not null)))
            .OrderBy(r => GwAttributeData.DisplayName(r.Name), StringComparer.CurrentCulture)
            .Select(r =>
            {
                // r.Name = clé logique (override/lookup) ; disp = nom affiché (langue courante).
                string disp = GwAttributeData.DisplayName(r.Name);
                string fixedAt = AppLanguage.IsFr ? "Fixé à" : "Fixed at";
                string s = withBonuses && AttributeOverrideValue(r.Name) is { } ov
                    ? (marked
                        ? $"{disp} {SkillProgression.MarkOverride}{fixedAt} {ov}{SkillProgression.MarkOverride}"
                        : $"{disp} {fixedAt} {ov}")
                    : $"{disp} {r.PointsDisplay}";
                if (withBonuses && r.FluxBonus > 0)
                    s += marked
                        ? $"{SkillProgression.MarkFlux}+{r.FluxBonus}{SkillProgression.MarkFlux}"
                        : $"+{r.FluxBonus}";
                if (withBonuses && r.SkillBoost > 0)
                    s += marked
                        ? $"{SkillProgression.MarkSkillBoost}+{r.SkillBoost}{SkillProgression.MarkSkillBoost}"
                        : $"+{r.SkillBoost}";
                return s;
            })
            .ToList();
        // Carac forcée à 5 par le mod "of the profession" : affichée mais hors budget [n/200].
        if (ModForcedAttribute() is { } modAttr)
            parts.Add($"{GwAttributeData.DisplayName(modAttr.Name)} 5");
        return string.Join(", ", parts);
    }

    // Résumé SANS bonus externes (texte brut) et version marquée AVEC flux + boosts de compétence
    // (roster de la fenêtre Spike, coloré via SkillMarkup).
    public string AttributeSummary       => BuildAttributeSummary(withBonuses: false, marked: false);
    public string AttributeSummaryMarkup => BuildAttributeSummary(withBonuses: true,  marked: true);

    private string OverlayFor(bool marked)
    {
        var summary = BuildAttributeSummary(withBonuses: true, marked: marked);
        return summary.Length == 0 ? string.Empty : $"{summary}  [{TotalAttributePoints}/200]";
    }

    // Overlay du teambuild (résumé + budget [n/200]) AVEC le +2 de flux : version brute (ToolTip
    // + test de vacuité) et version marquée pour la coloration du +2 (affichée via SkillMarkup).
    public string AttributeOverlay       => OverlayFor(marked: false);
    public string AttributeOverlayMarkup => OverlayFor(marked: true);

    // Notifie l'affichage des attributs (résumé Spike + overlay teambuild brut & marqué).
    private void RaiseAttributeDisplayChanged()
    {
        OnPropertyChanged(nameof(AttributeSummary));
        OnPropertyChanged(nameof(AttributeSummaryMarkup));
        OnPropertyChanged(nameof(AttributeOverlay));
        OnPropertyChanged(nameof(AttributeOverlayMarkup));
    }

    // Bascule de langue : re-notifie tout l'affichage dépendant de la langue SANS reconstruire
    // (RefreshAttributeRows effacerait les points investis). Le nom (placeholder), le résumé/overlay
    // d'attributs, chaque ligne d'attribut et chaque slot de compétence rebindent leur DisplayName.
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(DisplayName));
        RaiseProfessionDisplayChanged();
        RaiseAttributeDisplayChanged();
        foreach (var r in PrimaryAttributeRows)   r.RaiseLanguageChanged();
        foreach (var r in SecondaryAttributeRows) r.RaiseLanguageChanged();
        foreach (var r in TitleRankRows)          r.RaiseLanguageChanged();
        foreach (var s in SkillSlots)             s.RaiseLanguageChanged();
    }

    public string EquipmentSummary => _equipment == null ? string.Empty
        : GwEquipmentCodec.Summarize(_equipment);

    public ObservableCollection<SkillSlotViewModel> SkillSlots { get; } =
        new(Enumerable.Range(0, 8).Select(_ => new SkillSlotViewModel()));

    // ── Cap PvE ───────────────────────────────────────────────────────────────

    private long _pveSeq;

    // À appeler après avoir posé une skill dans un slot (drag depuis la liste).
    // Cap GW1 : max 3 skills PvE (Profession.None). Au-delà, on garde la skill qu'on vient
    // de poser et on retire la PvE pré-existante ajoutée le plus récemment (LIFO).
    public void EnforcePveCap(SkillSlotViewModel justPlaced)
    {
        if (justPlaced.Skill?.Profession != Profession.None) return;
        justPlaced.PveSeq = ++_pveSeq;

        var pveSlots = SkillSlots.Where(s => s.Skill?.Profession == Profession.None).ToList();
        if (pveSlots.Count <= 3) return;

        var toRemove = pveSlots
            .Where(s => s != justPlaced)
            .OrderByDescending(s => s.PveSeq)
            .First();
        toRemove.Skill = null;
    }

    // ── Violations ────────────────────────────────────────────────────────────

    public void RefreshViolations()
    {
        // Le cadre rouge signale UNIQUEMENT une incompatibilité de profession, et TOUJOURS
        // (indépendant de la case "Vérifier les compétences") : une skill hors PR/SEC est une
        // erreur grave. Les anciens contrôles (double-élite, doublon, >3 PvE) ne déclenchent plus
        // le cadre rouge — plusieurs élites peuvent être temporairement valides.
        foreach (var s in SkillSlots)
        {
            var skill = s.Skill;
            if (skill == null) { s.HasViolation = false; continue; }

            // Les skills d'allégeance (Profession.None mais verrouillées à une profession)
            // exigent que cette profession soit la PR ou la SEC.
            var required = GwAllegianceData.RequiredProfession(skill);
            var prof = required != Profession.None ? required : skill.Profession;

            s.HasViolation =
                prof != Profession.None &&
                prof != _primaryProfession &&
                prof != _secondaryProfession;
        }
    }

    // ── Profession picker actions ─────────────────────────────────────────────

    public void SetPrimaryProfession(Profession p)
    {
        if (p == _primaryProfession || p == Profession.None) return;
        // Choisir la secondaire actuelle comme primaire = swap PR/SEC (ex. P/N → N/P).
        if (p == _secondaryProfession)
        {
            SwapProfessions();
            return;
        }
        ClearPrimaryAttribute(_primaryProfession);
        _primaryProfession = p;
        OnPropertyChanged(nameof(PrimaryProfession));
        OnPropertyChanged(nameof(SecondaryProfession));
        RaiseProfessionDisplayChanged();
        OnPropertyChanged(nameof(HasPrimaryProfession));
        OnPropertyChanged(nameof(HasSecondaryProfession));
        RefreshAttributeRows();
    }

    public void SetSecondaryProfession(Profession p)
    {
        if (p == _secondaryProfession) return;
        // Choisir la profession PRIMAIRE (réelle) comme secondaire = swap PR/SEC (pas de N/N). Le test
        // exclut None : sélectionner « None » pour la SEC doit toujours l'effacer, même si la PR est None
        // (sinon None==_primaryProfession déclencherait un swap fantôme et rien ne se passerait).
        if (p != Profession.None && p == _primaryProfession)
        {
            if (_secondaryProfession != Profession.None) SwapProfessions();
            return;
        }
        _secondaryProfession = p;
        OnPropertyChanged(nameof(SecondaryProfession));
        RaiseProfessionDisplayChanged();
        OnPropertyChanged(nameof(HasSecondaryProfession));
        RefreshAttributeRows();
    }

    public void SwapProfessions()
    {
        // Rien à échanger uniquement si les deux sont identiques (= None/None). Sinon on autorise
        // aussi None/X ↔ X/None (utile au perso-requête de recherche). L'éditeur, lui, ne déclenche
        // jamais de swap produisant une PR None (son menu Swap exige deux professions posées).
        if (_primaryProfession == _secondaryProfession) return;
        ClearPrimaryAttribute(_primaryProfession);
        (_primaryProfession, _secondaryProfession) = (_secondaryProfession, _primaryProfession);
        OnPropertyChanged(nameof(PrimaryProfession));
        OnPropertyChanged(nameof(SecondaryProfession));
        RaiseProfessionDisplayChanged();
        OnPropertyChanged(nameof(HasPrimaryProfession));
        OnPropertyChanged(nameof(HasSecondaryProfession));
        RefreshAttributeRows();
    }

    // Efface la profession primaire (→ None). Réservé au perso-requête de recherche (« toute PR ») :
    // dans l'éditeur, un perso garde toujours une primaire (pas de None proposé côté PR).
    public void ClearPrimaryProfession()
    {
        if (_primaryProfession == Profession.None) return;
        ClearPrimaryAttribute(_primaryProfession);
        _primaryProfession = Profession.None;
        OnPropertyChanged(nameof(PrimaryProfession));
        RaiseProfessionDisplayChanged();
        OnPropertyChanged(nameof(HasPrimaryProfession));
        RefreshAttributeRows();
    }

    private void ClearPrimaryAttribute(Profession profession)
    {
        var attr = GwAttributeData.PrimaryFor(profession);
        if (attr != null)
            _attributes?.Allocations.RemoveAll(a => a.AttributeId == attr.Id);
    }

    // ── Attribute rows ────────────────────────────────────────────────────────

    private void RefreshAttributeRows()
    {
        foreach (var r in PrimaryAttributeRows)   r.PropertyChanged -= OnAttributeRowChanged;
        foreach (var r in SecondaryAttributeRows) r.PropertyChanged -= OnAttributeRowChanged;

        PrimaryAttributeRows.Clear();
        SecondaryAttributeRows.Clear();

        if (_primaryProfession != Profession.None)
        {
            foreach (var attr in GwAttributeData.ForProfession(_primaryProfession)
                                                .OrderBy(a => a.IsPrimary ? 0 : 1)
                                                .ThenBy(a => a.Name))
            {
                var row = new AttributeRowViewModel(attr.Id, attr.Name, attr.IsPrimary);
                row.Points = GetAttributePoints(attr.Id);
                row.PropertyChanged += OnAttributeRowChanged;
                PrimaryAttributeRows.Add(row);
            }
        }

        if (_secondaryProfession != Profession.None)
        {
            foreach (var attr in GwAttributeData.ForProfession(_secondaryProfession)
                                                .Where(a => !a.IsPrimary)
                                                .OrderBy(a => a.Name))
            {
                // IsSecondary : en PvP, aucun niveau bonus n'est permis sur ces caracs
                // (AttributeRowViewModel.EffectiveMaxBonus → 0). Rebâti à chaque changement
                // de profession, donc la règle s'applique aussi aux persos créés en PvP.
                var row = new AttributeRowViewModel(attr.Id, attr.Name, false) { IsSecondary = true };
                row.Points = GetAttributePoints(attr.Id);
                row.PropertyChanged += OnAttributeRowChanged;
                SecondaryAttributeRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(TotalAttributePoints));
        OnPropertyChanged(nameof(IsOverAttributeBudget));
        RaiseAttributeDisplayChanged();
        OnPropertyChanged(nameof(IsEmptyBuild));
        OnPropertyChanged(nameof(HasInvestedAttributes));
        OnPropertyChanged(nameof(HasBuildContent));
        NotifyTooltipsChanged();
    }

    private int GetAttributePoints(int attributeId)
        => _attributes?.Allocations.FirstOrDefault(a => a.AttributeId == attributeId)?.Points ?? 0;

    private void OnAttributeRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AttributeRowViewModel row) return;

        if (e.PropertyName == nameof(AttributeRowViewModel.Points))
        {
            _attributes ??= new AttributesBuild();
            _attributes.Allocations.RemoveAll(a => a.AttributeId == row.AttributeId);
            if (row.Points > 0)
                _attributes.Allocations.Add(new AttributeAllocation(row.AttributeId, row.Points));

            OnPropertyChanged(nameof(Attributes));
            OnPropertyChanged(nameof(HasAttributes));
            OnPropertyChanged(nameof(TotalAttributePoints));
            OnPropertyChanged(nameof(IsOverAttributeBudget));
        }

        // Points ET PointsDisplay (= changement de bonus) rafraîchissent l'overlay.
        if (e.PropertyName is nameof(AttributeRowViewModel.Points)
                           or nameof(AttributeRowViewModel.PointsDisplay))
        {
            RaiseAttributeDisplayChanged();
            OnPropertyChanged(nameof(IsEmptyBuild));
            OnPropertyChanged(nameof(HasInvestedAttributes));
            OnPropertyChanged(nameof(HasBuildContent));
            // Tout changement d'attribut peut modifier une description (variables résolues au
            // rang de l'attribut de la skill) → on rafraîchit les infobulles de tous les slots.
            NotifyTooltipsChanged();
        }
    }
}
