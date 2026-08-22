using System.Diagnostics;
using System.Text.Json;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;

namespace ZCodex.Core.Serialization;

public static class TeamBuildSerializer
{
    /// <summary>Extension native, écrite par Save.</summary>
    public const string Extension = ".zcx";

    /// <summary>
    /// Ancienne extension (avant le renommage de l'application), encore LUE mais jamais écrite :
    /// les fichiers déjà
    /// enregistrés restent ouvrables tels quels. Le contenu est identique — seul le nom change,
    /// il n'y a donc aucune conversion à faire. Un « Enregistrer sous » produira un .zcx.
    /// </summary>
    public const string LegacyExtension = ".pn3";

    /// <summary>Vrai pour les deux extensions du format natif (courante ou héritée).</summary>
    public static bool IsNativeExtension(string extension) =>
        extension.Equals(Extension, StringComparison.OrdinalIgnoreCase)
        || extension.Equals(LegacyExtension, StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Écrit le team build. L'écriture est ATOMIQUE : le .pn3 existant n'est remplacé qu'une fois
    /// le nouveau contenu intégralement sur le disque. Un File.WriteAllText direct tronque le
    /// fichier AVANT d'écrire — une coupure au mauvais moment (crash, coupure de courant, clé USB
    /// retirée) détruisait alors un build sans recours possible.
    /// Lève en cas d'échec : l'appelant doit prévenir l'utilisateur et considérer que rien n'a été
    /// enregistré (le fichier d'origine, lui, est intact).
    /// </summary>
    public static void Save(TeamBuild build, string filePath)
    {
        var json = Serialize(build);
        var tempPath = filePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(filePath))
            {
                // File.Replace est atomique sur NTFS mais n'est pas supporté par tous les
                // systèmes de fichiers (partages réseau, FAT32 d'une clé USB) → repli.
                try { File.Replace(tempPath, filePath, null); }
                catch (IOException) { File.Move(tempPath, filePath, overwrite: true); }
                catch (PlatformNotSupportedException) { File.Move(tempPath, filePath, overwrite: true); }
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        catch
        {
            // Ne pas laisser un .tmp orphelin à côté des builds de l'utilisateur.
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    public static TeamBuild? Load(string filePath, IReadOnlyDictionary<int, Skill> skillsById)
        => Load(filePath, skillsById, out _);

    /// <summary>
    /// Charge un team build et REMONTE les identifiants de compétence que le catalogue courant ne
    /// connaît pas (<paramref name="unresolvedIds"/>).
    ///
    /// Un id non résolu laisse le slot vide, et la sauvegarde suivante le réécrit en 0 : la
    /// compétence est alors perdue définitivement, sans que rien ne l'ait signalé. Les cas réels
    /// sont un .pn3 reçu d'un autre utilisateur dont le catalogue diffère, ou un catalogue local
    /// plus ancien que le fichier. L'appelant doit prévenir avant que l'utilisateur n'enregistre.
    /// </summary>
    public static TeamBuild? Load(string filePath, IReadOnlyDictionary<int, Skill> skillsById,
                                  out List<int> unresolvedIds)
    {
        unresolvedIds = [];
        if (!File.Exists(filePath)) return null;
        var build = Deserialize(File.ReadAllText(filePath), skillsById, unresolvedIds);
        if (build is null) Debug.WriteLine($"[Pn3] Load failed '{filePath}'");
        return build;
    }

    // Round-trip en mémoire : utilisé par Save/Load et par les snapshots d'undo.
    public static string Serialize(TeamBuild build)
        => JsonSerializer.Serialize(ToDto(build), Options);

    public static TeamBuild? Deserialize(string json, IReadOnlyDictionary<int, Skill> skillsById,
                                         List<int>? unresolvedIds = null)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<Pn3Dto>(json, Options);
            return dto is null ? null : FromDto(dto, skillsById, unresolvedIds);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[Pn3] Deserialize failed: {ex.Message}");
            return null;
        }
    }

    // ── DTOs (private) ────────────────────────────────────────────────────────

    private sealed class Pn3Dto
    {
        public int Version { get; set; } = 19; // v19 = compteur de projectiles du spike (sorts multi-projectiles) ; v18 = mods d'arme du spike (fractionnement / vampirique / arc corne) + compteurs d'attaques vampiriques ; v17 = mode de jeu PvE/PvP enregistré avec le build ; v16 = boosts d'attribut de compétences équipées par perso ; v15 = rang de simulation Tranquility + toggle prolongateurs de durée par perso ; v14 = rang de simulation Roaring Winds ; v13 = rituels de la nature actifs ; v12 = PV lanceur Grenth's Balance ; v11 = buffs d'arme spike ; v10 = seuil spike ; v9 = part conditionnelle spike ; v8 = procs spike ; v7 = flux ; v6 = roster Spike ; v5 = genre du perso
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = [];
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<CharacterDto> Characters { get; set; } = [];
        public List<VariantLockDto> Locks { get; set; } = []; // v2 — tuples de variantes
        public List<SpikeMemberDto> Spike { get; set; } = []; // v6 — absent des fichiers ≤ v5
        public int Flux { get; set; } // v7 — 0/absent (fichiers ≤ v6) = aucun flux ; 1-12 = mois du flux
        public List<int> NatureRituals { get; set; } = []; // v13 — SkillId des rituels de la nature actifs
        public int RoaringWindsRank { get; set; } = 12;    // v14 — rang de simulation Roaring Winds
        public int TranquilityRank { get; set; } = 12;     // v15 — rang de simulation Tranquility (durée d'enchantement)
        // v17 — mode de jeu à restaurer à l'ouverture ("All"/"PvE"/"PvP"). Chaîne vide ou valeur
        // inconnue = fichier ≤ v16 : aucun mode enregistré, le mode courant s'applique.
        public string GameMode { get; set; } = string.Empty;
        // v18 — attaques vampiriques comptées dans le spike, par valeur de vol de vie (3 = armes à
        // une main, 5 = à deux mains). Absent (≤ v17) → 1, le défaut des lignes artificielles.
        public int VampiricHits3 { get; set; } = 1;
        public int VampiricHits5 { get; set; } = 1;
    }

    private sealed class SpikeMemberDto
    {
        public Guid CharacterId { get; set; }
        public List<SpikeSkillDto> Skills { get; set; } = [];
        // v11 — buffs d'arme actifs du membre (clés SpikeWeaponBuffs) ; absent (≤ v10) → aucun.
        public List<string> Buffs { get; set; } = [];
        // Repli v6 précoce (07/07/2026, type par MEMBRE) : lus si Skills est vide, plus écrits.
        public List<int> Slots { get; set; } = [];
        public string? WeaponDamageType { get; set; }
    }

    private sealed class SpikeSkillDto
    {
        public int Slot { get; set; }
        public string? WeaponDamageType { get; set; }
        public int Ticks { get; set; } = 1; // absent des fichiers antérieurs au compteur → 1
        public int Projectiles { get; set; } // v19 — projectiles comptés d'un sort multi-projectiles ; absent → 0 = auto/tous
        public int Order { get; set; }       // v7 — ordre de cast Chain Combo (0/absent = non normalisé)
        public string? WeaponKind { get; set; } // v7 — arme manuelle d'une attaque libre (nom de maîtrise)
        public int Procs { get; set; } = 1;  // v8 — déclenchements d'un rider ; absent → 1
        public bool Conditional { get; set; } = true; // v9 — part conditionnelle comptée ; absent → true
        public int Threshold { get; set; } = -1; // v10 — compte du paquet à seuil ; absent → −1 = auto/max
        public int CasterCurrentHp { get; set; } = 480; // v12 — PV lanceur Grenth's Balance ; absent → 480
        public int CasterMaxHp { get; set; } = 480;     // v12 — idem
        public string? WeaponMod { get; set; }   // v18 — mod de préfixe physique ("sundering"/"vampiric") ; absent → aucun
        public bool SunderingProc { get; set; }  // v18 — proc du fractionnement compté sur la ligne ; absent → false
        public bool Hornbow { get; set; }        // v18 — l'arc de la ligne est un arc corne ; absent → false
    }

    private sealed class CharacterDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PrimaryProfession { get; set; }
        public int SecondaryProfession { get; set; }
        public bool IsFavorite { get; set; }
        public string Assignment { get; set; } = string.Empty;
        public int Gender { get; set; } // v5 — 0 = Male (défaut si absent des anciens fichiers)
        public int[] SkillIds { get; set; } = new int[8];
        public List<AttrDto> Attributes { get; set; } = [];
        public Dictionary<string, int> TitleRanks { get; set; } = []; // v4
        public EquipmentDto? Equipment { get; set; }
        public string Notes { get; set; } = string.Empty;
        public bool DurationBoostersEnabled { get; set; } // v15 — toggle prolongateurs de durée (Blessed Aura / Extend)
        public List<int> ActiveAttributeBoosts { get; set; } = []; // v16 — SkillId des boosts d'attribut actifs (Aura of the Lich...) ; absent (≤ v15) → aucun.
        public List<CharacterDto> Variants { get; set; } = []; // v2 — arbre récursif
    }

    private sealed class VariantLockDto
    {
        public int Index { get; set; }
        public string Color { get; set; } = string.Empty;
        public List<Guid> MemberIds { get; set; } = [];
    }

    private sealed record AttrDto(int Id, int Points);

    // v3 : armure partagée + sets d'armes. "Items" = ancien format plat (v2), lu pour la
    // migration douce (slots 0-1 → set F1, slots 2-6 → armure), plus jamais écrit.
    private sealed class EquipmentDto
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<EquipmentItemDto>? Items { get; set; }
        public List<EquipmentItemDto> Armor { get; set; } = [];
        public List<WeaponSetDto> WeaponSets { get; set; } = [];
        public int ActiveSet { get; set; }
    }

    private sealed class WeaponSetDto
    {
        public List<EquipmentItemDto> Items { get; set; } = [];
    }

    private sealed class EquipmentItemDto
    {
        public int Slot { get; set; }
        public int ItemId { get; set; }
        public int Dye { get; set; }
        public List<int> ModifierIds { get; set; } = [];
    }

    // ── TeamBuild → DTO ───────────────────────────────────────────────────────

    private static Pn3Dto ToDto(TeamBuild b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        Tags = b.Tags,
        Notes = b.Notes,
        CreatedAt = b.CreatedAt,
        UpdatedAt = b.UpdatedAt,
        Characters = b.Characters.Select(CharToDto).ToList(),
        Locks = b.Locks.Select(l => new VariantLockDto
        {
            Index = l.Index,
            Color = l.Color,
            MemberIds = l.MemberIds.ToList(),
        }).ToList(),
        Spike = b.Spike.Select(s => new SpikeMemberDto
        {
            CharacterId = s.CharacterId,
            Skills = s.Skills.Select(k => new SpikeSkillDto
            {
                Slot = k.Slot,
                WeaponDamageType = k.WeaponDamageType,
                Ticks = k.Ticks,
                Projectiles = k.Projectiles,
                Order = k.Order,
                WeaponKind = k.WeaponKind,
                Procs = k.Procs,
                Conditional = k.Conditional,
                Threshold = k.Threshold,
                CasterCurrentHp = k.CasterCurrentHp,
                CasterMaxHp = k.CasterMaxHp,
                WeaponMod = k.WeaponMod,
                SunderingProc = k.SunderingProc,
                Hornbow = k.Hornbow,
            }).ToList(),
            Buffs = s.Buffs.ToList(),
        }).ToList(),
        Flux = b.ActiveFlux is { } f ? (int)f : 0,
        NatureRituals = b.ActiveNatureRituals.ToList(),
        RoaringWindsRank = b.RoaringWindsRitualRank,
        TranquilityRank = b.TranquilityRitualRank,
        GameMode = b.GameMode?.ToString() ?? string.Empty,   // v17 — "" = aucun mode enregistré
        VampiricHits3 = b.VampiricHits3,
        VampiricHits5 = b.VampiricHits5,
    };

    private static CharacterDto CharToDto(CharacterBuild c)
    {
        var attrs = GwTemplateCodec.ToAttributesBuild(c.Attributes);
        return new CharacterDto
        {
            Id = c.Id,
            Name = c.Name,
            PrimaryProfession = (int)c.PrimaryProfession,
            SecondaryProfession = (int)c.SecondaryProfession,
            IsFavorite = c.IsFavorite,
            Assignment = c.Assignment,
            Gender = (int)c.Gender,
            SkillIds = c.Skills.Select(s => s?.Id ?? 0).ToArray(),
            Attributes = attrs.Allocations
                .Select(a => new AttrDto(a.AttributeId, a.Points))
                .ToList(),
            TitleRanks = new Dictionary<string, int>(c.TitleRanks),
            Equipment = EquipToDto(c.Equipment),
            Notes = c.Notes,
            DurationBoostersEnabled = c.DurationBoostersEnabled,
            ActiveAttributeBoosts = c.ActiveAttributeBoosts.ToList(),
            Variants = c.Variants.Select(CharToDto).ToList(),
        };
    }

    private static EquipmentItemDto ItemToDto(EquipmentItem i) => new()
    {
        Slot = i.Slot,
        ItemId = i.ItemId,
        Dye = i.Dye,
        ModifierIds = i.ModifierIds,
    };

    private static EquipmentDto? EquipToDto(EquipmentBuild? eq) =>
        eq is null ? null : new EquipmentDto
        {
            Armor = eq.Armor.Select(ItemToDto).ToList(),
            WeaponSets = eq.WeaponSets
                .Select(s => new WeaponSetDto { Items = s.Items.Select(ItemToDto).ToList() })
                .ToList(),
            ActiveSet = eq.ActiveSet,
        };

    // ── DTO → TeamBuild ───────────────────────────────────────────────────────

    private static TeamBuild FromDto(Pn3Dto dto, IReadOnlyDictionary<int, Skill> skillsById,
                                     List<int>? unresolvedIds = null) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Tags = dto.Tags,
        Notes = dto.Notes,
        CreatedAt = dto.CreatedAt,
        UpdatedAt = dto.UpdatedAt,
        Characters = dto.Characters.Select(c => CharFromDto(c, skillsById, unresolvedIds)).ToList(),
        Locks = dto.Locks.Select(l => new VariantLock
        {
            Index = l.Index,
            Color = l.Color,
            MemberIds = l.MemberIds.ToList(),
        }).ToList(),
        Spike = dto.Spike.Select(s => new SpikeMember
        {
            CharacterId = s.CharacterId,
            Skills = (s.Skills.Count > 0
                    ? s.Skills.Select(k => new SpikeSkill
                    {
                        Slot = k.Slot,
                        WeaponDamageType = k.WeaponDamageType,
                        Ticks = Math.Clamp(k.Ticks, 1, 30),
                        Projectiles = Math.Clamp(k.Projectiles, 0, 30),
                        Order = k.Order,
                        WeaponKind = k.WeaponKind,
                        Procs = Math.Clamp(k.Procs, 0, 25),
                        Conditional = k.Conditional,
                        Threshold = Math.Clamp(k.Threshold, -1, 99),
                        CasterCurrentHp = Math.Clamp(k.CasterCurrentHp, 1, 2000),
                        CasterMaxHp = Math.Clamp(k.CasterMaxHp, 1, 2000),
                        // Clé inconnue (fichier d'une version future) → aucun mod, et la case du
                        // proc tombe avec lui : même politique que le clamp des autres champs.
                        WeaponMod = Data.SpikeWeaponMods.ToKey(Data.SpikeWeaponMods.FromKey(k.WeaponMod)) is { Length: > 0 } m
                            ? m : null,
                        SunderingProc = k.SunderingProc
                            && Data.SpikeWeaponMods.FromKey(k.WeaponMod) == Data.SpikeWeaponMod.Sundering,
                        Hornbow = k.Hornbow,
                    })
                    : s.Slots.Select(i => new SpikeSkill { Slot = i, WeaponDamageType = s.WeaponDamageType }))
                .Where(k => k.Slot is >= 0 and <= 7)
                .GroupBy(k => k.Slot).Select(g => g.First())
                .ToList(),
            // Clés inconnues (fichier d'une version future) filtrées — même politique que le clamp.
            Buffs = s.Buffs.Where(k => Data.SpikeWeaponBuffs.FromKey(k) is not null)
                .Distinct(StringComparer.Ordinal).ToList(),
        }).ToList(),
        ActiveFlux = dto.Flux is >= 1 and <= 12 ? (Flux)dto.Flux : null,
        ActiveNatureRituals = dto.NatureRituals
            .Where(id => NatureRitualData.BySkillId(id) != null)   // ignore les SkillId inconnus
            .Distinct().ToList(),
        RoaringWindsRitualRank = Math.Clamp(dto.RoaringWindsRank, 0, NatureRitualData.MaxRitualRank),
        TranquilityRitualRank = Math.Clamp(dto.TranquilityRank, 0, NatureRitualData.MaxRitualRank),
        // v17 — valeur inconnue traitée comme absente (même politique que les autres champs) :
        // un fichier écrit par une version future ne doit pas imposer un mode incompréhensible.
        GameMode = Enum.TryParse<GameMode>(dto.GameMode, out var gm) ? gm : null,
        VampiricHits3 = Math.Clamp(dto.VampiricHits3, 0, 25),   // v18 — bornes du ComboBox des procs
        VampiricHits5 = Math.Clamp(dto.VampiricHits5, 0, 25),
    };

    private static CharacterBuild CharFromDto(CharacterDto dto, IReadOnlyDictionary<int, Skill> skillsById,
                                              List<int>? unresolvedIds = null)
    {
        var attrsBuild = new AttributesBuild
        {
            Allocations = dto.Attributes
                .Select(a => new AttributeAllocation(a.Id, a.Points))
                .ToList(),
        };

        var skills = new Skill?[8];
        for (int i = 0; i < 8 && i < dto.SkillIds.Length; i++)
        {
            int id = dto.SkillIds[i];
            if (id == 0) continue;
            // Id absent du catalogue : le slot reste vide et la prochaine sauvegarde le réécrira
            // en 0. On le remonte pour que l'appelant puisse avertir AVANT cette perte.
            if (!skillsById.TryGetValue(id, out skills[i]))
                unresolvedIds?.Add(id);
        }

        return new CharacterBuild
        {
            Id = dto.Id,
            Name = dto.Name,
            PrimaryProfession = (Profession)dto.PrimaryProfession,
            SecondaryProfession = (Profession)dto.SecondaryProfession,
            IsFavorite = dto.IsFavorite,
            Assignment = dto.Assignment,
            Gender = (Gender)dto.Gender,
            Attributes = GwTemplateCodec.ToAttributeDict(attrsBuild),
            TitleRanks = new Dictionary<string, int>(dto.TitleRanks),
            Skills = skills,
            Equipment = EquipFromDto(dto.Equipment),
            Notes = dto.Notes,
            DurationBoostersEnabled = dto.DurationBoostersEnabled,
            // Ignore les SkillId inconnus (même politique que NatureRituals) : robuste si un fichier
            // futur référence un boost pas encore reconnu par cette version. Heroic Refrain (Lot D)
            // n'est PAS dans AttributeBoostData (sa résolution est inter-perso, pas auto-résolue par
            // ce perso) → autorisé explicitement via son SkillId dédié.
            ActiveAttributeBoosts = dto.ActiveAttributeBoosts
                .Where(id => AttributeBoostData.BySkillId(id) != null || id == HeroicRefrainData.SkillId)
                .Distinct().ToList(),
            Variants = dto.Variants.Select(v => CharFromDto(v, skillsById, unresolvedIds)).ToList(),
        };
    }

    private static EquipmentItem ItemFromDto(EquipmentItemDto i) => new()
    {
        Slot = i.Slot,
        ItemId = i.ItemId,
        Dye = i.Dye,
        ModifierIds = i.ModifierIds,
    };

    private static EquipmentBuild? EquipFromDto(EquipmentDto? dto)
    {
        if (dto is null) return null;

        // Ancien fichier (liste plate v2) → armure + set F1.
        if (dto.Items is { Count: > 0 } && dto.Armor.Count == 0 && dto.WeaponSets.Count == 0)
            return EquipmentBuild.FromFlat(dto.Items.Select(ItemFromDto));

        var build = new EquipmentBuild
        {
            Armor = dto.Armor.Select(ItemFromDto).ToList(),
            WeaponSets = dto.WeaponSets
                .Take(EquipmentBuild.MaxWeaponSets)
                .Select(s => new WeaponSet { Items = s.Items.Select(ItemFromDto).ToList() })
                .ToList(),
            ActiveSet = dto.ActiveSet,
        };
        build.TrimWeaponSets();
        return build;
    }
}
