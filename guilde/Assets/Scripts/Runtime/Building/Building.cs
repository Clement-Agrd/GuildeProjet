using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Building
{
    // ─── Identité ─────────────────────────────────────────────────────────────
    public string       buildingId;
    public BuildingType buildingType;
    public string       definitionId;

    [NonSerialized] public BuildingSO definition;

    // ─── État ─────────────────────────────────────────────────────────────────
    public int           currentLevel         = 1;
    public BuildingState state                = BuildingState.Active;
    public float         upgradeTimeRemaining = 0f;

    // ─── Workers & production ─────────────────────────────────────────────────
    public List<string>         assignedAdventurerIds = new();
    public List<ProductionTask> activeTasks           = new();

    // ─── Events ───────────────────────────────────────────────────────────────
    [field: NonSerialized] public event Action<Building>                     OnUpgradeComplete;
    [field: NonSerialized] public event Action<Building, ProductionTask>     OnProductionComplete;

    // ─── Propriétés ───────────────────────────────────────────────────────────
    public int  MaxWorkers  => definition?.GetLevelData(currentLevel).maxWorkerSlots ?? 1;
    public bool HasFreeSlot => assignedAdventurerIds.Count < MaxWorkers;
    public bool IsActive    => state == BuildingState.Active;

    // ─── Constructeurs ────────────────────────────────────────────────────────
    public Building() { }

    public Building(BuildingSO def)
    {
        buildingId   = Guid.NewGuid().ToString();
        definition   = def;
        definitionId = def.name;
        buildingType = def.buildingType;
        currentLevel = 1;
        state        = BuildingState.Active;
    }

    // ─── Tick ─────────────────────────────────────────────────────────────────
    public void Tick(float deltaTime)
    {
        if (state == BuildingState.Upgrading) { TickUpgrade(deltaTime); return; }
        if (state == BuildingState.Active)    { TickProduction(deltaTime); }
    }

    private void TickUpgrade(float deltaTime)
    {
        upgradeTimeRemaining -= deltaTime;
        if (upgradeTimeRemaining <= 0f)
        {
            currentLevel         = Mathf.Min(currentLevel + 1, definition?.maxLevel ?? currentLevel);
            state                = BuildingState.Active;
            upgradeTimeRemaining = 0f;
            OnUpgradeComplete?.Invoke(this);
        }
    }

    private void TickProduction(float deltaTime)
    {
        float speedBonus = definition?.GetLevelData(currentLevel).productionSpeedBonus ?? 0f;
        float dt         = deltaTime * (1f + speedBonus);

        for (int i = activeTasks.Count - 1; i >= 0; i--)
        {
            // Une commande conserve son avancement quand son artisan se repose.
            if (!HasWorker(activeTasks[i].assignedAdventurerId)) continue;
            if (activeTasks[i].Tick(dt))
            {
                var task = activeTasks[i];
                activeTasks.RemoveAt(i);
                OnProductionComplete?.Invoke(this, task);
            }
        }
    }

    // ─── Workers ──────────────────────────────────────────────────────────────
    public bool AssignWorker(string adventurerId)
    {
        if (!HasFreeSlot || assignedAdventurerIds.Contains(adventurerId)) return false;
        assignedAdventurerIds.Add(adventurerId);
        return true;
    }

    public void RemoveWorker(string adventurerId) => assignedAdventurerIds.Remove(adventurerId);

    public bool HasWorker(string adventurerId) => assignedAdventurerIds.Contains(adventurerId);

    // ─── Production ───────────────────────────────────────────────────────────
    public void AddTask(ProductionTask task)    => activeTasks.Add(task);
    public void CancelTask(string taskId)       => activeTasks.RemoveAll(t => t.taskId == taskId);

    // ─── Upgrade ──────────────────────────────────────────────────────────────
    public bool CanUpgrade()
        => IsActive && definition != null && currentLevel < definition.maxLevel;

    public BuildingLevelData GetCurrentLevelData()
        => definition?.GetLevelData(currentLevel) ?? new BuildingLevelData();

    public BuildingLevelData GetNextLevelData()
        => definition?.GetLevelData(currentLevel + 1) ?? new BuildingLevelData();

    public void StartUpgrade()
    {
        state                = BuildingState.Upgrading;
        upgradeTimeRemaining = GetNextLevelData().upgradeTime;
    }

    // ─── Post-load ────────────────────────────────────────────────────────────
    public void OnAfterLoad(BuildingSO def) => definition = def;
}
