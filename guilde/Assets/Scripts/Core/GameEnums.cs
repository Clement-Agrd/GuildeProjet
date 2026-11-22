public enum AdventurerStateType { Idle, Working, OnMission, Resting, Injured, Dead }
public enum AffinityType        { Stranger, Friend, BestFriend, Rival, Enemy, Lover, Mentor }
public enum Race                { Human, Elf, Dwarf, Orc, Goblin, Goliath, Gnome, Halfling, HalfElf, Tiefling }
public enum WeaponType          { Sword, Axe, Mace, Bow, Staff, Grimoire, Dagger }
public enum EquipmentSlot       { MainWeapon, OffHand, Armor, Helmet, Gloves, Boots, Accessory, Bracelet, Necklace, Ring }
public enum EquipmentQuality    { Normal, Rare, Epic, Legendary }
public enum BuildingState       { Active, Upgrading, Damaged }
public enum MissionState        { Preparing, InProgress, Returning, Completed, Failed }
public enum MissionType         { Harvest, Hunt, Escort, Exploration }
public enum ResourceType        { Basic, Rare, Monster }
public enum TraitPolarity       { Positive, Negative }
public enum GameSpeed           { Paused = 0, Normal = 1, Fast = 2, VeryFast = 3 }

public enum BuildingType
{
    Lodge, TrainingRoom, Forge, Workshop, Library, Infirmary, Tavern, Jeweler, Recruiter, Market, Headquarters
}

public enum JobType
{
    None, Blacksmith, Craftsman, Merchant, Healer, Cook, Recruiter, Trainer, Scout
}

public enum JobEffectType
{
    ForgeDurationReduction = 0, ForgeQuality = 1, WorkshopDurationReduction = 2, WorkshopQuality = 3,
    SalePrice = 4, HealingSpeed = 5, RecruitmentSpeed = 6, TrainedRecruitChance = 7,
    TrainingSpeed = 8, ScoutingSpeed = 9, ScoutingQuality = 10, TavernRecovery = 11, WorkFatigue = 12
}

// ─── Stats ────────────────────────────────────────────────────────────────────

public enum PrimaryStat
{
    Vitalite, Resistance, Force, Intelligence, Charisme, Sagesse, Agilite, Chance
}

public enum CombatStat
{
    PointsDeVie, DefensePhysique, DefenseMagique,
    AttaquePhysique, AttaqueMagique,
    ChanceCrit, DegatsCrit, ResistanceCrit,
    Esquive, Precision,
    PuissanceSoin, TauxRecuperation
}

// Regroupe PrimaryStat (0–7) et CombatStat (8–19) dans un seul enum
// — utilisé pour la stat principale d'un équipement
public enum StatType
{
    // Stats primaires — indices 0 à 7 (= valeurs de PrimaryStat)
    Vitalite = 0, Resistance = 1, Force = 2, Intelligence = 3,
    Charisme = 4, Sagesse    = 5, Agilite  = 6, Chance     = 7,
    // Stats de combat — indices 8 à 19 (= CombatStat + 8)
    PointsDeVie = 8, DefensePhysique = 9,  DefenseMagique  = 10,
    AttaquePhysique = 11, AttaqueMagique = 12,
    ChanceCrit  = 13, DegatsCrit = 14, ResistanceCrit = 15,
    Esquive     = 16, Precision  = 17,
    PuissanceSoin = 18, TauxRecuperation = 19
}

public enum TraitEffectType
{
    // Stats primaires
    Vitalite_Multiplier, Resistance_Multiplier, Force_Multiplier,
    Intelligence_Multiplier, Charisme_Multiplier, Sagesse_Multiplier,
    Agilite_Multiplier, Chance_Multiplier,
    // Stats secondaires
    Moral_Flat, Loyaute_Flat, Fatigue_Flat,
    // Économie & progression
    Salary_Multiplier, Commission_Multiplier, AffinityGain_Multiplier,
    MissionSuccess_Modifier, ExperienceGain_Multiplier, FatigueRecovery_Multiplier
}
