namespace ZCodex.Core.Data;

// GENERE par le harnais EquipClassifier (scratchpad) le 2026-07-04 — ne pas editer a la main.
// Sources : wiki List_of_weapon_upgrades / Insignia / Rune (valeurs PARFAITES : plages a...b reduites a b).
// Regeneration : dotnet run --project EquipClassifier -- --gen-details <dossierHtml> <ceFichier>
public static class GwEquipmentModDetails
{
    public sealed record ModDetail(string Description, string WikiPath);

    public static readonly IReadOnlyDictionary<int, ModDetail> ByModId = new Dictionary<int, ModDetail>
    {
        {   1, new("Cold damage", "Icy") }, // Icy Axe Haft
        {   2, new("Earth damage", "Ebon") }, // Ebon Axe Haft
        {   3, new("Lightning damage", "Shocking") }, // Shocking Axe Haft
        {   4, new("Fire damage", "Fiery") }, // Fiery Axe Haft
        {   5, new("Cold damage", "Icy") }, // Icy Bow String
        {   6, new("Earth damage", "Ebon") }, // Ebon Bow String
        {   7, new("Lightning damage", "Shocking") }, // Shocking Bow String
        {   8, new("Fire damage", "Fiery") }, // Fiery Bow String
        {   9, new("Cold damage", "Icy") }, // Icy Hammer Haft
        {  10, new("Earth damage", "Ebon") }, // Ebon Hammer Haft
        {  11, new("Lightning damage", "Shocking") }, // Shocking Hammer Haft
        {  12, new("Fire damage", "Fiery") }, // Fiery Hammer Haft
        {  13, new("Cold damage", "Icy") }, // Icy Sword Hilt
        {  14, new("Earth damage", "Ebon") }, // Ebon Sword Hilt
        {  15, new("Lightning damage", "Shocking") }, // Shocking Sword Hilt
        {  16, new("Fire damage", "Fiery") }, // Fiery Sword Hilt
        {  17, new("Double Adrenaline on hit (Chance: 10%)", "Furious") }, // Furious Axe Haft
        {  18, new("Double Adrenaline on hit (Chance: 10%)", "Furious") }, // Furious Hammer Haft
        {  19, new("Double Adrenaline on hit (Chance: 10%)", "Furious") }, // Furious Sword Hilt
        {  22, new("Fast Casting +1 (Non-stacking)", "Rune_of_Fast_Casting") }, // Rune of Minor Fast Casting
        {  23, new("Domination Magic +1 (Non-stacking)", "Rune_of_Domination_Magic") }, // Rune of Minor Domination Magic
        {  24, new("Illusion Magic +1 (Non-stacking)", "Rune_of_Illusion_Magic") }, // Rune of Minor Illusion Magic
        {  25, new("Inspiration Magic +1 (Non-stacking)", "Rune_of_Inspiration_Magic") }, // Rune of Minor Inspiration Magic
        {  26, new("Blood Magic +1 (Non-stacking)", "Rune_of_Blood_Magic") }, // Rune of Minor Blood Magic
        {  27, new("Death Magic +1 (Non-stacking)", "Rune_of_Death_Magic") }, // Rune of Minor Death Magic
        {  28, new("Curses +1 (Non-stacking)", "Rune_of_Curses") }, // Rune of Minor Curses
        {  29, new("Soul Reaping +1 (Non-stacking)", "Rune_of_Soul_Reaping") }, // Rune of Minor Soul Reaping
        {  30, new("Energy Storage +1 (Non-stacking)", "Rune_of_Energy_Storage") }, // Rune of Minor Energy Storage
        {  31, new("Fire Magic +1 (Non-stacking)", "Rune_of_Fire_Magic") }, // Rune of Minor Fire Magic
        {  32, new("Air Magic +1 (Non-stacking)", "Rune_of_Air_Magic") }, // Rune of Minor Air Magic
        {  33, new("Earth Magic +1 (Non-stacking)", "Rune_of_Earth_Magic") }, // Rune of Minor Earth Magic
        {  34, new("Water Magic +1 (Non-stacking)", "Rune_of_Water_Magic") }, // Rune of Minor Water Magic
        {  35, new("Healing Prayers +1 (Non-stacking)", "Rune_of_Healing_Prayers") }, // Rune of Minor Healing Prayers
        {  36, new("Smiting Prayers +1 (Non-stacking)", "Rune_of_Smiting_Prayers") }, // Rune of Minor Smiting Prayers
        {  37, new("Protection Prayers +1 (Non-stacking)", "Rune_of_Protection_Prayers") }, // Rune of Minor Protection Prayers
        {  38, new("Divine Favor +1 (Non-stacking)", "Rune_of_Divine_Favor") }, // Rune of Minor Divine Favor
        {  39, new("Tactics +1 (Non-stacking)", "Rune_of_Tactics") }, // Rune of Minor Tactics
        {  40, new("Strength +1 (Non-stacking)", "Rune_of_Strength") }, // Rune of Minor Strength
        {  41, new("Axe Mastery +1 (Non-stacking)", "Rune_of_Axe_Mastery") }, // Rune of Minor Axe Mastery
        {  42, new("Hammer Mastery +1 (Non-stacking)", "Rune_of_Hammer_Mastery") }, // Rune of Minor Hammer Mastery
        {  43, new("Swordsmanship +1 (Non-stacking)", "Rune_of_Swordsmanship") }, // Rune of Minor Swordsmanship
        {  44, new("Wilderness Survival +1 (Non-stacking)", "Rune_of_Wilderness_Survival") }, // Rune of Minor Wilderness Survival
        {  45, new("Expertise +1 (Non-stacking)", "Rune_of_Expertise") }, // Rune of Minor Expertise
        {  46, new("Beast Mastery +1 (Non-stacking)", "Rune_of_Beast_Mastery") }, // Rune of Minor Beast Mastery
        {  47, new("Marksmanship +1 (Non-stacking)", "Rune_of_Marksmanship") }, // Rune of Minor Marksmanship
        {  48, new("Fast Casting +2 (Non-stacking) ; Health -35", "Rune_of_Fast_Casting") }, // Rune of Major Fast Casting
        {  49, new("Domination Magic +2 (Non-stacking) ; Health -35", "Rune_of_Domination_Magic") }, // Rune of Major Domination Magic
        {  50, new("Illusion Magic +2 (Non-stacking) ; Health -35", "Rune_of_Illusion_Magic") }, // Rune of Major Illusion Magic
        {  51, new("Inspiration Magic +2 (Non-stacking) ; Health -35", "Rune_of_Inspiration_Magic") }, // Rune of Major Inspiration Magic
        {  52, new("Blood Magic +2 (Non-stacking) ; Health -35", "Rune_of_Blood_Magic") }, // Rune of Major Blood Magic
        {  53, new("Death Magic +2 (Non-stacking) ; Health -35", "Rune_of_Death_Magic") }, // Rune of Major Death Magic
        {  54, new("Curses +2 (Non-stacking) ; Health -35", "Rune_of_Curses") }, // Rune of Major Curses
        {  55, new("Soul Reaping +2 (Non-stacking) ; Health -35", "Rune_of_Soul_Reaping") }, // Rune of Major Soul Reaping
        {  56, new("Energy Storage +2 (Non-stacking) ; Health -35", "Rune_of_Energy_Storage") }, // Rune of Major Energy Storage
        {  57, new("Fire Magic +2 (Non-stacking) ; Health -35", "Rune_of_Fire_Magic") }, // Rune of Major Fire Magic
        {  58, new("Air Magic +2 (Non-stacking) ; Health -35", "Rune_of_Air_Magic") }, // Rune of Major Air Magic
        {  59, new("Earth Magic +2 (Non-stacking) ; Health -35", "Rune_of_Earth_Magic") }, // Rune of Major Earth Magic
        {  60, new("Water Magic +2 (Non-stacking) ; Health -35", "Rune_of_Water_Magic") }, // Rune of Major Water Magic
        {  61, new("Healing Prayers +2 (Non-stacking) ; Health -35", "Rune_of_Healing_Prayers") }, // Rune of Major Healing Prayers
        {  62, new("Smiting Prayers +2 (Non-stacking) ; Health -35", "Rune_of_Smiting_Prayers") }, // Rune of Major Smiting Prayers
        {  63, new("Protection Prayers +2 (Non-stacking) ; Health -35", "Rune_of_Protection_Prayers") }, // Rune of Major Protection Prayers
        {  64, new("Divine Favor +2 (Non-stacking) ; Health -35", "Rune_of_Divine_Favor") }, // Rune of Major Divine Favor
        {  65, new("Tactics +2 (Non-stacking) ; Health -35", "Rune_of_Tactics") }, // Rune of Major Tactics
        {  66, new("Strength +2 (Non-stacking) ; Health -35", "Rune_of_Strength") }, // Rune of Major Strength
        {  67, new("Axe Mastery +2 (Non-stacking) ; Health -35", "Rune_of_Axe_Mastery") }, // Rune of Major Axe Mastery
        {  68, new("Hammer Mastery +2 (Non-stacking) ; Health -35", "Rune_of_Hammer_Mastery") }, // Rune of Major Hammer Mastery
        {  69, new("Swordsmanship +2 (Non-stacking) ; Health -35", "Rune_of_Swordsmanship") }, // Rune of Major Swordsmanship
        {  70, new("Wilderness Survival +2 (Non-stacking) ; Health -35", "Rune_of_Wilderness_Survival") }, // Rune of Major Wilderness Survival
        {  71, new("Expertise +2 (Non-stacking) ; Health -35", "Rune_of_Expertise") }, // Rune of Major Expertise
        {  72, new("Beast Mastery +2 (Non-stacking) ; Health -35", "Rune_of_Beast_Mastery") }, // Rune of Major Beast Mastery
        {  73, new("Marksmanship +2 (Non-stacking) ; Health -35", "Rune_of_Marksmanship") }, // Rune of Major Marksmanship
        {  74, new("Fast Casting +3 (Non-stacking) ; Health -75", "Rune_of_Fast_Casting") }, // Rune of Superior Fast Casting
        {  75, new("Domination Magic +3 (Non-stacking) ; Health -75", "Rune_of_Domination_Magic") }, // Rune of Superior Domination Magic
        {  76, new("Illusion Magic +3 (Non-stacking) ; Health -75", "Rune_of_Illusion_Magic") }, // Rune of Superior Illusion Magic
        {  77, new("Inspiration Magic +3 (Non-stacking) ; Health -75", "Rune_of_Inspiration_Magic") }, // Rune of Superior Inspiration Magic
        {  78, new("Blood Magic +3 (Non-stacking) ; Health -75", "Rune_of_Blood_Magic") }, // Rune of Superior Blood Magic
        {  79, new("Death Magic +3 (Non-stacking) ; Health -75", "Rune_of_Death_Magic") }, // Rune of Superior Death Magic
        {  80, new("Curses +3 (Non-stacking) ; Health -75", "Rune_of_Curses") }, // Rune of Superior Curses
        {  81, new("Soul Reaping +3 (Non-stacking) ; Health -75", "Rune_of_Soul_Reaping") }, // Rune of Superior Soul Reaping
        {  82, new("Energy Storage +3 (Non-stacking) ; Health -75", "Rune_of_Energy_Storage") }, // Rune of Superior Energy Storage
        {  83, new("Fire Magic +3 (Non-stacking) ; Health -75", "Rune_of_Fire_Magic") }, // Rune of Superior Fire Magic
        {  84, new("Air Magic +3 (Non-stacking) ; Health -75", "Rune_of_Air_Magic") }, // Rune of Superior Air Magic
        {  85, new("Earth Magic +3 (Non-stacking) ; Health -75", "Rune_of_Earth_Magic") }, // Rune of Superior Earth Magic
        {  86, new("Water Magic +3 (Non-stacking) ; Health -75", "Rune_of_Water_Magic") }, // Rune of Superior Water Magic
        {  87, new("Healing Prayers +3 (Non-stacking) ; Health -75", "Rune_of_Healing_Prayers") }, // Rune of Superior Healing Prayers
        {  88, new("Smiting Prayers +3 (Non-stacking) ; Health -75", "Rune_of_Smiting_Prayers") }, // Rune of Superior Smiting Prayers
        {  89, new("Protection Prayers +3 (Non-stacking) ; Health -75", "Rune_of_Protection_Prayers") }, // Rune of Superior Protection Prayers
        {  90, new("Divine Favor +3 (Non-stacking) ; Health -75", "Rune_of_Divine_Favor") }, // Rune of Superior Divine Favor
        {  91, new("Tactics +3 (Non-stacking) ; Health -75", "Rune_of_Tactics") }, // Rune of Superior Tactics
        {  92, new("Strength +3 (Non-stacking) ; Health -75", "Rune_of_Strength") }, // Rune of Superior Strength
        {  93, new("Axe Mastery +3 (Non-stacking) ; Health -75", "Rune_of_Axe_Mastery") }, // Rune of Superior Axe Mastery
        {  94, new("Hammer Mastery +3 (Non-stacking) ; Health -75", "Rune_of_Hammer_Mastery") }, // Rune of Superior Hammer Mastery
        {  95, new("Swordsmanship +3 (Non-stacking) ; Health -75", "Rune_of_Swordsmanship") }, // Rune of Superior Swordsmanship
        {  96, new("Wilderness Survival +3 (Non-stacking) ; Health -75", "Rune_of_Wilderness_Survival") }, // Rune of Superior Wilderness Survival
        {  97, new("Expertise +3 (Non-stacking) ; Health -75", "Rune_of_Expertise") }, // Rune of Superior Expertise
        {  98, new("Beast Mastery +3 (Non-stacking) ; Health -75", "Rune_of_Beast_Mastery") }, // Rune of Superior Beast Mastery
        {  99, new("Marksmanship +3 (Non-stacking) ; Health -75", "Rune_of_Marksmanship") }, // Rune of Superior Marksmanship
        { 100, new("Armor +5", "Defensive") }, // Defensive Staff Head
        { 101, new("Lengthens Bleeding duration on foes by 33%", "Barbed") }, // Barbed Axe Haft
        { 102, new("Lengthens Bleeding duration on foes by 33%", "Barbed") }, // Barbed Sword Hilt
        { 103, new("Lengthens Crippled duration on foes by 33%", "Crippling") }, // Crippling Axe Haft
        { 104, new("Lengthens Crippled duration on foes by 33%", "Crippling") }, // Crippling Sword Hilt
        { 105, new("Lengthens Deep Wound duration on foes by 33%", "Cruel") }, // Cruel Axe Haft
        { 106, new("Lengthens Deep Wound duration on foes by 33%", "Cruel") }, // Cruel Hammer Haft
        { 107, new("Lengthens Deep Wound duration on foes by 33%", "Cruel") }, // Cruel Sword Hilt
        { 108, new("Energy +5", "Insightful") }, // Insightful Staff Head
        { 109, new("Health +30", "Hale") }, // Hale Staff Head
        { 110, new("Lengthens Poison duration on foes by 33%", "Poisonous") }, // Poisonous Axe Haft
        { 111, new("Lengthens Poison duration on foes by 33%", "Poisonous") }, // Poisonous Bow String
        { 112, new("Lengthens Poison duration on foes by 33%", "Poisonous") }, // Poisonous Sword Hilt
        { 113, new("Lengthens Weakness duration on foes by 33%", "Heavy") }, // Heavy Axe Haft
        { 114, new("Lengthens Weakness duration on foes by 33%", "Heavy") }, // Heavy Hammer Haft
        { 115, new("Energy gain on hit: 1 ; Energy regeneration: -1", "Zealous") }, // Zealous Axe Haft
        { 116, new("Energy gain on hit: 1 ; Energy regeneration: -1", "Zealous") }, // Zealous Hammer Haft
        { 117, new("Energy gain on hit: 1 ; Energy regeneration: -1", "Zealous") }, // Zealous Bow String
        { 118, new("Energy gain on hit: 1 ; Energy regeneration: -1", "Zealous") }, // Zealous Sword Hilt
        { 119, new("Life Draining: 5, 3 ; Health regeneration: -1", "Vampiric") }, // Vampiric Axe Haft
        { 120, new("Life Draining: 5, 3 ; Health regeneration: -1", "Vampiric") }, // Vampiric Hammer Haft
        { 121, new("Life Draining: 5, 3 ; Health regeneration: -1", "Vampiric") }, // Vampiric Bow String
        { 122, new("Life Draining: 5, 3 ; Health regeneration: -1", "Vampiric") }, // Vampiric Sword Hilt
        { 123, new("Mysticism +1 (Non-stacking)", "Rune_of_Mysticism") }, // Rune of Minor Mysticism
        { 124, new("Earth Prayers +1 (Non-stacking)", "Rune_of_Earth_Prayers") }, // Rune of Minor Earth Prayers
        { 125, new("Scythe Mastery +1 (Non-stacking)", "Rune_of_Scythe_Mastery") }, // Rune of Minor Scythe Mastery
        { 126, new("Wind Prayers +1 (Non-stacking)", "Rune_of_Wind_Prayers") }, // Rune of Minor Wind Prayers
        { 127, new("Armor +5", "Of_Defense") }, // Axe Grip of Defense
        { 128, new("Armor +5", "Of_Defense") }, // Bow Grip of Defense
        { 129, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Axe Grip of Warding
        { 130, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Bow Grip of Warding
        { 131, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Hammer Grip of Warding
        { 132, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Staff Wrapping of Warding
        { 133, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Sword Pommel of Warding
        { 134, new("Armor +5", "Of_Defense") }, // Hammer Grip of Defense
        { 135, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Axe Grip of Shelter
        { 136, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Bow Grip of Shelter
        { 137, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Hammer Grip of Shelter
        { 138, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Staff Wrapping of Shelter
        { 139, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Sword Pommel of Shelter
        { 140, new("Armor +5", "Of_Defense") }, // Staff Wrapping of Defense
        { 141, new("Armor +5", "Of_Defense") }, // Sword Pommel of Defense
        { 142, new("Health +30", "Of_Fortitude") }, // Axe Grip of Fortitude
        { 143, new("Health +30", "Of_Fortitude") }, // Bow Grip of Fortitude
        { 144, new("Health +30", "Of_Fortitude") }, // Hammer Grip of Fortitude
        { 145, new("Health +30", "Of_Fortitude") }, // Staff Wrapping of Fortitude
        { 146, new("Health +30", "Of_Fortitude") }, // Sword Pommel of Fortitude
        { 147, new("Enchantments last 20% longer", "Of_Enchanting") }, // Axe Grip of Enchanting
        { 148, new("Enchantments last 20% longer", "Of_Enchanting") }, // Bow Grip of Enchanting
        { 149, new("Enchantments last 20% longer", "Of_Enchanting") }, // Hammer Grip of Enchanting
        { 150, new("Enchantments last 20% longer", "Of_Enchanting") }, // Staff Wrapping of Enchanting
        { 151, new("Enchantments last 20% longer", "Of_Enchanting") }, // Sword Pommel of Enchanting
        { 152, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Axe Grip of Mastery
        { 153, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Bow Grip of Mastery
        { 154, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Hammer Grip of Mastery
        { 155, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Sword Pommel of Mastery
        { 156, new("Health +30 (Non-stacking)", "Rune_of_Minor_Vigor") }, // Rune of Minor Vigor
        { 157, new("Health +41 (Non-stacking)", "Rune_of_Major_Vigor") }, // Rune of Major Vigor
        { 158, new("Health +50 (Non-stacking)", "Rune_of_Superior_Vigor") }, // Rune of Superior Vigor
        { 159, new("Reduces physical damage by 1 (Non-stacking)", "Rune_of_Absorption") }, // Rune of Minor Absorption
        { 160, new("Reduces physical damage by 2 (Non-stacking)", "Rune_of_Absorption") }, // Rune of Major Absorption
        { 161, new("Reduces physical damage by 3 (Non-stacking)", "Rune_of_Absorption") }, // Rune of Superior Absorption
        { 162, new("Critical Strikes +1 (Non-stacking)", "Rune_of_Critical_Strikes") }, // Rune of Minor Critical Strikes
        { 163, new("Dagger Mastery +1 (Non-stacking)", "Rune_of_Dagger_Mastery") }, // Rune of Minor Dagger Mastery
        { 164, new("Deadly Arts +1 (Non-stacking)", "Rune_of_Deadly_Arts") }, // Rune of Minor Deadly Arts
        { 165, new("Shadow Arts +1 (Non-stacking)", "Rune_of_Shadow_Arts") }, // Rune of Minor Shadow Arts
        { 166, new("Channeling Magic +1 (Non-stacking)", "Rune_of_Channeling_Magic") }, // Rune of Minor Channeling Magic
        { 167, new("Restoration Magic +1 (Non-stacking)", "Rune_of_Restoration_Magic") }, // Rune of Minor Restoration Magic
        { 168, new("Communing +1 (Non-stacking)", "Rune_of_Communing") }, // Rune of Minor Communing
        { 169, new("Spawning Power +1 (Non-stacking)", "Rune_of_Spawning_Power") }, // Rune of Minor Spawning Power
        { 170, new("Critical Strikes +2 (Non-stacking) ; Health -35", "Rune_of_Critical_Strikes") }, // Rune of Major Critical Strikes
        { 171, new("Dagger Mastery +2 (Non-stacking) ; Health -35", "Rune_of_Dagger_Mastery") }, // Rune of Major Dagger Mastery
        { 172, new("Deadly Arts +2 (Non-stacking) ; Health -35", "Rune_of_Deadly_Arts") }, // Rune of Major Deadly Arts
        { 173, new("Shadow Arts +2 (Non-stacking) ; Health -35", "Rune_of_Shadow_Arts") }, // Rune of Major Shadow Arts
        { 174, new("Channeling Magic +2 (Non-stacking) ; Health -35", "Rune_of_Channeling_Magic") }, // Rune of Major Channeling Magic
        { 175, new("Restoration Magic +2 (Non-stacking) ; Health -35", "Rune_of_Restoration_Magic") }, // Rune of Major Restoration Magic
        { 176, new("Communing +2 (Non-stacking) ; Health -35", "Rune_of_Communing") }, // Rune of Major Communing
        { 177, new("Spawning Power +2 (Non-stacking) ; Health -35", "Rune_of_Spawning_Power") }, // Rune of Major Spawning Power
        { 178, new("Critical Strikes +3 (Non-stacking) ; Health -75", "Rune_of_Critical_Strikes") }, // Rune of Superior Critical Strikes
        { 179, new("Dagger Mastery +3 (Non-stacking) ; Health -75", "Rune_of_Dagger_Mastery") }, // Rune of Superior Dagger Mastery
        { 180, new("Deadly Arts +3 (Non-stacking) ; Health -75", "Rune_of_Deadly_Arts") }, // Rune of Superior Deadly Arts
        { 181, new("Shadow Arts +3 (Non-stacking) ; Health -75", "Rune_of_Shadow_Arts") }, // Rune of Superior Shadow Arts
        { 182, new("Channeling Magic +3 (Non-stacking) ; Health -75", "Rune_of_Channeling_Magic") }, // Rune of Superior Channeling Magic
        { 183, new("Restoration Magic +3 (Non-stacking) ; Health -75", "Rune_of_Restoration_Magic") }, // Rune of Superior Restoration Magic
        { 184, new("Communing +3 (Non-stacking) ; Health -75", "Rune_of_Communing") }, // Rune of Superior Communing
        { 185, new("Spawning Power +3 (Non-stacking) ; Health -75", "Rune_of_Spawning_Power") }, // Rune of Superior Spawning Power
        { 186, new("Cold damage", "Icy") }, // Icy Dagger Tang
        { 187, new("Earth damage", "Ebon") }, // Ebon Dagger Tang
        { 188, new("Fire damage", "Fiery") }, // Fiery Dagger Tang
        { 189, new("Lightning damage", "Shocking") }, // Shocking Dagger Tang
        { 190, new("Energy gain on hit: 1 ; Energy regeneration: -1", "Zealous") }, // Zealous Dagger Tang
        { 191, new("Life Draining: 5, 3 ; Health regeneration: -1", "Vampiric") }, // Vampiric Dagger Tang
        { 192, new("Lengthens Bleeding duration on foes by 33%", "Barbed") }, // Barbed Dagger Tang
        { 193, new("Lengthens Crippled duration on foes by 33%", "Crippling") }, // Crippling Dagger Tang
        { 194, new("Lengthens Deep Wound duration on foes by 33%", "Cruel") }, // Cruel Dagger Tang
        { 195, new("Lengthens Poison duration on foes by 33%", "Poisonous") }, // Poisonous Dagger Tang
        { 196, new("Lengthens Dazed duration on foes by 33%", "Silencing") }, // Silencing Dagger Tang
        { 197, new("Double Adrenaline on hit (Chance: 10%)", "Furious") }, // Furious Dagger Tang
        { 198, new("Leadership +1 (Non-stacking)", "Rune_of_Leadership") }, // Rune of Minor Leadership
        { 199, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Dagger Handle of Mastery
        { 200, new("Armor +5", "Of_Defense") }, // Dagger Handle of Defense
        { 201, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Dagger Handle of Shelter
        { 202, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Dagger Handle of Warding
        { 203, new("Enchantments last 20% longer", "Of_Enchanting") }, // Dagger Handle of Enchanting
        { 204, new("Health +30", "Of_Fortitude") }, // Dagger Handle of Fortitude
        { 205, new("Lengthens Bleeding duration on foes by 33%", "Barbed") }, // Barbed Bow String
        { 206, new("Lengthens Crippled duration on foes by 33%", "Crippling") }, // Crippling Bow String
        { 207, new("Lengthens Dazed duration on foes by 33%", "Silencing") }, // Silencing Bow String
        { 208, new("Armor penetration +20% (Chance: 20%)", "Sundering") }, // Sundering Axe Haft
        { 209, new("Armor penetration +20% (Chance: 20%)", "Sundering") }, // Sundering Bow String
        { 210, new("Armor penetration +20% (Chance: 20%)", "Sundering") }, // Sundering Hammer Haft
        { 211, new("Armor penetration +20% (Chance: 20%)", "Sundering") }, // Sundering Sword Hilt
        { 212, new("Armor penetration +20% (Chance: 20%)", "Sundering") }, // Sundering Dagger Tang
        { 213, new("Motivation +1 (Non-stacking)", "Rune_of_Motivation") }, // Rune of Minor Motivation
        { 214, new("Command +1 (Non-stacking)", "Rune_of_Command") }, // Rune of Minor Command
        { 215, new("Spear Mastery +1 (Non-stacking)", "Rune_of_Spear_Mastery") }, // Rune of Minor Spear Mastery
        { 216, new("Mysticism +2 (Non-stacking) ; Health -35", "Rune_of_Mysticism") }, // Rune of Major Mysticism
        { 217, new("Earth Prayers +2 (Non-stacking) ; Health -35", "Rune_of_Earth_Prayers") }, // Rune of Major Earth Prayers
        { 218, new("Scythe Mastery +2 (Non-stacking) ; Health -35", "Rune_of_Scythe_Mastery") }, // Rune of Major Scythe Mastery
        { 219, new("Wind Prayers +2 (Non-stacking) ; Health -35", "Rune_of_Wind_Prayers") }, // Rune of Major Wind Prayers
        { 220, new("Leadership +2 (Non-stacking) ; Health -35", "Rune_of_Leadership") }, // Rune of Major Leadership
        { 221, new("Motivation +2 (Non-stacking) ; Health -35", "Rune_of_Motivation") }, // Rune of Major Motivation
        { 222, new("Command +2 (Non-stacking) ; Health -35", "Rune_of_Command") }, // Rune of Major Command
        { 223, new("Spear Mastery +2 (Non-stacking) ; Health -35", "Rune_of_Spear_Mastery") }, // Rune of Major Spear Mastery
        { 224, new("Mysticism +3 (Non-stacking) ; Health -75", "Rune_of_Mysticism") }, // Rune of Superior Mysticism
        { 225, new("Earth Prayers +3 (Non-stacking) ; Health -75", "Rune_of_Earth_Prayers") }, // Rune of Superior Earth Prayers
        { 226, new("Scythe Mastery +3 (Non-stacking) ; Health -75", "Rune_of_Scythe_Mastery") }, // Rune of Superior Scythe Mastery
        { 227, new("Wind Prayers +3 (Non-stacking) ; Health -75", "Rune_of_Wind_Prayers") }, // Rune of Superior Wind Prayers
        { 228, new("Leadership +3 (Non-stacking) ; Health -75", "Rune_of_Leadership") }, // Rune of Superior Leadership
        { 229, new("Motivation +3 (Non-stacking) ; Health -75", "Rune_of_Motivation") }, // Rune of Superior Motivation
        { 230, new("Command +3 (Non-stacking) ; Health -75", "Rune_of_Command") }, // Rune of Superior Command
        { 231, new("Spear Mastery +3 (Non-stacking) ; Health -75", "Rune_of_Spear_Mastery") }, // Rune of Superior Spear Mastery
        { 232, new("Cold damage", "Icy") }, // Icy Scythe Snathe
        { 233, new("Earth damage", "Ebon") }, // Ebon Scythe Snathe
        { 234, new("Energy gain on hit: 1 ; Energy regeneration: -1", "Zealous") }, // Zealous Scythe Snathe
        { 235, new("Life Draining: 5, 3 ; Health regeneration: -1", "Vampiric") }, // Vampiric Scythe Snathe
        { 236, new("Armor penetration +20% (Chance: 20%)", "Sundering") }, // Sundering Scythe Snathe
        { 237, new("Lengthens Bleeding duration on foes by 33%", "Barbed") }, // Barbed Scythe Snathe
        { 238, new("Lengthens Crippled duration on foes by 33%", "Crippling") }, // Crippling Scythe Snathe
        { 239, new("Lengthens Deep Wound duration on foes by 33%", "Cruel") }, // Cruel Scythe Snathe
        { 240, new("Double Adrenaline on hit (Chance: 10%)", "Furious") }, // Furious Scythe Snathe
        { 241, new("Lengthens Poison duration on foes by 33%", "Poisonous") }, // Poisonous Scythe Snathe
        { 242, new("Lengthens Weakness duration on foes by 33%", "Heavy") }, // Heavy Scythe Snathe
        { 243, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Scythe Grip of Mastery
        { 244, new("Armor +5", "Of_Defense") }, // Scythe Grip of Defense
        { 245, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Scythe Grip of Shelter
        { 246, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Scythe Grip of Warding
        { 247, new("Enchantments last 20% longer", "Of_Enchanting") }, // Scythe Grip of Enchanting
        { 248, new("Health +30", "Of_Fortitude") }, // Scythe Grip of Fortitude
        { 249, new("Fire damage", "Fiery") }, // Fiery Spearhead
        { 250, new("Lightning damage", "Shocking") }, // Shocking Spearhead
        { 251, new("Energy gain on hit: 1 ; Energy regeneration: -1", "Zealous") }, // Zealous Spearhead
        { 252, new("Life Draining: 5, 3 ; Health regeneration: -1", "Vampiric") }, // Vampiric Spearhead
        { 253, new("Armor penetration +20% (Chance: 20%)", "Sundering") }, // Sundering Spearhead
        { 254, new("Lengthens Bleeding duration on foes by 33%", "Barbed") }, // Barbed Spearhead
        { 255, new("Lengthens Crippled duration on foes by 33%", "Crippling") }, // Crippling Spearhead
        { 256, new("Lengthens Deep Wound duration on foes by 33%", "Cruel") }, // Cruel Spearhead
        { 257, new("Double Adrenaline on hit (Chance: 10%)", "Furious") }, // Furious Spearhead
        { 258, new("Lengthens Poison duration on foes by 33%", "Poisonous") }, // Poisonous Spearhead
        { 259, new("Lengthens Dazed duration on foes by 33%", "Silencing") }, // Silencing Spearhead
        { 260, new("Lengthens Weakness duration on foes by 33%", "Heavy") }, // Heavy Spearhead
        { 261, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Spear Grip of Mastery
        { 262, new("Armor +5", "Of_Defense") }, // Spear Grip of Defense
        { 263, new("Armor +7 (vs. physical damage)", "Of_Shelter") }, // Spear Grip of Shelter
        { 264, new("Armor +7 (vs. elemental damage)", "Of_Warding") }, // Spear Grip of Warding
        { 265, new("Enchantments last 20% longer", "Of_Enchanting") }, // Spear Grip of Enchanting
        { 266, new("Health +30", "Of_Fortitude") }, // Spear Grip of Fortitude
        { 267, new("Health +45 (while in a Stance)", "Of_Endurance") }, // Focus Core of Endurance
        { 268, new("Health +60 (while Hexed)", "Of_Valor") }, // Focus Core of Valor
        { 269, new("Fire damage", "Fiery") }, // Fiery Scythe Snathe
        { 270, new("Lightning damage", "Shocking") }, // Shocking Scythe Snathe
        { 271, new("Cold damage", "Icy") }, // Icy Spearhead
        { 272, new("Earth damage", "Ebon") }, // Ebon Spearhead
        { 273, new("Halves casting time of spells (Chance: 10%)", "Swift") }, // Swift Staff Head
        { 274, new("Health +45 (while Enchanted)", "Of_Devotion") }, // Staff Wrapping of Devotion
        { 275, new("Health +45 (while in a Stance)", "Of_Endurance") }, // Staff Wrapping of Endurance
        { 276, new("Health +60 (while Hexed)", "Of_Valor") }, // Staff Wrapping of Valor
        { 277, new("Halves skill recharge of spells (Chance: 10%)", "Inscription#Let_the_Memory_Live_Again") }, // "Let the Memory Live Again"
        { 278, new("Energy +5 (while Enchanted)", "Inscription#Have_Faith") }, // "Have Faith"
        { 279, new("Energy +7 (while Health is below 50%)", "Inscription#Don't_call_it_a_comeback!") }, // "Don't call it a comeback!"
        { 280, new("Energy +7 (while Hexed)", "Inscription#I_am_Sorrow.") }, // "I am Sorrow"
        { 281, new("Halves casting time of spells (Chance: 10%)", "Inscription#Don't_Think_Twice") }, // "Don't Think Twice"
        { 282, new("Damage +15% (vs. Hexed foes)", "Inscription#Too_Much_Information") }, // "Too Much Information"
        { 283, new("Damage +15% (while Enchanted)", "Inscription#Guided_by_Fate") }, // "Guided by Fate"
        { 284, new("Reduces Dazed duration on you by 20% (Stacking)", "Inscription#Soundness_of_Mind") }, // "Soundness of Mind"
        { 285, new("Reduces Weakness duration on you by 20% (Stacking)", "Inscription#Only_the_Strong_Survive") }, // "Only the Strong Survive"
        { 286, new("Damage +20% (while Hexed)", "Inscription#Don't_Fear_the_Reaper") }, // "Don't Fear the Reaper"
        { 287, new("Damage +15% (while in a Stance)", "Inscription#Dance_with_Death") }, // "Dance with Death"
        { 288, new("Damage +15% ; Energy -5", "Inscription#Brawn_over_Brains") }, // "Brawn over Brains"
        { 289, new("Damage +15% ; Armor -10 (while attacking)", "Inscription#To_the_Pain!") }, // "To the Pain!"
        { 290, new("Health +15 (on chest armor) ; Health +10 (on leg armor) ; Health +5 (on other armor)", "Survivor_Insignia") }, // Survivor Insignia
        { 291, new("Energy +3 (on chest armor) ; Energy +2 (on leg armor) ; Energy +1 (on other armor)", "Radiant_Insignia") }, // Radiant Insignia
        { 292, new("Armor +10 (vs. physical damage)", "Stalwart_Insignia") }, // Stalwart Insignia
        { 293, new("Armor +10 (while attacking)", "Brawler%27s_Insignia") }, // Brawler's Insignia
        { 294, new("Armor +10 (while affected by an Enchantment Spell)", "Blessed_Insignia") }, // Blessed Insignia
        { 295, new("Armor +10 (while holding an item)", "Herald%27s_Insignia") }, // Herald's Insignia
        { 296, new("Armor +10 (while in a stance)", "Sentry%27s_Insignia") }, // Sentry's Insignia
        { 297, new("Armor +10 (vs. physical damage) ; Armor +10 (vs. Blunt damage)", "Vanguard%27s_Insignia") }, // Vanguard's Insignia
        { 298, new("Armor +10 (vs. physical damage) ; Armor +10 (vs. Piercing damage)", "Infiltrator%27s_Insignia") }, // Infiltrator's Insignia
        { 299, new("Armor +10 (vs. physical damage) ; Armor +10 (vs. Slashing damage)", "Saboteur%27s_Insignia") }, // Saboteur's Insignia
        { 300, new("Armor +15 (while attacking)", "Nightstalker%27s_Insignia") }, // Nightstalker's Insignia
        { 301, new("Armor +15 (while activating skills)", "Virtuoso%27s_Insignia") }, // Virtuoso's Insignia
        { 302, new("Reduces casting time of spells ; that exploit corpses by 25% (Non-stacking)", "Bloodstained_Insignia") }, // Bloodstained Insignia
        { 303, new("Holy damage you receive increased by 6 (on chest armor) ; Holy damage you receive increased by 4 (on leg armor) ; Holy damage you receive increased by 2 (on other armor) ; Armor +10", "Tormentor%27s_Insignia") }, // Tormentor's Insignia
        { 304, new("Armor +15 (vs. Piercing damage)", "Bonelace_Insignia") }, // Bonelace Insignia
        { 305, new("Armor +5 (while you control 1 or more minions) ; Armor +5 (while you control 3 or more minions) ; Armor +5 (while you control 5 or more minions)", "Minion_Master%27s_Insignia") }, // Minion Master's Insignia
        { 306, new("Armor +20 (while affected by a Hex Spell)", "Blighter%27s_Insignia") }, // Blighter's Insignia
        { 307, new("Armor +10 (vs. elemental damage) ; Armor +10 (vs. Cold damage)", "Hydromancer_Insignia") }, // Hydromancer's Insignia
        { 308, new("Armor +10 (vs. elemental damage) ; Armor +10 (vs. Earth damage)", "Geomancer_Insignia") }, // Geomancer's Insignia
        { 309, new("Armor +10 (vs. elemental damage) ; Armor +10 (vs. Fire damage)", "Pyromancer_Insignia") }, // Pyromancer's Insignia
        { 310, new("Armor +10 (vs. elemental damage) ; Armor +10 (vs. Lightning damage)", "Aeromancer_Insignia") }, // Aeromancer's Insignia
        { 311, new("Armor +10 (vs. elemental damage)", "Wanderer%27s_Insignia") }, // Wanderer's Insignia
        { 312, new("Armor +15 (while affected by a Condition)", "Disciple%27s_Insignia") }, // Disciple's Insignia
        { 313, new("Received physical damage -3", "Knight%27s_Insignia") }, // Knight's Insignia
        { 314, new("Reduces Hex durations on you by 20% ; and damage dealt by you by 5% (Non-stacking) ; Armor -20", "Lieutenant%27s_Insignia") }, // Lieutenant's Insignia
        { 315, new("Increases knockdown time on foes by 1 second. ; (Maximum: 3 seconds)", "Stonefist_Insignia") }, // Stonefist Insignia
        { 316, new("Armor +10 (vs. elemental damage)", "Dreadnought_Insignia") }, // Dreadnought Insignia
        { 317, new("Armor +20 (Requires 13 Strength, vs. elemental damage)", "Sentinel%27s_Insignia") }, // Sentinel's Insignia
        { 318, new("Armor +15 (vs. Cold damage)", "Frostbound_Insignia") }, // Frostbound Insignia
        { 319, new("Armor +15 (vs. Fire damage)", "Pyrebound_Insignia") }, // Pyrebound Insignia
        { 320, new("Armor +15 (vs. Lightning damage)", "Stormbound_Insignia") }, // Stormbound Insignia
        { 321, new("Armor +10 (while using a Preparation)", "Scout%27s_Insignia") }, // Scout's Insignia
        { 322, new("Armor +5 (while you control 1 or more Spirits) ; Armor +5 (while you control 2 or more Spirits) ; Armor +5 (while you control 3 or more Spirits)", "Shaman%27s_Insignia") }, // Shaman's Insignia
        { 323, new("Armor +15 (while affected by a Weapon Spell)", "Ghost_Forge_Insignia") }, // Ghost Forge Insignia
        { 324, new("Armor +15 (while activating skills)", "Mystic%27s_Insignia") }, // Mystic's Insignia
        { 325, new("Armor +5 (while Enchanted)", "Inscription#Faith_is_My_Shield") }, // "Faith is My Shield"
        { 326, new("Energy +15 ; Energy regeneration -1", "Inscription#Live_for_Today") }, // "Live for Today"
        { 327, new("Halves skill recharge of spells (Chance: 10%)", "Inscription#Serenity_Now") }, // "Serenity Now"
        { 328, new("Halves skill recharge of spells of item's attribute (Chance: 20%)", "Inscription#Forget_Me_Not") }, // "Forget Me Not"
        { 329, new("Energy +5", "Inscription#I_have_the_power!") }, // "I have the power!"
        { 330, new("Received physical damage -5 (Chance: 20%)", "Inscription#Luck_of_the_Draw") }, // "Luck of the Draw"
        { 331, new("Received physical damage -2 (while Enchanted)", "Inscription#Sheltered_by_Faith") }, // "Sheltered by Faith"
        { 332, new("Received physical damage -3 (while Hexed)", "Inscription#Nothing_to_Fear") }, // "Nothing to Fear"
        { 333, new("Received physical damage -2 (while in a Stance)", "Inscription#Run_For_Your_Life!") }, // "Run For Your Life!"
        { 334, new("Item's attribute +1 (Chance: 20%)", "Inscription#Master_of_My_Domain!") }, // "Master of My Domain"
        { 335, new("Halves casting time of spells of item's attribute (Chance: 20%)", "Inscription#Aptitude_not_Attitude") }, // "Aptitude not Attitude"
        { 336, new("Energy +15 ; Energy regeneration -1", "Inscription#Seize_the_Day") }, // "Seize the Day"
        { 337, new("Energy +5 (while Health is above 50%)", "Inscription#Hale_and_Hearty") }, // "Hale and Hearty"
        { 338, new("Damage +15% (while Health is above 50%)", "Inscription#Strength_and_Honor") }, // "Strength and Honor"
        { 339, new("Damage +20% (while Health is below 50%)", "Inscription#Vengeance_is_Mine") }, // "Vengeance is Mine"
        { 340, new("Health +30", "Of_Fortitude") }, // Focus Core of Fortitude
        { 341, new("Health +45 (while Enchanted)", "Of_Devotion") }, // Focus Core of Devotion
        { 342, new("Halves casting time of spells (Chance: 10%)", "Of_Swiftness") }, // Focus Core of Swiftness
        { 343, new("Halves casting time of item's attribute spells (Chance: 20%)", "Of_Aptitude") }, // Focus Core of Aptitude
        { 344, new("Halves skill recharge of spells (Chance: 10%)", "Of_Quickening") }, // Wand Wrapping of Quickening
        { 345, new("Halves skill recharge of item's attribute spells (Chance: 20%)", "Of_Memory") }, // Wand Wrapping of Memory
        { 346, new("Health +30", "Of_Fortitude") }, // Shield Handle of Fortitude
        { 347, new("Health +45 (while Enchanted)", "Of_Devotion") }, // Shield Handle of Devotion
        { 348, new("Health +45 (while in a Stance)", "Of_Endurance") }, // Shield Handle of Endurance
        { 349, new("Health +60 (while Hexed)", "Of_Valor") }, // Shield Handle of Valor
        { 350, new("Halves casting time of spells of item's attribute (Chance: 20%)", "Adept") }, // Adept Staff Head
        { 351, new("Item's attribute +1 (20% chance while using skills)", "Of_Mastery") }, // Staff Wrapping of Mastery
        { 352, new("Energy +2", "Rune_of_Attunement") }, // Rune of Attunement
        { 353, new("Health +10", "Rune_of_Vitae") }, // Rune of Vitae
        { 354, new("Reduces Dazed and Deep Wound durations on you by 20% (Non-stacking)", "Rune_of_Recovery") }, // Rune of Recovery
        { 355, new("Reduces Bleeding and Crippled durations on you by 20% (Non-stacking)", "Rune_of_Restoration") }, // Rune of Restoration
        { 356, new("Reduces Blind and Weakness durations on you by 20% (Non-stacking)", "Rune_of_Clarity") }, // Rune of Clarity
        { 357, new("Reduces Disease and Poison durations on you by 20% (Non-stacking)", "Rune_of_Purity") }, // Rune of Purity
        { 358, new("Armor +3 (for each equipped Signet)", "Artificer%27s_Insignia") }, // Artificer's Insignia
        { 359, new("Armor +5 (while recharging 1 or more skills) ; Armor +5 (while recharging 3 or more skills) ; Armor +5 (while recharging 5 or more skills)", "Prodigy%27s_Insignia") }, // Prodigy's Insignia
        { 360, new("Armor +5 (while health is below 80%) ; Armor +5 (while health is below 60%) ; Armor +5 (while health is below 40%) ; Armor +5 (while health is below 20%)", "Undertaker%27s_Insignia") }, // Undertaker's Insignia
        { 361, new("Armor +5 (requires 9 Air Magic) ; Armor +5 (requires 9 Earth Magic) ; Armor +5 (requires 9 Fire Magic) ; Armor +5 (requires 9 Water Magic)", "Prismatic_Insignia") }, // Prismatic Insignia
        { 362, new("Armor +5 (while recharging 1 or more skills) ; Armor +5 (while recharging 3 or more skills) ; Armor +5 (while recharging 5 or more skills)", "Anchorite%27s_Insignia") }, // Anchorite's Insignia
        { 363, new("Armor +15 (vs. Earth damage)", "Earthbound_Insignia") }, // Earthbound Insignia
        { 364, new("Armor +10 (while your pet is alive)", "Beastmaster%27s_Insignia") }, // Beastmaster's Insignia
        { 365, new("Armor +5 (while affected by 1 or more Enchantment Spells) ; Armor +5 (while affected by 2 or more Enchantment Spells) ; Armor +5 (while affected by 3 or more Enchantment Spells) ; Armor +5 (while affected by 4 or more Enchantment Spells)", "Windwalker_Insignia") }, // Windwalker Insignia
        { 366, new("Armor +10 (while not affected by an Enchantment Spell)", "Forsaken_Insignia") }, // Forsaken Insignia
        { 367, new("Armor +10 (while affected by a Shout, Echo, or Chant)", "Centurion%27s_Insignia") }, // Centurion's Insignia
        { 368, new("Armor +5 ; Energy -5", "Inscription#Ignorance_is_Bliss") }, // "Ignorance is Bliss"
        { 369, new("Armor +5 ; Health -20", "Inscription#Life_is_Pain") }, // "Life is Pain"
        { 370, new("Armor +5 (vs. Elemental damage)", "Inscription#Man_for_All_Seasons") }, // "Man for All Seasons"
        { 371, new("Armor +5 (vs. Physical damage)", "Inscription#Survival_of_the_Fittest") }, // "Survival of the Fittest"
        { 372, new("Armor +5 (while attacking)", "Inscription#Might_Makes_Right") }, // "Might makes Right"
        { 373, new("Armor +5 (while casting)", "Inscription#Knowing_is_Half_the_Battle") }, // "Knowing is Half the Battle"
        { 374, new("Armor +10 (while Health is below 50%)", "Inscription#Down_But_Not_Out") }, // "Down But Not Out"
        { 375, new("Armor +5 (while Health is above 50%)", "Inscription#Hail_to_the_King") }, // "Hail to the King"
        { 376, new("Armor +10 (while hexed)", "Inscription#Be_Just_and_Fear_Not") }, // "Be Just and Fear Not"
        { 377, new("Armor +10 (vs. Blunt damage)", "Inscription#Not_the_Face!") }, // "Not the face!"
        { 378, new("Armor +10 (vs. Cold damage)", "Inscription#Leaf_on_the_Wind") }, // "Leaf on the Wind"
        { 379, new("Armor +10 (vs. Earth damage)", "Inscription#Like_a_Rolling_Stone") }, // "Like a Rolling Stone"
        { 380, new("Armor +10 (vs. Lightning damage)", "Inscription#Riders_on_the_Storm") }, // "Riders on the Storm"
        { 381, new("Armor +10 (vs. Fire damage)", "Inscription#Sleep_Now_in_the_Fire") }, // "Sleep Now in the Fire"
        { 382, new("Armor +10 (vs. Piercing damage)", "Inscription#Through_Thick_and_Thin") }, // "Through Thick and Thin"
        { 383, new("Armor +10 (vs. Slashing damage)", "Inscription#The_Riddle_of_Steel") }, // "The Riddle of Steel"
        { 384, new("Reduces Bleeding duration on you by 20% (Stacking)", "Inscription#Fear_Cuts_Deeper") }, // "Fear Cuts Deeper"
        { 385, new("Reduces Blind duration on you by 20% (Stacking)", "Inscription#I_Can_See_Clearly_Now") }, // "I Can See Clearly Now"
        { 386, new("Reduces Crippled duration on you by 20% (Stacking)", "Inscription#Swift_as_the_Wind") }, // "Swift as the Wind"
        { 387, new("Reduces Deep Wound duration on you by 20% (Stacking)", "Inscription#Strength_of_Body") }, // "Strength of Body"
        { 388, new("Reduces Disease duration on you by 20% (Stacking)", "Inscription#Cast_Out_the_Unclean") }, // "Cast Out the Unclean"
        { 389, new("Reduces Poison duration on you by 20% (Stacking)", "Inscription#Pure_of_Heart") }, // "Pure of Heart"
    };
}
