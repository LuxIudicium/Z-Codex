using ZCodex.App.Behaviors;
using ZCodex.App.Settings;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using System.Windows;
using System.Windows.Controls;
using Ritual = ZCodex.Core.Data.NatureRitualData.Ritual;

namespace ZCodex.App.Controls;

public partial class SkillTooltipControl : UserControl
{
    public SkillTooltipControl()
    {
        InitializeComponent();
        // Recalcul à chaque affichage de l'infobulle (Loaded se déclenche à chaque ouverture du
        // Popup) : reflète l'état courant du toggle « Dégâts selon l'armure » ET la langue courante.
        // Indispensable pour le nom et la ligne de type, liés à un POCO Skill sans notification :
        // sans ce recalcul ils resteraient figés dans la langue active à la 1re ouverture.
        Loaded += (_, _) => Refresh();
    }

    // Nom + ligne de type affichés dans l'en-tête, recalculés selon la langue courante (le binding
    // direct sur Skill.DisplayName / le converter ne se réévalue jamais au switch — Skill est un
    // POCO sans INotifyPropertyChanged).
    public static readonly DependencyProperty HeaderNameProperty =
        DependencyProperty.Register(nameof(HeaderName), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string HeaderName
    {
        get => (string)GetValue(HeaderNameProperty);
        private set => SetValue(HeaderNameProperty, value);
    }

    public static readonly DependencyProperty TypeLineProperty =
        DependencyProperty.Register(nameof(TypeLine), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string TypeLine
    {
        get => (string)GetValue(TypeLineProperty);
        private set => SetValue(TypeLineProperty, value);
    }

    public static readonly DependencyProperty SkillProperty =
        DependencyProperty.Register(nameof(Skill), typeof(Skill), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public Skill? Skill
    {
        get => (Skill?)GetValue(SkillProperty);
        set => SetValue(SkillProperty, value);
    }

    // Footer pré-calculé par le perso (contexte build, éventuellement multi-lignes).
    // Null = contexte catalogue (hors perso) → le contrôle affiche la plage "0...12...15".
    public static readonly DependencyProperty FooterOverrideProperty =
        DependencyProperty.Register(nameof(FooterOverride), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public string? FooterOverride
    {
        get => (string?)GetValue(FooterOverrideProperty);
        set => SetValue(FooterOverrideProperty, value);
    }

    // Description pré-calculée par le perso (contexte build : type retiré + variables résolues).
    // Null = catalogue → le contrôle affiche le corps concis avec ses plages.
    // TOUJOURS EN ANGLAIS : sert aussi d'entrée aux parseurs (dégâts selon l'armure, durée
    // d'enchantement, invocation). L'affichage FR passe par DisplayDescriptionOverride.
    public static readonly DependencyProperty DescriptionOverrideProperty =
        DependencyProperty.Register(nameof(DescriptionOverride), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public string? DescriptionOverride
    {
        get => (string?)GetValue(DescriptionOverrideProperty);
        set => SetValue(DescriptionOverrideProperty, value);
    }

    // Description AFFICHÉE en mode FR (résolue au rang sur le texte gwiki). Null = langue EN,
    // pas de texte FR, ou page FR suspecte → on affiche DescriptionOverride (EN) comme avant.
    // Ne JAMAIS brancher les parseurs dessus.
    public static readonly DependencyProperty DisplayDescriptionOverrideProperty =
        DependencyProperty.Register(nameof(DisplayDescriptionOverride), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public string? DisplayDescriptionOverride
    {
        get => (string?)GetValue(DisplayDescriptionOverrideProperty);
        set => SetValue(DisplayDescriptionOverrideProperty, value);
    }

    // Rang effectif de la maîtrise de l'ARME (fourni par le slot en contexte build).
    // Null = pas une attaque d'arme, catalogue, ou maîtrise absente → pas de table d'arme.
    public static readonly DependencyProperty WeaponMasteryRankProperty =
        DependencyProperty.Register(nameof(WeaponMasteryRank), typeof(int?), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public int? WeaponMasteryRank
    {
        get => (int?)GetValue(WeaponMasteryRankProperty);
        set => SetValue(WeaponMasteryRankProperty, value);
    }

    // Maîtrise de l'ARME effective de la ligne (fournie par le slot) : elle porte le choix manuel
    // d'arme fait dans la fenêtre Spike sur une attaque à arme libre, que la seule signature de la
    // skill ne peut pas donner. Null = catalogue ou pas de choix → arme du type d'attaque.
    public static readonly DependencyProperty WeaponMasteryProperty =
        DependencyProperty.Register(nameof(WeaponMastery), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public string? WeaponMastery
    {
        get => (string?)GetValue(WeaponMasteryProperty);
        set => SetValue(WeaponMasteryProperty, value);
    }

    // Rang de Force du perso (fourni par le slot pour les attaques d'arme uniquement) :
    // pénétration d'armure naturelle de 1 %/rang, mod « of the Warrior » compris.
    public static readonly DependencyProperty StrengthRankProperty =
        DependencyProperty.Register(nameof(StrengthRank), typeof(int?), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public int? StrengthRank
    {
        get => (int?)GetValue(StrengthRankProperty);
        set => SetValue(StrengthRankProperty, value);
    }

    // Rang de Critical Strikes du perso (fourni par le slot pour les attaques d'arme) :
    // +1 %/rang au taux de critique, toutes armes, mod « of the Assassin » compris.
    public static readonly DependencyProperty CriticalStrikesRankProperty =
        DependencyProperty.Register(nameof(CriticalStrikesRank), typeof(int?), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public int? CriticalStrikesRank
    {
        get => (int?)GetValue(CriticalStrikesRankProperty);
        set => SetValue(CriticalStrikesRankProperty, value);
    }

    // Rang d'Expertise du perso (attribut primaire Ranger), fourni par le slot quand la skill est
    // concernée (attaque, touch, rituel, tout skill Ranger). Null = catalogue ou skill non
    // concernée → coût de base affiché sans réduction.
    public static readonly DependencyProperty ExpertiseRankProperty =
        DependencyProperty.Register(nameof(ExpertiseRank), typeof(int?), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public int? ExpertiseRank
    {
        get => (int?)GetValue(ExpertiseRankProperty);
        set => SetValue(ExpertiseRankProperty, value);
    }

    // Rang de Puissance de l'invocation du perso (attribut primaire Ritualiste), fourni par le slot
    // quand la skill est concernée (sort d'altération d'arme, esprit, serviteur). Null = catalogue ou
    // skill non concernée → aucun bloc d'invocation.
    public static readonly DependencyProperty SpawningPowerRankProperty =
        DependencyProperty.Register(nameof(SpawningPowerRank), typeof(int?), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public int? SpawningPowerRank
    {
        get => (int?)GetValue(SpawningPowerRankProperty);
        set => SetValue(SpawningPowerRankProperty, value);
    }

    // Réduction d'énergie du flux « énergie » actif du perso (Jack of All Trades 20, All In 25 ;
    // 0 sinon), appliquée APRÈS Expertise (décision Philippe). Sur TOUTES les skills, pas seulement
    // celles concernées par l'Expertise. Fournie par le slot en contexte build.
    public static readonly DependencyProperty FluxEnergyPercentProperty =
        DependencyProperty.Register(nameof(FluxEnergyPercent), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int FluxEnergyPercent
    {
        get => (int)GetValue(FluxEnergyPercentProperty);
        set => SetValue(FluxEnergyPercentProperty, value);
    }

    // Bonus de dégâts du flux Jack of All Trades (+15 %, sinon 0) appliqué à la table « Dégâts selon
    // l'armure ». Fourni par le slot en contexte build.
    public static readonly DependencyProperty FluxDamagePercentProperty =
        DependencyProperty.Register(nameof(FluxDamagePercent), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int FluxDamagePercent
    {
        get => (int)GetValue(FluxDamagePercentProperty);
        set => SetValue(FluxDamagePercentProperty, value);
    }

    // Réduction du temps d'activation du flux Jack of All Trades (25 %, sinon 0 → temps × 0,75).
    // Fournie par le slot en contexte build.
    public static readonly DependencyProperty FluxCastPercentProperty =
        DependencyProperty.Register(nameof(FluxCastPercent), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int FluxCastPercent
    {
        get => (int)GetValue(FluxCastPercentProperty);
        set => SetValue(FluxCastPercentProperty, value);
    }

    // Rituels de la nature actifs (environnement global) : modifient énergie/recharge/cast/upkeep/
    // overcast. Fournis par le slot en contexte build ; null = catalogue → aucun effet.
    public static readonly DependencyProperty NatureRitualsProperty =
        DependencyProperty.Register(nameof(NatureRituals), typeof(IReadOnlySet<Ritual>), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public IReadOnlySet<Ritual>? NatureRituals
    {
        get => (IReadOnlySet<Ritual>?)GetValue(NatureRitualsProperty);
        set => SetValue(NatureRitualsProperty, value);
    }

    // Surcoût Roaring Winds résolu au rang du lanceur (0 si N/A). Fourni par le slot.
    public static readonly DependencyProperty RoaringWindsBonusProperty =
        DependencyProperty.Register(nameof(RoaringWindsBonus), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int RoaringWindsBonus
    {
        get => (int)GetValue(RoaringWindsBonusProperty);
        set => SetValue(RoaringWindsBonusProperty, value);
    }

    // Points retirés aux cris et chants par Energizing Chorus, résolus au rang du lanceur (0 si inactif,
    // lot 2b). Fourni par le slot.
    public static readonly DependencyProperty EnergizingChorusReductionProperty =
        DependencyProperty.Register(nameof(EnergizingChorusReduction), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int EnergizingChorusReduction
    {
        get => (int)GetValue(EnergizingChorusReductionProperty);
        set => SetValue(EnergizingChorusReductionProperty, value);
    }

    // Sorts de protection du bandeau d'équipe (lot 3b) : Time Ward résolue au rang de son porteur, Ebon Battle
    // Standard of Wisdom à −50 % fixes. Fournis par le slot, comme Energizing Chorus.
    public static readonly DependencyProperty TeamSpeedProperty =
        DependencyProperty.Register(nameof(TeamSpeed), typeof(NatureRitualData.TeamSpeed), typeof(SkillTooltipControl),
            new PropertyMetadata(default(NatureRitualData.TeamSpeed), OnInputsChanged));

    public NatureRitualData.TeamSpeed TeamSpeed
    {
        get => (NatureRitualData.TeamSpeed)GetValue(TeamSpeedProperty);
        set => SetValue(TeamSpeedProperty, value);
    }

    // Surcoût d'incantation de Nature's Renewal, en % « plus long » : 100 = le ×2 du PvE (défaut),
    // 50…83 en PvP où l'effet suit le rang de Survie du lanceur. Fourni par le slot.
    public static readonly DependencyProperty NaturesRenewalCastPctProperty =
        DependencyProperty.Register(nameof(NaturesRenewalCastPct), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(100, OnInputsChanged));

    public int NaturesRenewalCastPct
    {
        get => (int)GetValue(NaturesRenewalCastPctProperty);
        set => SetValue(NaturesRenewalCastPctProperty, value);
    }

    // ── Durée d'enchantement (Lot D) : % des modificateurs applicables à cette compétence ────
    // Arme « of Enchanting » (+20), prolongateur personnel (Blessed Aura/Extend), Tranquility (−).
    // Fournis par le slot ; 0 en catalogue ou hors enchantement → aucune ligne de durée.
    public static readonly DependencyProperty EnchantEnchantingPctProperty =
        DependencyProperty.Register(nameof(EnchantEnchantingPct), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int EnchantEnchantingPct
    {
        get => (int)GetValue(EnchantEnchantingPctProperty);
        set => SetValue(EnchantEnchantingPctProperty, value);
    }

    public static readonly DependencyProperty EnchantExtenderPctProperty =
        DependencyProperty.Register(nameof(EnchantExtenderPct), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int EnchantExtenderPct
    {
        get => (int)GetValue(EnchantExtenderPctProperty);
        set => SetValue(EnchantExtenderPctProperty, value);
    }

    public static readonly DependencyProperty EnchantTranquilityPctProperty =
        DependencyProperty.Register(nameof(EnchantTranquilityPct), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int EnchantTranquilityPct
    {
        get => (int)GetValue(EnchantTranquilityPctProperty);
        set => SetValue(EnchantTranquilityPctProperty, value);
    }

    // Gain d'adrénaline par touche du perso, en centièmes de coup (100 = aucun effet) : compétences
    // actives qui l'accélèrent (chantier infobulle, lot 1). Fourni par le slot ; 100 en catalogue.
    public static readonly DependencyProperty AdrenalineGainPerHitProperty =
        DependencyProperty.Register(nameof(AdrenalineGainPerHit), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(100, OnInputsChanged));

    public int AdrenalineGainPerHit
    {
        get => (int)GetValue(AdrenalineGainPerHitProperty);
        set => SetValue(AdrenalineGainPerHitProperty, value);
    }

    // Soothing (effet ennemi du bandeau d'équipe, lot 1b) : le gain TOTAL est divisé par deux.
    // Fourni par le slot ; faux en catalogue.
    public static readonly DependencyProperty AdrenalineSlowedProperty =
        DependencyProperty.Register(nameof(AdrenalineSlowed), typeof(bool), typeof(SkillTooltipControl),
            new PropertyMetadata(false, OnInputsChanged));

    public bool AdrenalineSlowed
    {
        get => (bool)GetValue(AdrenalineSlowedProperty);
        set => SetValue(AdrenalineSlowedProperty, value);
    }

    // Un effet du bandeau d'équipe pèse sur l'adrénaline → coups nécessaires en couleur rituel (ambre),
    // même priorité que la ligne d'énergie. Fourni par le slot.
    public static readonly DependencyProperty AdrenalineFromTeamProperty =
        DependencyProperty.Register(nameof(AdrenalineFromTeam), typeof(bool), typeof(SkillTooltipControl),
            new PropertyMetadata(false, OnInputsChanged));

    public bool AdrenalineFromTeam
    {
        get => (bool)GetValue(AdrenalineFromTeamProperty);
        set => SetValue(AdrenalineFromTeamProperty, value);
    }

    // Réductions de coût des compétences actives du perso qui touchent cette compétence (chantier
    // infobulle, lot 2 : Songkai, Divine Spirit, glyphes…). Fournies par le slot ; aucune en catalogue.
    public static readonly DependencyProperty EnergyReductionProperty =
        DependencyProperty.Register(nameof(EnergyReduction), typeof(EnergyReduction), typeof(SkillTooltipControl),
            new PropertyMetadata(default(EnergyReduction), OnInputsChanged));

    public EnergyReduction EnergyReduction
    {
        get => (EnergyReduction)GetValue(EnergyReductionProperty);
        set => SetValue(EnergyReductionProperty, value);
    }

    // Effets des compétences actives du perso sur la recharge et l'incantation de cette compétence (chantier infobulle,
    // lot 3 : Serpent's Quickness, glyphes, Weapon of Quickening reçue…). Fournis par le slot ; aucun en catalogue.
    public static readonly DependencyProperty SkillSpeedProperty =
        DependencyProperty.Register(nameof(SkillSpeed), typeof(SkillSpeed), typeof(SkillTooltipControl),
            new PropertyMetadata(default(SkillSpeed), OnInputsChanged));

    public SkillSpeed SkillSpeed
    {
        get => (SkillSpeed)GetValue(SkillSpeedProperty);
        set => SetValue(SkillSpeedProperty, value);
    }

    // Rallonge de durée propre en % (lot 4a) : 0 en catalogue, ou si aucun allongeur allumé ne vise cette compétence.
    public static readonly DependencyProperty DurationBoostPctProperty =
        DependencyProperty.Register(nameof(DurationBoostPct), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int DurationBoostPct
    {
        get => (int)GetValue(DurationBoostPctProperty);
        set => SetValue(DurationBoostPctProperty, value);
    }

    // Effets du perso sur les durées de CONDITIONS (lot 4b) : conditions ajoutées aux attaques de cette
    // compétence par un effet actif, et allongeurs (Sceau de l'Archer, préfixes d'arme). Aucun en catalogue.
    public static readonly DependencyProperty ConditionDurationsProperty =
        DependencyProperty.Register(nameof(ConditionDurations), typeof(ConditionDurations), typeof(SkillTooltipControl),
            new PropertyMetadata(default(ConditionDurations), OnInputsChanged));

    public ConditionDurations ConditionDurations
    {
        get => (ConditionDurations)GetValue(ConditionDurationsProperty);
        set => SetValue(ConditionDurationsProperty, value);
    }

    // Rang de Fast Casting du perso (0 en catalogue, ou si le perso ne l'a pas) : incantation des sorts et sceaux, recharge
    // des sorts d'Envoûteur en PvE. Caractéristique toujours active : elle ne colore rien, comme l'Expertise.
    public static readonly DependencyProperty FastCastingRankProperty =
        DependencyProperty.Register(nameof(FastCastingRank), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int FastCastingRank
    {
        get => (int)GetValue(FastCastingRankProperty);
        set => SetValue(FastCastingRankProperty, value);
    }

    // Coups d'adrénaline donnés par Rage of the Ntouka (lot 3) : ils se retranchent des coups nécessaires affichés.
    public static readonly DependencyProperty AdrenalineStrikesGivenProperty =
        DependencyProperty.Register(nameof(AdrenalineStrikesGiven), typeof(int), typeof(SkillTooltipControl),
            new PropertyMetadata(0, OnInputsChanged));

    public int AdrenalineStrikesGiven
    {
        get => (int)GetValue(AdrenalineStrikesGivenProperty);
        set => SetValue(AdrenalineStrikesGivenProperty, value);
    }

    // Visibilité de la ligne de recharge : pilotée par la recharge EFFECTIVE, pour afficher « 3 (0) » quand Rage of the
    // Ntouka ajoute ses secondes à une compétence d'adrénaline sans recharge (patron EnergyVisibility).
    public static readonly DependencyProperty RechargeVisibilityProperty =
        DependencyProperty.Register(nameof(RechargeVisibility), typeof(Visibility), typeof(SkillTooltipControl),
            new PropertyMetadata(Visibility.Collapsed));

    public Visibility RechargeVisibility
    {
        get => (Visibility)GetValue(RechargeVisibilityProperty);
        private set => SetValue(RechargeVisibilityProperty, value);
    }

    // ── Coût en adrénaline affiché : « coups nécessaires (base) » si un effet actif le change ────
    public static readonly DependencyProperty AdrenalineTextProperty =
        DependencyProperty.Register(nameof(AdrenalineText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string AdrenalineText
    {
        get => (string)GetValue(AdrenalineTextProperty);
        private set => SetValue(AdrenalineTextProperty, value);
    }

    // ── Coût en énergie affiché : « réduit (base) » si l'Expertise l'abaisse, sinon base ────
    public static readonly DependencyProperty EnergyTextProperty =
        DependencyProperty.Register(nameof(EnergyText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string EnergyText
    {
        get => (string)GetValue(EnergyTextProperty);
        private set => SetValue(EnergyTextProperty, value);
    }

    // Visibilité de la ligne énergie : pilotée par le coût EFFECTIF (>0), pour afficher les
    // compétences de base 0 dont un rituel ajoute un coût (signet+Primal Echoes, chant/cri+Roaring
    // Winds, attaque adrénaline+Quicksand). Sinon la base 0 masquerait la ligne.
    public static readonly DependencyProperty EnergyVisibilityProperty =
        DependencyProperty.Register(nameof(EnergyVisibility), typeof(Visibility), typeof(SkillTooltipControl),
            new PropertyMetadata(Visibility.Collapsed));

    public Visibility EnergyVisibility
    {
        get => (Visibility)GetValue(EnergyVisibilityProperty);
        private set => SetValue(EnergyVisibilityProperty, value);
    }

    // ── Temps d'activation affiché : « réduit (base) » si le flux JoT l'abaisse, sinon base ──────
    public static readonly DependencyProperty CastTextProperty =
        DependencyProperty.Register(nameof(CastText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string CastText
    {
        get => (string)GetValue(CastTextProperty);
        private set => SetValue(CastTextProperty, value);
    }

    // ── Recharge / Upkeep / Overcast affichés : « modifié (base) » si un rituel les change ───────
    public static readonly DependencyProperty RechargeTextProperty =
        DependencyProperty.Register(nameof(RechargeText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string RechargeText
    {
        get => (string)GetValue(RechargeTextProperty);
        private set => SetValue(RechargeTextProperty, value);
    }

    public static readonly DependencyProperty UpkeepTextProperty =
        DependencyProperty.Register(nameof(UpkeepText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string UpkeepText
    {
        get => (string)GetValue(UpkeepTextProperty);
        private set => SetValue(UpkeepTextProperty, value);
    }

    public static readonly DependencyProperty OvercastTextProperty =
        DependencyProperty.Register(nameof(OvercastText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string OvercastText
    {
        get => (string)GetValue(OvercastTextProperty);
        private set => SetValue(OvercastTextProperty, value);
    }

    // ── Durée d'enchantement affichée « Durée d'ench. : modifié (base) s » (ambre) — Lot D ───
    public static readonly DependencyProperty DurationTextProperty =
        DependencyProperty.Register(nameof(DurationText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string DurationText
    {
        get => (string)GetValue(DurationTextProperty);
        private set => SetValue(DurationTextProperty, value);
    }

    public static readonly DependencyProperty DurationVisibilityProperty =
        DependencyProperty.Register(nameof(DurationVisibility), typeof(Visibility), typeof(SkillTooltipControl),
            new PropertyMetadata(Visibility.Collapsed));

    public Visibility DurationVisibility
    {
        get => (Visibility)GetValue(DurationVisibilityProperty);
        private set => SetValue(DurationVisibilityProperty, value);
    }

    // ── Description affichée (résolue ou plage selon le contexte) ─────────────
    public static readonly DependencyProperty DescriptionTextProperty =
        DependencyProperty.Register(nameof(DescriptionText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string DescriptionText
    {
        get => (string)GetValue(DescriptionTextProperty);
        private set => SetValue(DescriptionTextProperty, value);
    }

    // ── Mention de caractéristique (collée à la fin de la description) ────────
    // Rang du perso dans la caractéristique de la compétence. Null = rang inconnu (catalogues,
    // ou attribut d'une profession absente du build) → la mention affiche la plage. Fournie par
    // les contextes qui résolvent AUSSI la description, pour que les deux racontent la même chose.
    public static readonly DependencyProperty AttributeRankProperty =
        DependencyProperty.Register(nameof(AttributeRank), typeof(int?), typeof(SkillTooltipControl),
            new PropertyMetadata(null, OnInputsChanged));

    public int? AttributeRank
    {
        get => (int?)GetValue(AttributeRankProperty);
        set => SetValue(AttributeRankProperty, value);
    }

    public static readonly DependencyProperty AttributeLineProperty =
        DependencyProperty.Register(nameof(AttributeLine), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string AttributeLine
    {
        get => (string)GetValue(AttributeLineProperty);
        private set => SetValue(AttributeLineProperty, value);
    }

    // ── Avertissement de repli FR→EN (cf. UpdateFrWarning) ────────────────────
    public static readonly DependencyProperty FrWarningTextProperty =
        DependencyProperty.Register(nameof(FrWarningText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string FrWarningText
    {
        get => (string)GetValue(FrWarningTextProperty);
        private set => SetValue(FrWarningTextProperty, value);
    }

    public static readonly DependencyProperty FrWarningVisibleProperty =
        DependencyProperty.Register(nameof(FrWarningVisible), typeof(bool), typeof(SkillTooltipControl),
            new PropertyMetadata(false));

    public bool FrWarningVisible
    {
        get => (bool)GetValue(FrWarningVisibleProperty);
        private set => SetValue(FrWarningVisibleProperty, value);
    }

    // ── Footer calculé (dépend de Skill ET PrimaryRank) ──────────────────────
    public static readonly DependencyProperty FooterTextProperty =
        DependencyProperty.Register(nameof(FooterText), typeof(string), typeof(SkillTooltipControl),
            new PropertyMetadata(string.Empty));

    public string FooterText
    {
        get => (string)GetValue(FooterTextProperty);
        private set => SetValue(FooterTextProperty, value);
    }

    public static readonly DependencyProperty FooterVisibleProperty =
        DependencyProperty.Register(nameof(FooterVisible), typeof(bool), typeof(SkillTooltipControl),
            new PropertyMetadata(false));

    public bool FooterVisible
    {
        get => (bool)GetValue(FooterVisibleProperty);
        private set => SetValue(FooterVisibleProperty, value);
    }

    private static void OnInputsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SkillTooltipControl)d).Refresh();

    // Recalcule tout l'affichage de l'infobulle depuis les entrées (Skill + overrides du slot) et la
    // langue courante. Appelé sur tout changement d'entrée ET à chaque ouverture (Loaded).
    private void Refresh()
    {
        UpdateHeader();
        UpdateFooter();
        UpdateDescription();
        UpdateEnergy();
        UpdateCast();
        UpdateRecharge();
        UpdateUpkeep();
        UpdateOvercast();
        UpdateDuration();
        UpdateDamage();
        UpdateSummon();
        UpdateAdrenaline();
    }

    // Coût en adrénaline : coups nécessaires au gain par touche du perso (AdrenalineGain), affiché
    // « modifié (base) » quand un effet actif le change — en couleur rituel (ambre) si un effet du
    // bandeau d'équipe intervient, en couleur de boost de compétence (violet) sinon, même priorité
    // que la ligne d'énergie ; sinon le coût du jeu tel quel (catalogue compris : gain 100 par défaut).
    private void UpdateAdrenaline()
    {
        int cost = Skill?.Adrenaline ?? 0;
        int strikes = AdrenalineGain.StrikesNeeded(cost, AdrenalineGainPerHit, AdrenalineSlowed);
        // Rage of the Ntouka donne des coups d'adrénaline à TOUTES les compétences d'adrénaline (un gain les remplit
        // toutes) : ils se retranchent des coups nécessaires, sans descendre sous 0 (décision Philippe du 16/09/2026).
        if (AdrenalineStrikesGiven > 0) strikes = Math.Max(0, strikes - AdrenalineStrikesGiven);
        if (strikes == cost) { AdrenalineText = cost.ToString(); return; }
        string shown = AdrenalineFromTeam
            ? RitualMark(strikes.ToString())
            : $"{SkillProgression.MarkSkillBoost}{strikes}{SkillProgression.MarkSkillBoost}";
        AdrenalineText = $"{shown} ({cost})";
    }

    // En-tête : nom + ligne de type, résolus dans la langue courante (AppLanguage.IsFr).
    private void UpdateHeader()
    {
        if (Skill is not { } s) { HeaderName = string.Empty; TypeLine = string.Empty; return; }
        HeaderName = s.DisplayName;
        TypeLine = ZCodex.App.Converters.SkillTypeLineConverter.Build(s);
    }

    // ── Bloc « Invocation » (Puissance de l'invocation, attribut primaire Ritualiste) ──────────
    // Affiché SEULEMENT si le perso a du rang (investi, ou forcé à 5 par le mod « of the Ritualist »
    // — les deux portés par AttributeLevel) : tooltip sobre pour les non-Ritualistes (décision
    // Philippe). Contexte build uniquement (DescriptionOverride = niveau/durée résolus au rang).
    // Format « relevé (base) », façon Expertise : la valeur de base reste lisible entre parenthèses.
    private void UpdateSummon()
    {
        if (SummonPanel is null) return;   // DP posée avant la fin d'InitializeComponent
        SummonPanel.Children.Clear();
        SummonPanel.Visibility = Visibility.Collapsed;

        if (Skill is not { } skill || DescriptionOverride is not { } resolved
            || SpawningPower.Analyze(resolved, skill, SpawningPowerRank ?? 0) is not { } s)
            return;

        var title = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, Text = "Invocation" };
        title.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        SummonPanel.Children.Add(title);

        // TextBlock sans Text : SkillMarkup remplit les Inlines (bleu clair = valeur relevée par
        // l'invocation, vert = valeur de base). TextWrapping obligatoire — la ligne du serviteur
        // dépasse sinon le MaxWidth de la racine et WPF la ROGNE au lieu de la replier.
        foreach (var line in SummonLines(s))
        {
            var tb = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
            tb.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
            SkillMarkup.SetText(tb, line);
            SummonPanel.Children.Add(tb);
        }

        SummonPanel.Visibility = Visibility.Visible;
    }

    // Valeur de base / non modifiée par l'invocation → vert (comme les valeurs résolues de la
    // description) ; valeur RELEVÉE par l'invocation → bleu clair.
    private static string Green(int v) => $"{SkillProgression.Mark}{v}{SkillProgression.Mark}";
    private static string Blue(int v)  => $"{SkillProgression.MarkSummon}{v}{SkillProgression.MarkSummon}";

    // « relevé (base) » : le relevé en bleu clair, la base en vert entre parenthèses. Base seule
    // (en vert) si l'invocation ne change rien — à bas rang l'entier peut ne pas bouger.
    private static string Raised(int baseValue, int boosted) =>
        boosted > baseValue ? $"{Blue(boosted)} ({Green(baseValue)})" : Green(baseValue);

    // Libellé bilingue baké dans le tooltip (les lignes sont construites en chaîne).
    private static string L(string fr, string en) => ZCodex.Core.Models.AppLanguage.IsFr ? fr : en;

    private static IEnumerable<string> SummonLines(SpawningPower.Summon s)
    {
        switch (s.Type)
        {
            case SpawningPower.Kind.WeaponSpell:
                yield return $"{L("Durée", "Duration")} : {Raised(s.Base, s.Boosted)} s";
                break;

            case SpawningPower.Kind.Spirit:
                yield return $"{L("Esprit niv.", "Spirit lvl.")} {Green(s.Level)} · {L("PV max", "max Health")} {Raised(s.Base, s.Boosted)}"
                           + (s.Armor is { } a ? $" · {L("Armure", "Armor")} {Green(a)}" : string.Empty);
                break;

            case SpawningPower.Kind.Minion:
                // Deux lignes : « niv. N (nom) · PV max … · Armure … » sur une seule dépasse la
                // largeur de l'infobulle et se replierait salement au milieu des PV.
                var fr = s.Creature is { } c ? $" ({SpawningPower.CreatureLabel(c)})" : string.Empty;
                yield return $"{L("Serviteur niv.", "Minion lvl.")} {Green(s.Level)}{fr}";
                yield return $"{L("PV max", "max Health")} {Raised(s.Base, s.Boosted)}"
                           + (s.Armor is { } ar ? $" · {L("Armure", "Armor")} {Green(ar)}" : string.Empty);
                // Un serviteur n'a pas de durée annoncée : il meurt par décomposition. L'invocation
                // l'allonge donc indirectement, via les PV — d'où l'estimation.
                if (s.LifespanBase is { } lb && s.LifespanBoosted is { } lx)
                    yield return L("Durée de vie estimée : ", "Estimated lifespan: ")
                               + (lx > lb ? $"~{Blue(lx)} s ({Green(lb)} s)" : $"~{Green(lb)} s");
                break;
        }
    }

    private static readonly IReadOnlySet<Ritual> EmptyRituals = new HashSet<Ritual>();

    // Valeur d'infobulle « modifiée (base) », modifiée en couleur rituel, sinon telle quelle.
    private static string RitualMark(string value) =>
        $"{SkillProgression.MarkRitual}{value}{SkillProgression.MarkRitual}";

    // Coût en énergie affiché « modifié (base) ». Toute la cascade (Expertise, flux, rituels de la
    // nature ET réductions des compétences du perso, lot 2) est calculée par NatureRitualData.EnergyCost —
    // qui reproduit EXACTEMENT l'ancien comportement quand rien n'est actif. La part modifiée est marquée en
    // couleur rituel si un effet du bandeau (rituel, Energizing Chorus) l'a changée, sinon en couleur de boost
    // de compétence (violet) si une compétence du perso l'a changée, sinon en couleur flux (comportement historique).
    private void UpdateEnergy()
    {
        int baseCost = Skill?.EnergyCost ?? 0;
        if (Skill is not { } s)
        {
            EnergyText = baseCost.ToString();
            EnergyVisibility = baseCost > 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var rituals = NatureRituals ?? EmptyRituals;
        // Un signet 0-énergie sous Primal Echoes, ou toute skill sous Quicksand/Roaring Winds, peut
        // avoir un coût non nul même si sa base est 0 → ne pas court-circuiter dans ces cas.
        bool mayRaiseFromZero =
            (rituals.Contains(Ritual.PrimalEchoes) && NatureRitualData.IsSignet(s))
            || rituals.Contains(Ritual.Quicksand)
            || (rituals.Contains(Ritual.RoaringWinds) && NatureRitualData.IsChantOrShout(s));
        if (baseCost <= 0 && !mayRaiseFromZero)
        {
            EnergyText = baseCost.ToString();
            EnergyVisibility = Visibility.Collapsed;   // base 0, aucun rituel n'ajoute de coût
            return;
        }

        var r = NatureRitualData.EnergyCost(baseCost, s, rituals, ExpertiseRank ?? 0, FluxEnergyPercent, RoaringWindsBonus,
                                            EnergyReduction, EnergizingChorusReduction);
        // Visible dès que le coût effectif est non nul (couvre les bases 0 relevées par un rituel), ou que la
        // base l'est : un coût ramené à 0 par une compétence (Way of the Empty Palm) s'affiche « 0 (5) ».
        EnergyVisibility = r.Final > 0 || baseCost > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (r.Final == baseCost) { EnergyText = baseCost.ToString(); return; }

        string shown = r.RitualChanged ? RitualMark(r.Final.ToString())
                     : r.SkillChanged  ? $"{SkillProgression.MarkSkillBoost}{r.Final}{SkillProgression.MarkSkillBoost}"
                     : r.FluxLowered   ? $"{SkillProgression.MarkFlux}{r.Final}{SkillProgression.MarkFlux}"
                     :                    r.Final.ToString();
        EnergyText = $"{shown} ({baseCost})";
    }

    // Temps d'activation : toute la composition (Jaundiced Gaze, compétences du perso, flux Jack of All Trades, Nature's
    // Renewal, plafond de 50 %, incantation instantanée) est calculée par NatureRitualData.CastTime — identique à l'ancien
    // calcul quand aucune compétence n'agit. Couleur : rituel (ambre) si Nature's Renewal l'a changé, sinon boost de
    // compétence (violet) si une compétence du perso l'a changé (lot 3), sinon flux.
    private void UpdateCast()
    {
        float baseCast = Skill?.CastTime ?? 0f;
        if (baseCast <= 0f || Skill is not { } s) { CastText = baseCast > 0f ? baseCast.ToString("0.##") : string.Empty; return; }

        var r = NatureRitualData.CastTime(baseCast, s, NatureRituals ?? EmptyRituals, NaturesRenewalCastPct, FluxCastPercent,
                                          SkillSpeed, FastCastingRank, TeamSpeed);
        if (Math.Abs(r.Final - baseCast) < 0.001f) { CastText = baseCast.ToString("0.##"); return; }

        // Fast Casting ne colore rien (caractéristique toujours active, comme l'Expertise sur l'énergie) : si rien d'autre
        // n'a joué, la valeur s'affiche sans marque.
        string shown = r.RitualChanged ? RitualMark(r.Final.ToString("0.##"))
                     : r.SkillChanged  ? $"{SkillProgression.MarkSkillBoost}{r.Final.ToString("0.##")}{SkillProgression.MarkSkillBoost}"
                     : r.FluxChanged   ? $"{SkillProgression.MarkFlux}{r.Final.ToString("0.##")}{SkillProgression.MarkFlux}"
                     :                    r.Final.ToString("0.##");
        CastText = $"{shown} ({baseCast.ToString("0.##")})";
    }

    // Recharge : Quickening Zephyr ×0,5, Energizing Wind ×1,25 et compétences du perso (lot 3), « modifiée (base) » — ambre
    // si un rituel l'a changée, violet si seule une compétence du perso. Ligne visible dès que la recharge effective est non
    // nulle (Rage of the Ntouka sur une compétence d'adrénaline sans recharge : « 3 (0) »).
    private void UpdateRecharge()
    {
        float baseR = Skill?.Recharge ?? 0f;
        var speed = SkillSpeed;
        var r = NatureRitualData.Recharge(baseR, Skill, NatureRituals ?? EmptyRituals, speed, FastCastingRank, TeamSpeed);
        RechargeVisibility = r.Final > 0f || baseR > 0f || speed.RechargeBlock > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (RechargeVisibility != Visibility.Visible) { RechargeText = string.Empty; return; }
        if (speed.RechargeBlock == 0 && Math.Abs(r.Final - baseR) < 0.001f) { RechargeText = baseR.ToString("0.##"); return; }

        // Deadly Paradox bloque TOUTES les attaques pendant 10 s : « +10, recharge » (format demandé par Philippe le
        // 16/09/2026 — Critical Chop « +10, 15 (15) », Black Lotus Strike « +10, 4 (6) »). Ces secondes ne sont réduites
        // par rien, et s'affichent que la recharge bouge ou non.
        string value = speed.RechargeBlock > 0
            ? $"+{speed.RechargeBlock}, {r.Final.ToString("0.##")}"
            : r.Final.ToString("0.##");
        // Fast Casting seul ne colore rien (caractéristique toujours active, patron de l'Expertise sur l'énergie).
        string shown = r.RitualChanged ? RitualMark(value)
                     : r.SkillChanged  ? $"{SkillProgression.MarkSkillBoost}{value}{SkillProgression.MarkSkillBoost}"
                     :                    value;
        RechargeText = $"{shown} ({baseR.ToString("0.##")})";
    }

    // Upkeep (entretien) : Nature's Renewal double celui des enchantements. Affiché « -N » (pip).
    private void UpdateUpkeep()
    {
        int baseUp = Skill?.Upkeep ?? 0;
        if (baseUp <= 0 || Skill is not { } s) { UpkeepText = string.Empty; return; }
        int newUp = NatureRitualData.Upkeep(baseUp, s, NatureRituals ?? EmptyRituals);
        UpkeepText = newUp == baseUp ? $"-{baseUp}" : $"{RitualMark($"-{newUp}")} (-{baseUp})";
    }

    // Overcast : Equinox ajoute 10 aux sorts à overcast (couleur rituel) ; Glyph of Energy (lot 2) l'annule,
    // celui d'Equinox compris (couleur de boost de compétence). « modifié (base) ».
    private void UpdateOvercast()
    {
        int baseOc = Skill?.Overcast ?? 0;
        if (baseOc <= 0) { OvercastText = string.Empty; return; }
        bool removed = EnergyReduction.RemovesOvercast;
        int newOc = NatureRitualData.Overcast(baseOc, NatureRituals ?? EmptyRituals, removed);
        OvercastText = newOc == baseOc ? baseOc.ToString()
                     : removed ? $"{SkillProgression.MarkSkillBoost}{newOc}{SkillProgression.MarkSkillBoost} ({baseOc})"
                     : $"{RitualMark(newOc.ToString())} ({baseOc})";
    }

    // Durées effectives — chaque ligne NOMME la durée touchée (décision Philippe du 16/09/2026 : « on spécifiera
    // à chaque fois la durée affectée »). Affichées SEULEMENT en contexte build (description résolue) et quand un
    // effet actif change vraiment le chiffre ; le résultat seul, sans la base, comme la maquette de Philippe.
    //  • Durée PROPRE, une ligne au plus, nommée par la famille de la compétence :
    //     – enchantements (Lot D, inchangé) : arme « of Enchanting », prolongateur personnel Blessed Aura/Extend,
    //       Tranquility, composés par EnchantmentDuration.Compose — couleur rituel (ambre) d'origine ;
    //     – poses, préparations, maléfices, cris et chants (lot 4a) : rallonge des allongeurs allumés sur la carte
    //       du perso — couleur de boost de compétence (violet), la source étant personnelle.
    //  • Durées de CONDITIONS (lot 4b), une ligne par condition : celles que la compétence inflige et qu'un
    //    allongeur modifie, plus celles qu'un effet actif AJOUTE à ses attaques (Apply Poison…) — même violet.
    private void UpdateDuration()
    {
        DurationText = string.Empty;
        DurationVisibility = Visibility.Collapsed;
        if (Skill is not { } s || DescriptionOverride is not { } resolved) return;   // catalogue → rien

        var lines = new List<string>();
        if (OwnDurationLine(s, resolved) is { } own) lines.Add(own);
        foreach (var line in ConditionDurationData.Lines(s, resolved, ConditionDurations))
            lines.Add($"{ConditionDurationLabel(line.Condition)} : "
                      + $"{SkillProgression.MarkSkillBoost}{line.Seconds}{SkillProgression.MarkSkillBoost} s");

        if (lines.Count == 0) return;
        DurationText = string.Join("\n", lines);
        DurationVisibility = Visibility.Visible;
    }

    // Ligne de durée PROPRE de la compétence, null si rien ne la change.
    private string? OwnDurationLine(Skill s, string resolved)
    {
        var family = SkillDurationBoostData.FamilyOf(s);
        if (family == DurationFamily.Enchantment)
        {
            if (EnchantmentDuration.Seconds(resolved) is not { } baseSec) return null;  // maintenu / sans durée
            var r = EnchantmentDuration.Compose(baseSec, EnchantEnchantingPct, EnchantExtenderPct, EnchantTranquilityPct);
            return !r.Changed ? null                                                    // aucun modificateur actif
                : $"{L("Durée d'enchantement effective", "Effective enchantment duration")} : {RitualMark(r.Final.ToString())} s";
        }

        if (family == DurationFamily.None || DurationBoostPct <= 0) return null;
        if (SkillDurationBoostData.OwnSeconds(resolved) is not { } own) return null;     // aucune durée propre annoncée
        int final = SkillDurationBoostData.Extend(own, DurationBoostPct);
        if (final == own) return null;                                                   // rallonge trop faible pour bouger l'entier
        return $"{DurationFamilyLabel(family)} : "
             + $"{SkillProgression.MarkSkillBoost}{final}{SkillProgression.MarkSkillBoost} s";
    }

    // « Durée de poison effective », « Durée d'aveuglement effective »… Le nom de la condition vient de
    // GwConditionData (FR validés par Philippe) ; en français il passe en minuscule et « de » s'élide.
    private static string ConditionDurationLabel(string condition)
    {
        string name = GwConditionData.DisplayName(condition);
        if (!AppLanguage.IsFr) return $"Effective {name} duration";
        name = char.ToLowerInvariant(name[0]) + name[1..];
        string de = "aeiouyàâéèêîïôûù".Contains(name[0]) ? "d'" : "de ";
        return $"Durée {de}{name} effective";
    }

    // Libellé qui NOMME la durée touchée. Mots du client français du jeu (« pose », « préparation », « maléfice »,
    // « cri », « chant »), cf. [[reference_fr_client_fait_foi]].
    private static string DurationFamilyLabel(DurationFamily family) => family switch
    {
        DurationFamily.Stance      => L("Durée de pose effective",        "Effective stance duration"),
        DurationFamily.Preparation => L("Durée de préparation effective", "Effective preparation duration"),
        DurationFamily.Hex         => L("Durée de maléfice effective",    "Effective hex duration"),
        DurationFamily.Shout       => L("Durée de cri effective",         "Effective shout duration"),
        DurationFamily.Chant       => L("Durée de chant effective",       "Effective chant duration"),
        _                          => string.Empty,
    };

    private void UpdateFooter()
    {
        // Footer affiché uniquement en contexte build (FooterOverride fourni par le perso).
        // Catalogue (FooterOverride null) → aucun footer.
        FooterText = FooterOverride ?? string.Empty;
        FooterVisible = !string.IsNullOrEmpty(FooterText);
    }

    private void UpdateDescription()
    {
        // Build : description fournie par le perso (variables résolues), en FR si disponible
        // (DisplayDescriptionOverride) sinon EN. Catalogue : corps concis dans la langue
        // affichée (DisplayDescriptionBody), variables laissées en plage.
        DescriptionText = DisplayDescriptionOverride
            ?? DescriptionOverride
            ?? (Skill is { } s ? s.DisplayDescriptionBody : string.Empty);

        UpdateAttributeLine();
        UpdateFrWarning();
    }

    // Mention « (Caract. : 0 Force) » collée à la fin de la description, en gris clair.
    // Le rang vient du contexte : valeur du perso si fournie (AttributeRank), sinon la PLAGE de
    // l'échelle — 0...12...15 pour une caractéristique, 0...10 pour un rang de titre (les
    // compétences PvE progressent sur 11 rangs, pas sur l'échelle d'attribut).
    // Libellés FR = ceux des filtres du catalogue (GwAttributeData) et NON Skill.AttributeFr,
    // scrapé du wiki FR : ce dernier est vide sur ~la moitié des compétences et carrément faux sur
    // d'autres (des « No Attribute » étiquetées « Magie de domination », Mysticisme → « Maîtrise
    // de la faux »). Décision Philippe 12/08/2026.
    private void UpdateAttributeLine()
    {
        if (Skill is not { } s) { AttributeLine = string.Empty; return; }

        string label = L("Caract. : ", "Attribute: ");
        if (GwAttributeData.IsNoAttribute(s.Attribute))
        {
            AttributeLine = $"({label}{L("Aucune", "None")})";
            return;
        }

        string rank = AttributeRank?.ToString()
            ?? (GwAttributeData.IsTitleRank(s.Attribute) ? "0...10" : "0...12...15");
        AttributeLine = $"({label}{rank} {GwAttributeData.DisplayName(s.Attribute)})";
    }

    // Avertit quand le texte affiché n'est pas le français attendu : sans ce repère, l'utilisateur
    // en mode FR croit à un bug d'affichage. Deux motifs distincts (cf. Skill.DescriptionFallback).
    private void UpdateFrWarning()
    {
        FrWarningText = Skill?.DescriptionFallback switch
        {
            ZCodex.Core.Models.Skill.FrFallback.NoFrenchPage =>
                "⚠ Pas de page française : description affichée en anglais.",
            ZCodex.Core.Models.Skill.FrFallback.StaleRanges =>
                "⚠ Plages de valeurs françaises douteuses (page FR en retard sur le jeu) : "
                + "description affichée en anglais.",
            ZCodex.Core.Models.Skill.FrFallback.StaleText =>
                "⚠ Description française incomplète ou périmée : description affichée en anglais.",
            _ => string.Empty,
        };
        FrWarningVisible = FrWarningText.Length > 0;
    }

    // ── Dégâts selon l'armure ─────────────────────────────────────────────────

    // Largeur de tooltip par défaut, et largeur occupée par la colonne icône (66 + marge 8)
    // à ajouter à celle de la table de dégâts quand elle impose d'élargir la racine.
    private const double BaseMaxWidth = 380;
    private const double IconColumnWidth = 74;

    // Section construite en code : le nombre de colonnes d'AL dépend du niveau custom.
    // Contexte build uniquement (DescriptionOverride non null = valeurs résolues au rang).
    private void UpdateDamage()
    {
        if (DamagePanel is null) return;   // DP posée avant la fin d'InitializeComponent
        DamagePanel.Children.Clear();
        DamagePanel.Visibility = Visibility.Collapsed;
        LayoutRoot.MaxWidth = BaseMaxWidth;
        if (!ArmorDamageDisplay.Enabled || Skill is not { } skill || DescriptionOverride is not { } resolved)
            return;

        var analysis = SkillDamage.Analyze(resolved, skill.Name);
        // Kind == Damage seulement : vol/perte de vie (utilitaire Spike) restent hors tooltip
        // (décision chantier 10 : ni vol de vie ni perte de vie affichés ici).
        var damage = analysis.Rows.Where(r => r.Kind == SkillDamage.RowKind.Damage).ToList();
        var respecting = damage.Where(r => !r.IgnoresArmor).ToList();
        var ignoring   = damage.Where(r => r.IgnoresArmor).ToList();

        // Table d'arme (total/critique, arme customisée) pour TOUTE attaque d'arme mappée, avec
        // le 1er bonus « +X » absorbé. Exceptions Q7 (décisions Philippe, via ModsFor) : « no
        // damage », dégât fixe remplaçant l'arme (Power Shot…) ou vol de vie pur → pas de table
        // (les lignes « X — ignore l'armure » hors bonus restent : paquets conditionnels de
        // Swift Chop « if blocked », remplacements fixes…) ; « less/of normal damage » →
        // multiplié ; « always a critical hit » → ligne critique seule. Sur toute attaque
        // d'arme, AUCUNE ligne « +X — ignore l'armure » (bonus lisible dans l'en-tête et le
        // total) ; Pet Attack garde la sienne (pas une arme).
        // Choix manuel de la fenêtre Spike prioritaire (il porte déjà la règle « arme libre »),
        // sinon l'arme du type d'attaque.
        var weapon = WeaponStrike.ByMasteryName(WeaponMastery) ?? WeaponStrike.For(skill);
        var mods = WeaponStrike.ModsFor(skill.Name, resolved);
        int bonus = ignoring.FirstOrDefault(r => r.IsBonus)?.Value ?? 0;
        bool weaponTable = weapon is not null && WeaponMasteryRank is not null && !mods.NoWeaponDamage;
        if (WeaponStrike.IsWeaponAttack(skill))
            ignoring.RemoveAll(r => r.IsBonus);
        if (!weaponTable && respecting.Count == 0 && ignoring.Count == 0) return;

        // Pénétration naturelle (rang de Force, non null seulement pour une attaque d'arme) :
        // en MAX avec celle de la description, jamais cumulée — wiki/Strength : « to attack
        // skills that don't already have a higher amount of armor penetration ».
        int penetration = Math.Max(analysis.ArmorPenetration, StrengthRank ?? 0);

        // Flux Jack of All Trades : +15 % sur TOUS les dégâts affichés (décision Philippe : nombres
        // recalculés en couleur normale, boost signalé par une note colorée dans le titre).
        int fluxDmg = FluxDamagePercent;

        string titleText = L("Dégâts selon l'armure", "Damage by armor");
        if (weaponTable)
        {
            titleText += $" — {weapon!.DisplayName} {weapon.Min}–{weapon.Max}";
            if (bonus > 0) titleText += $" +{bonus}";
        }
        if (penetration > 0 && (weaponTable || respecting.Count > 0))
            titleText += $" — {L("pénétration", "penetration")} {penetration}%";
        if (fluxDmg > 0)
            titleText += $" · {SkillProgression.MarkFlux}+{fluxDmg} % Flux{SkillProgression.MarkFlux}";
        // TextBlock sans Text (SkillMarkup remplit les Inlines) → colore la note « +15 % Flux »,
        // le reste du titre restant en run simple (couleur TextSecondaryBrush héritée).
        var title = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold };
        title.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        SkillMarkup.SetText(title, titleText);
        DamagePanel.Children.Add(title);

        int level = ArmorDamageDisplay.CharacterLevel;
        int targetLevel = ArmorDamageDisplay.TargetLevel;
        if (weaponTable || respecting.Count > 0)
        {
            var damageGrid = BuildDamageGrid(respecting, penetration,
                weaponTable ? weapon : null, WeaponMasteryRank ?? 0, bonus, mods, level, targetLevel,
                CriticalStrikesRank ?? 0, fluxDmg);
            // Au-delà de ~7 colonnes d'AL la table déborde du MaxWidth de 380 et WPF rogne les
            // colonnes de droite : on élargit la racine à la largeur mesurée de la table.
            damageGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            LayoutRoot.MaxWidth = Math.Max(BaseMaxWidth,
                damageGrid.DesiredSize.Width + IconColumnWidth);
            DamagePanel.Children.Add(damageGrid);
        }

        foreach (var row in ignoring)
            DamagePanel.Children.Add(MakeText($"{IgnoringLine(row, fluxDmg)} {L("— ignore l'armure", "— ignores armor")}", 11, "TextSecondaryBrush"));

        // Le niveau de la cible n'apparaît que sur les tables d'arme (il ne sert qu'au taux de
        // critique) ; ses dégâts subis ne dépendent que de son AL (colonnes).
        var legend = MakeText(weaponTable
            ? L($"Théorique — personnage niveau {level}, cible niveau {targetLevel}, arme customisée (+20 %).",
                $"Theoretical — character level {level}, target level {targetLevel}, customized weapon (+20%).")
            : L($"Théorique — personnage niveau {level}, hors dégâts d'arme.",
                $"Theoretical — character level {level}, excluding weapon damage."), 9, "TextFaintBrush");
        legend.FontStyle = FontStyles.Italic;
        legend.Margin = new Thickness(0, 2, 0, 0);
        DamagePanel.Children.Add(legend);

        DamagePanel.Visibility = Visibility.Visible;
    }

    // Table : ligne d'en-tête AL 60/80/100/120 (+ custom « * » insérée à sa place), puis
    // pour une attaque d'arme les lignes « total » (min–max + bonus) et « critique »
    // (max ×√2, faux ×1.09, + bonus, taux d'Izzy contre le niveau de cible custom), puis une
    // ligne par dégât armor-respecting détecté. Valeurs tronquées à l'entier.
    // Boost de flux (+pct %) sur un dégât entier, tronqué (v + v×pct/100) — même arithmétique que
    // le Lot B (SpikeViewModel.Pct). pct = 0 → valeur inchangée.
    private static int Boost(int v, int pct) => pct > 0 ? v + v * pct / 100 : v;

    private static Grid BuildDamageGrid(List<SkillDamage.Row> rows, int penetration,
        WeaponStrike.Weapon? weapon, int masteryRank, int bonus, WeaponStrike.AttackMods mods,
        int level, int targetLevel, int criticalStrikesRank, int fluxDamagePercent)
    {
        var columns = ArmorDamageDisplay.Columns();
        int weaponRows = weapon is null ? 0 : mods.AlwaysCritical ? 1 : 2;
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int c = 0; c < columns.Count; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 34 });
        for (int r = 0; r <= weaponRows + rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition());

        AddCell(grid, 0, 0, "AL", "TextFaintBrush", bold: true);
        for (int c = 0; c < columns.Count; c++)
            AddCell(grid, 0, c + 1, columns[c].IsCustom ? $"{columns[c].Al}*" : columns[c].Al.ToString(),
                    "TextFaintBrush", bold: true);

        if (weapon is not null)
        {
            int critRow = mods.AlwaysCritical ? 1 : 2;
            if (!mods.AlwaysCritical) AddCell(grid, 1, 0, L("total", "total"), "TextSecondaryBrush");
            // Taux de critique d'Izzy contre le niveau de cible custom (+ Critical Strikes du
            // perso) — pas pour « always a critical hit » (Keen Chop) où il est forcé par la skill.
            AddCell(grid, critRow, 0, mods.AlwaysCritical ? L("critique", "critical")
                : $"{L("critique", "critical")} ({100 * WeaponStrike.CriticalChance(masteryRank, level, targetLevel, criticalStrikesRank):0}%)",
                "TextSecondaryBrush");
            for (int c = 0; c < columns.Count; c++)
            {
                if (!mods.AlwaysCritical)
                {
                    int min = Boost(WeaponStrike.DamageAt(weapon.Min, masteryRank, columns[c].Al, penetration, mods.Multiplier, level) + bonus, fluxDamagePercent);
                    int max = Boost(WeaponStrike.DamageAt(weapon.Max, masteryRank, columns[c].Al, penetration, mods.Multiplier, level) + bonus, fluxDamagePercent);
                    AddCell(grid, 1, c + 1, $"{min}–{max}", "TextPrimaryBrush");
                }
                AddCell(grid, critRow, c + 1,
                    Boost(WeaponStrike.CriticalAt(weapon, masteryRank, columns[c].Al, penetration, mods.Multiplier, level) + bonus, fluxDamagePercent).ToString(),
                    "TextPrimaryBrush");
            }
        }

        for (int r = 0; r < rows.Count; r++)
        {
            AddCell(grid, weaponRows + r + 1, 0, RowLabel(rows[r]), "TextSecondaryBrush");
            for (int c = 0; c < columns.Count; c++)
                AddCell(grid, weaponRows + r + 1, c + 1,
                        Boost(SkillDamage.DamageAt(rows[r].Value, columns[c].Al, penetration, level), fluxDamagePercent).ToString(),
                        "TextPrimaryBrush");
        }
        return grid;
    }

    private static void AddCell(Grid grid, int row, int col, string text, string brushKey, bool bold = false)
    {
        var tb = MakeText(text, 11, brushKey);
        if (bold) tb.FontWeight = FontWeights.Bold;
        if (col > 0)
        {
            tb.TextAlignment = TextAlignment.Right;
            tb.Margin = new Thickness(6, 0, 0, 0);
        }
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private static string? FrType(string? damageType)
        => damageType is { } t ? SkillDamage.DisplayType(t) : null;

    // Libellé de ligne de la table (armor-respecting, donc toujours typé sauf exception).
    private static string RowLabel(SkillDamage.Row row) => SkillDamage.DisplayType(row.DamageType);

    // Ligne armor-ignoring : « 46 », « +34 » (bonus d'attaque), « 49 (sacré) ». Boostée du +pct %
    // de flux (Jack of All Trades) comme tous les dégâts affichés.
    private static string IgnoringLine(SkillDamage.Row row, int fluxDamagePercent)
    {
        int value = Boost(row.Value, fluxDamagePercent);
        var val = row.IsBonus ? $"+{value}" : value.ToString();
        return FrType(row.DamageType) is { } type ? $"{val} ({type})" : val;
    }

    private static TextBlock MakeText(string text, double size, string brushKey)
    {
        var tb = new TextBlock { Text = text, FontSize = size };
        tb.SetResourceReference(ForegroundProperty, brushKey);
        return tb;
    }
}
