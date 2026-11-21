using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Building_New", menuName = "Guildmaster/Building/BuildingDefinition")]
public class BuildingSO : ScriptableObject
{
    [Header("Identité")]
    public string       buildingName;
    public BuildingType buildingType;
    [TextArea(1, 3)] public string description;
    public Sprite icon;

    [Header("Progression")]
    public int maxLevel = 4;
    [Tooltip("index 0 = niveau 1")]
    public List<BuildingLevelData> levelData = new();

    [Header("Métier associé")]
    public JobType associatedJobType;

    public BuildingLevelData GetLevelData(int level)
    {
        if (levelData.Count == 0) return new BuildingLevelData();
        return levelData[Mathf.Clamp(level - 1, 0, levelData.Count - 1)];
    }
}

[Serializable]
public class BuildingLevelData
{
    [Header("Upgrade")]
    public int              goldCost;
    public List<ResourceCost> resourceCosts = new();
    public float            upgradeTime = 30f;

    [Header("Capacités")]
    public int   maxWorkerSlots       = 1;
    public float productionSpeedBonus = 0f;

    [TextArea(1, 2)] public string levelDescription;
}

// Struct partagé par BuildingSO, CraftRecipeSO et l'économie
[Serializable]
public class ResourceCost
{
    public ResourceSO resource;
    public int        amount = 1;
}