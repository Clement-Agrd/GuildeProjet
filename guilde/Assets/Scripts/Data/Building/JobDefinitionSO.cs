using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Job_New", menuName = "Guildmaster/Building/JobDefinition")]
public class JobDefinitionSO : ScriptableObject
{
    [Header("Identité")]
    public string       jobName;
    public JobType      jobType;
    public BuildingType associatedBuilding;
    public Sprite       icon;
    [TextArea(1, 3)] public string description;

    [Header("Stats favorisées (poids, somme idéale = 1)")]
    public List<StatWeight> primaryStatWeights = new();

    [Header("Progression")]
    public int maxLevel = 10;
    [Tooltip("10 entrées — index 0 = niveau 1")]
    public List<JobLevelData> levelData = new();

    public float CalculatePerformanceScore(AdventurerStats stats)
    {
        float score = 0f;
        foreach (var w in primaryStatWeights)
            score += stats.GetPrimary(w.stat) * w.weight;
        return score;
    }

    public JobLevelData GetLevelData(int level)
    {
        if (level <= 0 || levelData.Count == 0) return new JobLevelData();
        return levelData[Mathf.Clamp(level - 1, 0, levelData.Count - 1)];
    }
}

[Serializable]
public class StatWeight
{
    public PrimaryStat stat;
    [Range(0f, 1f)] public float weight;
}

// ── Data par niveau ──────────────────────────────────────────────────────────

[Serializable]
public class JobLevelData
{
    public int experienceRequired = 100;

    [Header("Bonus de production")]
    [Range(0f, 1f)]
    [Tooltip("+X de chance d'obtenir une qualité supérieure")]
    public float qualityBonus = 0f;

    [Range(0f, 0.8f)]
    [Tooltip("Réduction de la durée de fabrication")]
    public float efficiencyBonus = 0f;

    [Tooltip("Multiplicateur du nombre d'items produits")]
    public float outputMultiplier = 1f;

    [Header("Coût")]
    [Tooltip("Multiplicateur du salaire de base — un expert coûte plus cher")]
    public float salaryMultiplier = 1f;

    [TextArea(1, 2)] public string levelDescription;
}

// ── Instance runtime (portée par Adventurer) ─────────────────────────────────

[Serializable]
public class JobInstance
{
    public JobType jobType;
    public int currentLevel      = 0; // 0 = non initié, pas de bonus
    public int currentExperience = 0;

    public JobInstance() { }
    public JobInstance(JobType type) { jobType = type; }

    /// <summary>Retourne true si montée de niveau.</summary>
    public bool AddExperience(JobDefinitionSO definition, int amount)
    {
        if (definition == null || currentLevel >= definition.maxLevel) return false;

        currentExperience += amount;
        int required = definition.GetLevelData(currentLevel + 1).experienceRequired;

        if (currentExperience >= required)
        {
            currentExperience -= required;
            currentLevel = Mathf.Min(currentLevel + 1, definition.maxLevel);
            return true;
        }
        return false;
    }

    public bool IsUntrained => currentLevel == 0;

    // Raccourcis sur les données du niveau courant
    public float GetQualityBonus(JobDefinitionSO def)      => def != null ? def.GetLevelData(currentLevel).qualityBonus : 0f;
    public float GetEfficiencyBonus(JobDefinitionSO def)   => def != null ? def.GetLevelData(currentLevel).efficiencyBonus : 0f;
    public float GetOutputMultiplier(JobDefinitionSO def)  => def != null ? def.GetLevelData(currentLevel).outputMultiplier : 1f;
    public float GetSalaryMultiplier(JobDefinitionSO def)  => def != null ? def.GetLevelData(currentLevel).salaryMultiplier : 1f;
}
