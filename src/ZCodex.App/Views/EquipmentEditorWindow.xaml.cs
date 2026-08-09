using Microsoft.Win32;
using ZCodex.App.ViewModels;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;
using ZCodex.Scraper;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZCodex.App.Views;

public partial class EquipmentEditorWindow : Window
{
    // Raccourci de localisation (fenêtre modale : la langue courante est figée à l'ouverture).
    private static string T(string key) => LanguageManager.T(key);

    // ── Slot view model ───────────────────────────────────────────────────────

    private sealed class SlotVm : INotifyPropertyChanged
    {
        public int    SlotId   { get; init; }
        public string SlotName { get; init; } = "";

        // Sémantique paw·ned² : la case ne gate que le SKIN (ItemId) dans le template exporté ;
        // le slot lui-même est exporté dès qu'il a du contenu (item ou mods).
        private bool _saveSkin = true;
        public bool SaveSkin
        {
            get => _saveSkin;
            set { _saveSkin = value; Notify(); }
        }

        // Slot off-hand neutralisé par une arme à deux mains dans le slot Main hand.
        private bool _isLocked;
        public bool IsLocked
        {
            get => _isLocked;
            set { _isLocked = value; Notify(); NotifyDisplay(); }
        }

        private int _itemId;
        public int ItemId
        {
            get => _itemId;
            set { _itemId = value; Notify(); NotifyDisplay(); }
        }

        public int DyeId { get; set; }

        private int _modId1, _modId2, _modId3;
        public int ModId1 { get => _modId1; set { _modId1 = value; NotifyDisplay(); } } // insigne / préfixe
        public int ModId2 { get => _modId2; set { _modId2 = value; NotifyDisplay(); } } // rune / suffixe
        public int ModId3 { get => _modId3; set { _modId3 = value; NotifyDisplay(); } } // inscription

        public IEnumerable<int> ModIds => new[] { _modId1, _modId2, _modId3 }.Where(id => id > 0);

        public bool HasContent => _itemId != 0 || _modId1 != 0 || _modId2 != 0 || _modId3 != 0;

        // Alerte « combinaison impossible en jeu » : texte de l'infobulle détaillant le conflit
        // de profession, null si l'emplacement est portable tel quel (cf. RefreshViolations).
        private string? _violation;
        public string? Violation
        {
            get => _violation;
            set { _violation = value; Notify(); Notify(nameof(HasViolation)); }
        }

        public bool HasViolation => _violation != null;

        // Nom composé façon paw·ned² : "Poisonous Spear of Defense", "Vanguard's Seitung Guise of
        // Superior Vigor" ; en FR, grammaire propre (cf. ComposeItemNameFr) : « Javelot de poison
        // (de Défense) », « Tenue de Seitung de l'avant-garde (Vigueur : bonus supérieur) ».
        public string ItemDisplayName =>
            IsLocked    ? T("S.Equip.TwoHanded") :
            !HasContent ? T("S.Equip.Empty") :
            GwEquipmentInfo.ComposeItemName(_itemId, ModIds);

        // Remise à zéro complète (rechargement d'un autre équipement dans la fenêtre).
        public void Reset()
        {
            _itemId = 0;
            DyeId   = 0;
            _modId1 = _modId2 = _modId3 = 0;
            _saveSkin = true;
            _isLocked = false;
            _violation = null;
            Notify(nameof(SaveSkin));
            Notify(nameof(ItemId));
            Notify(nameof(IsLocked));
            Notify(nameof(Violation));
            Notify(nameof(HasViolation));
            NotifyDisplay();
        }

        private void NotifyDisplay()
        {
            Notify(nameof(ItemDisplayName));
            Notify(nameof(HasContent));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    // Entrée de ComboBox de mod : nom + description parfaite grisée.
    private sealed record ModChoice(int Id, string Name, string Desc)
    {
        public override string ToString() => Name;
    }

    // ── Données statiques ─────────────────────────────────────────────────────

    // Libellés indexés par SlotId du format (0=arme, 1=off-hand, 2=torse, 3=jambes, 4=tête,
    // 5=pieds, 6=gants) ; l'ordre d'AFFICHAGE de la liste est DisplaySlotOrder. Source unique
    // partagée avec le résumé d'équipement (infobulle ⚔) : GwEquipmentData.DisplaySlot.
    private static string SlotName(int slotId) => GwEquipmentData.DisplaySlot(slotId);

    private static readonly int[] DisplaySlotOrder = [0, 1, 4, 2, 6, 3, 5];

    private static string DefaultHint => T("S.Equip.PickSlot");
    private static string LockedHint  => T("S.Equip.SlotLocked");

    // ── Champs ────────────────────────────────────────────────────────────────

    // Armure PARTAGÉE entre les sets (index = SlotId 2-6 ; 0-1 restent null) ; armes par set
    // F1-F4 (index = [set][SlotId 0-1]). Slot(id) résout vers le set actif.
    private readonly SlotVm?[] _armorSlots = new SlotVm?[7];
    private readonly SlotVm[][] _weaponSets;
    private int _activeSet;

    // Profession filtrant l'armure et les stats : celle du perso en mode personnage, celle du
    // sélecteur en mode template autonome (File > New Equipment Template / fichier du browser).
    private Profession _profession;
    private readonly bool _standalone;
    private readonly string? _templatesDir; // dossier initial du dialogue "Charger un template"
    private static readonly Profession[] _profChoices = Enum.GetValues<Profession>();
    private List<ModChoice> _itemList = new();
    private List<ModChoice> _mod1List = new();
    private List<ModChoice> _mod2List = new();
    private List<ModChoice> _mod3List = new();
    private readonly List<(int Id, string Name)> _dyeList;
    private bool _loading;

    // Genre du skin d'armure affiché (radio H/F). Persisté sur le perso en mode personnage.
    private bool _female;

    // Skins décodés partagés (évite de redécoder le fichier à chaque re-sélection de slot).
    private static readonly ConcurrentDictionary<string, ImageSource> _skinCache = new();

    public EquipmentBuild? OutputEquipment { get; private set; }

    // Genre choisi à la fermeture — appliqué au perso par l'appelant (mode personnage).
    public Gender OutputGender => _female ? Gender.Female : Gender.Male;

    // ── Constructeurs ─────────────────────────────────────────────────────────

    // Mode personnage : profession imposée par le perso, pas de sélecteur ; le résultat est
    // récupéré par l'appelant via OutputEquipment.
    public EquipmentEditorWindow(CharacterSlotViewModel charVm, string? templatesDir = null)
        : this(charVm.Equipment, charVm.PrimaryProfession, charVm.Gender, standalone: false, templatesDir)
    {
        Title = string.Format(T("S.Equip.TitleFor"), charVm.Name);
    }

    // Mode template autonome (File > New Equipment Template, ouverture d'un fichier du
    // browser) : sélecteur de profession affiché ; la sauvegarde fichier est gérée par
    // l'appelant (MainWindow) à partir de OutputEquipment. Genre = preview seul (le .txt de
    // template n'encode pas le genre) → défaut Male.
    public EquipmentEditorWindow(EquipmentBuild? initial, Profession profession,
        string? templatesDir, string title)
        : this(initial, profession, Gender.Male, standalone: true, templatesDir)
    {
        Title = title;
    }

    private EquipmentEditorWindow(EquipmentBuild? initial, Profession profession, Gender gender,
        bool standalone, string? templatesDir)
    {
        InitializeComponent();
        _profession   = profession;
        _standalone   = standalone;
        _templatesDir = templatesDir;
        _female       = gender == Gender.Female;

        // Nom de teinture dans la langue courante (l'id reste la clé du format).
        _dyeList = GwEquipmentData.Dyes
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, Name: GwEquipmentData.DisplayDye(kv.Key)))
            .ToList();
        DyeCombo.ItemsSource = _dyeList.Select(e => e.Name).ToList();

        if (standalone)
        {
            ProfessionRow.Visibility = Visibility.Visible;
            _loading = true;
            ProfCombo.ItemsSource = _profChoices
                .Select(p => p == Profession.None ? T("S.Equip.NoProf") : p.DisplayName()).ToList();
            ProfCombo.SelectedIndex = Math.Max(0, Array.IndexOf(_profChoices, profession));
            _loading = false;
        }

        // Radio genre initialisé sans déclencher le handler (aucun slot encore sélectionné).
        _loading = true;
        if (_female) FemaleRadio.IsChecked = true; else MaleRadio.IsChecked = true;
        _loading = false;

        // Initialiser les 5 slots d'armure partagée + 4 sets de 2 slots d'armes.
        for (int i = 2; i < 7; i++)
            _armorSlots[i] = new SlotVm { SlotId = i, SlotName = SlotName(i) };
        _weaponSets = new SlotVm[EquipmentBuild.MaxWeaponSets][];
        for (int k = 0; k < _weaponSets.Length; k++)
            _weaponSets[k] =
            [
                new SlotVm { SlotId = 0, SlotName = SlotName(0) },
                new SlotVm { SlotId = 1, SlotName = SlotName(1) },
            ];

        LoadBuild(initial);

        // F1-F4 : bascule de set d'armes au clavier, comme en jeu.
        PreviewKeyDown += (_, e) =>
        {
            int set = e.Key switch
            { Key.F1 => 0, Key.F2 => 1, Key.F3 => 2, Key.F4 => 3, _ => -1 };
            if (set < 0) return;
            SetTab(set).IsChecked = true;
            e.Handled = true;
        };

        Loaded += (_, _) => { if (SlotListBox.SelectedIndex < 0) SlotListBox.SelectedIndex = 0; };
    }

    // Charge un équipement dans la fenêtre — remplacement complet (constructeur et bouton
    // "Charger un template d'équipement").
    private void LoadBuild(EquipmentBuild? eq)
    {
        foreach (var s in _armorSlots) s?.Reset();
        foreach (var set in _weaponSets) foreach (var s in set) s.Reset();
        _activeSet = 0;

        if (eq != null)
        {
            foreach (var item in eq.Armor)
                if (item.Slot is >= 2 and <= 6)
                    LoadItem(_armorSlots[item.Slot]!, item);
            for (int k = 0; k < eq.WeaponSets.Count && k < _weaponSets.Length; k++)
                foreach (var item in eq.WeaponSets[k].Items)
                    if (item.Slot is 0 or 1)
                        LoadItem(_weaponSets[k][item.Slot], item);
            _activeSet = Math.Clamp(eq.ActiveSet, 0, _weaponSets.Length - 1);
        }

        for (int k = 0; k < _weaponSets.Length; k++)
            UpdateOffHandLock(k);

        // Cocher l'onglet du set actif : SetTab_Checked court-circuite (set == _activeSet),
        // donc on rafraîchit la liste et les entêtes explicitement.
        SetTab(_activeSet).IsChecked = true;
        RefreshSlotList();
        UpdateSetTabHeaders();
        RefreshStats();
        SlotListBox.SelectedIndex = 0;
    }

    // Les mods sont rangés par catégorie (les anciens .pn3 stockaient une liste sans ordre
    // garanti) : 1 = insigne/préfixe, 2 = rune/suffixe, 3 = inscription ; mod inconnu →
    // premier emplacement libre. ItemId 0 avec mods = skin non sauvegardé (case décochée).
    private static void LoadItem(SlotVm s, EquipmentItem item)
    {
        s.ItemId   = item.ItemId;
        s.DyeId    = item.Dye;
        s.SaveSkin = item.ItemId != 0;
        foreach (var modId in item.ModifierIds)
        {
            var cat = GwEquipmentInfo.Mods.TryGetValue(modId, out var mi)
                ? mi.Category : ModCategory.Unknown;
            switch (cat)
            {
                case ModCategory.Insignia or ModCategory.WeaponPrefix: s.ModId1 = modId; break;
                case ModCategory.Rune or ModCategory.WeaponSuffix:     s.ModId2 = modId; break;
                case ModCategory.Inscription:                          s.ModId3 = modId; break;
                default:
                    if      (s.ModId1 == 0) s.ModId1 = modId;
                    else if (s.ModId2 == 0) s.ModId2 = modId;
                    else                    s.ModId3 = modId;
                    break;
            }
        }
    }

    // ── Sets d'armes (onglets F1-F4) ──────────────────────────────────────────

    private RadioButton SetTab(int set) => set switch
    { 0 => SetTab0, 1 => SetTab1, 2 => SetTab2, _ => SetTab3 };

    // SlotVm affiché pour un SlotId : armes = set actif, armure = partagée.
    private SlotVm Slot(int slotId) =>
        slotId <= 1 ? _weaponSets[_activeSet][slotId] : _armorSlots[slotId]!;

    private void SetTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        int set = int.Parse(tag);
        if (set == _activeSet) return;

        _activeSet = set;
        RefreshSlotList();
        RefreshStats();
    }

    // Reconstruit la liste affichée (armes du set actif + armure) en préservant la position
    // sélectionnée — changer d'onglet en gardant "Main hand" sélectionné recharge le détail.
    private void RefreshSlotList()
    {
        int sel = SlotListBox.SelectedIndex;
        SlotListBox.ItemsSource = DisplaySlotOrder.Select(Slot).ToList();
        if (sel >= 0) SlotListBox.SelectedIndex = sel;
    }

    private void UpdateSetTabHeaders()
    {
        for (int k = 0; k < _weaponSets.Length; k++)
        {
            var tab   = SetTab(k);
            var armed = _weaponSets[k].Where(s => s.HasContent && !s.IsLocked).ToList();
            tab.Content = armed.Count > 0 ? $"F{k + 1} ●" : $"F{k + 1}";
            tab.ToolTip = armed.Count > 0
                ? string.Join("\n", armed.Select(s => s.ItemDisplayName))
                : string.Format(T("S.Equip.SetEmptyTip"), k + 1);
        }
    }

    // ── Aides de classification ───────────────────────────────────────────────

    private static WeaponKind KindOf(int itemId) =>
        GwEquipmentInfo.Items.TryGetValue(itemId, out var info) ? info.Weapon : WeaponKind.None;

    // Description « parfaite » du mod, traduite dans la langue courante (l'anglais reste la
    // donnée source lue par les parseurs d'armure/durées).
    private static string DescOf(int modId) =>
        GwEquipmentModDetails.ByModId.TryGetValue(modId, out var d)
            ? GwEquipmentDescFr.Translate(d.Description) : "";

    // Nom affiché d'un objet dans la langue courante ; DisplayItemName (EN, sans « PvP ») reste la clé.
    private static string ItemName(string en) =>
        GwEquipmentItemsFr.DisplayName(GwEquipmentInfo.DisplayItemName(en));

    // Nom affiché d'un mod dans la langue courante. INSIGNES → GwInsigniaFr (partagé avec le
    // calculateur d'armure) ; RUNES + INSCRIPTIONS → GwEquipmentModsFr. Mods d'arme
    // (préfixes/suffixes) et noms d'objets restent en anglais (pas encore scrapés, lot C).
    private static string ModName(int modId, string name, bool insignia) =>
        insignia ? GwInsigniaFr.ShortNameFromFull(modId, name)
                 : GwEquipmentModsFr.DisplayName(modId, name);

    // ── Cohérence de profession de l'armure ───────────────────────────────────
    // En jeu, l'armure est liée à la profession PRIMAIRE : une pièce, un insigne ou une rune
    // d'une autre profession ne peut pas être portée. L'éditeur filtre déjà ce qu'il PROPOSE
    // (RebuildItemCombo, GwEquipmentInfo.ModsFor) ; ce qui manquait était de revalider ce qui
    // est DÉJÀ POSÉ — une valeur survit sinon à un changement de profession, à un template
    // chargé ou à un perso qui change de PR (KeepLegacy la réinjecte même dans la liste).
    // Les armes sont neutres en GW1 : seuls les 5 emplacements d'armure sont concernés.

    // Profession exigée par un objet / un mod (None = universel : pièce commune, rune de
    // Vigueur, insigne Survivant…).
    private static Profession ItemProfession(int itemId) =>
        itemId != 0 && GwEquipmentInfo.Items.TryGetValue(itemId, out var i) ? i.Profession : Profession.None;

    private static Profession ModProfession(int modId) =>
        modId != 0 && GwEquipmentInfo.Mods.TryGetValue(modId, out var m) ? m.Profession : Profession.None;

    // Éléments d'un emplacement impossibles à porter avec la profession donnée, libellés dans
    // la langue d'affichage (« • Œil omniscient (Élémentaliste uniquement) »). Liste vide pour
    // une arme, pour un emplacement sain, et pour la profession « Aucune » (pas de cadre → pas
    // de verdict : c'est justement l'état où l'éditeur ne filtre rien).
    private static List<string> ConflictsOf(SlotVm slot, Profession profession)
    {
        var conflicts = new List<string>();
        if (profession == Profession.None || !GwEquipmentInfo.IsArmorSlot(slot.SlotId)) return conflicts;

        void Add(string name, Profession owner)
        {
            if (owner != Profession.None && owner != profession)
                conflicts.Add(string.Format(T("S.Equip.ConflictOnly"), name, owner.DisplayName()));
        }

        if (GwEquipmentData.Items.TryGetValue(slot.ItemId, out var itemName))
            Add(ItemName(itemName), ItemProfession(slot.ItemId));
        if (GwEquipmentData.Modifiers.TryGetValue(slot.ModId1, out var insigniaName))
            Add(ModName(slot.ModId1, insigniaName, insignia: true), ModProfession(slot.ModId1));
        if (GwEquipmentData.Modifiers.TryGetValue(slot.ModId2, out var runeName))
            Add(ModName(slot.ModId2, runeName, insignia: false), ModProfession(slot.ModId2));

        return conflicts;
    }

    // Marqueur d'alerte des 5 pièces d'armure (⚠ rouge + infobulle dans la liste de gauche).
    // Non destructif : couvre ce qui arrive de l'extérieur (template chargé dont l'armure n'a
    // pas de skin — GuessProfession la croit neutre —, .pn3 ancien, perso qui a changé de PR).
    private void RefreshViolations()
    {
        foreach (var slot in _armorSlots)
        {
            if (slot == null) continue;
            var conflicts = ConflictsOf(slot, _profession);
            slot.Violation = conflicts.Count == 0
                ? null
                : string.Format(T("S.Equip.ConflictHeader"),
                    _profession.DisplayName(), string.Join("\n", conflicts));
        }
    }

    // Retire d'un emplacement d'armure ce que _profession ne peut pas porter. Le skin retiré
    // laisse l'emplacement exportable s'il garde un mod (sémantique « skin non sauvegardé »).
    private void PurgeIncompatible(SlotVm slot)
    {
        if (_profession == Profession.None || !GwEquipmentInfo.IsArmorSlot(slot.SlotId)) return;
        bool Incompatible(Profession owner) => owner != Profession.None && owner != _profession;

        if (Incompatible(ItemProfession(slot.ItemId))) slot.ItemId = 0;
        if (Incompatible(ModProfession(slot.ModId1))) slot.ModId1 = 0;
        if (Incompatible(ModProfession(slot.ModId2))) slot.ModId2 = 0;
    }

    // Mode template autonome : tant que la profession est « Aucune », RIEN n'est filtré (42
    // coiffes, 45 insignes, toutes professions mêlées) — poser les objets PUIS la profession
    // composait donc une armure impossible. Le premier élément professionnel posé fixe la
    // profession, qui filtre le reste ; le sélecteur reste libre ensuite.
    private void InferProfession(Profession profession)
    {
        if (!_standalone || profession == Profession.None || _profession != Profession.None) return;
        SelectProfession(profession); // → ProfCombo_SelectionChanged : refiltre les combos + stats
    }

    private static void OpenWiki(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://wiki.guildwars.com/wiki/" + path) { UseShellExecute = true });

    // ── Sélection du slot ─────────────────────────────────────────────────────

    private void SlotListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlotListBox.SelectedItem is not SlotVm slot)
        {
            NoSelectionText.Text       = DefaultHint;
            NoSelectionText.Visibility = Visibility.Visible;
            DetailFields.Visibility    = Visibility.Collapsed;
            return;
        }

        if (slot.IsLocked)
        {
            NoSelectionText.Text       = LockedHint;
            NoSelectionText.Visibility = Visibility.Visible;
            DetailFields.Visibility    = Visibility.Collapsed;
            return;
        }

        NoSelectionText.Text       = DefaultHint;
        NoSelectionText.Visibility = Visibility.Collapsed;
        DetailFields.Visibility    = Visibility.Visible;

        _loading = true;

        IncludeCheck.IsChecked = slot.SaveSkin;
        RebuildItemCombo(slot);
        RebuildModCombos(slot);
        DyeCombo.SelectedIndex = Math.Max(0, _dyeList.FindIndex(x => x.Id == slot.DyeId));
        WikiBtn.IsEnabled      = slot.ItemId > 0;
        UpdateApplySkinVisibility(slot);
        RefreshSkinImage(slot);

        _loading = false;
    }

    // "×5" du skin : visible sur les pièces du CORPS (la coiffe est un choix d'attribut, pas
    // de style) quand l'objet posé appartient à une famille d'art connue.
    private void UpdateApplySkinVisibility(SlotVm slot)
    {
        bool canApply = GwEquipmentInfo.IsArmorSlot(slot.SlotId)
            && slot.SlotId != GwEquipmentInfo.SlotHead
            && slot.ItemId != 0
            && GwEquipmentInfo.ArmorFamilyOf(slot.ItemId) != null;
        ApplySkinBtn.Visibility = canApply ? Visibility.Visible : Visibility.Collapsed;
    }

    // Bascule H/F : ne change que la variante de skin d'armure affichée (les armes sont neutres).
    // FemaleRadio null = event levé pendant InitializeComponent (les champs de contrôle ne sont
    // pas tous encore instanciés) → on ignore, l'init réel se fait dans le constructeur.
    private void GenderRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || FemaleRadio is null) return;
        _female = FemaleRadio.IsChecked == true;
        if (SlotListBox.SelectedItem is SlotVm slot) RefreshSkinImage(slot);
    }

    // Affiche le skin visuel de la pièce (armure genrée / icône d'arme) au-dessus de l'objet.
    // Téléchargement paresseux + cache disque via EquipImageService ; l'image absente du wiki
    // (coiffes d'attribut, cf. docs/armor_skins_coverage.md) → rien d'affiché (placeholder implicite).
    private async void RefreshSkinImage(SlotVm slot)
    {
        int itemId = slot.ItemId;
        if (itemId == 0)
        {
            SkinImage.Source = null;
            SkinImage.Visibility = Visibility.Collapsed;
            return;
        }

        bool armor = GwEquipmentInfo.IsArmorSlot(slot.SlotId);
        var file = armor ? GwEquipmentInfo.ArmorSkinFile(itemId, _female)
                         : GwEquipmentInfo.WeaponIconFile(itemId);
        if (file == null)
        {
            SkinImage.Source = null;
            SkinImage.Visibility = Visibility.Collapsed;
            return;
        }

        // Déjà décodé → affichage immédiat, pas de re-téléchargement ni re-décodage.
        if (_skinCache.TryGetValue(file, out var cached))
        {
            SkinImage.Source = cached;
            SkinImage.Visibility = Visibility.Visible;
            return;
        }

        var local = await (armor ? EquipImageService.GetArmorSkinAsync(file)
                                  : EquipImageService.GetWeaponIconAsync(file));

        // La sélection a pu changer pendant le téléchargement : n'applique que si le slot tient.
        if (SlotListBox.SelectedItem is not SlotVm cur || cur.ItemId != itemId) return;

        ImageSource? img = local != null ? LoadSkin(file, local) : null;
        SkinImage.Source = img;
        SkinImage.Visibility = img != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // Décode un fichier local en ImageSource figée (partageable entre threads/slots), mise en cache.
    private static ImageSource? LoadSkin(string cacheKey, string path)
    {
        return _skinCache.GetOrAdd(cacheKey, _ =>
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // ne verrouille pas le fichier
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        });
    }

    // Applique le style (famille d'art + teinture) de la pièce affichée aux autres pièces du
    // corps — chaque slot reçoit la pièce de la MÊME famille (Seitung Guise → Seitung Gloves…).
    private void ApplySkin_Click(object sender, RoutedEventArgs e)
    {
        if (SlotListBox.SelectedItem is not SlotVm slot || slot.ItemId == 0) return;
        if (GwEquipmentInfo.ArmorFamilyOf(slot.ItemId) is not { } family) return;
        if (!GwEquipmentInfo.Items.TryGetValue(slot.ItemId, out var srcInfo)) return;

        foreach (int t in (int[])[GwEquipmentInfo.SlotChest, GwEquipmentInfo.SlotLegs,
                                  GwEquipmentInfo.SlotFeet, GwEquipmentInfo.SlotHands])
        {
            if (t == slot.SlotId) continue;
            var piece = GwEquipmentInfo.ArmorPieceOfFamily(family, srcInfo.Profession, t);
            if (piece == null) continue;
            var target = _armorSlots[t]!;
            target.ItemId   = piece.ItemId;
            target.SaveSkin = true;
            target.DyeId    = slot.DyeId;
        }
        RefreshStats();
    }

    // Combo Objet : items du slot uniquement (affichés sans "PvP ") ; pour l'armure, art de la
    // profession primaire du perso (+ pièces communes). L'item déjà posé reste listé même hors
    // filtre (fichier legacy).
    private void RebuildItemCombo(SlotVm slot)
    {
        var infos = GwEquipmentInfo.ItemsForSlot(slot.SlotId);
        if (GwEquipmentInfo.IsArmorSlot(slot.SlotId) && _profession != Profession.None)
            infos = infos.Where(i => i.Profession == Profession.None
                                  || i.Profession == _profession);

        // Description grisée de l'objet, façon description de mod :
        //   coiffe → le +1 inhérent ("+1 Magie de la domination") ;
        //   arme / objet tenu / bouclier → l'attribut REQUIS ("Magie de la domination").
        // Le niveau requis (9) est le même pour tout l'équipement PvP : l'afficher n'aiderait
        // personne à choisir, seul l'attribut distingue les objets. Les deux cas s'excluent
        // (une coiffe n'a pas de requis d'arme), l'ordre du ?? est donc sans importance.
        static string ItemDesc(int itemId)
        {
            if (GwEquipmentInfo.HeadgearAttribute(itemId) is { } head)
                return $"+1 {GwAttributeData.DisplayName(head)}";
            if (GwEquipmentInfo.WeaponAttribute(itemId) is { } req)
                return GwAttributeData.DisplayName(req);
            return "";
        }

        _itemList = new List<ModChoice> { new(0, T("S.Equip.Empty"), "") };
        _itemList.AddRange(infos.Select(i =>
            new ModChoice(i.ItemId, ItemName(i.Name), ItemDesc(i.ItemId))));

        if (slot.ItemId != 0 && _itemList.FindIndex(x => x.Id == slot.ItemId) < 0
            && GwEquipmentData.Items.TryGetValue(slot.ItemId, out var legacyName))
            _itemList.Add(new ModChoice(slot.ItemId, ItemName(legacyName), ItemDesc(slot.ItemId)));

        ItemCombo.ItemsSource   = _itemList;
        ItemCombo.SelectedIndex = Math.Max(0, _itemList.FindIndex(x => x.Id == slot.ItemId));
    }

    // Combos d'upgrades typés selon le slot : armure = Insigne + Rune (filtrées par profession
    // primaire) ; arme = Préfixe (si le type en a) + Suffixe + Inscription (matrice wiki).
    // Slot arme sans objet → pas de combos (le type d'arme est inconnu).
    private void RebuildModCombos(SlotVm slot)
    {
        static List<ModChoice> BuildList(IEnumerable<EquipModInfo> mods, bool insignia = false)
        {
            var list = new List<ModChoice> { new(0, T("S.Equip.NoMod"), "") };
            list.AddRange(mods.Select(m =>
                new ModChoice(m.ModId, ModName(m.ModId, m.Name, insignia), DescOf(m.ModId))));
            return list;
        }

        // Un mod déjà posé reste listé même hors filtre (fichier legacy / changement d'objet).
        static void KeepLegacy(List<ModChoice> list, int modId, bool insignia = false)
        {
            if (modId != 0 && list.FindIndex(x => x.Id == modId) < 0
                && GwEquipmentData.Modifiers.TryGetValue(modId, out var n))
                list.Add(new ModChoice(modId, ModName(modId, n, insignia), DescOf(modId)));
        }

        bool armor = GwEquipmentInfo.IsArmorSlot(slot.SlotId);
        var kind   = armor ? WeaponKind.None : KindOf(slot.ItemId);

        if (armor)
        {
            Mod1Label.Text = T("S.Equip.Insignia");
            Mod2Label.Text = T("S.Equip.Rune");
            _mod1List = BuildList(GwEquipmentInfo.ModsFor(slot.SlotId, kind, ModCategory.Insignia, _profession), insignia: true);
            _mod2List = BuildList(GwEquipmentInfo.ModsFor(slot.SlotId, kind, ModCategory.Rune,     _profession));
            _mod3List = new List<ModChoice> { new(0, T("S.Equip.NoMod"), "") };
            Mod1Row.Visibility = Visibility.Visible;
            Mod2Row.Visibility = Visibility.Visible;
            Mod3Row.Visibility = Visibility.Collapsed;
            ApplyInsigniaBtn.Visibility = slot.ModId1 != 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (kind == WeaponKind.None)
        {
            _mod1List = new List<ModChoice> { new(0, T("S.Equip.NoMod"), "") };
            _mod2List = new List<ModChoice> { new(0, T("S.Equip.NoMod"), "") };
            _mod3List = new List<ModChoice> { new(0, T("S.Equip.NoMod"), "") };
            Mod1Row.Visibility = Visibility.Collapsed;
            Mod2Row.Visibility = Visibility.Collapsed;
            Mod3Row.Visibility = Visibility.Collapsed;
            ApplyInsigniaBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            Mod1Label.Text = T("S.Equip.Prefix");
            Mod2Label.Text = T("S.Equip.Suffix");
            Mod3Label.Text = T("S.Equip.Inscription");
            _mod1List = BuildList(GwEquipmentInfo.ModsFor(slot.SlotId, kind, ModCategory.WeaponPrefix));
            _mod2List = BuildList(GwEquipmentInfo.ModsFor(slot.SlotId, kind, ModCategory.WeaponSuffix));
            _mod3List = BuildList(GwEquipmentInfo.ModsFor(slot.SlotId, kind, ModCategory.Inscription));
            Mod1Row.Visibility = _mod1List.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Mod2Row.Visibility = Visibility.Visible;
            Mod3Row.Visibility = Visibility.Visible;
            ApplyInsigniaBtn.Visibility = Visibility.Collapsed;
        }

        KeepLegacy(_mod1List, slot.ModId1, insignia: armor);
        KeepLegacy(_mod2List, slot.ModId2);
        KeepLegacy(_mod3List, slot.ModId3);

        Mod1Combo.ItemsSource = _mod1List;
        Mod2Combo.ItemsSource = _mod2List;
        Mod3Combo.ItemsSource = _mod3List;
        Mod1Combo.SelectedIndex = Math.Max(0, _mod1List.FindIndex(x => x.Id == slot.ModId1));
        Mod2Combo.SelectedIndex = Math.Max(0, _mod2List.FindIndex(x => x.Id == slot.ModId2));
        Mod3Combo.SelectedIndex = Math.Max(0, _mod3List.FindIndex(x => x.Id == slot.ModId3));
    }

    // Une arme à deux mains neutralise le slot Off-hand DU MÊME SET (règle GW1 : elle occupe
    // les deux slots). Le contenu off-hand n'est pas effacé : re-sélectionner une arme à une
    // main le rend de nouveau éditable.
    private void UpdateOffHandLock(int set)
    {
        var weapon  = _weaponSets[set][GwEquipmentInfo.SlotWeapon];
        var offHand = _weaponSets[set][GwEquipmentInfo.SlotOffHand];
        bool twoHanded = GwEquipmentInfo.IsTwoHanded(KindOf(weapon.ItemId));

        if (offHand.IsLocked == twoHanded) return;
        offHand.IsLocked = twoHanded;

        // Rafraîchir le panneau de droite si le slot off-hand est affiché.
        if (ReferenceEquals(SlotListBox.SelectedItem, offHand))
            SlotListBox_SelectionChanged(SlotListBox, null!);
    }

    // ── Contrôles de détail ───────────────────────────────────────────────────

    private void IncludeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || SlotListBox.SelectedItem is not SlotVm slot) return;
        slot.SaveSkin = IncludeCheck.IsChecked == true;
    }

    // Sélecteur de profession (mode template autonome) : refiltre l'armure et les
    // insignes/runes du slot affiché (les valeurs hors filtre restent listées, cf. KeepLegacy)
    // et recalcule les stats (AR de base, inhérents d'armure). Comme l'armure est liée à la
    // profession, ce qui devient inportable est RETIRÉ — après confirmation, le geste pouvant
    // vider les 5 emplacements d'un coup.
    private void ProfCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfCombo.SelectedIndex < 0) return;
        var p = _profChoices[ProfCombo.SelectedIndex];
        if (p == _profession) return;

        var doomed = _armorSlots.Where(s => s != null).SelectMany(s => ConflictsOf(s!, p)).ToList();
        if (doomed.Count > 0
            && MessageBox.Show(
                   string.Format(T("S.Equip.ProfChangeAsk"), p.DisplayName(), string.Join("\n", doomed)),
                   T("S.Equip.ProfChangeTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
               != MessageBoxResult.Yes)
        {
            _loading = true; // revenir au choix précédent sans relancer ce handler
            SelectProfession(_profession);
            _loading = false;
            return;
        }

        _profession = p;
        foreach (var s in _armorSlots)
            if (s != null) PurgeIncompatible(s);

        if (SlotListBox.SelectedItem is SlotVm slot && !slot.IsLocked)
        {
            _loading = true;
            RebuildItemCombo(slot);
            RebuildModCombos(slot);
            _loading = false;
        }
        RefreshStats();
    }

    private void SelectProfession(Profession p)
        => ProfCombo.SelectedIndex = Math.Max(0, Array.IndexOf(_profChoices, p));

    // Charge un template d'équipement enregistré (fichier .txt : un code P par ligne) dans la
    // fenêtre — remplacement complet, comme un import de code. Dossier Equipment disponible →
    // picker filtré par la profession du perso (mode personnage) ; sinon dialogue de fichier.
    private void LoadTemplate_Click(object sender, RoutedEventArgs e)
    {
        string? path;
        if (_templatesDir != null && Directory.Exists(_templatesDir))
        {
            var picker = new EquipmentTemplatePickerWindow(
                _templatesDir, _standalone ? Profession.None : _profession) { Owner = this };
            if (picker.ShowDialog() != true) return;
            path = picker.SelectedPath;
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title  = T("S.Win.LoadEquipTitle"),
                Filter = T("S.Filter.EquipTpl"),
            };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }
        if (path == null) return;

        string text;
        try { text = File.ReadAllText(path); }
        catch { return; }

        var builds = GwEquipmentCodec.DecodeLines(text);
        if (builds.Count == 0)
        {
            MessageBox.Show(T("S.Msg.FileNoEquipCode"),
                T("S.Msg.LoadTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var eq = builds.Count == 1 ? builds[0] : EquipmentBuild.Combine(builds);
        LoadBuild(eq);

        // Mode template : aligner le sélecteur de profession sur l'armure chargée.
        var guessed = GwEquipmentInfo.GuessProfession(eq);
        if (_standalone && guessed != Profession.None)
            SelectProfession(guessed);
    }

    private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SlotListBox.SelectedItem is not SlotVm slot) return;
        if (ItemCombo.SelectedIndex < 0 || ItemCombo.SelectedIndex >= _itemList.Count) return;

        int newId = _itemList[ItemCombo.SelectedIndex].Id;
        if (newId == slot.ItemId) return;

        var oldKind = KindOf(slot.ItemId);
        slot.ItemId = newId;
        WikiBtn.IsEnabled = slot.ItemId > 0;
        UpdateApplySkinVisibility(slot);
        RefreshSkinImage(slot);

        // Changement de type d'arme → les upgrades de l'ancien type ne sont plus valides.
        if (!GwEquipmentInfo.IsArmorSlot(slot.SlotId))
        {
            var newKind = KindOf(newId);
            if (newKind != oldKind)
            {
                bool StillValid(int modId, ModCategory cat) =>
                    modId != 0 && GwEquipmentInfo.Mods.TryGetValue(modId, out var mi)
                    && (mi.Category == cat
                        ? mi.Weapon == newKind
                        : mi.Category == ModCategory.Inscription
                          && GwEquipmentInfo.InscriptionFits(mi.Inscription, newKind));
                if (!StillValid(slot.ModId1, ModCategory.WeaponPrefix)) slot.ModId1 = 0;
                if (!StillValid(slot.ModId2, ModCategory.WeaponSuffix)) slot.ModId2 = 0;
                if (!StillValid(slot.ModId3, ModCategory.Inscription))  slot.ModId3 = 0;
            }

            _loading = true;
            RebuildModCombos(slot);
            _loading = false;
        }

        UpdateOffHandLock(_activeSet);
        InferProfession(ItemProfession(newId));
        RefreshStats();
    }

    private void DyeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SlotListBox.SelectedItem is not SlotVm slot) return;
        if (DyeCombo.SelectedIndex >= 0 && DyeCombo.SelectedIndex < _dyeList.Count)
            slot.DyeId = _dyeList[DyeCombo.SelectedIndex].Id;
    }

    private void ModCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SlotListBox.SelectedItem is not SlotVm slot) return;
        var combo = (ComboBox)sender;
        var list  = combo == Mod1Combo ? _mod1List : combo == Mod2Combo ? _mod2List : _mod3List;
        if (combo.SelectedIndex < 0 || combo.SelectedIndex >= list.Count) return;
        int modId = list[combo.SelectedIndex].Id;
        if      (combo == Mod1Combo) slot.ModId1 = modId;
        else if (combo == Mod2Combo) slot.ModId2 = modId;
        else                         slot.ModId3 = modId;

        // "×5" visible dès qu'une insigne est sélectionnée sur une pièce d'armure.
        if (combo == Mod1Combo && GwEquipmentInfo.IsArmorSlot(slot.SlotId))
            ApplyInsigniaBtn.Visibility = modId != 0 ? Visibility.Visible : Visibility.Collapsed;

        // Insigne/rune d'une profession posé sur un template encore neutre : il la fixe.
        if (GwEquipmentInfo.IsArmorSlot(slot.SlotId)) InferProfession(ModProfession(modId));
        RefreshStats();
    }

    // "×5" : applique l'insigne sélectionnée aux 5 pièces d'armure — y compris vides (un slot
    // avec mod seul est exporté skin non sauvegardé, comme paw·ned²).
    private void ApplyInsignia_Click(object sender, RoutedEventArgs e)
    {
        if (SlotListBox.SelectedItem is not SlotVm slot
            || !GwEquipmentInfo.IsArmorSlot(slot.SlotId) || slot.ModId1 == 0) return;

        foreach (var s in _armorSlots)
            if (s != null) s.ModId1 = slot.ModId1;
        RefreshStats();
    }

    // Pour une pièce d'armure, préfère la page de STYLE (table de fabrication) à la page d'item
    // plate quand elle est identifiée (GwEquipmentInfo.ArmorStyleWikiPath) ; fallback = page d'item.
    private void WikiBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SlotListBox.SelectedItem is not SlotVm slot || slot.ItemId == 0) return;
        if (!GwEquipmentData.Items.TryGetValue(slot.ItemId, out var name)) return;

        var stylePath = GwEquipmentInfo.IsArmorSlot(slot.SlotId)
            ? GwEquipmentInfo.ArmorStyleWikiPath(slot.ItemId)
            : null;
        OpenWiki(stylePath ?? Uri.EscapeDataString(name.Replace(' ', '_')));
    }

    // Bouton wiki d'une ligne de mod : page du mod sélectionné (avec ancre pour les
    // inscriptions), sinon page générique de la catégorie.
    private void ModWikiBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SlotListBox.SelectedItem is not SlotVm slot) return;

        bool armor = GwEquipmentInfo.IsArmorSlot(slot.SlotId);
        int modId; ModCategory category;
        if (ReferenceEquals(sender, Mod1WikiBtn))
        { modId = slot.ModId1; category = armor ? ModCategory.Insignia : ModCategory.WeaponPrefix; }
        else if (ReferenceEquals(sender, Mod2WikiBtn))
        { modId = slot.ModId2; category = armor ? ModCategory.Rune : ModCategory.WeaponSuffix; }
        else
        { modId = slot.ModId3; category = ModCategory.Inscription; }

        OpenWiki(modId != 0 && GwEquipmentModDetails.ByModId.TryGetValue(modId, out var d)
            ? d.WikiPath
            : GwEquipmentInfo.CategoryWikiPath(category));
    }

    // ── Calcul des stats ──────────────────────────────────────────────────────

    // AR max de base par profession : remontée en Core (ArmorCalculator.BaseArmor, chantier 14)
    // pour être partagée avec le calculateur d'armure. Comportement identique. Les bonus inhérents
    // CONDITIONNELS (Guerrier +20 vs physique, Rôdeur +30 vs élémentaire) restent exclus ici,
    // comme tous les bonus conditionnels.
    private static int BaseArmorRating(Profession p) => ArmorCalculator.BaseArmor(p);

    // Règles wiki/Rune (04/07/2026) : bonus de runes du même attribut NON cumulables (le
    // meilleur prime), malus PV cumulables ; Vigor +30/+41/+50 non cumulable, sans malus ;
    // Vitae +10 cumulable ; Attunement +2 ; malus -35/-75 réservés aux runes d'attribut.
    // Les bonus INCONDITIONNELS des autres mods (armes, inscriptions, insignes) sont lus
    // depuis la description parfaite générée : segments "Health/Energy/Armor/Energy
    // regeneration ±n" sans parenthèse (une parenthèse = condition → ignoré, décision Philippe).
    private void RefreshStats()
    {
        int hp = 480;
        int energyDelta = 0, pipsDelta = 0, armorBonus = 0;
        int vigorBest = 0;
        var runeBest    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var attrBonuses = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Slots comptés : armes du set AFFICHÉ + armure partagée (la barre suit l'onglet).
        var activeSlots = DisplaySlotOrder.Select(Slot)
            .Where(s => s.HasContent && !s.IsLocked).ToList();
        foreach (var slot in activeSlots)
        {
            foreach (var modId in slot.ModIds)
            {
                if (!GwEquipmentData.Modifiers.TryGetValue(modId, out var name)) continue;

                // Insignes à valeur par pièce : torse / jambes / autres.
                if (name == "Survivor Insignia")
                { hp += slot.SlotId == GwEquipmentInfo.SlotChest ? 15 : slot.SlotId == GwEquipmentInfo.SlotLegs ? 10 : 5; continue; }
                if (name == "Radiant Insignia")
                { energyDelta += slot.SlotId == GwEquipmentInfo.SlotChest ? 3 : slot.SlotId == GwEquipmentInfo.SlotLegs ? 2 : 1; continue; }

                if (name.StartsWith("Rune of ", StringComparison.Ordinal))
                {
                    var rest = name[8..];
                    int tier = 0;
                    if      (rest.StartsWith("Superior ", StringComparison.Ordinal)) { tier = 3; rest = rest[9..]; }
                    else if (rest.StartsWith("Major ", StringComparison.Ordinal))    { tier = 2; rest = rest[6..]; }
                    else if (rest.StartsWith("Minor ", StringComparison.Ordinal))    { tier = 1; rest = rest[6..]; }

                    switch (rest)
                    {
                        case "Vigor":
                            vigorBest = Math.Max(vigorBest, tier == 1 ? 30 : tier == 2 ? 41 : 50);
                            break;
                        case "Vitae":      hp += 10;        break;
                        case "Attunement": energyDelta += 2; break;
                        case "Absorption" or "Clarity" or "Purity" or "Recovery" or "Restoration":
                            break; // pas d'effet sur la barre de stats
                        default: // rune d'attribut de profession
                            hp -= tier == 3 ? 75 : tier == 2 ? 35 : 0;
                            runeBest.TryGetValue(rest, out var best);
                            if (tier > best) runeBest[rest] = tier;
                            break;
                    }
                    continue;
                }

                // Mods d'arme / inscriptions / autres insignes : segments inconditionnels.
                if (GwEquipmentModDetails.ByModId.TryGetValue(modId, out var detail))
                {
                    foreach (var seg in detail.Description.Split(';', StringSplitOptions.TrimEntries))
                    {
                        if (seg.Contains('(')) continue; // conditionnel → ignoré
                        var m = System.Text.RegularExpressions.Regex.Match(
                            seg, @"^(Health|Energy|Armor|Energy regeneration):? ([+-]\d+)$");
                        if (!m.Success) continue;
                        int v = int.Parse(m.Groups[2].Value);
                        switch (m.Groups[1].Value)
                        {
                            case "Health":              hp          += v; break;
                            case "Energy":              energyDelta += v; break;
                            case "Armor":               armorBonus  += v; break;
                            case "Energy regeneration": pipsDelta   += v; break;
                        }
                    }
                }
            }
        }

        hp += vigorBest;
        foreach (var (attr, best) in runeBest) attrBonuses[attr] = best;

        // +1 inhérent de la coiffe (cumule avec la rune du même attribut).
        var head = Slot(GwEquipmentInfo.SlotHead);
        if (head.ItemId != 0 && GwEquipmentInfo.HeadgearAttribute(head.ItemId) is { } headAttr)
        {
            attrBonuses.TryGetValue(headAttr, out var curHead);
            attrBonuses[headAttr] = curHead + 1;
        }

        // Énergie de base nue = 20 pour toutes les professions ; les écarts viennent des bonus
        // inhérents d'armure ci-dessous (validé Philippe 04/07 : Assassin 25 = 20 + torse +5,
        // − 5 de "Brawn over Brains" → 20). L'ancien switch par profession sur-comptait.
        const int energyBase = 20;

        // Bonus inhérents à l'armure selon la profession :
        //   chestE / armsE  = énergie max (slot 2 et 6)
        //   legsPip / feetPip = +1 rég. énergie (slot 3 et 5)
        //   chestHp          = PV supplémentaires (slot 2, Dervish uniquement)
        var (chestE, armsE, legsPip, feetPip, chestHp) = _profession switch
        {
            Profession.Warrior      => (0, 0, false, false, 0),
            Profession.Ranger       => (5, 0, true,  false, 0),
            Profession.Monk         => (5, 5, true,  true,  0),
            Profession.Necromancer  => (5, 5, true,  true,  0),
            Profession.Mesmer       => (5, 5, true,  true,  0),
            Profession.Elementalist => (5, 5, true,  true,  0),
            Profession.Assassin     => (5, 0, true,  true,  0),
            Profession.Ritualist    => (5, 5, true,  true,  0),
            Profession.Dervish      => (0, 5, true,  true,  25),
            Profession.Paragon      => (5, 5, false, false, 0),
            _                       => (0, 0, false, false, 0),
        };

        int armorE = 0;
        int pips   = 2;
        if (Slot(2).HasContent) { armorE += chestE; hp += chestHp; }
        if (Slot(6).HasContent)   armorE += armsE;
        if (Slot(3).HasContent && legsPip) pips++;
        if (Slot(5).HasContent && feetPip) pips++;

        int energy = energyBase + armorE + energyDelta;
        pips = Math.Max(0, pips + pipsDelta);

        // Armure : AR max de base de la profession + bonus inconditionnels des mods.
        // Bouclier : +16 si son requis est atteint, sinon la moitié (+8) — affichés "86 (78)".
        // Bug du jeu (Philippe 04/07) : le bouclier de Force donne +9 au lieu de +8 quand le
        // requis n'est pas atteint.
        var offHand   = Slot(GwEquipmentInfo.SlotOffHand);
        bool shield   = !offHand.IsLocked && KindOf(offHand.ItemId) == WeaponKind.Shield;
        int armorBase = BaseArmorRating(_profession) + armorBonus;

        HpText.Text     = hp.ToString();
        EnergyText.Text = energy.ToString();
        PipsText.Text   = $"{pips} pip{(pips > 1 ? "s" : "")}";

        if (shield)
        {
            bool strengthShield = GwEquipmentData.Items.TryGetValue(offHand.ItemId, out var shieldName)
                                  && shieldName == "PvP Strength Shield";
            int met   = armorBase + 16;
            int unmet = armorBase + (strengthShield ? 9 : 8);
            ArmorText.Text    = $"{met} ({unmet})";
            ArmorText.ToolTip = T("S.Equip.ShieldReqTip")
                + (strengthShield ? string.Format(T("S.Equip.ShieldReqBug"), unmet) : "");
        }
        else
        {
            ArmorText.Text    = armorBase.ToString();
            ArmorText.ToolTip = null;
        }
        // Les clés d'attributs restent anglaises (runes/coiffes) → traduites à l'affichage.
        AttrBonusesText.Text = attrBonuses.Count > 0
            ? string.Join(", ", attrBonuses.Select(kv => $"+{kv.Value} {GwAttributeData.DisplayName(kv.Key)}"))
            : T("S.Equip.None");

        UpdateSetTabHeaders();
        RefreshViolations();
    }

    // ── OK / sauvegarde ───────────────────────────────────────────────────────

    // Construit l'équipement courant de la fenêtre (armure + 4 sets, slots verrouillés
    // exclus, sets vides de fin élagués). Null si tout est vide.
    private EquipmentBuild? BuildCurrent()
    {
        static EquipmentItem Export(SlotVm slot)
        {
            var item = new EquipmentItem
            {
                Slot   = slot.SlotId,
                ItemId = slot.SaveSkin ? slot.ItemId : 0,
                Dye    = slot.DyeId,
            };
            item.ModifierIds.AddRange(slot.ModIds);
            return item;
        }

        var build = new EquipmentBuild { ActiveSet = _activeSet };
        foreach (var slots in _weaponSets)
            build.WeaponSets.Add(new WeaponSet
            {
                Items = slots.Where(s => s.HasContent && !s.IsLocked).Select(Export).ToList(),
            });
        build.Armor.AddRange(_armorSlots
            .Where(s => s is { HasContent: true })
            .Select(s => Export(s!)));

        build.TrimWeaponSets();
        return build.IsEmpty ? null : build;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        OutputEquipment = BuildCurrent();
        DialogResult    = true;
    }

    // Sauvegarde l'équipement courant comme template réutilisable (.txt : un code P par
    // ligne, un par set) — disponible dans les deux modes, sans fermer la fenêtre.
    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (BuildCurrent() is not { } eq)
        {
            MessageBox.Show(T("S.Msg.NothingToSave"), T("S.Msg.SaveTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title  = T("S.Dlg.SaveEquipTpl"),
            Filter = T("S.Filter.EquipTplSave"),
        };
        if (_templatesDir != null && Directory.Exists(_templatesDir))
            dlg.InitialDirectory = _templatesDir;
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, GwEquipmentCodec.EncodeFileText(eq));
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(T("S.Msg.CantSaveTemplate"), ex.Message),
                T("S.Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
