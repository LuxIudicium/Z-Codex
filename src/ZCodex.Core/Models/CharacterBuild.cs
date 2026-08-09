namespace ZCodex.Core.Models;

public class CharacterBuild
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "(unnamed)";
    public Profession PrimaryProfession { get; set; }
    public Profession SecondaryProfession { get; set; }
    public bool IsFavorite { get; set; } = false;
    public string Assignment { get; set; } = "(unassigned)";

    // Genre — pilote la variante de skin d'armure H/F (cf. Gender). Cosmétique, hors template GW1.
    public Gender Gender { get; set; } = Gender.Male;
    public Dictionary<string, int> Attributes { get; set; } = new();

    // Rangs de titre PvE (simulation Z-Codex, cf. AttributesBuild.TitleRanks) — distinct
    // d'Attributes : jamais lu/écrit par GwTemplateCodec (le format de template GW1 réel n'a
    // pas de notion de rang de titre).
    public Dictionary<string, int> TitleRanks { get; set; } = new();

    public Skill?[] Skills { get; set; } = new Skill?[8];
    public EquipmentBuild? Equipment { get; set; }
    public string Notes { get; set; } = string.Empty;

    // v15 — toggle « prolongateurs de durée d'enchantement » (Blessed Aura / Extend Enchantments)
    // de CE perso : quand actif, ses enchantements Monk/Derviche voient leur durée rallongée dans
    // l'infobulle. Simulation Z-Codex, hors template GW1.
    public bool DurationBoostersEnabled { get; set; } = false;

    // v16 — SkillId des boosts d'attribut de compétences équipées ACTIFS sur CE perso (Aura of the
    // Lich, Awaken the Blood...). Toggle par perso, simulation Z-Codex (la durée réelle est ignorée).
    public List<int> ActiveAttributeBoosts { get; set; } = new();

    // Variantes = copies totales et indépendantes de ce personnage à leur création (arbre récursif).
    // L'appartenance à cette liste définit le lien parent→enfant ; aucun lien vivant sur les données.
    public List<CharacterBuild> Variants { get; set; } = new();
}
