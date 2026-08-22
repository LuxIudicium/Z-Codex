namespace ZCodex.Core.Models;

// Membre du roster « Spike damage calculus » d'un teambuild : une ligne de l'arbre (racine ou
// variante — 8 max, cap appliqué côté UI) + les slots de skills sélectionnés pour le calcul
// (indices 0–7, cadre vert). Référence par CharacterBuild.Id, comme les cadenas. Format v6.
public class SpikeMember
{
    public Guid CharacterId { get; set; }
    public List<SpikeSkill> Skills { get; set; } = new();
    // v11 — clés (SpikeWeaponBuffs.Descriptor.Key) des buffs d'arme actifs sur CE membre
    // (état par perso, décision Philippe). Absent (fichiers ≤ v10) → aucun buff.
    public List<string> Buffs { get; set; } = new();
}

// Une skill cochée du spike : slot 0–7 + type de dégât de l'arme pour CETTE attaque (lore
// Philippe : mods élémentaires sur toutes les armes, jitte contondante, pic colossal perforant,
// battlepick, faux « Sufferer » ténèbres, dagues des 3 physiques ; par ATTAQUE car les switchs
// d'armes existent — ex. W/A Backbreaker marteau puis décharge aux dagues). Null = type natif.
public class SpikeSkill
{
    public int Slot { get; set; }
    public string? WeaponDamageType { get; set; }
    // Nombre de ticks comptés pour les skills à dégâts périodiques (« each second ») ; 1 sinon.
    public int Ticks { get; set; } = 1;
    // v19 — nombre de projectiles comptés pour un sort multi-projectiles (Stone Daggers 2,
    // Dancing Daggers 3). 0 = auto (tous touchent, défaut optimiste). Absent (fichiers ≤ v18) → 0.
    public int Projectiles { get; set; }
    // Indice de l'ordre de cast (1..N) pour Chain Combo. 0/absent (fichiers ≤ v6) = non normalisé.
    public int Order { get; set; }
    // Arme choisie manuellement pour une attaque libre non déductible (nom de maîtrise). Null = auto.
    public string? WeaponKind { get; set; }
    // v8 — nombre de procs (déclenchements d'un rider sur la séquence d'attaques). Défaut 1.
    public int Procs { get; set; } = 1;
    // v9 — part de dégâts conditionnelle comptée (« X more damage if [état] ») : true = comptée
    // (défaut, comme le comportement historique), false = exclue. Absent (fichiers ≤ v8) → true.
    public bool Conditional { get; set; } = true;
    // v10 — compte d'unités du paquet à seuil (« X for each [ressource] (maximum Y) »).
    // −1 = auto (défaut optimiste : compte atteignant le plafond). Absent (fichiers ≤ v9) → −1.
    public int Threshold { get; set; } = -1;
    // v12 — PV du lanceur pour Grenth's Balance (courants / max). Défaut 480 (niveau 20 pleine
    // vie → dégât 0). Absent (fichiers ≤ v11) → 480. Ignorés hors Grenth's Balance.
    public int CasterCurrentHp { get; set; } = 480;
    public int CasterMaxHp { get; set; } = 480;
    // v18 — mod de PRÉFIXE physique de l'arme pour cette attaque (clé SpikeWeaponMods :
    // "sundering" / "vampiric"). Null ou vide = aucun. Exclusif d'un type de dégâts élémentaire :
    // les deux occupent le même emplacement de préfixe.
    public string? WeaponMod { get; set; }
    // v18 — le proc 20 % du mod de fractionnement est compté sur cette ligne (pénétration BONUS
    // +20 % pour TOUTE la ligne). Absent (fichiers ≤ v17) → false.
    public bool SunderingProc { get; set; }
    // v18 — l'arc de cette attaque est un ARC CORNE : +10 % de pénétration BONUS permanente
    // (pas un proc). Ignoré hors arc. Absent (fichiers ≤ v17) → false.
    public bool Hornbow { get; set; }
}
