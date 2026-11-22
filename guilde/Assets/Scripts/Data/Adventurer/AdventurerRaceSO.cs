using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Race_New", menuName = "Guildmaster/Adventurer/Race")]
public class AdventurerRaceSO : ScriptableObject
{
    [Header("Identité")]
    public string raceName;
    [TextArea(2, 4)] public string description;
    public Race   raceType;
    public Sprite icon;

    [Header("Modificateurs de stats primaires")]
    public List<PrimaryStatModifier> statModifiers = new();

    [Header("Bonus d’effet de métier")]
    public List<RacialJobEffect> jobEffects = new();

    [Header("Relations inter-raciales")]
    public List<RaceAffinityModifier> raceRelationModifiers = new();

    [Header("Traits raciaux automatiques")]
    [Tooltip("Ajoutés à tout aventurier de cette race à la création")]
    public List<TraitSO> racialTraits = new();

    public void ApplyToStats(AdventurerStats stats)
    {
        foreach (var m in statModifiers)
            stats.AddToPrimary(m.stat, m.value);
    }

    public int GetModifier(PrimaryStat stat)
    {
        foreach (var m in statModifiers)
            if (m.stat == stat) return m.value;
        return 0;
    }

    public float GetJobEffectBonus(JobEffectType effect)
    {
        if (jobEffects == null) return 0f;
        float bonus = 0f;
        foreach (var entry in jobEffects)
            if (entry != null && entry.effect == effect) bonus += entry.bonus;
        return bonus;
    }

    public int GetAffinityModifierWith(Race otherRace)
    {
        foreach (var mod in raceRelationModifiers)
            if (mod.targetRace == otherRace) return mod.affinityStartModifier;
        return 0;
    }
}

[Serializable]
public class RacialJobEffect
{
    public JobEffectType effect;
    [Tooltip("0,10 ajoute 10 points de pourcentage. Pour la fatigue, -0,10 retire 0,10 point par unité de travail.")]
    public float bonus;
}

[Serializable]
public class PrimaryStatModifier
{
    public PrimaryStat stat;
    public int         value;
}

[Serializable]
public class RaceAffinityModifier
{
    public Race targetRace;
    [Range(-50, 50)] public int affinityStartModifier;
    [TextArea(1, 2)] public string loreNote;
}
