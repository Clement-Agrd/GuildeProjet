using System;
using UnityEngine;

[Serializable]
public class AdventurerStats
{
    [Header("Stats primaires (1–100)")]
    public int vitalite     = 10;
    public int resistance   = 10;
    public int force        = 10;
    public int intelligence = 10;
    public int charisme     = 10;
    public int sagesse      = 10;
    public int agilite      = 10;
    public int chance       = 10;

    [Header("Stats secondaires (0–100)")]
    public float moral   = 70f;
    public float fatigue = 0f;
    public float loyaute = 50f;

    [Header("Progression")]
    public int level                 = 1;
    public int experience            = 0;
    public int experienceToNextLevel = 100;

    public AdventurerStats() { }

    public AdventurerStats(AdventurerStats src)
    {
        vitalite     = src.vitalite;
        resistance   = src.resistance;
        force        = src.force;
        intelligence = src.intelligence;
        charisme     = src.charisme;
        sagesse      = src.sagesse;
        agilite      = src.agilite;
        chance       = src.chance;
        moral        = src.moral;
        fatigue      = src.fatigue;
        loyaute      = src.loyaute;
        level        = src.level;
        experience   = src.experience;
        experienceToNextLevel = src.experienceToNextLevel;
    }

    // ─── Accès générique ──────────────────────────────────────────────────────

    public int GetPrimary(PrimaryStat stat) => stat switch
    {
        PrimaryStat.Vitalite     => vitalite,
        PrimaryStat.Resistance   => resistance,
        PrimaryStat.Force        => force,
        PrimaryStat.Intelligence => intelligence,
        PrimaryStat.Charisme     => charisme,
        PrimaryStat.Sagesse      => sagesse,
        PrimaryStat.Agilite      => agilite,
        PrimaryStat.Chance       => chance,
        _                        => 0
    };

    public void AddToPrimary(PrimaryStat stat, int value)
    {
        switch (stat)
        {
            case PrimaryStat.Vitalite:     vitalite     += value; break;
            case PrimaryStat.Resistance:   resistance   += value; break;
            case PrimaryStat.Force:        force        += value; break;
            case PrimaryStat.Intelligence: intelligence += value; break;
            case PrimaryStat.Charisme:     charisme     += value; break;
            case PrimaryStat.Sagesse:      sagesse      += value; break;
            case PrimaryStat.Agilite:      agilite      += value; break;
            case PrimaryStat.Chance:       chance       += value; break;
        }
    }

    // ─── Scores composites pour le calcul des missions ────────────────────────

    public int GetPhysicalScore() => (force + vitalite + resistance) / 3;
    public int GetMagicalScore()  => (intelligence + charisme + sagesse) / 3;
    public int GetHarvestScore()  => (vitalite + agilite) / 2;
    public int GetSocialScore()   => (charisme + sagesse) / 2;

    // ─── Pénalités ────────────────────────────────────────────────────────────

    public float GetFatiguePenalty() => fatigue > 70f ? (fatigue - 70f) / 30f : 0f;
    public float GetMoralPenalty()   => moral   < 30f ? (30f - moral)   / 30f : 0f;

    // ─── Progression ──────────────────────────────────────────────────────────

    // Retourne true si montée de niveau
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
        loyaute = Mathf.Clamp(loyaute, 0f, 100f);
    }
}