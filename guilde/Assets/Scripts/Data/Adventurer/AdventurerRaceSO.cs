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

    public int GetAffinityModifierWith(Race otherRace)
    {
        foreach (var mod in raceRelationModifiers)
            if (mod.targetRace == otherRace) return mod.affinityStartModifier;
        return 0;
    }
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