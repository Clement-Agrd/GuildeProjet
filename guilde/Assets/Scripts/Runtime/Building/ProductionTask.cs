using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ProductionTask
{
    // ─── Identité ─────────────────────────────────────────────────────────────
    public string taskId;
    public string recipeId;
    public string assignedAdventurerId;
    public JobType craftedByJobType;
    public ForgeTechnique forgeTechnique;

    [NonSerialized] public CraftRecipeSO recipe;
    [NonSerialized] private List<ResourceCost> _inputResources;

    public List<ResourceCost> InputResources => _inputResources ??= recipe?.GetInputs(forgeTechnique);

    // ─── Progression ──────────────────────────────────────────────────────────
    public float progress;
    public float totalDuration;
    public int seriesQuantity = 1;
    public int seriesUnit = 1;
    public bool awaitingInputs;
    public bool stopAfterCurrentUnit;
    internal string ComponentBlockReason { get; set; }
    public int LastCommission { get; internal set; }

    public bool IsSeries => seriesQuantity > 1;
    public bool HasNextUnit => !stopAfterCurrentUnit && seriesUnit < seriesQuantity;

    // ─── Résultat tiré à la création ─────────────────────────────────────────
    public EquipmentQuality rolledQuality;

    // ─── Propriétés ───────────────────────────────────────────────────────────
    public bool  IsComplete      => progress >= 1f;
    public float ProgressPercent => Mathf.Clamp01(progress) * 100f;

    // ─── Constructeurs ────────────────────────────────────────────────────────
    public ProductionTask() { }

    public ProductionTask(CraftRecipeSO recipe, string adventurerId,
        float duration, float qualityBonus, ForgeTechnique technique = ForgeTechnique.Standard)
    {
        taskId               = Guid.NewGuid().ToString();
        this.recipe          = recipe;
        recipeId             = recipe.name;
        assignedAdventurerId = adventurerId;
        craftedByJobType     = recipe.requiredJob;
        forgeTechnique       = technique;
        BeginUnit(duration, qualityBonus);
    }

    internal void BeginUnit(float duration, float qualityBonus)
    {
        totalDuration = Mathf.Max(0.1f, duration);
        progress = 0f;
        rolledQuality = recipe.RollQuality(qualityBonus, forgeTechnique);
        awaitingInputs = false;
    }

    // ─── Tick — retourne true si la tâche vient de se terminer ───────────────
    public bool Tick(float deltaTime)
    {
        if (awaitingInputs || IsComplete || deltaTime <= 0f) return false;
        progress += deltaTime / totalDuration;
        if (progress >= 1f) { progress = 1f; return true; }
        return false;
    }

    public void OnAfterLoad(CraftRecipeSO def)
    {
        recipe = def;
        _inputResources = null;
    }
}
