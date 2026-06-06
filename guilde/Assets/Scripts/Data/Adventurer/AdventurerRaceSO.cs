using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Race_New", menuName = "Guildmaster/Adventurer/Race")]
public class AdventurerRaceSO : ScriptableObject
{
    [Header("Identité")]
    public string raceName;
    [TextArea(2, 4)] public string description;
    public Race raceType;
    public Sprite icon;

    [Header("Modificateurs de stats primaires (additif)")]
    public int forceMod        = 0;
    public int agilityMod      = 0;
    public int intelligenceMod = 0;
    public int enduranceMod    = 0;
    public int charismaMod     = 0;
    public int dexterityMod    = 0;

    [Header("Relations inter-raciales")]
    [Tooltip("Modificateur sur la valeur d'affinité initiale avec les membres d'une autre race")]
    public List<RaceAffinityModifier> raceRelationModifiers = new();

    [Header("Traits raciaux automatiques")]
    [Tooltip("Ajoutés à tout aventurier de cette race à la création")]
    public List<TraitSO> racialTraits = new();

    /// <summary>Applique les modificateurs raciaux sur un AdventurerStats existant.</summary>
    public void ApplyToStats(AdventurerStats stats)
    {
        stats.force        += forceMod;
        stats.agility      += agilityMod;
        stats.intelligence += intelligenceMod;
        stats.endurance    += enduranceMod;
        stats.charisma     += charismaMod;
        stats.dexterity    += dexterityMod;
    }

    public int GetAffinityModifierWith(Race otherRace)
    {
        foreach (var mod in raceRelationModifiers)
            if (mod.targetRace == otherRace) return mod.affinityStartModifier;
        return 0;
    }
}

[Serializable]
public class RaceAffinityModifier
{
    public Race targetRace;
    [Range(-50, 50)] public int affinityStartModifier;
    [TextArea(1, 2)] public string loreNote;
}