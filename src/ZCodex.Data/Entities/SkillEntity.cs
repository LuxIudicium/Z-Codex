using System.ComponentModel.DataAnnotations;

namespace ZCodex.Data.Entities;

public class SkillEntity
{
    [Key]
    public int Id { get; set; }
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    public int ProfessionId { get; set; }
    [MaxLength(100)]
    public string Attribute { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EnergyCost { get; set; }
    public int Adrenaline { get; set; }
    public int Sacrifice { get; set; }
    public int Overcast { get; set; }
    public int Upkeep { get; set; }
    public float CastTime { get; set; }
    public float Recharge { get; set; }
    [MaxLength(50)]
    public string SkillType { get; set; } = string.Empty;
    [MaxLength(50)]
    public string Campaign { get; set; } = string.Empty;
    // Icône native 64px (in-game) — utilisée pour l'affichage UI (petit = net en 1:1).
    [MaxLength(500)]
    public string IconUrl { get; set; } = string.Empty;
    // Icône HD 248px ("(large)") — réserve pour un affichage agrandi futur. "" si pas de HD.
    [MaxLength(500)]
    public string IconUrlHd { get; set; } = string.Empty;
    [MaxLength(500)]
    public string WikiUrl { get; set; } = string.Empty;
    // Table de progression sérialisée en JSON (string[][] = variables × rangs). "" = aucune.
    public string Progression { get; set; } = string.Empty;
    // Conditions infligeables, CSV (ex : "Bleeding,Deep Wound" ; "X:self" = subie par le
    // lanceur). Peuplée par la passe conditions du scraper. "" = aucune.
    public string Conditions { get; set; } = string.Empty;
    // ── Textes français (gwiki.fr, phase 7 du scraper). "" = pas de page FR. ──
    [MaxLength(200)]
    public string NameFr { get; set; } = string.Empty;
    // desc_concise gwiki : corps seul, plages "a...c" (ancres rangs 0/15).
    public string DescriptionFr { get; set; } = string.Empty;
    [MaxLength(100)]
    public string AttributeFr { get; set; } = string.Empty;
    [MaxLength(50)]
    public string TypeFr { get; set; } = string.Empty;
    // Page FR suspecte (stat d'infobox ≠ EN ou plage non appariée) → description EN affichée.
    public bool FrSuspect { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
