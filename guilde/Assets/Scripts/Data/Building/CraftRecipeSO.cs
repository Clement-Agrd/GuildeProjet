using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Recipe_New", menuName = "Guildmaster/Building/CraftRecipe")]
public class CraftRecipeSO : ScriptableObject
{
    [Header("Identité")]
    public string  recipeName;
    [TextArea(1, 3)] public string description;
    public JobType requiredJob;
    [Range(0, 10)] public int requiredJobLevel = 0;
    public bool isUnlocked = true;

    [Header("Coûts")]
    public List<ResourceCost> inputResources = new();

    [Header("Output — renseigner l'un ou l'autre")]
    public ResourceSO  outputResource;
    public int         outputResourceAmount = 1;
    public EquipmentSO outputEquipment;      // prioritaire si non null

    [Header("Durée & valeur")]
    public float baseCraftDuration = 60f;
    public int   baseGoldValue     = 50;

    [Header("Chances de qualité (somme doit être égale à 1)")]
    [Range(0f, 1f)] public float normalChance    = 0.70f;
    [Range(0f, 1f)] public float rareChance      = 0.20f;
    [Range(0f, 1f)] public float epicChance      = 0.08f;
    [Range(0f, 1f)] public float legendaryChance = 0.02f;

    public bool ProducesEquipment => outputEquipment != null;

    public EquipmentQuality RollQuality(float qualityBonus)
    {
        // Le bonus redistribue du Normal vers les raretés supérieures
        float leg  = legendaryChance + qualityBonus * 0.15f;
        float epic = epicChance      + qualityBonus * 0.35f;
        float rare = rareChance      + qualityBonus * 0.50f;

        float roll = Random.value;
        if (roll < leg)                return EquipmentQuality.Legendary;
        if (roll < leg + epic)         return EquipmentQuality.Epic;
        if (roll < leg + epic + rare)  return EquipmentQuality.Rare;
        return EquipmentQuality.Normal;
    }

    public float GetActualDuration(float efficiencyBonus)
        => baseCraftDuration * Mathf.Max(0.2f, 1f - efficiencyBonus);

    public int GetGoldValueForQuality(EquipmentQuality quality) => quality switch
    {
        EquipmentQuality.Rare      => Mathf.RoundToInt(baseGoldValue * 2f),
        EquipmentQuality.Epic      => Mathf.RoundToInt(baseGoldValue * 4f),
        EquipmentQuality.Legendary => Mathf.RoundToInt(baseGoldValue * 10f),
        _                          => baseGoldValue
    };
}