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
                    OnPropertyChanged(nameof(DurationBoosterSkill));
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

    // ── Ligne d'ATTAQUE NORMALE du spike (lot 6d-2) ───────────────────────────
    // Q19 : deux niveaux de cases — la maîtresse est au build (TeamBuildViewModel.ShowNormalAttacks),
    // celle-ci est la case de CE perso, sur sa carte du roster. Cochée, elle ajoute une ligne
    // artificielle en fin de liste : ses coups d'arme normaux, comptés à la main. Persisté .zcx v23.
    private bool _spikeNormalRow;
    private int _spikeNormalHits = 1;
    private string _spikeNormalWeaponModKey = string.Empty;
    private bool _spikeNormalSunderingProc, _spikeNormalHornbow;

    public bool SpikeNormalRow { get => _spikeNormalRow; set => SetField(ref _spikeNormalRow, value); }

    /// <summary>Nombre de coups d'arme normaux comptés pour ce perso (bornes du ComboBox des procs).</summary>
    public int SpikeNormalHits { get => _spikeNormalHits; set => SetField(ref _spikeNormalHits, value); }

    /// <summary>Mod de PRÉFIXE de l'arme des coups normaux (clé SpikeWeaponMods ; vide = aucun) — Q21.</summary>
    public string SpikeNormalWeaponModKey
    {
        get => _spikeNormalWeaponModKey;
        set
        {
            if (!SetField(ref _spikeNormalWeaponModKey, value ?? string.Empty)) return;
            // Même règle que la ligne d'attaque : la case du proc n'a de sens que sous « de
            // fractionnement » et ne survit pas à un changement de mod.
            if (SpikeWeaponMods.FromKey(_spikeNormalWeaponModKey) != SpikeWeaponMod.Sundering)
                SpikeNormalSunderingProc = false;
        }
    }

    public bool SpikeNormalSunderingProc
    {
        get => _spikeNormalSunderingProc;
        set => SetField(ref _spikeNormalSunderingProc, value);
    }

    public bool SpikeNormalHornbow { get => _spikeNormalHornbow; set => SetField(ref _spikeNormalHornbow, value); }

    // Coup CRITIQUE forcé sur la ligne d'attaque normale de ce perso (28/09/2026) : même case
    // « Critique » que sur une ligne d'attaque, et même maîtresse (« Tout en critique »).
    // Persisté (.zcx v25).
    private bool _spikeNormalCritical;
    public bool SpikeNormalCritical { get => _spikeNormalCritical; set => SetField(ref _spikeNormalCritical, value); }

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

    // Rang effectif d'un effet à rang : le plus fort des PORTEUR(S) qui l'ont équipé (décision
    // Philippe : « le plus fort gagne »), ou null si personne ne l'équipe — pour un esprit, l'appelant
    // retombe alors sur le rang de SIMULATION du bandeau ; un effet « équipé seulement » (Mark of Fury,
    // Energizing Chorus) n'en a pas. La reconnaissance passe par BySkillId, donc la variante « (PvP) »
    // d'un rituel splitté compte comme sa jumelle PvE. Caractéristique propre à chaque effet : Survie
    // (rituels d'origine), Expertise (Infuriating Heat), Magie du sang (Mark of Fury), Motivation
    // (Energizing Chorus). Public : le bandeau en tire le badge des effets « équipés seulement ».
    public static int? WearerRank(
        NatureRitualData.Ritual ritual, IEnumerable<CharacterSlotViewModel> characters)
    {
        string attribute = NatureRitualData.AttributeOf(ritual);
        int? best = null;
        foreach (var c in characters)
            foreach (var slot in c.SkillSlots)
                if (slot.Skill is { } s && NatureRitualData.BySkillId(s.Id)?.Ritual == ritual)
                    best = Math.Max(best ?? 0, c.AttributeLevel(attribute) ?? 0);
        return best;
    }

    // Bonus Roaring Winds pour un ensemble de persos. 0 si le rituel n'est pas actif.
    public static int RoaringWindsBonusFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters, int simRank)
    {
        if (!active.Contains(NatureRitualData.Ritual.RoaringWinds)) return 0;
        return NatureRitualData.RoaringWindsBonusAtRank(
            WearerRank(NatureRitualData.Ritual.RoaringWinds, characters) ?? simRank);
    }

    // Saignement de Ronces (chantier infobulle, lot 4c) : durée en secondes posée sur toute créature assommée
    // à portée, 0 si l'esprit est éteint. Même patron que Roaring Winds — rang de Survie du porteur le plus
    // fort, sinon rang de simulation du bandeau.
    public static int BramblesBleedFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters, int simRank)
    {
        if (!active.Contains(NatureRitualData.Ritual.Brambles)) return 0;
        return NatureRitualData.BramblesBleedAtRank(
            WearerRank(NatureRitualData.Ritual.Brambles, characters) ?? simRank);
    }

    public Func<int>? BramblesBleedProvider { get; set; }

    public int BramblesBleedSeconds => BramblesBleedProvider?.Invoke() ?? OwnerBuild?.BramblesBleedSeconds ?? 0;

    // ── Energizing Chorus : énergie retirée aux cris et chants (chantier infobulle, lot 2b) ──
    // Pas de rang de simulation (Philippe, 15/09/2026) : l'effet n'existe que s'il est équipé, au rang de
    // Motivation le plus haut de ses porteurs.

    public Func<int>? EnergizingChorusReductionProvider { get; set; }
    // Points retirés au prochain cri ou chant de ce perso (0 si Energizing Chorus est inactif).
    public int EnergizingChorusReduction =>
        EnergizingChorusReductionProvider?.Invoke() ?? OwnerBuild?.EnergizingChorusReduction ?? 0;

    // Points retirés pour un ensemble de persos : 0 si l'effet est éteint ou si personne ne le porte.
    public static int EnergizingChorusReductionFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters) =>
        active.Contains(NatureRitualData.Ritual.EnergizingChorus)
        && WearerRank(NatureRitualData.Ritual.EnergizingChorus, characters) is { } rank
            ? NatureRitualData.EnergizingChorusReductionAtRank(rank)
            : 0;

    // ── Sorts de protection du bandeau : incantation et recharge (chantier infobulle, lot 3b) ──
    // Mêmes règles qu'Energizing Chorus : aucun ne joue sans porteur, aucun rang de simulation. Time Ward prend
    // le rang d'Incantation rapide de son porteur le plus fort ; l'Étendard de sagesse n'a pas de rang du tout
    // (sa réduction vaut toujours 50 %, son rang de titre ne change que la durée et la chance).

    public Func<NatureRitualData.TeamSpeed>? TeamSpeedProvider { get; set; }

    public NatureRitualData.TeamSpeed TeamSpeed =>
        TeamSpeedProvider?.Invoke() ?? OwnerBuild?.TeamSpeed ?? default;

    public static NatureRitualData.TeamSpeed TeamSpeedFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters)
    {
        // WearerRank parcourt la liste : on la matérialise (l'arbre d'un team build est un itérateur).
        var list = characters as IReadOnlyList<CharacterSlotViewModel> ?? characters.ToList();
        int timeWard = active.Contains(NatureRitualData.Ritual.TimeWard)
                       && WearerRank(NatureRitualData.Ritual.TimeWard, list) is { } rank
            ? NatureRitualData.TimeWardPercentAtRank(rank)
            : 0;
        // Aucun rang à résoudre : seul « quelqu'un la porte-t-il ? » compte (WearerRank rend 0, pas null, dès
        // qu'un perso l'équipe).
        bool ebon = active.Contains(NatureRitualData.Ritual.EbonBattleStandard)
                    && WearerRank(NatureRitualData.Ritual.EbonBattleStandard, list) is not null;
        return new(timeWard, ebon);
    }

    // ── Tranquility : durée d'enchantement (Lot D) ────────────────────────────

    public Func<int>? TranquilityPercentProvider { get; set; }
    // Pourcentage « expire plus vite » de Tranquility applicable à ce perso (0 si inactif).
    public int TranquilityPercent =>
        TranquilityPercentProvider?.Invoke() ?? OwnerBuild?.TranquilityPercent ?? 0;

    // % Tranquility pour un ensemble de persos. Même règle que Roaring Winds (même attribut, Survie
    // en pleine nature) : rang effectif = MAX des PORTEUR(S) équipé(s), sinon le rang de SIMULATION.
    // 0 si Tranquility n'est pas actif. Le % lui-même dépend du mode (20…50 % en PvE, 10…30 % en PvP).
    public static int TranquilityPercentFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters, int simRank)
    {
        if (!active.Contains(NatureRitualData.Ritual.Tranquility)) return 0;
        return NatureRitualData.TranquilityPercentAtRank(
            WearerRank(NatureRitualData.Ritual.Tranquility, characters) ?? simRank);
    }

    // ── Nature's Renewal : surcoût d'incantation (splitté PvE/PvP) ────────────

    public Func<int>? NaturesRenewalPercentProvider { get; set; }
    // Surcoût d'incantation « plus long » (%) applicable aux enchantements/maléfices de ce perso.
    // Toujours renseigné (100 en PvE) : c'est NatureRitualData.CastTime qui teste l'activation.
    public int NaturesRenewalPercent =>
        NaturesRenewalPercentProvider?.Invoke() ?? OwnerBuild?.NaturesRenewalPercent ?? 100;

    // % Nature's Renewal pour un ensemble de persos. En PvE c'est 100 (×2, sans rang) ; en PvP,
    // même règle de rang que Roaring Winds / Tranquility.
    public static int NaturesRenewalPercentFor(
        IEnumerable<CharacterSlotViewModel> characters, int simRank)
    {
        if (!NatureRitualData.PvpVariants) return 100;
        return NatureRitualData.NaturesRenewalPercentAtRank(
            WearerRank(NatureRitualData.Ritual.NaturesRenewal, characters) ?? simRank);
    }

    // ── Prolongateurs de durée d'enchantement par perso (Lot D) ───────────────

    // Le perso porte-t-il un prolongateur (Blessed Aura / Extend Enchantments) → l'icône de toggle
    // apparaît. Re-notifié quand une skill change (cf. constructeur).
    public bool HasDurationBooster => DurationBoosterSkill is not null;

    // La compétence qui porte le toggle, pour afficher SON icône plutôt qu'un glyphe d'horloge (demande de
    // Philippe du 16/09/2026). Un perso ne peut en porter qu'une en pratique ; la 1re de la barre gagne.
    public Skill? DurationBoosterSkill =>
        SkillSlots.Select(s => s.Skill)
                  .FirstOrDefault(sk => sk?.Id is EnchantmentDuration.BlessedAuraSkillId
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
    public int EnchantingModPercent =>
        ActiveWeaponSetHasMod(EnchantmentDuration.IsEnchantingMod) ? EnchantmentDuration.EnchantingModPercent : 0;

    // L'ARMURE du perso porte-t-elle l'insigne Poing-de-fer ? → ses assommements durent 1 s de plus (lot 4c).
    // Détecté tout seul, sans icône, comme le mod « of Enchanting » (décision Q7 du lot 4) : un insigne n'a ni
    // charge ni chance. L'armure est partagée par les 4 sets d'armes, donc le set actif n'entre pas en compte ;
    // deux pièces insignées ne cumulent pas (le jeu plafonne à 3 s de toute façon).
    public bool HasStonefistInsignia =>
        _equipment?.Armor.SelectMany(i => i.ModifierIds).Contains(KnockdownData.StonefistModId) == true;

    // Le set d'armes ACTIF porte-t-il un mod « Furious » ? → icône du mod dans le bandeau du perso
    // (chantier infobulle, lot 1a ; allumée = le doublement d'adrénaline a proc).
    public bool HasFuriousMod => ActiveWeaponSetHasMod(AdrenalineBoostData.IsFuriousMod);

    private bool ActiveWeaponSetHasMod(Func<int, bool> isMod) => ActiveWeaponSetModIds().Any(isMod);

    private IEnumerable<int> ActiveWeaponSetModIds() =>
        ActiveWeaponSetItems().SelectMany(i => i.ModifierIds);

    private IEnumerable<EquipmentItem> ActiveWeaponSetItems()
    {
        var eq = _equipment;
        if (eq is null || eq.WeaponSets.Count == 0) return [];
        return eq.WeaponSets[Math.Clamp(eq.ActiveSet, 0, eq.WeaponSets.Count - 1)].Items;
    }

    // Type de l'arme de MAIN du set d'armes actif (None = aucune arme renseignée). Il décide le périmètre des
    // ajouteurs de condition à arme libre et la présence de l'arc qu'exige le Sceau de l'Archer (lot 4b).
    private WeaponKind ActiveWeaponKind()
    {
        foreach (var item in ActiveWeaponSetItems())
            if (item.Slot == GwEquipmentInfo.SlotWeapon
                && GwEquipmentInfo.Items.TryGetValue(item.ItemId, out var info)
                && info.Weapon != WeaponKind.None)
                return info.Weapon;
        return WeaponKind.None;
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
        bool changed = active ? _activeAttributeBoosts.Add(skillId) : _activeAttributeBoosts.Remove(skillId);
        // Un seul effet par famille sur un perso (règle du jeu ; glyphes le 15/09/2026, puis postures, préparations, sorts
        // d'arme, formes et sorts d'objet au cadrage du lot 3) : allumer une icône éteint celles de sa famille.
        if (active && ExclusiveFamilyOf(skillId) is { } family)
        {
            changed |= _activeAttributeBoosts.RemoveWhere(id => id != skillId && ExclusiveFamilyOf(id) == family) > 0;
            // ⚠ Et les CASES de la fenêtre Spike, qui portent le même genre d'état pour les buffs sans
            // icône de carte — l'Arme d'éclats est un sort d'arme (28/09/2026). Sans ce pont, allumer
            // l'icône d'un sort d'arme laissait sa case cochée : deux sorts d'arme actifs à la fois,
            // ce que le jeu interdit et que la fenêtre interdisait déjà dans l'autre sens.
            if (family == WeaponSpellType)
                foreach (var t in SpikeBuffToggles)
                    if (t.IsWeaponSpell && t.Descriptor.CardToggleId != skillId
                        && IsSpikeBuffActive(t.Descriptor.Key))
                    {
                        SetSpikeBuff(t.Descriptor.Key, false);
                        t.RaiseActiveChanged();
                    }
        }
        if (changed)
        {
            NotifyTooltipsChanged();
            // « A l'aide ! » et Atmosphère enchanteresse (lot 7a) changent les infobulles des AUTRES membres.
            // Vol de vitesse et Vents glaciaux (lot 7b) aussi, depuis la carte de leur lanceur.
            if (TargetedAllyEffectData.IsToggleId(skillId) || TargetedFoeEffectData.IsTeamToggleId(skillId))
                OwnerBuild?.RefreshOtherSkillTooltips(this);
            // AttributeBoostToggles construit des instances FRAÎCHES à chaque lecture (IsActive lu au
            // constructeur) : sans cette notification, l'ItemsControl du bandeau garde ses anciens
            // conteneurs et le cadre vert ne bascule jamais visuellement (skills ≠ changées, donc le
            // handler du constructeur ne se déclenche pas ici).
            OnPropertyChanged(nameof(AttributeBoostToggles));
            // Lot 6e : la fenêtre Spike affiche une VUE FILTRÉE de la même rangée. Sans cette seconde
            // notification, cliquer une icône DANS le Spike laisserait son cadre vert inchangé — le
            // recalcul, lui, se ferait quand même, et le total bougerait sans que l'icône bouge.
            OnPropertyChanged(nameof(SpikeBoostToggles));
            OnPropertyChanged(nameof(HasSpikeBoostToggles));
        }
    }

    // Bandeau local : une icône par compétence qualifiante ÉQUIPÉE (bascule indépendante par
    // compétence — un perso peut porter plusieurs boosts à la fois, ex. Aura of the Lich +
    // Masochism sur un même Necro), boost d'attribut ou accélérateur d'adrénaline personnel
    // (chantier infobulle, lot 1a), dans l'ordre de la barre. Puis les effets reçus d'un allié,
    // proposés si N'IMPORTE QUEL perso de l'équipe les porte — y compris le lanceur lui-même :
    // Heroic Refrain (Lot D) et Weapon of Fury (lot 1a) ne sont délibérément PAS des boosts
    // personnels (leur résolution est inter-perso), donc le scan "équipée" ne les fait jamais
    // remonter, même chez le lanceur. Enfin le mod « Furious », si le set d'armes actif le porte.
    public IEnumerable<AttributeBoostIndicatorViewModel> AttributeBoostToggles
    {
        get
        {
            var items = SkillSlots.Select(s => s.Skill)
                .Where(sk => sk != null)
                .Select(sk => (Skill: sk!, ToggleId: PersonalToggleIdOf(sk!)))
                .Where(t => t.ToggleId != null)
                .Select(t => new AttributeBoostIndicatorViewModel(this, t.Skill, t.ToggleId!.Value, received: false));

            var (hrSkill, _) = HeroicRefrain;
            if (hrSkill != null)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, hrSkill, HeroicRefrainData.SkillId, received: true));
            if (WeaponOfFury is { } wof)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, wof, AdrenalineBoostData.WeaponOfFurySkillId, received: true));
            if (WeaponOfQuickening is { } woq)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, woq, SkillSpeedBoostData.WeaponOfQuickeningSkillId, received: true));
            if (SunderingWeapon.Skill is { } sw)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, sw, ConditionDurationData.SunderingWeaponSkillId, received: true));
            if (JudgesInsight is { } ji)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, ji, ConditionDurationData.JudgesInsightSkillId, received: true));
            if (GreatDwarfWeapon is { } gdw)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, gdw, KnockdownData.GreatDwarfWeaponSkillId, received: true));
            // Effets de dégâts reçus d'un allié (lot 6b) : les 6 NOUVELLES icônes. Les trois lignes
            // ci-dessus émettent déjà celles que les lots 4b et 4c avaient posées pour d'autres raisons
            // (armure brisée, conversion en sacré, chance d'assommer) — leur rôle « dégâts » s'ajoute sur
            // la MÊME icône, il ne lui en crée pas une deuxième.
            foreach (int toggleId in DamageBoostData.ReceivedToggleIds)
            {
                if (toggleId is ConditionDurationData.SunderingWeaponSkillId
                             or ConditionDurationData.JudgesInsightSkillId
                             or KnockdownData.GreatDwarfWeaponSkillId) continue;
                if (ReceivedDamageBoosts.TryGetValue(toggleId, out var recv))
                    items = items.Append(new AttributeBoostIndicatorViewModel(this, recv.Skill, toggleId, received: true));
            }
            // Effets posés sur un allié (lot 7a) : « A l'aide ! », Atmosphère enchanteresse, Harmonie persistante.
            foreach (var (toggleId, skill) in TargetedAllyEffects.OrderBy(kv => kv.Key))
                items = items.Append(new AttributeBoostIndicatorViewModel(this, skill, toggleId, received: true));
            if (HasFuriousMod)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, null, AdrenalineBoostData.FuriousModToggleId, received: false));
            if (HasSunderingMod)
                items = items.Append(new AttributeBoostIndicatorViewModel(this, null, DamageBoostData.SunderingModToggleId, received: false));

            return items;
        }
    }

    // Id d'icône d'une compétence personnelle togglable, null sinon : boost d'attribut (id de la
    // compétence), accélérateur d'adrénaline « you », réduction de coût d'énergie (lot 2) ou effet de recharge et
    // d'incantation (lot 3), allongeur de durée (lot 4a) ou compétence de substitution de caractéristique (lot 5)
    // — id de base : une variante « (PvP) » partage l'icône de sa
    // jumelle et reste allumée quand le catalogue change de mode. Une compétence présente dans plusieurs tables (Glyph of
    // Energy, mais aussi Pose de pratique et Lingwah, dans deux lots chacune) n'a qu'une icône.
    private static int? PersonalToggleIdOf(Skill skill) =>
        AttributeBoostData.BySkillId(skill.Id) is not null ? skill.Id
        : AdrenalineBoostData.BySkillId(skill.Id) is { Scope: AdrenalineBoostScope.Self } d ? d.ToggleId
        : EnergyCostBoostData.BySkillId(skill.Id) is { } e ? e.ToggleId
        : SkillSpeedBoostData.BySkillId(skill.Id) is { Received: false } v ? v.ToggleId
        : SkillDurationBoostData.BySkillId(skill.Id) is { Received: false } u ? u.ToggleId
        : ConditionDurationData.BySkillId(skill.Id) is { Received: false } c ? c.ToggleId
        : ConditionDurationData.ConverterBySkillId(skill.Id) is { Received: false } k ? k.ToggleId
        : skill.Id == ConditionDurationData.ArcherSignetSkillId ? skill.Id
        : AttributeSubstitutionData.BySkillId(skill.Id) is not null ? skill.Id
        : DamageBoostData.BySkillId(skill.Id) is { Received: false } b ? b.ToggleId
        : null;
    // ⚠ Les effets du BANDEAU (lot 6c) en sont absents par construction : DamageBoostData.BySkillId ne les
    // connaît pas. Sans ça, un Rôdeur qui porte le Vannage sur sa barre aurait DEUX icônes pour le même effet.

    // Familles dont un perso ne porte qu'un effet à la fois (wiki *Effect stacking* : « one stance, one preparation, one
    // glyph, one weapon spell, and one form at a time », plus un seul objet tenu). Null = icône hors de ces familles.
    private static readonly HashSet<string> ExclusiveSkillTypes = new(StringComparer.Ordinal)
        { "Glyph", "Stance", "Preparation", "Form", "Item Spell", "Weapon Spell" };

    // Le SkillType des sorts d'arme, tel que la base l'écrit : la seule famille dont l'exclusivité
    // franchit la frontière icônes ↔ cases de la fenêtre Spike (cf. SetAttributeBoost).
    private const string WeaponSpellType = "Weapon Spell";

    private string? ExclusiveFamilyOf(int toggleId)
    {
        // Sorts d'arme reçus d'un allié : leur compétence n'est pas forcément sur la barre de CE perso.
        if (toggleId is AdrenalineBoostData.WeaponOfFurySkillId or SkillSpeedBoostData.WeaponOfQuickeningSkillId
                     or ConditionDurationData.SunderingWeaponSkillId or KnockdownData.GreatDwarfWeaponSkillId)
            return "Weapon Spell";
        // Effets de dégâts reçus (lot 6b) : la recherche par la barre, plus bas, ne peut pas les voir —
        // leur compétence est chez le LANCEUR. L'Arme brute et l'Arme du tourment rejoignent donc ici la
        // famille « un seul sort d'arme à la fois », déduite du type de la compétence et non d'une liste.
        if (ReceivedDamageBoosts.TryGetValue(toggleId, out var received)
            && ExclusiveSkillTypes.Contains(received.Skill.SkillType))
            return received.Skill.SkillType;
        // Les effets qui exigent un TYPE D'ARME (3 conjurations + Aura de poussière d'ébène) : une arme
        // n'inflige qu'un type de dégâts à la fois, donc au plus un d'entre eux peut agir (lot 6a).
        if (DamageBoostData.ExclusiveElementFamily(toggleId) is { } element) return element;
        return SkillSlots.Select(s => s.Skill)
            .FirstOrDefault(sk => sk is not null && ExclusiveSkillTypes.Contains(sk.SkillType) && PersonalToggleIdOf(sk) == toggleId)
            ?.SkillType;
    }

    public bool HasAttributeBoostToggles => AttributeBoostToggles.Any();

    /// <summary>
    /// Lot 6e — la rangée d'icônes de la carte du perso DANS la fenêtre Spike : les mêmes que celles de
    /// la vue Build, filtrées à celles dont l'état change un chiffre de cette fenêtre-là.
    ///
    /// ⚠ Le filtre n'est pas une liste, ce sont DEUX questions posées au code :
    ///  • l'icône porte-t-elle un descripteur de dégâts que le Spike ne compte pas déjà par un autre
    ///    chemin (<see cref="SpikeBoostCoverage.AlreadyCounted"/>) — les 23 effets du lot 6e ;
    ///  • ou est-elle l'icône d'un des buffs d'arme de la fenêtre (28/09/2026, demande de Philippe) ?
    ///    Depuis la règle « allumé d'un côté OU de l'autre » du 6e-b, l'allumer AGIT sur les chiffres,
    ///    elle a donc sa place ici — et surtout, c'était le seul interrupteur qui manquait quand le
    ///    lanceur du buff est hors du roster du spike : sa case, elle, n'est alors pas proposée.
    ///
    /// Le reste sort tout seul : l'adrénaline, l'énergie, la recharge, les durées et la substitution de
    /// caractéristique ne déplacent aucun chiffre de spike. ⚠ Et les effets comptés par un compteur
    /// « Procs » (conjurations, Ordres, Cent lames…) restent dehors exprès : leur icône n'y changerait
    /// rien, c'est le compteur de la ligne qui les porte.
    /// </summary>
    /// ⚠ Une icône SANS compétence (le mod « de fractionnement », le mod « Furieux ») n'a aucun
    /// descripteur : elle sort d'elle-même, et c'est juste — la fenêtre a sa case de ligne pour ce mod.
    /// ⚠ Les effets du BANDEAU d'équipe (« Visez les yeux ! », « Ensemble et unis ! ») ne sont dans
    /// aucune rangée de carte, par construction : ils ont leur propre bandeau en haut de la fenêtre
    /// depuis le 28/09/2026 (SpikeViewModel.TeamEffects).
    public IEnumerable<AttributeBoostIndicatorViewModel> SpikeBoostToggles =>
        AttributeBoostToggles.Where(t => t.Skill is { } sk
            && (DamageBoostData.DescriptorsFor(sk.Id).Any(d => !SpikeBoostCoverage.AlreadyCounted(d, sk.Name))
                || SpikeWeaponBuffs.FromCardToggleId(t.ToggleId) is not null));

    public bool HasSpikeBoostToggles => SpikeBoostToggles.Any();

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
            // Caractéristique substituée (lot 5) : le bonus se lit sur elle, sinon la ligne d'attribut
            // afficherait un autre chiffre que l'infobulle de la compétence qui le donne. Aucun cycle
            // possible — SubstitutedAttributeFor ne lit aucun rang, et la lecture reste DIRECTE.
            int value = d.FixedValue ?? SkillProgression.IntAt(
                sk.Progression is { } p && d.ProgressionIndex < p.Length ? p[d.ProgressionIndex] : null,
                FindAttributeRow(SubstitutedAttributeFor(sk) ?? sk.Attribute)?.EffectiveLevel ?? 0) ?? 0;
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

    // ── Accélérateurs d'adrénaline (chantier infobulle, lot 1a) ───────────────
    // Même stockage que les boosts d'attribut (IsAttributeBoostActive/SetAttributeBoost) : id de
    // base de la compétence, 1749 pour Weapon of Fury reçue, id réservé pour le mod « Furious ».

    // Weapon of Fury (« target ally ») : patron Heroic Refrain, sans rang (+100 % fixe). Compétence
    // d'un porteur de l'équipe (icône/tooltip du bandeau), null si personne ne l'équipe.
    public static Skill? WeaponOfFuryFor(IEnumerable<CharacterSlotViewModel> characters) =>
        characters.SelectMany(c => c.SkillSlots).Select(s => s.Skill)
            .FirstOrDefault(sk => sk?.Id == AdrenalineBoostData.WeaponOfFurySkillId);

    // Provider explicite pour le build simple (pas d'OwnerBuild → patron HeroicRefrainProvider).
    public Func<Skill?>? WeaponOfFuryProvider { get; set; }

    public Skill? WeaponOfFury => WeaponOfFuryProvider is { } p ? p() : OwnerBuild?.WeaponOfFury;

    // Weapon of Quickening (lot 3, « target = allies » sur le wiki) : même patron que Weapon of Fury, sans rang (−33 % fixe).
    public static Skill? WeaponOfQuickeningFor(IEnumerable<CharacterSlotViewModel> characters) =>
        characters.SelectMany(c => c.SkillSlots).Select(s => s.Skill)
            .FirstOrDefault(sk => sk?.Id == SkillSpeedBoostData.WeaponOfQuickeningSkillId);

    public Func<Skill?>? WeaponOfQuickeningProvider { get; set; }

    public Skill? WeaponOfQuickening => WeaponOfQuickeningProvider is { } p ? p() : OwnerBuild?.WeaponOfQuickening;

    // Sundering Weapon (lot 4b, « target ally ») : même patron, mais AVEC un rang — la durée d'armure brisée
    // qu'elle ajoute suit le Communion du LANCEUR, donc le plus fort de ses porteurs (« le plus fort gagne »).
    public static (Skill? Skill, int Rank) SunderingWeaponFor(IEnumerable<CharacterSlotViewModel> characters)
    {
        Skill? found = null;
        int best = 0;
        foreach (var c in characters)
            foreach (var slot in c.SkillSlots)
                if (slot.Skill is { } sk && sk.Id == ConditionDurationData.SunderingWeaponSkillId)
                {
                    found ??= sk;
                    best = Math.Max(best, c.AttributeLevel(ConditionDurationData.SunderingWeaponAttribute) ?? 0);
                }
        return (found, best);
    }

    public Func<(Skill? Skill, int Rank)>? SunderingWeaponProvider { get; set; }

    public (Skill? Skill, int Rank) SunderingWeapon =>
        SunderingWeaponProvider is { } p ? p() : OwnerBuild?.SunderingWeapon ?? (null, 0);

    // Clairvoyance du juge (lot 4b, « target ally ») : patron Weapon of Fury, sans rang — elle convertit les
    // attaques du receveur en sacré, ce qui suspend l'Application de poison.
    public static Skill? JudgesInsightFor(IEnumerable<CharacterSlotViewModel> characters) =>
        characters.SelectMany(c => c.SkillSlots).Select(s => s.Skill)
            .FirstOrDefault(sk => sk?.Id == ConditionDurationData.JudgesInsightSkillId);

    public Func<Skill?>? JudgesInsightProvider { get; set; }

    public Skill? JudgesInsight => JudgesInsightProvider is { } p ? p() : OwnerBuild?.JudgesInsight;

    // Arme du Grand Nain (lot 4c) : patron Clairvoyance du juge, sans rang — seul compte le fait qu'elle donne
    // une chance d'assommer (icône allumée = la chance a joué, idiome du chantier).
    // ⚠ C'est la SEULE des cinq diffusions à dire « Cannot self-target » : son porteur ne peut JAMAIS la
    // recevoir. <paramref name="receiver"/> est donc exclu du balayage — un perso seul qui la porte n'a pas
    // d'icône du tout, et dans une équipe où deux persos la portent, chacun la reçoit de l'autre.
    public static Skill? GreatDwarfWeaponFor(
        IEnumerable<CharacterSlotViewModel> characters, CharacterSlotViewModel? receiver = null) =>
        characters.Where(c => !ReferenceEquals(c, receiver))
            .SelectMany(c => c.SkillSlots).Select(s => s.Skill)
            .FirstOrDefault(sk => sk?.Id == KnockdownData.GreatDwarfWeaponSkillId);

    public Func<CharacterSlotViewModel, Skill?>? GreatDwarfWeaponProvider { get; set; }

    public Skill? GreatDwarfWeapon =>
        GreatDwarfWeaponProvider is { } p ? p(this) : OwnerBuild?.GreatDwarfWeaponFor(this);

    // ── Effets posés sur un ALLIÉ (chantier infobulle, lot 7a) ──────────────
    // « A l'aide ! », Atmosphère enchanteresse et Harmonie persistante : l'icône vit sur la carte de l'allié qui
    // REÇOIT l'effet, proposée dès qu'un membre de l'équipe porte la compétence. ⚠ Les deux premières changent les
    // infobulles des AUTRES membres (les sorts qui visent cet allié) : c'est la première fois qu'une icône de carte
    // agit hors de son perso, d'où OthersHaveLit et le rafraîchissement d'équipe de SetAttributeBoost.

    /// <summary>Les effets du lot 7a que <paramref name="receiver"/> peut recevoir, par id d'icône (compétence d'un
    /// porteur, pour l'icône et son infobulle). « A l'aide ! » n'est proposée que s'il existe un AUTRE membre : ses
    /// propres sorts n'en profitent pas, donc seul, l'icône ne ferait rien. Atmosphère : « Cannot self-target ».</summary>
    public static IReadOnlyDictionary<int, Skill> TargetedAllyEffectsFor(
        IEnumerable<CharacterSlotViewModel> characters, CharacterSlotViewModel receiver)
    {
        var list = characters as IReadOnlyCollection<CharacterSlotViewModel> ?? characters.ToList();
        Dictionary<int, Skill>? found = null;
        bool hasOther = list.Any(c => !ReferenceEquals(c, receiver));
        foreach (var c in list)
            foreach (var slot in c.SkillSlots)
            {
                if (slot.Skill is not { } sk) continue;
                int? toggleId = TargetedAllyEffectData.ToggleIdOf(sk.Id)
                    ?? (sk.Id == SkillDurationBoostData.EnduringHarmonySkillId ? sk.Id : null);
                if (toggleId is not { } id || (found?.ContainsKey(id) ?? false)) continue;
                if (TargetedAllyEffectData.CannotSelfTarget(id) && ReferenceEquals(c, receiver)) continue;
                if (id == TargetedAllyEffectData.HelpSkillId && !hasOther) continue;
                (found ??= [])[id] = sk;
            }
        return found ?? EmptyTargetedAllyEffects;
    }

    private static readonly Dictionary<int, Skill> EmptyTargetedAllyEffects = [];

    public Func<CharacterSlotViewModel, IReadOnlyDictionary<int, Skill>>? TargetedAllyEffectsProvider { get; set; }

    public IReadOnlyDictionary<int, Skill> TargetedAllyEffects =>
        TargetedAllyEffectsProvider is { } p ? p(this)
        : OwnerBuild?.TargetedAllyEffectsFor(this) ?? EmptyTargetedAllyEffects;

    /// <summary>Effet du lot 7a allumé sur CE perso ET encore proposé (un porteur existe) — sinon une icône restée
    /// allumée dans un fichier enregistré agirait sans rien à l'écran pour l'éteindre.</summary>
    public bool HasLitTargetedAllyEffect(int toggleId) =>
        IsAttributeBoostActive(toggleId) && TargetedAllyEffects.ContainsKey(toggleId);

    /// <summary>Noms des AUTRES membres qui portent cet effet allumé, joints (null = personne ; build simple : jamais).
    /// Le nom sert à la phrase de l'infobulle : sans lui, rien ne dit d'où vient le chiffre changé.</summary>
    private string? OthersWithLit(int toggleId) =>
        OwnerBuild?.OthersWithLitTargetedAllyEffect(this, toggleId) is { Count: > 0 } names ? string.Join(", ", names) : null;

    // ── Effets de dégâts du BANDEAU D'ÉQUIPE (chantier infobulle, lot 6c) ────
    // Le bandeau est un environnement GLOBAL : ses effets touchent tous les persos à la fois, et leur
    // compétence n'est sur la barre de personne. Il faut donc pouvoir la retrouver dans le catalogue —
    // ambiant, comme NatureRitualData.PvpVariants et AppLanguage.IsFr, posé une fois par MainViewModel.
    public static Func<IReadOnlyCollection<Skill>>? SkillCatalog { get; set; }

    private static readonly Dictionary<int, Skill> _bandSkills = [];
    private static int _bandSkillsFrom = -1;

    /// <summary>La compétence d'un effet du bandeau, prise au catalogue (null tant qu'il n'est pas chargé).
    /// Cache reconstruit quand le catalogue change de taille — il ne change qu'au chargement.</summary>
    private static Skill? BandSkill(int skillId)
    {
        var all = SkillCatalog?.Invoke();
        if (all is null || all.Count == 0) return null;
        if (_bandSkillsFrom != all.Count)
        {
            _bandSkills.Clear();
            foreach (var s in all)
                // ⚠ Les VARIANTES « (PvP) » entrent dans le cache depuis le lot 6c-2 : deux effets portés
                // (Hymne d'envie, « Visez les yeux ! ») ont des CHIFFRES différents en PvP, et c'est la
                // compétence du mode courant qui les porte. BySkillId reconnaît les deux ids et rend la même
                // entrée, donc ce test-ci attrape la jumelle sans avoir à la nommer.
                if (NatureRitualData.BySkillId(s.Id) is { } band && DamageBoostData.IsBandSkillId(band.SkillId))
                    _bandSkills[s.Id] = s;
            _bandSkillsFrom = all.Count;
        }
        // Compétence du MODE DE JEU courant (PvE ou « (PvP) »), comme le bandeau lui-même.
        int displayed = NatureRitualData.BySkillId(skillId)?.DisplaySkillId ?? skillId;
        return _bandSkills.GetValueOrDefault(displayed);
    }

    /// <summary>
    /// Rang du PORTEUR le plus fort de chaque effet de bandeau dont le chantier infobulle a besoin, par
    /// rituel (lot 6c-2) ; **absent du dictionnaire = personne ne le porte**, donc l'effet n'existe pas,
    /// même resté allumé dans un fichier enregistré avant le retrait de la compétence.
    ///
    /// ⚠ Nouveauté du 6c-2 : les trois esprits du 6c-1 ne portaient que des littéraux, les effets PORTÉS
    /// ont tous un rang — et il se lit chez leur PORTEUR. La caractéristique d'échelle est le plus souvent
    /// absente de la barre de celui qui en profite (la Magie du sang de l'Ordre de la douleur chez un
    /// Guerrier, le rang de l'Avant-garde d'Ebon chez qui ne porte pas l'Étendard) : la lire sur le receveur
    /// rendrait null, donc 0, et le bonus disparaîtrait en silence.
    ///
    /// ⚠ Deux familles y entrent, et la seconde n'est pas une évidence : les effets qui posent un chiffre,
    /// ET les **enchantements de Nécromant**, parce qu'ils ANNULENT l'Ordre du vampire. La Fureur noire
    /// n'a aucun chiffre de dégâts, mais il faut quand même savoir si quelqu'un la porte — sinon un
    /// bandeau resté allumé dans un vieux fichier ferait taire l'Ordre du vampire sans que rien ne
    /// l'explique (l'effet fautif serait invisible : un « porté seulement » sans porteur n'est pas affiché).
    /// </summary>
    public static IReadOnlyDictionary<NatureRitualData.Ritual, int> BandRanksFor(
        IEnumerable<CharacterSlotViewModel> characters)
    {
        var list = characters as IReadOnlyCollection<CharacterSlotViewModel> ?? characters.ToList();
        Dictionary<NatureRitualData.Ritual, int>? found = null;
        foreach (var d in NatureRitualData.All)
        {
            if (!NeedsBandRank(d)) continue;
            if (WearerRank(d.Ritual, list) is not { } rank) continue;
            (found ??= [])[d.Ritual] = rank;
        }
        return found ?? EmptyBandRanks;
    }

    // Les trois esprits du 6c-1 portent des LITTÉRAUX : leur chercher un porteur serait un parcours d'arbre
    // pour rien, et ils sont de toute façon proposés sans porteur.
    private static bool NeedsBandRank(NatureRitualData.Descriptor d) =>
        NatureRitualData.IsNecromancerEnchantment(d.Ritual)
        || DamageBoostData.BandAll.Any(b => b.SkillId == d.SkillId && b.Fixed == 0);

    private static readonly Dictionary<NatureRitualData.Ritual, int> EmptyBandRanks = [];

    public Func<IReadOnlyDictionary<NatureRitualData.Ritual, int>>? BandRanksProvider { get; set; }

    /// <summary>Rangs des porteurs des effets de bandeau utiles au chantier, vus par CE perso
    /// (l'environnement est global : ils sont les mêmes pour tout le monde).</summary>
    public IReadOnlyDictionary<NatureRitualData.Ritual, int> BandRanks =>
        BandRanksProvider?.Invoke() ?? OwnerBuild?.BandRanks ?? EmptyBandRanks;

    /// <summary>Hiver est-il posé ? → les dégâts élémentaires s'AFFICHENT en froid. ⚠ Étiquette seulement :
    /// Hiver convertit les dégâts REÇUS, il ne change pas ce que l'arme inflige, donc il ne déclenche
    /// jamais une conjuration (§ 6.1 du plan, tranché le 26/09/2026).</summary>
    private bool WinterLit => ActiveNatureRituals.Contains(NatureRitualData.Ritual.Winter);

    /// <summary>Briseur de pierre allumé sur ce perso ? Il force en TERRE tout ce qu'il inflige
    /// d'élémentaire ou de physique, **sorts compris** — d'où un drapeau porté par les boosts, et pas
    /// seulement une étape de la chaîne des attaques.</summary>
    private bool StoneStrikerLit =>
        IsAttributeBoostActive(ConditionDurationData.StoneStrikerSkillId)
        && FindEquippedSkill(ConditionDurationData.StoneStrikerSkillId) is not null;

    /// <summary>Les dégâts du FAMILIER sont-ils encore physiques ? Un familier n'inflige que du physique,
    /// et le seul effet modélisé qui le convertisse est le Grand brasier (« creatures in range » — le
    /// familier en est une). Les convertisseurs personnels du perso, eux, ne touchent que SON arme.</summary>
    private bool PetDamageStillPhysical =>
        !ActiveNatureRituals.Contains(NatureRitualData.Ritual.GreaterConflagration);

    // ── Effets de dégâts REÇUS d'un allié (chantier infobulle, lot 6b) ────────
    // ⚠ Une SEULE voie pour les 9 sources, pilotée par DamageBoostData.ReceivedAll — là où les lots 1a
    // à 4c avaient un bloc copié par effet (Weapon of Fury, Weapon of Quickening, Sundering Weapon,
    // Clairvoyance du juge, Arme du Grand Nain). Ajouter une source ne demande donc qu'un descripteur,
    // et le piège du lot 4c (une diffusion absente de la signature du teambuild n'apparaît jamais chez
    // les AUTRES persos) ne peut plus se produire source par source : la signature est construite ici.
    //
    // Rang : celui du LANCEUR le plus fort qui la porte (« le plus fort gagne », patron de l'Arme de
    // fractionnement du lot 4b), lu sur la caractéristique de la compétence source — donc la
    // substitution du lot 5 s'y applique gratuitement. ⚠ Deux sources tournent sur un rang de TITRE
    // (Arme du Grand Nain : Deldrimor) : si son porteur n'a pas saisi son rang, la ligne vaut 0 et le
    // bonus tombe au minimum, en silence. Accepté par Philippe le 26/09/2026 (Q5 du cadrage 6b).
    public static IReadOnlyDictionary<int, (Skill Skill, int Rank)> ReceivedDamageBoostsFor(
        IEnumerable<CharacterSlotViewModel> characters, CharacterSlotViewModel? receiver = null)
    {
        var list = characters as IReadOnlyCollection<CharacterSlotViewModel> ?? characters.ToList();
        Dictionary<int, (Skill Skill, int Rank)>? found = null;
        foreach (var d in DamageBoostData.ReceivedAll)
            foreach (var c in list)
            {
                // « Cannot self-target » : le porteur est exclu du balayage qui lui proposerait l'effet.
                if (d.CannotSelfTarget && ReferenceEquals(c, receiver)) continue;
                if (c.FindEquippedSkill(d.SkillId) is not { } sk) continue;
                int rank = c.AttributeLevel(c.SubstitutedAttributeFor(sk) ?? sk.Attribute) ?? 0;
                found ??= [];
                found[d.ToggleId] = found.TryGetValue(d.ToggleId, out var prev) && prev.Rank >= rank
                    ? prev : (sk, rank);
            }
        return found ?? EmptyReceivedBoosts;
    }

    private static readonly Dictionary<int, (Skill Skill, int Rank)> EmptyReceivedBoosts = [];

    public Func<CharacterSlotViewModel, IReadOnlyDictionary<int, (Skill Skill, int Rank)>>? ReceivedDamageBoostsProvider { get; set; }

    /// <summary>Les effets de dégâts que CE perso peut recevoir d'un allié, par id d'icône.</summary>
    public IReadOnlyDictionary<int, (Skill Skill, int Rank)> ReceivedDamageBoosts =>
        ReceivedDamageBoostsProvider is { } p ? p(this)
        : OwnerBuild?.ReceivedDamageBoostsFor(this) ?? EmptyReceivedBoosts;

    // Effets d'adrénaline du bandeau d'équipe (lot 1b) : Infuriating Heat, Dark Fury, Mark of Fury,
    // Soothing. Environnement du teambuild propriétaire, ou (build simple) provider de l'éditeur.
    public Func<AdrenalineGain.TeamEffects>? TeamAdrenalineProvider { get; set; }

    public AdrenalineGain.TeamEffects TeamAdrenaline =>
        TeamAdrenalineProvider?.Invoke() ?? OwnerBuild?.TeamAdrenaline ?? AdrenalineGain.TeamEffects.None;

    // Effets pour un ensemble de persos : rang du lanceur le plus fort (Expertise pour Infuriating Heat, sinon
    // rang de simulation du bandeau ; Magie du sang pour Mark of Fury). Dark Fury et Mark of Fury ne sont
    // proposés que portés (Philippe, 15/09/2026) : sans porteur — fichier enregistré avant cette règle —, ils
    // n'agissent pas, faute d'icône pour les éteindre.
    public static AdrenalineGain.TeamEffects TeamAdrenalineFor(
        IReadOnlySet<NatureRitualData.Ritual> active, IEnumerable<CharacterSlotViewModel> characters,
        int infuriatingHeatSimRank)
    {
        var list = characters as IReadOnlyCollection<CharacterSlotViewModel> ?? characters.ToList();
        int ih = active.Contains(NatureRitualData.Ritual.InfuriatingHeat)
            ? WearerRank(NatureRitualData.Ritual.InfuriatingHeat, list) ?? infuriatingHeatSimRank : 0;
        int? mof = active.Contains(NatureRitualData.Ritual.MarkOfFury)
            ? WearerRank(NatureRitualData.Ritual.MarkOfFury, list) : null;
        bool Unworn(NatureRitualData.Ritual r) => r switch
        {
            NatureRitualData.Ritual.DarkFury   => WearerRank(r, list) is null,
            NatureRitualData.Ritual.MarkOfFury => mof is null,
            _                                  => false,
        };
        IReadOnlySet<NatureRitualData.Ritual> worn = active.Any(Unworn) ? active.Where(r => !Unworn(r)).ToHashSet() : active;
        return NatureRitualData.AdrenalineTeamEffects(worn, ih, mof ?? 0);
    }

    // Effets d'adrénaline actifs sur CE perso : accélérateurs personnels équipés ET allumés, Weapon
    // of Fury reçue, mod « Furious » qui a proc, effets du bandeau d'équipe. Focused Anger se lit au
    // rang effectif de Leadership du perso — aucun cycle : le gain d'adrénaline ne nourrit aucune
    // caractéristique.
    private IEnumerable<AdrenalineGain.Effect> ActiveAdrenalineEffects()
    {
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && AdrenalineBoostData.BySkillId(sk.Id) is { Scope: AdrenalineBoostScope.Self } d
                && IsAttributeBoostActive(d.ToggleId))
                yield return AdrenalineBoostData.EffectOf(d, sk,
                    d.ScalingAttribute is { } attr ? AttributeLevel(attr) ?? 0 : 0);
        if (IsAttributeBoostActive(AdrenalineBoostData.WeaponOfFurySkillId) && WeaponOfFury is { } wof
            && AdrenalineBoostData.BySkillId(wof.Id) is { } wd)
            yield return AdrenalineBoostData.EffectOf(wd, wof, 0);
        if (IsAttributeBoostActive(AdrenalineBoostData.FuriousModToggleId) && HasFuriousMod)
            yield return AdrenalineBoostData.FuriousModEffect;
        foreach (var e in TeamAdrenaline.Effects)
            yield return e;
    }

    // Gain par touche en centièmes de coup (100 = aucun effet), pour les coups nécessaires des
    // infobulles de compétences d'adrénaline.
    public int AdrenalineGainPerHit => AdrenalineGain.GainPerHit(ActiveAdrenalineEffects());

    // Soothing, lancé par l'ennemi, divise le gain d'adrénaline de CE perso (lot 1b).
    public bool AdrenalineSlowed => TeamAdrenaline.Slowed;

    // Un effet du bandeau d'équipe pèse-t-il sur l'adrénaline ? → coups nécessaires en ambre.
    public bool HasTeamAdrenalineEffect => TeamAdrenaline.Any;

    // Un effet actif enchante-t-il CE perso (Onslaught, Dark Fury) ? Natural Temper est alors sans
    // effet (décision Philippe 14/09/2026) — lu aussi par son icône pour le signaler.
    public bool IsEnchantedByAdrenalineEffect => ActiveAdrenalineEffects().Any(e => e.Enchants);

    // ── Réductions de coût d'énergie (chantier infobulle, lot 2) ──────────────
    // Même stockage que les boosts d'attribut (id de base de la compétence). Rang de la caractéristique
    // d'échelle lu via AttributeLevel — aucun cycle : un coût d'énergie ne nourrit aucune caractéristique.

    // Réductions actives du perso qui touchent cette compétence : compétences équipées ET allumées.
    public EnergyReduction EnergyReductionFor(Skill target)
    {
        var active = new List<(EnergyCostBoostDescriptor, int)>();
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && EnergyCostBoostData.BySkillId(sk.Id) is { } d && IsAttributeBoostActive(d.ToggleId))
                active.Add((d, EnergyCostBoostData.ValueOf(d, sk,
                    d.ScalingAttribute is { } attr ? AttributeLevel(attr) ?? 0 : 0)));
        // Atmosphère enchanteresse (lot 7a) : posée sur CE perso (ses enchantements sur lui-même) ou sur un AUTRE
        // membre (ses enchantements qui visent un allié) — icône allumée = on vise l'allié qui la porte.
        // Le nom de l'allié visé part avec la réduction : un autre membre si l'enchantement peut le viser, sinon soi ("").
        bool onMe = HasLitTargetedAllyEffect(TargetedAllyEffectData.AirOfEnchantmentSkillId);
        string? others = OthersWithLit(TargetedAllyEffectData.AirOfEnchantmentSkillId);
        string? aoeAlly = null;
        if (TargetedAllyEffectData.AirOfEnchantmentReaches(target, onMe: false, onOther: others is not null)) aoeAlly = others;
        else if (TargetedAllyEffectData.AirOfEnchantmentReaches(target, onMe, onOther: false)) aoeAlly = "";
        if (aoeAlly is not null)
            active.Add((TargetedAllyEffectData.AirOfEnchantmentCost, TargetedAllyEffectData.AirOfEnchantmentCost.FixedValue));
        return active.Count == 0 ? default
            : EnergyCostBoostData.ReductionFor(target, active) with { AirOfEnchantmentAlly = aoeAlly };
    }

    // ── Recharge et incantation (chantier infobulle, lot 3) ───────────────────
    // Même stockage que les boosts d'attribut (id de base de la compétence, 1268 pour Weapon of Quickening reçue). Rang lu
    // via AttributeLevel (une recharge ne nourrit aucune caractéristique), sauf Ritual Lord : son propre bonus ne compte pas.

    // Effets actifs du perso qui touchent cette compétence : compétences équipées ET allumées, Weapon of Quickening reçue.
    public SkillSpeed SkillSpeedFor(Skill target)
    {
        var active = new List<(SkillSpeedBoostDescriptor, Skill, int)>();
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && SkillSpeedBoostData.BySkillId(sk.Id) is { Received: false } d && IsAttributeBoostActive(d.ToggleId))
                active.Add((d, sk, d.ScalingAttribute is not { } attr ? 0
                    : d.RawRank ? FindAttributeRow(attr)?.EffectiveLevel ?? 0
                    : AttributeLevel(attr) ?? 0));
        if (IsAttributeBoostActive(SkillSpeedBoostData.WeaponOfQuickeningSkillId) && WeaponOfQuickening is { } woq
            && SkillSpeedBoostData.BySkillId(woq.Id) is { } wd)
            active.Add((wd, woq, 0));
        // « A l'aide ! » (lot 7a) : allumée chez un AUTRE membre = ses sorts qui visent cet allié s'incantent 50 % plus
        // vite. Jamais les siens propres (« other allies' spells »), d'où l'absence de test sur CE perso.
        string? helpAlly = OthersWithLit(TargetedAllyEffectData.HelpSkillId) is { } names
            && TargetedAllyEffects.ContainsKey(TargetedAllyEffectData.HelpSkillId)
            && TargetedAllyEffectData.HelpCast.Applies(target) ? names : null;
        if (helpAlly is not null)
            active.Add((TargetedAllyEffectData.HelpCast, TargetedAllyEffects[TargetedAllyEffectData.HelpSkillId], 0));
        // Vol de vitesse (lot 7b) : allumé chez un AUTRE membre qui porte la version PvE = ses sorts qui visent l'ennemi
        // maudit s'incantent 50 % plus vite chez tous. Si CE perso l'a lui-même allumé, c'est sa propre icône qui joue.
        string? stolenAlly = null;
        if (!IsAttributeBoostActive(TargetedFoeEffectData.StolenSpeedSkillId) && TargetedFoeEffectData.TargetsFoe(target)
            && TeamFoeEffectFrom(TargetedFoeEffectData.StolenSpeedSkillId) is { } ss
            && SkillSpeedBoostData.BySkillId(ss.Skill.Id) is { } sd)
        {
            stolenAlly = ss.Names;
            active.Add((sd, ss.Skill, 0));
        }
        return active.Count == 0 ? default
            : SkillSpeedBoostData.SpeedFor(target, active) with { HelpAlly = helpAlly, StolenSpeedAlly = stolenAlly };
    }

    // ── Allongeurs de durée propre (chantier infobulle, lot 4a) ───────────────

    /// <summary>Pourcentage de rallonge (0 = aucun) que les allongeurs ALLUMÉS de ce perso appliquent à la durée propre
    /// de <paramref name="target"/>. Même collecte que SkillSpeedFor : la compétence équipée porte la progression, le
    /// rang vient de sa caractéristique d'échelle.</summary>
    public int DurationBoostFor(Skill target)
    {
        var active = new List<(SkillDurationBoostDescriptor, Skill, int)>();
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && SkillDurationBoostData.BySkillId(sk.Id) is { Received: false } d && IsAttributeBoostActive(d.ToggleId))
                active.Add((d, sk, d.ScalingAttribute is { } attr ? AttributeLevel(attr) ?? 0 : 0));
        // Harmonie persistante reçue (lot 7a) : allonge les cris et chants que CE perso lance.
        if (HasLitTargetedAllyEffect(SkillDurationBoostData.EnduringHarmonySkillId)
            && SkillDurationBoostData.BySkillId(SkillDurationBoostData.EnduringHarmonySkillId) is { } eh)
            active.Add((eh, TargetedAllyEffects[SkillDurationBoostData.EnduringHarmonySkillId], 0));
        // Vents glaciaux posé par un AUTRE membre (lot 7b) : ses maléfices d'Eau qui visent l'ennemi durent plus longtemps.
        if (ChillingWindsFromAlly(target) is { } cw && SkillDurationBoostData.BySkillId(cw.Skill.Id) is { } cd)
            active.Add((cd, cw.Skill, cw.Rank));
        return active.Count == 0 ? 0 : SkillDurationBoostData.PercentFor(target, active);
    }

    /// <summary>Vents glaciaux allumé chez un AUTRE membre et qui touche <paramref name="target"/> (null sinon) : nom(s)
    /// du ou des lanceurs pour la phrase ambre de l'infobulle. Si CE perso l'a lui-même allumé, sa propre icône joue.</summary>
    public string? ChillingWindsAllyFor(Skill target) => ChillingWindsFromAlly(target)?.Names;

    private TeamFoeEffect? ChillingWindsFromAlly(Skill target) =>
        !IsAttributeBoostActive(TargetedFoeEffectData.ChillingWindsSkillId) && TargetedFoeEffectData.IsWaterHexOnFoe(target)
            ? TeamFoeEffectFrom(TargetedFoeEffectData.ChillingWindsSkillId) : null;

    // ── Effets posés sur un ENNEMI (chantier infobulle, lot 7b) ─────────────
    // L'icône vit sur la carte du LANCEUR (descripteur personnel des lots 3 et 4a) ; la part « alliés » est lue ici,
    // chez les autres membres. Plusieurs lanceurs : le plus haut rang joue, tous sont nommés.

    /// <summary>Un effet du lot 7b partagé par d'autres membres : la compétence du porteur au plus haut rang (pour sa
    /// progression), ce rang, et les noms de tous les porteurs qui l'ont allumé.</summary>
    public sealed record TeamFoeEffect(Skill Skill, int Rank, string Names);

    private TeamFoeEffect? TeamFoeEffectFrom(int toggleId) => OwnerBuild?.TeamFoeEffectFor(this, toggleId);

    /// <summary>La compétence qui PARTAGE l'effet <paramref name="toggleId"/> avec les alliés, si CE perso la porte et
    /// l'a allumée (null sinon) — avec le rang de sa caractéristique d'échelle.</summary>
    public (Skill Skill, int Rank)? SharedFoeEffect(int toggleId)
    {
        if (!IsAttributeBoostActive(toggleId)) return null;
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && TargetedFoeEffectData.SharesWithAllies(sk.Id, toggleId))
            {
                string? attr = SkillSpeedBoostData.BySkillId(sk.Id)?.ScalingAttribute
                            ?? SkillDurationBoostData.BySkillId(sk.Id)?.ScalingAttribute;
                return (sk, attr is null ? 0 : AttributeLevel(attr) ?? 0);
            }
        return null;
    }

    // ── Durées de conditions (chantier infobulle, lot 4b) ─────────────────────

    /// <summary>Ce que les effets actifs de ce perso font aux conditions de <paramref name="target"/> :
    /// conditions AJOUTÉES à ses attaques (Apply Poison, Sharpen Daggers…, Sundering Weapon reçue) et
    /// allongeurs applicables (Sceau de l'Archer allumé arc en main, préfixes d'arme du set actif). Ces
    /// derniers ne dépendent PAS de la compétence : le sceau allonge toute condition qu'elle applique.</summary>
    public ConditionDurations ConditionDurationsFor(Skill target)
    {
        var equipped = ActiveWeaponKind();

        List<ConditionInfliction.Inflicted>? added = null;
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && ConditionDurationData.BySkillId(sk.Id) is { Received: false } d
                && IsAttributeBoostActive(d.ToggleId) && ConditionDurationData.AddsTo(d, sk, target, equipped)
                && !(d.RequiresPhysical && AttackConverted(target, equipped)))
                (added ??= []).AddRange(ConditionInfliction.For(sk, AttributeLevel(d.ScalingAttribute) ?? 0));

        if (IsAttributeBoostActive(ConditionDurationData.SunderingWeaponSkillId)
            && SunderingWeapon is { Skill: { } sw } received
            && ConditionDurationData.BySkillId(sw.Id) is { } wd
            && ConditionDurationData.AddsTo(wd, sw, target, equipped))
            (added ??= []).AddRange(ConditionInfliction.For(sw, received.Rank));

        int all = FindEquippedSkill(ConditionDurationData.ArcherSignetSkillId) is not null
                  && IsAttributeBoostActive(ConditionDurationData.ArcherSignetSkillId)
                  && ConditionDurationData.BowWielded(equipped)
            ? ConditionDurationData.ArcherSignetPercent : 0;

        return new ConditionDurations(added, all, ConditionDurationData.ModConditionsOf(ActiveWeaponSetModIds()));
    }

    // ── Assommement (chantier infobulle, lot 4c) ──────────────────────────────

    /// <summary>Ce que les effets actifs font aux assommements de ce perso : l'insigne Poing-de-fer de son
    /// armure (+1 s), Lien terrestre au bandeau (plancher 3 s), le saignement de Ronces posé sur toute créature
    /// assommée, et l'Arme du Grand Nain reçue, qui donne une chance d'assommer à TOUTES ses attaques d'arme.
    /// AUCUN ne dépend de la compétence visée — c'est <see cref="KnockdownData.Lines"/> qui décide, depuis sa
    /// description, ce qui s'écrit vraiment : d'où une propriété par PERSO, et non par compétence.
    /// La Force sert à la seule Brise-échine, dont les 4 s exigent Force 8.</summary>
    public KnockdownEffects KnockdownEffects => new(
        Stonefist: HasStonefistInsignia,
        Earthbind: ActiveNatureRituals.Contains(NatureRitualData.Ritual.Earthbind),
        BleedSeconds: BramblesBleedSeconds,
        StrengthRank: AttributeLevel("Strength") ?? 0,
        GreatDwarfWeapon: IsAttributeBoostActive(KnockdownData.GreatDwarfWeaponSkillId)
                          && GreatDwarfWeapon is not null);

    /// <summary>Le set d'armes actif porte une arme RENSEIGNÉE qui n'est pas un arc → le Sceau de l'Archer
    /// n'allonge rien, et sa note d'icône le dit (même idiome que Natural Temper sous enchantement). Aucune arme
    /// renseignée : on fait confiance à l'icône, donc pas de note.</summary>
    public bool ArcherSignetWithoutBow =>
        !ConditionDurationData.BowWielded(ActiveWeaponKind());

    /// <summary>Les dégâts de <paramref name="target"/> sont-ils convertis en autre chose que du physique ?
    /// Quatre sources (toutes validées par Philippe le 16/09/2026) : une compétence du perso allumée (forme,
    /// enchantement éclair, Flèches enflammées, Briseur de pierre), la Clairvoyance du juge reçue d'un allié, un
    /// esprit du bandeau (Grand brasier sur tout le physique, Brasier sur les flèches) et un mod d'arme
    /// élémentaire du set actif — ce dernier ne convertit QUE les attaques de l'arme qui le porte.</summary>
    private bool AttackConverted(Skill target, WeaponKind equipped)
    {
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && ConditionDurationData.ConverterBySkillId(sk.Id) is { Received: false } k
                && IsAttributeBoostActive(k.ToggleId)
                && ConditionDurationData.Converts(k.Weapon, target, equipped))
                return true;

        if (IsAttributeBoostActive(ConditionDurationData.JudgesInsightSkillId) && JudgesInsight is not null
            && ConditionDurationData.Converts(ConditionWeaponScope.Physical, target, equipped))
            return true;

        var rituals = ActiveNatureRituals;
        if (rituals.Contains(NatureRitualData.Ritual.GreaterConflagration)
            && ConditionDurationData.Converts(ConditionWeaponScope.Physical, target, equipped))
            return true;
        if (rituals.Contains(NatureRitualData.Ritual.Conflagration)
            && ConditionDurationData.Converts(ConditionWeaponScope.Bow, target, equipped))
            return true;

        return ActiveWeaponSetModIds().Any(ConditionDurationData.IsElementalMod)
               && ConditionDurationData.UsesEquippedWeapon(target, equipped);
    }

    /// <summary>Un effet actif convertit-il TOUTES les attaques de ce perso ? Alors l'Application de poison
    /// n'empoisonne plus rien, et sa note d'icône le dit — sans annuler la préparation elle-même (Philippe,
    /// 16/09/2026 : « ça annule simplement ses effets jusqu'à ce que ses conditions soient à nouveau réunies »).
    /// Les convertisseurs PARTIELS (Avatar de Grenth à la faux, Brasier sur les flèches, mod élémentaire d'une
    /// seule arme) n'allument pas la note : la ligne disparaît là où il faut, et nulle part ailleurs.</summary>
    public bool AttacksNoLongerPhysical
    {
        get
        {
            foreach (var slot in SkillSlots)
                if (slot.Skill is { } sk
                    && ConditionDurationData.ConverterBySkillId(sk.Id) is { Received: false, Weapon: ConditionWeaponScope.Physical } k
                    && IsAttributeBoostActive(k.ToggleId))
                    return true;
            return (IsAttributeBoostActive(ConditionDurationData.JudgesInsightSkillId) && JudgesInsight is not null)
                   || ActiveNatureRituals.Contains(NatureRitualData.Ritual.GreaterConflagration);
        }
    }

    // ── Bonus de dégâts et de critique (chantier infobulle, lot 6a) ───────────
    // Même stockage que les autres icônes du bandeau local. Aucun cycle possible : un bonus de dégâts ne
    // nourrit aucune caractéristique, donc le rang se lit par AttributeLevel (et la substitution du lot 5
    // s'y applique gratuitement, comme le veut le § 6.5 du plan).

    /// <summary>Ce que les effets ALLUMÉS de ce perso font aux dégâts de <paramref name="target"/> : les
    /// paquets de la ligne « bonus d'effets », les points de critique de « Craignez-moi ! », et la
    /// pénétration d'armure (de BASE pour les deux sorts d'objet Ritualiste, en BONUS pour le mod d'arme
    /// « de fractionnement »). Les deux effets qui relèvent un chiffre DANS LE TEXTE (familier, esprits)
    /// n'entrent PAS ici : ils passent par <see cref="TextDamageBonusFor"/>.</summary>
    public DamageBoosts DamageBoostsFor(Skill target)
        => BoostsFor(target, ActiveWeaponKind(), spikeOnly: false, includeSkill: null);

    /// <summary>
    /// Lot 6e — les mêmes effets, vus par la fenêtre SPIKE. Trois différences, et trois seulement :
    ///
    ///  • <b>le filtre</b> : les effets que le Spike compte déjà par un autre chemin sont écartés
    ///    (<see cref="SpikeBoostCoverage.AlreadyCounted"/>), sinon ils compteraient DEUX fois ;
    ///  • <b>le périmètre</b> se juge sur l'arme de la LIGNE (<paramref name="rowWeapon"/>) et non sur
    ///    celle du set actif — tranché par Philippe le 27/09/2026. Le Spike connaît l'arme de chaque
    ///    ligne, forçage manuel compris, et la Méthode de l'Assassin (dagues) et celle du Maître (hors
    ///    dagues) étant des complémentaires exacts, c'est la seule façon de ne jamais appliquer la
    ///    mauvaise des deux ;
    ///  • <b>le tri par ligne</b> : <paramref name="includeSkill"/> dit, pour chaque effet du lot (clé =
    ///    l'id de son descripteur), s'il compte sur CETTE ligne. Il sert aux effets à CHARGES, qui ne
    ///    valent que sur les premières attaques — et, sur la ligne d'attaque normale, à demander les
    ///    effets un par un pour savoir lequel s'arrête au 3ᵉ coup et lequel tient jusqu'au 8ᵉ.
    ///    ⚠ Il porte sur TOUS les effets du lot, pas seulement ceux à charges : demander « seulement
    ///    celui-ci » doit rendre celui-ci SEUL, sinon les permanents reviendraient dans chaque appel et
    ///    seraient comptés autant de fois qu'il y a d'effets à charges.
    ///
    /// ⚠ <paramref name="target"/> à null = un COUP NORMAL (lot 6d-2) : pas une compétence, donc pas de
    /// préparation à perdre, aucun chiffre à relever dans un texte, et un paquet qui subit l'armure par
    /// nature. Le mod d'arme « de fractionnement » est retiré du résultat : la fenêtre a sa propre case
    /// par ligne pour lui, l'y laisser le compterait deux fois lui aussi.
    /// </summary>
    public DamageBoosts SpikeDamageBoostsFor(Skill? target, WeaponKind rowWeapon,
                                             Func<int, bool>? includeSkill = null)
        => BoostsFor(target, rowWeapon, spikeOnly: true, includeSkill);

    // Le CORPS unique de la chaîne. ⚠ Il n'en existe qu'un : la faire vivre deux fois la condamnerait à
    // diverger au prochain effet (leçon du lot 6d-2, où EffectiveElement a été coupée de la même façon).
    // `scopeWeapon` décide du seul PÉRIMÈTRE ; `equipped` (le set actif) reste la vérité de la chaîne de
    // conversion et des règles qui en dépendent — c'est la règle du 6d-1, et aucun des 23 effets du 6e
    // n'exige un élément ni des dégâts physiques, ce que le harnais verrouille.
    private DamageBoosts BoostsFor(Skill? target, WeaponKind scopeWeapon, bool spikeOnly,
                                   Func<int, bool>? includeSkill)
    {
        var equipped = ActiveWeaponKind();
        string? element = target is null ? null : EffectiveElementFor(target, equipped);

        List<DamageBoostPacket>? packets = null;
        List<DamageBoostSource>? sources = null;
        int critical = 0, basePenetration = 0, bonusPenetration = 0, lifeSteal = 0;
        double multiplier = 1.0, weaponMultiplier = 1.0;
        bool elementTaken = false;
        // Arme brute : « aucun effet si l'allié visé est enchanté ». Deuxième entorse assumée à « icône
        // allumée = ça marche » (la première est le § 6.1), tranchée par Philippe le 26/09/2026 : dès que
        // l'application VOIT un enchantement allumé sur ce perso, l'Arme brute tombe. Le cas est courant
        // dès ce lot — Force de l'honneur, Clairvoyance du juge, Vengeance et Affinité vitale sont
        // toutes les quatre des enchantements reçus. Patron : Nature colérique (lot 1a).
        bool enchanted = IsEnchantedByLitEffect;

        List<SuppressedBoost>? suppressed = null;
        void Suppress(Skill sk, DamageBoostSuppression reason) =>
            (suppressed ??= []).Add(new SuppressedBoost(sk.DisplayName, reason));

        // Analyse des paquets de dégâts de la compétence survolée : ne sert qu'à l'Étendard d'honneur, donc
        // calculée à la demande et une seule fois (l'infobulle en refait une de son côté, mais seulement
        // quand elle s'affiche — ici on est sur le chemin de TOUTES les compétences).
        SkillDamage.Analysis? armorAnalysis = null;

        foreach (var (sk, d) in LitDamageBoosts())
        {
            if (d.Kind == DamageBoostKind.TextDamage) continue;
            // ⚠⚠ LE filtre du lot 6e, et la raison d'être du lot : 33 des 56 descripteurs sont DÉJÀ
            // comptés par la fenêtre Spike (compteurs « Procs », cases des 9 buffs d'arme, cas Grenth).
            // Les y importer les compterait DEUX fois. La partition est calculée depuis ces chemins-là,
            // jamais recopiée — cf. SpikeBoostCoverage.
            if (spikeOnly && SpikeBoostCoverage.AlreadyCounted(d, sk.Name)) continue;
            // Le tri PAR LIGNE de l'appelant. Il sert d'abord aux effets à charges (« vos 5 prochaines
            // attaques ») : sans lui, « Je suis le plus fort ! » donnerait son +20 à TOUTES les attaques
            // du spike au lieu des 5 à 8 premières. ⚠ Il vient APRÈS le filtre du 6e, donc il ne voit que
            // les effets du lot — la table de l'infobulle, elle, ne le passe jamais.
            if (includeSkill is not null && !includeSkill(d.SkillId)) continue;
            // ⚠ Depuis le lot 6c-3b, le vol de vie conféré à un esprit qui vole DÉJÀ de la vie passe lui
            // aussi par TextDamageBonusFor : il RELÈVE son chiffre au lieu de s'afficher sur une ligne à
            // part, sinon le lecteur devrait additionner deux nombres qui décrivent le même coup.
            if (target is not null && DamageBoostData.LifeStealReadInText(d, target)) continue;
            // ⚠ Le PÉRIMÈTRE d'abord : un effet qui ne visait pas cette compétence n'a rien à expliquer.
            // Les trois annulations qui suivent, elles, portent sur un effet qui LA VISAIT — c'est
            // précisément quand un chiffre disparaît sous les yeux qu'il faut dire pourquoi.
            // Cible nulle = un coup normal : le périmètre se juge alors sur la seule arme, et le texte
            // de Concentration experte (« bow attack SKILLS ») l'en écarte (Q20 du lot 6d-2).
            bool inScope = target is null
                ? DamageBoostData.AffectsPlainAttack(d, scopeWeapon) && !SpikeBoostCoverage.SkillOnly(d.SkillId)
                : DamageBoostData.AffectsScope(d, target, scopeWeapon);
            if (!inScope) continue;
            if (target is not null && DamageBoostData.PreparationLost(sk, target))
            {
                Suppress(sk, DamageBoostSuppression.PreparationRemoved);
                continue;
            }
            if (d.LostWhenEnchanted && enchanted)
            {
                Suppress(sk, DamageBoostSuppression.Enchanted);
                continue;
            }
            // Ordre du vampire : annulé par TOUT autre enchantement de Nécromant — donc, en pratique, par son
            // propre frère l'Ordre de la douleur et par la Fureur noire. Le test est plus coûteux que les
            // autres (il balaie les trois origines), donc il vient APRÈS le périmètre, jamais avant.
            if (d.LostWhenNecroEnchanted && IsUnderOtherNecromancerEnchantment(sk))
            {
                Suppress(sk, DamageBoostSuppression.NecromancerEnchanted);
                continue;
            }
            // « Augmente les dégâts physiques » (Vannage, et l'Ordre de la douleur au 6c-2) : le bonus SAUTE
            // dès que l'arme est convertie (glossaire G3). Et il le dit ICI, là où le chiffre manque — la
            // leçon de la QA du 6b.
            // ⚠ Sur un COUP NORMAL, un bonus « aux dégâts physiques » est écarté PUREMENT ET SIMPLEMENT,
            // et c'est un choix prudent, pas un oubli : aucun des 23 effets du lot 6e n'a RequiresPhysical
            // (les deux qui l'ont — Vannage et Ordre de la douleur — sont comptés par leur compteur
            // « Procs »), donc ce chemin est mort aujourd'hui. Le harnais le VERROUILLE : si un futur
            // descripteur arrive avec RequiresPhysical sans chemin Spike, il rougit au lieu de laisser
            // un chiffre entrer sans que personne ait jugé sa conversion.
            if (d.RequiresPhysical && (target is null || AttackConverted(target, equipped)))
            {
                if (target is not null) Suppress(sk, DamageBoostSuppression.NoLongerPhysical);
                continue;
            }
            // Étendard d'honneur (lot 6c-2) : il ne donne son +8…15 qu'à ce qui SUBIT l'armure. Le périmètre
            // ci-dessus n'a pu écarter que le familier, les esprits et les dégâts déclenchés par l'ennemi ;
            // la vraie question — « cette compétence a-t-elle un paquet soumis à l'armure ? » — demande la
            // description RÉSOLUE. L'analyse ne se fait donc qu'ici, et une seule fois par infobulle.
            // ⚠ Aucune ligne « sans effet ici » : sur un soin ou une Flamme d'obsidienne, l'Étendard n'avait
            // rien à donner, il n'y a aucun chiffre manquant à justifier.
            // ⚠ Un COUP NORMAL n'a pas de description à analyser — et il n'en a pas besoin : un coup
            // d'arme SUBIT l'armure par nature, donc il profite de l'Étendard sans autre examen.
            if (d.Scope == DamageBoostScope.ArmorRespectingDamage && target is not null)
            {
                armorAnalysis ??= SkillDamage.Analyze(ResolveDescription(target), target.Name);
                if (!DamageBoostData.BenefitsFromArmorRespectingBonus(target, armorAnalysis)) continue;
            }
            // Seule entorse du chantier à « icône allumée = ça marche » : une conjuration ne s'applique
            // que si le type de dégâts effectif est le sien — mais uniquement quand l'application le SAIT
            // (cf. § 6.1 du plan et DamageBoostData.ElementSatisfied).
            // ⚠ VERROU du 28/09/2026 : un COUP NORMAL passe toujours `element` à null (voir plus haut) et
            // ElementSatisfied est PERMISSIF sur l'inconnu — la conjuration y serait donc accordée sans que
            // personne ait jugé l'arme, alors qu'elle est refusée sur la ligne d'une compétence du même
            // perso avec la même arme. On écarte par PRUDENCE, comme le RequiresPhysical juste au-dessus :
            // même situation d'ignorance, donc même réponse. Chemin MORT aujourd'hui — les 4 descripteurs
            // à RequiresElement sont tous proc-comptés, donc ce `continue` ne retire aucun chiffre, et
            // SpikeBoostCoverage.ElementEffectsReachingPlainAttack le VERROUILLE. Le jour où ce verrou se
            // remplit, la prudence deviendrait un chiffre manquant : il faudra alors faire descendre le
            // type de dégâts CHOISI sur la ligne jusqu'ici, au lieu de « je ne sais pas ».
            if (d.RequiresElement is not null && target is null) continue;
            if (!DamageBoostData.ElementSatisfied(d.RequiresElement, element))
            {
                Suppress(sk, DamageBoostSuppression.WrongElement);
                continue;
            }
            // Une arme n'inflige qu'un type de dégâts à la fois : au plus UN effet à exigence d'élément
            // peut agir. L'exclusivité des icônes (ExclusiveFamilyOf) l'empêche déjà à l'allumage ; ce
            // garde-fou couvre les fichiers enregistrés AVANT cette règle, qui peuvent en porter deux.
            if (d.RequiresElement is not null)
            {
                if (elementTaken) continue;
                elementTaken = true;
            }
            int value = ValueOfBoost(d, sk);
            // ⚠ « == 0 » et non « <= 0 » : un MALUS reçu (Affinité vitale, Arme du tourment) est
            // légitimement négatif depuis le lot 6b.
            if (value == 0) continue;
            // Lot 6e : l'effet a réellement agi, donc le détail d'une ligne du Spike peut le NOMMER. Le
            // suffixe « (PvP) » tombe, comme pour les noms des 9 buffs d'arme — la copie équipée est
            // signalée ailleurs, et la répéter sur chaque ligne du détail serait du bruit.
            (sources ??= []).Add(new DamageBoostSource(
                ZCodex.Core.Search.SkillVariants.BaseName(sk.DisplayName), d.Kind, value));
            switch (d.Kind)
            {
                case DamageBoostKind.Damage:
                    (packets ??= []).Add(new DamageBoostPacket(value, d.DamageType, d.IsBonus));
                    break;
                // Les chances de critique s'ADDITIONNENT (Q7 : « s'ajoute tel quel au taux affiché ») ;
                // seul l'affichage plafonne à 100 %. La pénétration de BASE, elle, ne se cumule jamais :
                // seul le plus fort compte (Q8) — celle en BONUS se cumule au contraire.
                case DamageBoostKind.CriticalChance:   critical += value; break;
                case DamageBoostKind.BasePenetration:  basePenetration = Math.Max(basePenetration, value); break;
                case DamageBoostKind.BonusPenetration: bonusPenetration += value; break;
                // Les multiplicateurs se COMPOSENT : Vengeance (+25) sous Affinité vitale (−30) donne
                // ×1,25 × 0,70. Chacun porte des points de pourcentage signés.
                case DamageBoostKind.Multiplier:       multiplier *= 1.0 + value / 100.0; break;
                // Rafale (lot 6c-3) : son −25 % ne compose qu'avec les autres multiplicateurs d'ARME, dans un
                // canal séparé — sinon il ferait baisser le « +20 » d'un Coup de taille, qu'il ne touche pas.
                case DamageBoostKind.WeaponMultiplier: weaponMultiplier *= 1.0 + value / 100.0; break;
                // Vol de vie conféré (Ordre du vampire, Arme du tourment) : sa propre ligne, hors de la
                // table et hors du multiplicateur — le vol de vie n'est pas un dégât. Deux effets actifs en
                // même temps s'ADDITIONNENT, comme les paquets de dégâts.
                case DamageBoostKind.LifeSteal:        lifeSteal += value; break;
            }
        }

        // ⚠ Les DEUX multiplicateurs partent en ÉCART À 1 : cf. DamageBoosts.Multiplier, où le piège est
        // expliqué — un facteur « par défaut 1 » vaudrait 0 pour default(DamageBoosts).
        // ⚠ Le mod d'arme « de fractionnement » ne rejoint PAS le résultat du Spike : la fenêtre a sa
        // propre case par ligne (SpikeWeaponMods), qui l'ajoute déjà à la pénétration — même piège de
        // double compte que les 33 descripteurs filtrés plus haut, mais par un quatrième chemin, qui
        // n'est pas un effet de compétence et n'a donc pas sa place dans SpikeBoostCoverage.
        return new DamageBoosts(packets, critical, basePenetration,
                                bonusPenetration + (spikeOnly || target is null ? 0 : SunderingModPercentFor(target, equipped)),
                                multiplier - 1.0,
                                suppressed, WinterLit, StoneStrikerLit,
                                target is null ? null : TypeNoteFor(target, equipped, element),
                                lifeSteal, weaponMultiplier - 1.0, sources);
    }

    /// <summary>Les effets de dégâts ALLUMÉS de ce perso, des DEUX origines : sa propre barre (lots 6a)
    /// et ce qu'un allié lui envoie (lot 6b). Un effet reçu porte la compétence du LANCEUR, donc son rang
    /// se lit chez lui — c'est pour ça que la paire rend la compétence source et pas seulement l'id.</summary>
    private IEnumerable<(Skill Source, DamageBoostDescriptor Descriptor)> LitDamageBoosts()
    {
        // ⚠ TOUS les descripteurs de la compétence, pas le premier : depuis le lot 6c-2b, l'Arme du tourment
        // en porte DEUX (son malus de dégâts et son vol de vie). N'en prendre qu'un afficherait la moitié de
        // ce que l'effet fait, sans rien signaler.
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk)
                foreach (var d in DamageBoostData.DescriptorsFor(sk.Id))
                    if (!d.Received && IsAttributeBoostActive(d.ToggleId))
                        yield return (sk, d);

        foreach (var (toggleId, recv) in ReceivedDamageBoosts)
            if (IsAttributeBoostActive(toggleId))
                foreach (var d in DamageBoostData.DescriptorsFor(recv.Skill.Id))
                    if (d.Received)
                        yield return (recv.Skill, d);

        // Troisième origine (lot 6c) : le BANDEAU d'équipe. Aucune icône de carte — c'est l'effet posé qui
        // allume, pour tout le monde en même temps. Les trois esprits du 6c-1 n'ont aucun rang à résoudre
        // (leurs chiffres sont des littéraux) ; les 5 effets PORTÉS du 6c-2 en ont un, celui de leur porteur.
        var rituals = ActiveNatureRituals;
        var bandRanks = BandRanks;
        foreach (var d in DamageBoostData.BandAll)
            if (NatureRitualData.BySkillId(d.SkillId) is { } band && rituals.Contains(band.Ritual)
                // ⚠ Un effet « porté seulement » que PLUS PERSONNE n'équipe n'existe pas, même si le bandeau
                // le garde allumé : l'état est persisté par SkillId, donc un fichier enregistré AVANT le
                // retrait de la compétence le rouvre allumé. Sans ce test il donnerait son chiffre au rang 0,
                // ou pire une ligne « sans effet ici » fantôme sur l'Ordre de la douleur.
                && (!band.EquippedOnly || bandRanks.ContainsKey(band.Ritual))
                && BandSkill(d.SkillId) is { } sk)
                yield return (sk, d);
    }

    /// <summary>
    /// Un AUTRE enchantement de Nécromant est-il allumé sur ce perso ? Règle de l'Ordre du vampire
    /// (« party members under another Necromancer enchantment are not affected »), version étroite de celle
    /// de l'Arme brute. <paramref name="source"/> = l'effet examiné, à écarter du balayage : l'Ordre du
    /// vampire est lui-même un enchantement de Nécromant, il s'annulerait tout seul sans ça.
    ///
    /// Les trois origines comptent — sa propre barre, un allié, le bandeau. ⚠ En pratique ce sont les deux
    /// du bandeau qui frappent : l'Ordre de la douleur et la Fureur noire. Les deux Ordres ne se cumulent
    /// donc jamais, ce qui est bien le comportement du jeu.
    /// </summary>
    private bool IsUnderOtherNecromancerEnchantment(Skill source)
    {
        foreach (var slot in SkillSlots)
            if (slot.Skill is { Profession: Profession.Necromancer, SkillType: EnchantmentSkillType } sk
                && sk.Id != source.Id
                && PersonalToggleIdOf(sk) is { } id && IsAttributeBoostActive(id))
                return true;
        foreach (var (toggleId, recv) in ReceivedDamageBoosts)
            if (recv.Skill is { Profession: Profession.Necromancer, SkillType: EnchantmentSkillType }
                && recv.Skill.Id != source.Id && IsAttributeBoostActive(toggleId))
                return true;
        var rituals = ActiveNatureRituals;
        var bandRanks = BandRanks;
        foreach (var d in NatureRitualData.All)
            // ⚠ « porté seulement » sans porteur = l'effet n'est pas là, et il n'est même pas AFFICHÉ au
            // bandeau : le laisser annuler l'Ordre du vampire produirait un « sans effet ici » qu'aucune
            // icône visible n'expliquerait. C'est pourquoi la Fureur noire, qui n'a aucun chiffre de dégâts,
            // figure quand même dans BandRanks.
            if (NatureRitualData.IsNecromancerEnchantment(d.Ritual) && d.SkillId != source.Id
                && rituals.Contains(d.Ritual)
                && (!d.EquippedOnly || bandRanks.ContainsKey(d.Ritual)))
                return true;
        return false;
    }

    /// <summary>Un ENCHANTEMENT est-il allumé sur ce perso ? Compte ses propres icônes d'enchantement et
    /// celles qu'il reçoit d'un allié. Sert la règle de l'Arme brute (lot 6b, Q4).</summary>
    public bool IsEnchantedByLitEffect
    {
        get
        {
            foreach (var slot in SkillSlots)
                if (slot.Skill is { SkillType: EnchantmentSkillType } sk
                    && PersonalToggleIdOf(sk) is { } id && IsAttributeBoostActive(id))
                    return true;
            foreach (var (toggleId, recv) in ReceivedDamageBoosts)
                if (recv.Skill.SkillType == EnchantmentSkillType && IsAttributeBoostActive(toggleId))
                    return true;
            return false;
        }
    }

    private const string EnchantmentSkillType = "Enchantment Spell";

    // Valeur d'un effet au rang de SA PROPRE caractéristique (substitution du lot 5 comprise). Rang null =
    // caractéristique hors du build : la description de l'effet reste alors en plage verte, donc son bonus
    // n'a pas de chiffre non plus — on ne lui donne PAS la valeur du rang 0.
    private int ValueOfBoost(DamageBoostDescriptor descriptor, Skill source)
    {
        // ⚠ Passer par ValueOf même pour un littéral : c'est LUI qui porte le signe des malus (lot 6b).
        if (descriptor.Fixed > 0) return DamageBoostData.ValueOf(descriptor, source, 0);
        int? rank = RankOfBoost(descriptor, source);
        return rank is null ? 0 : DamageBoostData.ValueOf(descriptor, source, rank.Value);
    }

    // Le rang auquel se résout un effet. Un effet REÇU ou de BANDEAU se lit au rang de son PORTEUR,
    // jamais à celui du perso qui en profite : retomber sur le receveur rendrait null, donc 0, et le
    // bonus tomberait en silence (lot 6c-2).
    private int? RankOfBoost(DamageBoostDescriptor descriptor, Skill source)
        => descriptor.Received || descriptor.Band
            ? ReceivedRankOf(descriptor) ?? BandRankOf(descriptor)
            : AttributeLevel(SubstitutedAttributeFor(source) ?? source.Attribute);

    /// <summary>
    /// Lot 6e — la compétence SOURCE d'un effet de dégâts ALLUMÉ sur ce perso et le rang auquel elle se
    /// résout, ou null si l'effet est éteint (ou absent des trois origines). Sert au décompte des
    /// CHARGES : « Je suis le plus fort ! » annonce le nombre d'attaques couvertes dans sa propre
    /// progression, à une colonne autre que celle de ses dégâts.
    ///
    /// ⚠ Il passe par la MÊME énumération que tout le reste du chantier : l'état allumé et le rang ne
    /// peuvent donc pas diverger de ce que la table des dégâts affiche.
    /// </summary>
    public (Skill Source, int Rank)? LitBoostAt(int descriptorSkillId)
    {
        foreach (var (sk, d) in LitDamageBoosts())
            if (d.SkillId == descriptorSkillId)
                return (sk, RankOfBoost(d, sk) ?? 0);
        return null;
    }

    /// <summary>
    /// La valeur du paquet de DÉGÂTS d'un effet allumé sur ce perso, au rang de son porteur ; 0 si
    /// l'effet est éteint ou n'a pas de paquet. Sert aux buffs de la fenêtre Spike qu'on allume par
    /// l'icône de la carte alors que leur LANCEUR est hors du roster du spike : le balayage du roster
    /// ne trouve alors aucune valeur, et le buff serait actif à +0 (lot 6e, extension de Q13).
    ///
    /// ⚠ Il ne rejoue PAS les annulations de <see cref="DamageBoostsFor"/> (l'Arme brute qui tombe sous
    /// enchantement, notamment) : la fenêtre Spike laisse cet arbitrage à l'utilisateur depuis le
    /// chantier 14 — son infobulle de case dit « laisser l'icône éteinte dans ce cas ». Changer ça ici
    /// serait un autre arbitrage, non demandé.
    /// </summary>
    public int LitBoostDamageValue(int descriptorSkillId)
    {
        foreach (var (sk, d) in LitDamageBoosts())
            if (d.SkillId == descriptorSkillId && d.Kind == DamageBoostKind.Damage)
                return ValueOfBoost(d, sk);
        return 0;
    }

    /// <summary>
    /// L'icône de CET effet est-elle allumée sur la carte de ce perso ? ⚠ La question est posée à la
    /// rangée elle-même, et non au seul ensemble persisté : un id peut y rester allumé alors que plus
    /// personne ne lance l'effet (fichier enregistré avant un changement de barre). La rangée, elle,
    /// n'émet une icône que si l'effet est réellement là — c'est donc la seule réponse qui ne peut pas
    /// mentir. Même garde-fou que le <c>JudgesInsight is not null</c> de <see cref="JudgesInsightLit"/>,
    /// mais valable pour les 7 buffs d'un coup (lot 6e).
    /// </summary>
    public bool IsBoostIconLit(int toggleId) => LitBoostIcon(toggleId) is not null;

    /// <summary>
    /// Éteint les icônes de SORTS D'ARME allumées de ce perso, sauf celle passée en exception. Sert aux
    /// cases de la fenêtre Spike qui portent elles-mêmes l'état, faute d'icône (l'Arme d'éclats, et
    /// l'Arme du Grand Nain chez son propre porteur) : allumer une telle case doit éteindre les sorts
    /// d'arme allumés PAR L'ICÔNE, sinon le perso en aurait deux à la fois (28/09/2026).
    ///
    /// ⚠ La famille n'est pas devinée : elle sort de <c>ExclusiveFamilyOf</c>, la même que celle qui
    /// arbitre déjà les icônes entre elles dans <see cref="SetAttributeBoost"/>.
    /// </summary>
    public void TurnOffOtherWeaponSpellIcons(int exceptToggleId)
    {
        foreach (var t in AttributeBoostToggles.ToList())
            if (t.ToggleId != exceptToggleId && t.IsActive && ExclusiveFamilyOf(t.ToggleId) == WeaponSpellType)
                SetAttributeBoost(t.ToggleId, false);
    }

    /// <summary>L'icône EXISTE-t-elle sur la carte de ce perso, allumée ou non ? Sert aux cases de buff de
    /// la fenêtre Spike (28/09/2026) : quand l'icône existe, c'est ELLE qui porte l'état, pour qu'un seul
    /// interrupteur commande l'effet. ⚠ Elle peut manquer alors que la case est proposée : Vengeance et
    /// l'Arme du Grand Nain ne peuvent pas se cibler elles-mêmes, leur porteur n'a donc pas l'icône.</summary>
    public bool HasBoostIcon(int toggleId) =>
        toggleId != 0 && AttributeBoostToggles.Any(t => t.ToggleId == toggleId);

    /// <summary>
    /// La COPIE réellement allumée derrière cette icône (core ou « (PvP) »), null si elle est éteinte ou
    /// absente. ⚠ Une variante PvP partage l'icône de sa jumelle (l'id de bascule est celui de la base),
    /// donc l'id ne dit PAS quelle copie agit — et les deux n'ont pas toujours le même chiffre : Glaive
    /// était destructrice pénètre 20 % en core et 10 % en PvP. Seule l'icône porte la vraie compétence.
    /// </summary>
    public Skill? LitBoostIconSkill(int toggleId) => LitBoostIcon(toggleId)?.Skill;

    // L'icône allumée elle-même : une seule règle pour les deux questions ci-dessus. La rangée n'émet
    // une icône que si l'effet est réellement là, un id persisté seul ne suffit donc jamais.
    private AttributeBoostIndicatorViewModel? LitBoostIcon(int toggleId) =>
        toggleId == 0 ? null
        : AttributeBoostToggles.FirstOrDefault(t => t.ToggleId == toggleId && t.IsActive);

    /// <summary>Rang d'un effet de BANDEAU (lot 6c-2) : celui de son porteur le plus fort. null = personne ne
    /// le porte, ou l'effet n'a pas de rang du tout (les trois esprits du 6c-1, qui passent par Fixed).</summary>
    private int? BandRankOf(DamageBoostDescriptor descriptor) =>
        descriptor.Band && NatureRitualData.BySkillId(descriptor.SkillId) is { } band
        && BandRanks.TryGetValue(band.Ritual, out int rank) ? rank : null;

    /// <summary>Rang d'un effet REÇU : celui de son lanceur le plus fort, jamais celui du receveur — la
    /// caractéristique d'échelle est souvent absente de la barre de celui qui en profite (le Communion
    /// de l'Arme brute chez un Guerrier, le rang Deldrimor chez qui ne porte pas la compétence).</summary>
    private int? ReceivedRankOf(DamageBoostDescriptor descriptor) =>
        descriptor.Received && ReceivedDamageBoosts.TryGetValue(descriptor.ToggleId, out var recv)
            ? recv.Rank : null;

    /// <summary>Points de pénétration d'armure en BONUS apportés par le mod d'arme « de fractionnement » du
    /// set actif (icône allumée = les 20 % de chance ont joué, idiome du chantier). Il ne vaut que pour les
    /// attaques de l'arme qui le porte — 0 partout ailleurs.</summary>
    private int SunderingModPercentFor(Skill target, WeaponKind equipped)
    {
        if (!IsAttributeBoostActive(DamageBoostData.SunderingModToggleId)
            || !ConditionDurationData.UsesEquippedWeapon(target, equipped)) return 0;
        int best = 0;
        foreach (int id in ActiveWeaponSetModIds())
            best = Math.Max(best, ConditionDurationData.PenetrationModPercent(id));
        return best;
    }

    /// <summary>Le set d'armes ACTIF porte-t-il un mod « de fractionnement » ? → icône du mod dans le bandeau.</summary>
    public bool HasSunderingMod => ActiveWeaponSetModIds().Any(id => ConditionDurationData.PenetrationModPercent(id) > 0);

    /// <summary>Bonus qu'un effet actif ajoute au chiffre de dégâts écrit DANS la description de
    /// <paramref name="target"/> (Q12) : Agression barbare sur les 16 attaques de familier, Sceau de
    /// puissance spectrale sur les attaques des esprits. Rend la colonne de progression à relever et le
    /// bonus ; colonne −1 = rien à relever.
    ///
    /// ⚠ Le bonus peut être NÉGATIF depuis le lot 6c-3 : l'Aura de sangsue de l'esprit RETIRE 5…20 dégâts
    /// aux attaques des esprits. Elle et le Sceau de puissance spectrale se compensent donc dans la même
    /// somme, et la valeur relevée est plancher-née à 0 par <see cref="SkillProgression.Resolve"/>.</summary>
    public (int Column, int Bonus) TextDamageBonusFor(Skill target)
    {
        // ⚠ SOMME depuis le lot 6c, plus « le premier qui gagne » : un familier peut cumuler l'Agression
        // barbare (carte du perso) et le Vannage (bandeau). La colonne, elle, est une propriété de la
        // compétence survolée — elle est donc la même pour tous les effets qui la visent.
        int column = -1, total = 0;
        foreach (var (sk, d) in LitDamageBoosts())
        {
            // ⚠ Le vol de vie conféré à un esprit qui vole DÉJÀ de la vie entre ici aussi (lot 6c-3b) : sur
            // ces 3 esprits-là, c'est SON chiffre de vol de vie que l'effet relève, pas des dégâts.
            if (d.Kind != DamageBoostKind.TextDamage
                && !DamageBoostData.LifeStealReadInText(d, target)) continue;
            if (!DamageBoostData.Affects(d, sk, target, WeaponKind.None)) continue;
            // Un bonus « aux dégâts physiques » posé sur le familier tombe si le Grand brasier convertit
            // ses attaques (le familier est une « créature à portée »).
            if (d.RequiresPhysical && d.Scope == DamageBoostScope.PetAttacks && !PetDamageStillPhysical) continue;
            int bonus = ValueOfBoost(d, sk);
            // ⚠ « == 0 » et non « <= 0 » depuis le lot 6c-3, exactement comme dans DamageBoostsFor : un
            // MALUS est légitimement négatif, et le refuser ici aurait fait taire l'Aura de sangsue de
            // l'esprit en silence, build vert.
            if (bonus == 0) continue;
            // ⚠ Le descripteur, pas son périmètre : c'est ce qui permet au malus de dégâts et au vol de vie
            // du MÊME effet de viser deux colonnes différentes de la même cible. Sur la Mélodie du sang, le
            // malus ne trouve aucune colonne de dégâts (col = −1) et s'écarte tout seul — c'est exactement
            // le comportement voulu : il n'y a pas de dégâts à retirer.
            int col = DamageBoostData.TextBonusColumn(d, target);
            if (col < 0) continue;
            if (column < 0) column = col;
            if (col == column) total += bonus;
        }
        // total == 0 : les effets se sont exactement compensés (le Sceau de puissance spectrale sous l'Aura de
        // sangsue), donc le chiffre affiché est bien celui de la base — rien à relever, et rien à marquer.
        return column >= 0 && total != 0 ? (column, total) : (-1, 0);
    }

    /// <summary>
    /// La phrase « vos attaques infligent des dégâts de X » (demande de Philippe, 26/09/2026), ou null.
    ///
    /// Deux règles différentes, et c'est voulu :
    ///  • le PERSO ne la voit que si un effet a **converti** quelque chose — son type de dégâts vient de son
    ///    arme, qu'il a choisie, donc l'annoncer en permanence serait une ligne de bruit sur 1517 infobulles ;
    ///  • le FAMILIER la voit **toujours** : son type dépend de l'espèce (perforant pour les oiseaux,
    ///    tranchant pour les loups et félins, feu pour le seul Molosse de Balthazar), l'app ne sait pas
    ///    laquelle il a, et rien d'autre dans l'infobulle ne le dit.
    ///
    /// ⚠ Le type INTRINSÈQUE ne compte pas comme une conversion : un Javelot d'éclair est de la foudre par
    /// nature, il n'y a rien à signaler — sauf si Hiver le passe en froid, et là si.
    /// </summary>
    private DamageTypeNote? TypeNoteFor(Skill target, WeaponKind equipped, string? element)
    {
        if (target.SkillType == "Pet Attack")
        {
            // ⚠ Le familier subit Hiver comme tout le monde : sous Grand brasier + Hiver ses attaques
            // passent en feu PUIS en froid. Briseur de pierre, lui, ne le touche pas — il ne vise que
            // « the damage YOU deal », et un familier n'est pas son maître. (Retour de Philippe, 27/09.)
            string? pet = DamageBoostData.PetEffectiveElement(ConversionState(target));
            string? petShown = DamageBoostData.DisplayedType(pet ?? "physical", WinterLit);
            return new DamageTypeNote(petShown!, Pet: true, Natural: pet is null && !WinterLit);
        }
        if (!WeaponStrike.IsWeaponAttack(target)) return null;
        string? shown = DamageBoostData.DisplayedType(element, WinterLit, StoneStrikerLit);
        return shown is not null && shown != DamageBoostData.IntrinsicType(target)
            ? new DamageTypeNote(shown, Pet: false, Natural: false)
            : null;
    }

    /// <summary>Type de dégâts effectif des attaques de <paramref name="target"/> : la chaîne de conversion
    /// du § 6.1 est dans le Core (<see cref="DamageBoostData.EffectiveElement"/>), ce perso n'en fournit
    /// que l'état courant.</summary>
    private string? EffectiveElementFor(Skill target, WeaponKind equipped) =>
        DamageBoostData.EffectiveElement(ConversionState(target), target, equipped);

    /// <summary>Élément du mod d'arme du set ACTIF (null s'il n'y en a pas).</summary>
    private string? ActiveElementalMod
    {
        get
        {
            foreach (int id in ActiveWeaponSetModIds())
                if (ConditionDurationData.ElementalModType(id) is { } element) return element;
            return null;
        }
    }

    /// <summary>Le mod élémentaire du set actif occupe-t-il le PRÉFIXE de l'arme de cette attaque ? La
    /// fenêtre Spike n'a plus le droit d'y proposer un mod de fractionnement ou vampirique quand la place
    /// est prise, et elle le VOIT maintenant au lieu d'attendre un choix manuel (Q15, 27/09/2026).</summary>
    public bool ElementalModAppliesTo(Skill target) =>
        ActiveElementalMod is not null
        && ConditionDurationData.UsesEquippedWeapon(target, ActiveWeaponKind());

    /// <summary>Clairvoyance du juge reçue et ALLUMÉE sur ce perso. Publique parce que la fenêtre Spike a
    /// sa propre case pour le même effet : allumé d'un côté ou de l'autre suffit (Q13, 27/09/2026).</summary>
    public bool JudgesInsightLit =>
        IsAttributeBoostActive(ConditionDurationData.JudgesInsightSkillId) && JudgesInsight is not null;

    // ── Type de dégâts pour la fenêtre Spike (lot 6d-1) ───────────────────────

    /// <summary>
    /// Type de dégâts qu'une attaque de ce perso fait ARRIVER chez la cible du Spike — la seule chose que
    /// <see cref="SpikeTarget.EffectiveArmor"/> doit recevoir, puisque la cible y a une armure PAR TYPE.
    ///
    /// <paramref name="picked"/> = le type choisi à la main sur la ligne du Spike (vide = aucun). Il ne
    /// COURT-CIRCUITE pas la chaîne du § 6.1, il y entre (Q15) : élémentaire, il prend la place du mod de
    /// préfixe, donc Briseur de pierre garde le dernier mot comme en jeu ; skin non élémentaire (jitte
    /// contondante, faux « Sufferer »…), il sert de type de départ que les convertisseurs peuvent encore
    /// écraser. <paramref name="native"/> = le type natif de l'arme de la ligne, retenu quand rien n'a
    /// converti et que rien n'est choisi. <paramref name="judgesInsight"/> = la case Clairvoyance du juge
    /// de la fenêtre Spike : elle s'AJOUTE à l'icône de la carte, allumé d'un côté ou de l'autre suffit
    /// (Q13, 27/09/2026) — sans quoi le même effet aurait deux interrupteurs et deux vérités.
    ///
    /// ⚠ Le périmètre des convertisseurs se juge sur l'arme du set ACTIF, comme dans l'infobulle, et non
    /// sur l'arme arrêtée de la ligne : une seule vérité pour la chaîne. La différence ne peut tomber que
    /// sur une attaque d'arme LIBRE dont la ligne a changé l'arme à la main — et c'est précisément le cas
    /// que la liste déroulante de type est là pour couvrir.
    /// </summary>
    public SpikeDamageType SpikeDamageTypeFor(Skill target, string? picked, string? native,
                                              bool judgesInsight)
    {
        var state = ConversionState(target);
        if (WeaponStrike.IsElementalType(picked)) state = state with { ElementalMod = picked };
        if (judgesInsight) state = state with { JudgesInsight = true };

        string? declared = string.IsNullOrEmpty(picked) ? native : picked;
        string? effective = DamageBoostData.EffectiveElement(state, target, ActiveWeaponKind()) ?? declared;
        string? received = DamageBoostData.DisplayedType(effective, WinterLit, StoneStrikerLit);
        // ⚠ Le type INTRINSÈQUE ne compte pas comme une conversion — même règle que la phrase de type de
        // l'infobulle : un Javelot d'éclair est de la foudre par nature, annoncer « type converti » dessus
        // sans qu'aucun effet ne soit allumé serait un mensonge. Sous Hiver, en revanche, il l'est.
        string? natural = DamageBoostData.IntrinsicType(target) ?? declared;
        return new SpikeDamageType(received, received != natural);
    }

    /// <summary>Arme de main du set d'armes ACTIF (None = aucune arme renseignée). Publique pour la ligne
    /// d'ATTAQUE NORMALE de la fenêtre Spike (lot 6d-2, Q17) : son arme est celle du set actif, et à défaut
    /// celle déduite de la barre.</summary>
    public WeaponKind ActiveSetWeaponKind => ActiveWeaponKind();

    /// <summary>Le mod élémentaire du set actif porte-t-il sur un coup normal de <paramref name="kind"/> ?
    /// Vrai seulement si c'est bien l'arme du set qui frappe : une arme DÉDUITE de la barre (set non
    /// renseigné) ne porte aucun mod. Décide aussi le grisage de la liste « Mod » de la ligne (Q15).</summary>
    public bool ElementalModOnNormalAttack(WeaponKind kind) =>
        ActiveElementalMod is not null && kind != WeaponKind.None && kind == ActiveWeaponKind();

    /// <summary>Type de dégâts qu'un COUP NORMAL de ce perso fait arriver chez la cible du Spike (lot 6d-2) :
    /// la chaîne du § 6.1 sans compétence — aucun type intrinsèque, périmètre jugé sur la seule arme.
    /// <paramref name="native"/> = le type natif de l'arme qui frappe, retenu quand rien ne convertit.
    /// <paramref name="judgesInsight"/> = la case Clairvoyance du juge de la fenêtre, qui s'ajoute à l'icône
    /// de la carte (Q13).</summary>
    public SpikeDamageType SpikeNormalAttackType(WeaponKind kind, string? native, bool judgesInsight)
    {
        var state = ConversionState(null);
        if (judgesInsight) state = state with { JudgesInsight = true };
        string? effective = DamageBoostData.PlainAttackElement(
            state, kind, ElementalModOnNormalAttack(kind)) ?? native;
        string? received = DamageBoostData.DisplayedType(effective, WinterLit, StoneStrikerLit);
        return new SpikeDamageType(received, received != native);
    }

    /// <summary>Type d'un paquet de dégâts de la description tel que la cible du Spike le REÇOIT. Briseur
    /// de pierre force la terre sur TOUT ce que le perso infliger, sorts compris, et Hiver passe
    /// l'élémentaire en froid — les deux déplacent donc aussi l'armure des paquets qui ne sont pas des
    /// coups d'arme (boules de feu, paquets typés d'une attaque…).</summary>
    public SpikeDamageType SpikePacketTypeFor(string? packetType)
    {
        string? received = DamageBoostData.DisplayedType(packetType, WinterLit, StoneStrikerLit);
        return new SpikeDamageType(received, received != packetType);
    }

    // target null = une ATTAQUE NORMALE (lot 6d-2) : il n'y a pas de compétence frappée, donc aucune
    // préparation à perdre — Barrage et Volée sont les seules à en retirer, et un coup normal n'en est pas.
    private DamageBoostData.ConversionState ConversionState(Skill? target)
    {
        List<DamageConverterDescriptor>? lit = null;
        foreach (var slot in SkillSlots)
            if (slot.Skill is { } sk && ConditionDurationData.ConverterBySkillId(sk.Id) is { Received: false } k
                && IsAttributeBoostActive(k.ToggleId)
                // ⚠ Tir de barrage et Volée retirent les préparations avant de frapper : les Flèches
                // enflammées n'y convertissent donc RIEN, et c'est le mod d'arme qui décide du type.
                && (target is null || !DamageBoostData.PreparationLost(sk, target)))
                (lit ??= []).Add(k);

        var rituals = ActiveNatureRituals;
        return new DamageBoostData.ConversionState(
            LitConverters: lit,
            ElementalMod: ActiveElementalMod,
            JudgesInsight: JudgesInsightLit,
            GreaterConflagration: rituals.Contains(NatureRitualData.Ritual.GreaterConflagration),
            Conflagration: rituals.Contains(NatureRitualData.Ritual.Conflagration),
            StoneStriker: IsAttributeBoostActive(ConditionDurationData.StoneStrikerSkillId)
                          && FindEquippedSkill(ConditionDurationData.StoneStrikerSkillId) is not null);
    }

    /// <summary>Une conjuration (ou l'Aura de poussière d'ébène) est-elle allumée alors que l'arme n'inflige
    /// pas son type de dégâts ? → note d'icône, sinon son absence de tout effet passe pour un bug. Rend le
    /// type exigé, null s'il n'y a rien à signaler. Jugée sur une attaque de RÉFÉRENCE à l'arme du set
    /// actif : c'est une note de BANDEAU, elle ne connaît pas la compétence survolée.</summary>
    public string? BoostElementMismatch(Skill skill)
    {
        if (DamageBoostData.BySkillId(skill.Id) is not { RequiresElement: { } required }) return null;
        var reference = SkillSlots.Select(s => s.Skill)
            .FirstOrDefault(sk => sk is not null && WeaponStrike.IsWeaponAttack(sk));
        if (reference is null) return null;
        string? element = EffectiveElementFor(reference, ActiveWeaponKind());
        return element is not null && element != required ? required : null;
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

    // Fast Casting (lot 3, retouche du 16/09/2026) : caractéristique TOUJOURS active, donc sans icône — l'infobulle en tient
    // compte pour l'incantation des sorts et des sceaux, et (en PvE) la recharge des sorts d'Envoûteur. 0 si le perso ne l'a
    // pas. Même chemin que l'Expertise (AttributeLevel), donc le mod « of the Mesmer » est déjà couvert.
    public int FastCastingRank => AttributeLevel("Fast Casting") ?? 0;

    // Coups d'adrénaline donnés par Rage of the Ntouka quand son icône est allumée (rang de Force) : ils se retranchent des
    // coups nécessaires de TOUTES les compétences d'adrénaline (un gain d'adrénaline les remplit toutes). 0 sinon.
    public int AdrenalineStrikesGiven =>
        IsAttributeBoostActive(AdrenalineBoostData.RageOfTheNtoukaSkillId)
        && FindEquippedSkill(AdrenalineBoostData.RageOfTheNtoukaSkillId) is { } rotn
            ? AdrenalineBoostData.StrikesGranted(rotn, AttributeLevel("Strength") ?? 0)
            : 0;

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

    // ── Caractéristique de substitution (lot 5) ──────────────────────────────────────────────
    // Caractéristique imposée à CETTE compétence par une compétence de substitution équipée ET
    // allumée sur ce perso (Sceau des illusions → Magie d'illusion sur les sorts hors Illusion ;
    // Célérité symbolique → Incantation rapide sur les sceaux), ou null. Les deux périmètres sont
    // DISJOINTS, donc la première trouvée est la bonne : aucun arbitrage à faire.
    public string? SubstitutedAttributeFor(Skill skill)
    {
        foreach (var slot in SkillSlots)
        {
            if (slot.Skill is not { } sk || AttributeSubstitutionData.BySkillId(sk.Id) is not { } d) continue;
            if (IsAttributeBoostActive(d.SkillId) && d.Affects(skill)) return d.Attribute;
        }
        return null;
    }

    // Rang auquel résoudre les chiffres de CETTE compétence, et la caractéristique substituée qui
    // l'a donné (null = pas de substitution, rang de la caractéristique propre).
    // ⚠ Sous substitution, une caractéristique SANS ligne chez ce perso vaut 0 et non « inconnue » :
    // Incantation rapide est la caractéristique PRIMAIRE du Mesmer, donc un E/Me qui porte la
    // Célérité symbolique est réellement à 0 en jeu et ses sceaux tombent au minimum. Décision de
    // Philippe du 17/09/2026 (Q3b) : on résout à 0, on n'affiche pas une plage.
    private (int? Rank, string? Substituted) EffectiveRank(Skill skill)
        => SubstitutedAttributeFor(skill) is { } attr
            ? (AttributeLevel(attr) ?? 0, attr)
            : (AttributeLevel(skill.Attribute), null);

    // Description de la skill : phrase de type retirée + variables résolues au rang de l'attribut
    // de la skill (Skill.Attribute), ou de la caractéristique substituée (lot 5). Plages non
    // résolues (pas de progression scrapée, ou attribut non édité) laissées en notation de plage verte.
    public string ResolveDescription(Skill skill)
    {
        var body = SkillText.ConciseBody(skill.Description, skill.SkillType);
        var (rank, subst) = EffectiveRank(skill);
        // Un flux qui relève l'attribut RÉELLEMENT lu → valeurs marquées dans la couleur du flux
        // (toute la description scale sur ce seul attribut : marquage uniforme). La substitution
        // prime sur ce marquage, cf. SkillProgression.Resolve.
        bool fluxBoosted = FluxAttributeBonus(subst ?? skill.Attribute) > 0;
        // Chiffre relevé dans le texte par un effet actif (lot 6a, Q12) : le marqueur posé ici est dans
        // MarkChars, donc les parseurs de description le voient — la ligne « ignore l'armure » de
        // l'infobulle suit toute seule la valeur relevée.
        var (column, bonus) = TextDamageBonusFor(skill);
        return SkillProgression.Resolve(body, skill.Progression, rank, fluxBoosted, substituted: subst is not null,
                                        bonusColumn: column, bonus: bonus);
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
        var (rank, subst) = EffectiveRank(skill);
        bool fluxBoosted = FluxAttributeBonus(subst ?? skill.Attribute) > 0;
        // Le texte FR résout depuis les MÊMES colonnes de progression : la colonne à relever, elle, se
        // déduit de la clause ANGLAISE (texte canonique), donc elle vaut pour les deux langues.
        var (column, bonus) = TextDamageBonusFor(skill);
        return SkillProgression.Resolve(skill.DescriptionFr, skill.Progression,
            rank, fluxBoosted, frAnchors: true, substituted: subst is not null,
            bonusColumn: column, bonus: bonus);
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
        set
        {
            if (!SetField(ref _name, value)) return;
            OnPropertyChanged(nameof(IsEmptyBuild));
            OnPropertyChanged(nameof(DisplayName));
            // Les phrases du lot 7a chez les autres membres NOMMENT ce perso : elles doivent suivre le renommage.
            if (HasAnyLitTargetedAllyEffect) OwnerBuild?.RefreshOtherSkillTooltips(this);
        }
    }

    // Nom AFFICHÉ : le nom réel, ou un placeholder localisé quand le build n'est pas nommé.
    // « (unnamed) » reste la sentinelle interne (IsEmptyBuild, .pn3) ; seul l'affichage change.
    // Dans un teambuild, le placeholder est NUMÉROTÉ par la place dans l'arbre — « Build Name 2 », variantes
    // « Build Name 2.1 » (demande de Philippe du 28/09/2026) : les phrases du lot 7a nomment l'allié source, et
    // tous les persos s'appelaient pareil. Build simple : pas d'arbre, placeholder d'origine.
    public string DisplayName =>
        !(string.IsNullOrWhiteSpace(_name) || _name == "(unnamed)") ? _name
        : TreeNumber() is { } n ? string.Format(ZCodex.App.LanguageManager.T("S.Misc.BuildNameNumbered"), n)
        : ZCodex.App.LanguageManager.T("S.Misc.BuildNamePlaceholder");

    // « 2 » pour la 2e racine, « 2.1 » pour sa 1re variante ; null hors teambuild.
    private string? TreeNumber()
    {
        if (_parent is { } p)
            return p.TreeNumber() is { } pn ? $"{pn}.{p.Variants.IndexOf(this) + 1}" : null;
        int i = OwnerBuild?.Characters.IndexOf(this) ?? -1;
        return i < 0 ? null : (i + 1).ToString();
    }

    /// <summary>« A l'aide ! » ou Atmosphère allumée (et proposée) sur ce perso : les autres membres le nomment.</summary>
    public bool HasAnyLitTargetedAllyEffect =>
        HasLitTargetedAllyEffect(TargetedAllyEffectData.HelpSkillId)
        || HasLitTargetedAllyEffect(TargetedAllyEffectData.AirOfEnchantmentSkillId);

    /// <summary>Le numéro du placeholder dépend de la place dans l'arbre : à renotifier après toute mutation.</summary>
    public void RaiseDisplayNameChanged() => OnPropertyChanged(nameof(DisplayName));

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
        set
        {
            SetField(ref _equipment, value);
            OnPropertyChanged(nameof(HasEquipment));
            OnPropertyChanged(nameof(EquipmentSummary));
            // Les mods du set d'armes actif pèsent sur les infobulles (« of Enchanting », « Furious »)
            // et l'icône « Furious » du bandeau apparaît ou disparaît avec eux.
            OnPropertyChanged(nameof(AttributeBoostToggles));
            OnPropertyChanged(nameof(HasAttributeBoostToggles));
            RefreshSkillTooltips();
        }
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

    // Vide les points investis dans une carac qui n'appartient plus aux professions du perso.
    // Sans ça, changer de profession laissait dans Allocations une carac ORPHELINE : aucune ligne
    // ne l'affiche plus (on ne bâtit ci-dessous que les caracs de PR/SEC), TotalAttributePoints ne
    // la compte pas (il somme les lignes) — mais GwTemplateCodec.Encode, lui, écrit TOUT le
    // dictionnaire. Le code O partait donc avec une carac hors profession, le jeu refusait le
    // template, et RIEN dans l'app ne le laissait voir. Cas réel remonté par un utilisateur :
    // Prières du vent 8 (Derviche) dans un Moine/Assassin, code de 27 caractères au lieu de 26
    // (l'ID élevé de la carac orpheline élargit le champ, d'où un code plus long — la signature
    // reconnaissable du bug).
    //
    // Le filtre est le MIROIR EXACT des lignes bâties plus bas — primaire : toutes ses caracs ;
    // secondaire : ses NON-primaires seulement. Sinon une Faveur divine sur un Guerrier/Moine
    // survivrait à la purge tout en restant invisible : le même bug, sous un autre nom.
    //
    // Appelé depuis RefreshAttributeRows, point de passage commun au changement de profession ET
    // au chargement d'un build (setter Attributes) : un .zcx déjà infecté est donc nettoyé à
    // l'ouverture, onglet Build comme teambuild, variantes comprises.
    private void PruneOrphanAttributes()
    {
        if (_attributes is null || _attributes.Allocations.Count == 0) return;

        // Perso sans AUCUNE profession : rien ne permet de juger une carac, et purger ici viderait
        // un build dont les professions n'ont pas encore été posées. On laisse passer — le code
        // produit serait de toute façon sans profession.
        if (_primaryProfession == Profession.None && _secondaryProfession == Profession.None) return;

        var kept = GwAttributeData.ForProfession(_primaryProfession)
            .Concat(GwAttributeData.ForProfession(_secondaryProfession).Where(a => !a.IsPrimary))
            .Select(a => a.Id)
            .ToHashSet();

        _attributes.Allocations.RemoveAll(a => !kept.Contains(a.AttributeId));
    }

    // ── Attribute rows ────────────────────────────────────────────────────────

    private void RefreshAttributeRows()
    {
        PruneOrphanAttributes();

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
