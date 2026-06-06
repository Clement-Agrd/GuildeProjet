using System;
using UnityEngine;

[Serializable]
public class AdventurerStats
{
    [Header("Stats primaires (1-100)")]
    public int force        = 10;
    public int agility      = 10;
    public int intelligence = 10;
    public int endurance    = 10;
    public int charisma     = 10;
    public int dexterity    = 10;

    [Header("Stats secondaires (0-100)")]
    public float moral   = 70f;
    public float fatigue = 0f;
    public float loyalty = 50f;
    public float stress  = 0f;

    [Header("Progression")]
    public int level                = 1;
    public int experience           = 0;
    public int experienceToNextLevel = 100;

    public AdventurerStats() { }

    // Copy constructor — utilisé pour instancier à partir d'un template SO
    public AdventurerStats(AdventurerStats source)
    {
        force        = source.force;
        agility      = source.agility;
        intelligence = source.intelligence;
        endurance    = source.endurance;
        charisma     = source.charisma;
        dexterity    = source.dexterity;
        moral        = source.moral;
        fatigue      = source.fatigue;
        loyalty      = source.loyalty;
        stress       = source.stress;
        level        = source.level;
        experience   = source.experience;
        experienceToNextLevel = source.experienceToNextLevel;
    }

    // Scores composites utilisés dans le calcul des missions
    public int GetCombatScore()  => (force + agility + dexterity) / 3;
    public int GetHarvestScore() => (endurance + dexterity) / 2;
    public int GetSocialScore()  => (charisma + intelligence) / 2;

    // Pénalités appliquées aux rolls de mission (0 = aucune, 1 = -100%)
    public float GetFatiguePenalty() => fatigue > 70f ? (fatigue - 70f) / 30f : 0f;
    public float GetMoralPenalty()   => moral   < 30f ? (30f - moral)   / 30f : 0f;

    // Retourne true si l'aventurier vient de monter de niveau
    public bool AddExperience(int amount)
    {
        experience += amount;
        if (experience >= experienceToNextLevel)
        {
            experience -= experienceToNextLevel;
            level++;
            experienceToNextLevel = Mathf.RoundToInt(experienceToNextLevel * 1.5f);
            return true;
        }
        return false;
    }

    public void ClampSecondaryStats()
    {
        moral   = Mathf.Clamp(moral,   0f, 100f);
        fatigue = Mathf.Clamp(fatigue, 0f, 100f);
        loyalty = Mathf.Clamp(loyalty, 0f, 100f);
        stress  = Mathf.Clamp(stress,  0f, 100f);
    }
}