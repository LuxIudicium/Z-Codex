namespace ZCodex.Core.Models;

public class Skill
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Profession Profession { get; set; }
    public string Attribute { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EnergyCost { get; set; }
    public int Adrenaline { get; set; }
    // % de vie max sacrifiée (ex : 10 = "10% Health sacrifice"). Distinct de l'énergie et de l'upkeep.
    public int Sacrifice { get; set; }
    // Énergie additionnelle payée une fois si l'attribut lié est overcast (ex : rituels du Ritualiste).
    public int Overcast { get; set; }
    // Dégénération d'énergie continue tant que la compétence est maintenue (ex : 1 = "-1 pip"). Distinct du coût ponctuel.
    public int Upkeep { get; set; }
    public float CastTime { get; set; }
    public float Recharge { get; set; }
    public string SkillType { get; set; } = string.Empty;
    public string Campaign { get; set; } = string.Empty;
    // Icône native 64px (in-game) — affichage UI, net en 1:1.
    public string IconPath { get; set; } = string.Empty;
    // Icône HD 248px ("(large)") — réserve pour affichage agrandi. "" si pas de HD.
    public string IconPathHd { get; set; } = string.Empty;
    public string WikiUrl { get; set; } = string.Empty;

    // Table de progression scrapée du wiki : variables × rangs (Progression[v][rank]).
    // Null = non scrapée / pas de progression → les plages de la description restent en plage.
    public string[][]? Progression { get; set; }

    // Conditions que la compétence peut infliger (pages wiki « Skills that cause X »).
    // Entrée "X" = infligée à la cible ; "X:self" = subie par le lanceur (infligeable
    // seulement via une compétence de transfert, cf. GwConditionData). Vide = aucune.
    public string[] Conditions { get; set; } = [];

    // ── Localisation française (gwiki.fr, jointure par ID officiel GW1). Vides = pas
    //    de page FR trouvée → l'affichage retombe champ par champ sur l'anglais. ──
    public string NameFr { get; set; } = string.Empty;
    // Description concise FR (desc_concise de l'infobox gwiki) : corps seul (pas de phrase
    // de type), plages "a...c" aux ancres rangs 0/15 (cf. SkillProgression frAnchors).
    public string DescriptionFr { get; set; } = string.Empty;
    public string AttributeFr { get; set; } = string.Empty;
    public string TypeFr { get; set; } = string.Empty;
    // Page FR suspecte de retard sur le jeu (stat d'infobox ≠ DB, ou plage non appariée à
    // la progression) → la DESCRIPTION affichée retombe sur l'anglais ; le nom FR reste.
    public bool FrSuspect { get; set; }

    // ── Affichage selon la langue courante (AppLanguage.IsFr). Jamais utilisés par le
    //    moteur : les calculs et matchings lisent Name/Description/Attribute/SkillType. ──
    public string DisplayName => AppLanguage.IsFr && NameFr.Length > 0 ? NameFr : Name;
    public string DisplayType => AppLanguage.IsFr && TypeFr.Length > 0 ? TypeFr : SkillType;
    // Corps concis affichable : FR tel quel (déjà sans phrase de type), sinon EN concis.
    public string DisplayDescriptionBody =>
        AppLanguage.IsFr && DescriptionFr.Length > 0 && !FrSuspect && !Data.FrStaleDescriptions.IsStale(Id)
            ? DescriptionFr
            : Data.SkillText.ConciseBody(Description, SkillType);

    public bool IsElite => Description.StartsWith("Elite ", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pourquoi la description affichée n'est pas en français, en mode FR.
    /// <see cref="FrFallback.None"/> = description bien affichée en FR (ou mode anglais).</summary>
    public enum FrFallback { None, NoFrenchPage, StaleRanges, StaleText }

    /// <summary>Motif du repli sur l'anglais — sert à avertir l'utilisateur dans l'infobulle
    /// (sinon il croit à un bug d'affichage). Aligné sur <see cref="DisplayDescriptionBody"/>.</summary>
    public FrFallback DescriptionFallback =>
        !AppLanguage.IsFr                     ? FrFallback.None
        : DescriptionFr.Length == 0           ? FrFallback.NoFrenchPage
        : FrSuspect                           ? FrFallback.StaleRanges
        : Data.FrStaleDescriptions.IsStale(Id) ? FrFallback.StaleText
                                               : FrFallback.None;
}
