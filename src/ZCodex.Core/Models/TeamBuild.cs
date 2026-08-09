namespace ZCodex.Core.Models;

public class TeamBuild
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Z-Codex Template";
    public List<CharacterBuild> Characters { get; set; } = new();
    public List<VariantLock> Locks { get; set; } = new(); // v2 — tuples de variantes (cf. VariantLock)
    public List<SpikeMember> Spike { get; set; } = new(); // v6 — roster du « Spike damage calculus »
    public Flux? ActiveFlux { get; set; } // v7 — flux simulé sur ce build (null = aucun / grisé)
    public List<int> ActiveNatureRituals { get; set; } = new(); // v13 — SkillId des rituels de la nature actifs (environnement de simulation)
    public int RoaringWindsRitualRank { get; set; } = 12; // v14 — rang de simulation de Roaring Winds (utilisé quand non équipé)
    public int TranquilityRitualRank { get; set; } = 12; // v15 — rang de simulation de Tranquility (durée d'enchantement, utilisé quand non équipé)
    // v18 — nombre d'attaques vampiriques comptées dans le spike, par valeur de vol de vie (3 = armes
    // à une main, 5 = à deux mains). GLOBAL et non par perso : le découpage donnerait jusqu'à 16
    // lignes pour un total identique. 0 = ligne affichée mais non comptée. Absent (≤ v17) → 1.
    public int VampiricHits3 { get; set; } = 1;
    public int VampiricHits5 { get; set; } = 1;
    // v17 — mode de jeu au moment de l'enregistrement, restauré à la réouverture. Null = fichier
    // antérieur à v17 (ou format hérité .pwnd/.txt) : le mode courant s'applique alors, comme avant.
    public GameMode? GameMode { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
