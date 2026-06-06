public enum AdventurerStateType { Idle, Working, OnMission, Resting, Injured, Dead }

public enum AffinityType { Stranger, Friend, BestFriend, Rival, Enemy, Lover, Mentor }

public enum Race { Human, Elf, Dwarf, Orc, Goblin, Goliath, Gnome }

public enum WeaponType { Sword, Axe, Mace, Bow, Staff, Grimoire, Dagger }

public enum EquipmentSlot { MainWeapon, OffHand, Armor, Helmet, Gloves, Boots, Accessory }

public enum EquipmentQuality { Normal, Rare, Epic, Legendary }

public enum BuildingType
{
    Lodge, TrainingRoom, Forge, Workshop, Library, Infirmary, Tavern, Jeweler, Recruiter, Market
}

public enum BuildingState { Active, Upgrading, Damaged }

public enum JobType
{
    None,
    Blacksmith,  // Forgeron → Forge
    Craftsman,   // Artisan  → Workshop
    Merchant,    // Marchand → Market
    Healer,      // Soigneur → Infirmary
    Cook,        // Cuisinier → Tavern
    Recruiter    // Recruteur → Recruiter building
}

public enum MissionState { Preparing, InProgress, Returning, Completed, Failed }

public enum MissionType { Harvest, Hunt, Escort, Exploration }

public enum ResourceType { Basic, Rare, Monster }

public enum SkillCategory { Combat, Harvest, Social, Craft }

public enum TraitPolarity { Positive, Negative }

public enum TraitEffectType
{
    Force_Multiplier,
    Agility_Multiplier,
    Intelligence_Multiplier,
    Endurance_Multiplier,
    Charisma_Multiplier,
    Dexterity_Multiplier,
    Moral_Flat,
    Loyalty_Flat,
    Stress_Flat,
    Salary_Multiplier,
    Commission_Multiplier,
    AffinityGain_Multiplier,
    MissionSuccess_Modifier,
    ExperienceGain_Multiplier,
    FatigueRecovery_Multiplier
}

public enum GameSpeed { Paused = 0, Normal = 1, Fast = 2, VeryFast = 3 }