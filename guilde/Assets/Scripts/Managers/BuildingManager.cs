using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BuildingManager : MonoBehaviour
{
    public const int MaxSeriesQuantity = 999;
    public static BuildingManager Instance { get; private set; }

    [SerializeField] private List<BuildingSO>      buildingDefinitions = new();
    [SerializeField] private List<JobDefinitionSO> jobDefinitions      = new();
    [SerializeField] private List<CraftRecipeSO>   allRecipes          = new();

    private readonly List<Building>                       _buildings    = new();
    private readonly Dictionary<BuildingType, Building>    _byType       = new();
    private readonly Dictionary<string, Building>          _byId         = new();
    private readonly Dictionary<JobType, JobDefinitionSO>  _jobByType    = new();
    private readonly Dictionary<string, CraftRecipeSO>     _recipeByName = new();
    private readonly HashSet<Adventurer> _observedWorkers = new();

    public event Action<Building>                 OnBuildingUnlocked;
    public event Action<Building>                 OnBuildingUpgraded;
    public event Action<Building, Adventurer>     OnAdventurerAssigned;
    public event Action<Building, Adventurer>     OnAdventurerUnassigned;
    public event Action<Building, ProductionTask> OnProductionCompleted;
    public event Action<Building, ProductionTask, string> OnProductionBlocked;
    public event Action<Adventurer, JobType, int> OnWorkerJobLevelUp;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        foreach (var jd in jobDefinitions) _jobByType[jd.jobType] = jd;
        foreach (var r  in allRecipes)     _recipeByName[r.name]  = r;
    }

    private void OnDestroy()
    {
        ResetForNewGame();
        foreach (var asset in _generatedDefinitions) Destroy(asset);
        if (Instance == this) Instance = null;
    }

    public void ResetForNewGame()
    {
        ResetGuildServices();
        foreach (var a in _observedWorkers) a.OnStateChanged -= HandleWorkerStateChanged;
        _observedWorkers.Clear();
        foreach (var b in _buildings)
        {
            b.OnUpgradeComplete -= HandleUpgradeComplete;
            b.OnProductionComplete -= HandleProductionComplete;
        }
        _buildings.Clear();
        _byType.Clear();
        _byId.Clear();
    }

    public void InitializeDefaultBuildings(List<BuildingType> startingTypes)
    {
        ConfigureGuildBuildings();
        foreach (var type in startingTypes)
        {
            if (type == BuildingType.Lodge) continue;
            var def = FindDefinition(type);
            if (def != null) UnlockBuilding(def);
        }
    }

    public void UnlockBuilding(BuildingSO def)
    {
        if (_byType.ContainsKey(def.buildingType)) return;
        var building = new Building(def);
        RegisterBuilding(building);
        OnBuildingUnlocked?.Invoke(building);
    }

    private void RegisterBuilding(Building b)
    {
        if (!_buildings.Contains(b)) _buildings.Add(b);
        _byType[b.buildingType] = b;
        _byId[b.buildingId]     = b;

        b.OnUpgradeComplete    -= HandleUpgradeComplete;
        b.OnUpgradeComplete    += HandleUpgradeComplete;
        b.OnProductionComplete -= HandleProductionComplete;
        b.OnProductionComplete += HandleProductionComplete;
    }

    public void Tick(float deltaTime)
    {
        foreach (var b in _buildings)
        {
            b.Tick(deltaTime);
            if (b.IsActive) StartWaitingUnits(b);
        }
        TickGuildServices(deltaTime);
    }

    // =========================================================================
    // Assignation
    // =========================================================================

    public bool AssignAdventurer(Adventurer adventurer, BuildingType buildingType)
    {
        var building = GetBuilding(buildingType);
        if (!CanTakePost(adventurer, building) || !building.HasFreeSlot) return false;
        if (!building.AssignWorker(adventurer.id)) return false;
        JobType jobType = building.definition.associatedJobType;

        if (_observedWorkers.Add(adventurer))
            adventurer.OnStateChanged += HandleWorkerStateChanged;

        adventurer.AssignToBuilding(building.buildingId, jobType);

        OnAdventurerAssigned?.Invoke(building, adventurer);
        return true;
    }

    private bool CanTakePost(Adventurer adventurer, Building building)
        => adventurer != null && adventurer.IsAvailable && AdventurerManager.Instance.GetById(adventurer.id) == adventurer
            && building?.IsActive == true && building.definition != null
            && building.definition.associatedJobType != JobType.None
            && _jobByType.ContainsKey(building.definition.associatedJobType);

    public bool ReplaceAdventurer(Adventurer incoming, BuildingType buildingType, string outgoingId)
    {
        var building = GetBuilding(buildingType);
        if (!CanTakePost(incoming, building) || building.HasFreeSlot || !building.HasWorker(outgoingId)) return false;
        var outgoing = AdventurerManager.Instance.GetById(outgoingId);
        if (outgoing?.currentStateType != AdventurerStateType.Working || outgoing.currentBuildingId != building.buildingId)
            return false;

        UnassignAdventurer(outgoing);
        return AssignAdventurer(incoming, buildingType);
    }

    public void UnassignAdventurer(Adventurer adventurer)
    {
        if (adventurer?.currentStateType != AdventurerStateType.Working) return;
        adventurer.TransitionToState(new IdleState(adventurer));
    }

    private void HandleWorkerStateChanged(Adventurer adventurer, AdventurerStateType previous,
        AdventurerStateType current)
    {
        if (previous != AdventurerStateType.Working || current == AdventurerStateType.Working) return;
        // Resting/Injured.Enter a déjà effacé currentBuildingId : retrouver le poste par son occupant.
        foreach (var building in _buildings)
        {
            if (!building.HasWorker(adventurer.id)) continue;
            building.RemoveWorker(adventurer.id);
            if (building.buildingType == BuildingType.Headquarters) _scoutProgress.Remove(adventurer.id);
            if (building.buildingType == BuildingType.Recruiter) AdventurerManager.Instance.ResetRecruitSearch(adventurer.id);
            OnAdventurerUnassigned?.Invoke(building, adventurer);
        }
        adventurer.OnStateChanged -= HandleWorkerStateChanged;
        _observedWorkers.Remove(adventurer);
    }

    // =========================================================================
    // Production
    // =========================================================================

    public int ReservedEquipmentSlots
    {
        get
        {
            int count = 0;
            foreach (var building in _buildings)
                foreach (var task in building.activeTasks)
                    if (!task.awaitingInputs && task.recipe != null && task.recipe.ProducesEquipment) count++;
            return count;
        }
    }

    public string GetProductionSupplyIssue(CraftRecipeSO recipe, string componentBlockReason = null,
        ForgeTechnique technique = ForgeTechnique.Standard)
    {
        if (recipe == null || !recipe.SupportsTechnique(technique)) return "Recette ou technique indisponible";
        var stock = GuildInventory.Instance;
        if (recipe.ProducesEquipment && !stock.CanReserveEquipmentSlot)
            return "En attente d’une place en réserve";
        if (!stock.HasResources(recipe.GetInputs(technique)))
            return componentBlockReason ?? "En attente de composants";
        return null;
    }

    public bool CanQueueProduction(CraftRecipeSO recipe, ForgeTechnique technique = ForgeTechnique.Standard)
    {
        if (recipe == null || !recipe.SupportsTechnique(technique)) return false;
        var stock = GuildInventory.Instance;
        if (recipe.ProducesEquipment && !stock.CanReserveEquipmentSlot) return false;
        return stock.HasResources(recipe.GetInputs(technique)) || EconomyManager.Instance.AutoBuyComponents;
    }

    public string GetProductionStartIssue(Adventurer adventurer, Building building, CraftRecipeSO recipe,
        ForgeTechnique technique = ForgeTechnique.Standard)
    {
        if (building?.IsActive != true) return "Bâtiment indisponible";
        if (adventurer == null || adventurer.currentStateType != AdventurerStateType.Working
            || adventurer.currentBuildingId != building.buildingId || !building.HasWorker(adventurer.id))
            return "Choisissez un aventurier affecté à ce bâtiment";
        if (recipe == null || !recipe.isUnlocked || recipe.requiredJob != adventurer.currentJobType
            || !allRecipes.Contains(recipe) || !recipe.SupportsTechnique(technique)
            || (!recipe.ProducesEquipment && (recipe.outputResource == null || recipe.outputResourceAmount <= 0)))
            return "Recette ou technique indisponible";
        var job = adventurer.GetJobInstance(recipe.requiredJob);
        int requiredLevel = recipe.GetRequiredJobLevel(technique);
        if (job == null || job.currentLevel < requiredLevel)
            return $"Niveau de métier {requiredLevel} requis";
        if (GetTaskForWorker(adventurer.id) != null) return "Cet aventurier a déjà une commande";
        return CanQueueProduction(recipe, technique) ? null : GetProductionSupplyIssue(recipe, technique: technique);
    }

    public bool StartProduction(Adventurer adventurer, CraftRecipeSO recipe, int quantity = 1,
        ForgeTechnique technique = ForgeTechnique.Standard)
    {
        if (quantity < 1 || quantity > MaxSeriesQuantity) return false;
        var building = GetBuildingById(adventurer?.currentBuildingId);
        if (GetProductionStartIssue(adventurer, building, recipe, technique) != null) return false;
        if (!_jobByType.TryGetValue(recipe.requiredJob, out var jobDef)) return false;

        var (duration, qualityBonus) = JobPerformance.GetCrafting(adventurer, recipe, jobDef, technique);
        var task = new ProductionTask(recipe, adventurer.id, duration, qualityBonus, technique) { seriesQuantity = quantity };
        var stock = GuildInventory.Instance;
        task.awaitingInputs = !stock.HasResources(task.InputResources);
        building.AddTask(task);
        if (!task.awaitingInputs) stock.ConsumeResources(task.InputResources);
        return true;
    }

    private void StartWaitingUnits(Building building)
    {
        var stock = GuildInventory.Instance;
        foreach (var task in building.activeTasks)
        {
            if (!task.awaitingInputs || !building.HasWorker(task.assignedAdventurerId)) continue;
            var recipe = task.recipe;
            if (recipe == null || !recipe.SupportsTechnique(task.forgeTechnique)
                || (recipe.ProducesEquipment && !stock.CanReserveEquipmentSlot)) continue;
            var worker = AdventurerManager.Instance.GetById(task.assignedAdventurerId);
            var job = worker?.GetJobInstance(task.craftedByJobType);
            if (job == null || worker.currentStateType != AdventurerStateType.Working
                || job.currentLevel < recipe.GetRequiredJobLevel(task.forgeTechnique)) continue;

            if (!stock.HasResources(task.InputResources))
            {
                string blocked = EconomyManager.Instance.BuyMissingComponents(task.InputResources);
                if (blocked != null)
                {
                    if (task.ComponentBlockReason != blocked)
                    {
                        task.ComponentBlockReason = blocked;
                        OnProductionBlocked?.Invoke(building, task, blocked);
                    }
                    continue;
                }
            }
            task.ComponentBlockReason = null;
            var (duration, qualityBonus) = JobPerformance.GetCrafting(worker, recipe, GetJobDefinition(task.craftedByJobType), task.forgeTechnique);
            task.BeginUnit(duration, qualityBonus);
            stock.ConsumeResources(task.InputResources);
        }
    }

    public void CancelProduction(string buildingId, string taskId)
    {
        if (!_byId.TryGetValue(buildingId, out var building)) return;
        var task = building.activeTasks.Find(t => t.taskId == taskId);
        if (task == null) return;
        building.CancelTask(taskId);
        if (task.recipe == null || task.awaitingInputs) return;
        foreach (var cost in task.InputResources)
            GuildInventory.Instance.AddResource(cost.resource, cost.amount);
    }

    public bool StopSeriesAfterCurrentUnit(string buildingId, string taskId)
    {
        if (!_byId.TryGetValue(buildingId, out var building)) return false;
        var task = building.activeTasks.Find(t => t.taskId == taskId);
        if (task == null || !task.IsSeries || task.stopAfterCurrentUnit) return false;
        if (task.awaitingInputs)
        {
            building.CancelTask(taskId);
            return true;
        }
        if (!task.HasNextUnit) return false;

        task.stopAfterCurrentUnit = true;
        return true;
    }

    public ProductionTask GetTaskForWorker(string adventurerId)
    {
        foreach (var building in _buildings)
            foreach (var task in building.activeTasks)
                if (task.assignedAdventurerId == adventurerId) return task;
        return null;
    }

    public void CancelTasksForWorker(string adventurerId)
    {
        foreach (var building in _buildings)
            for (int i = building.activeTasks.Count - 1; i >= 0; i--)
                if (building.activeTasks[i].assignedAdventurerId == adventurerId)
                    CancelProduction(building.buildingId, building.activeTasks[i].taskId);
    }

    // =========================================================================
    // Upgrade
    // =========================================================================

    public bool UpgradeBuilding(BuildingType buildingType)
    {
        if (!_byType.TryGetValue(buildingType, out var building)) return false;
        if (!building.CanUpgrade()) return false;

        var cost = building.GetNextLevelData();

        // Les 2 préconditions sont vérifiées AVANT toute dépense (évite un gold dépensé pour rien)
        if (!GuildInventory.Instance.HasResources(cost.resourceCosts)) return false;
        if (EconomyManager.Instance.CurrentGold < cost.goldCost) return false;

        EconomyManager.Instance.SpendGold(cost.goldCost);
        GuildInventory.Instance.ConsumeResources(cost.resourceCosts);
        building.StartUpgrade();
        return true;
    }

    // =========================================================================
    // Queries
    // =========================================================================

    public Building            GetBuilding(BuildingType type)     => _byType.TryGetValue(type, out var b) ? b : null;
    public Building            GetBuildingById(string id)         => id != null && _byId.TryGetValue(id, out var b) ? b : null;
    public List<Building>      GetAllBuildings()                  => _buildings;
    public bool                HasBuilding(BuildingType type)     => _byType.ContainsKey(type);
    public JobDefinitionSO     GetJobDefinition(JobType jobType)  => _jobByType.TryGetValue(jobType, out var d) ? d : null;
    public List<CraftRecipeSO> GetRecipesForJob(JobType jobType)  => allRecipes.FindAll(r => r.requiredJob == jobType && r.isUnlocked);

    public static JobType GetJobTypeFor(BuildingType b) => b switch
    {
        BuildingType.Forge     => JobType.Blacksmith,
        BuildingType.Workshop  => JobType.Craftsman,
        BuildingType.Market    => JobType.Merchant,
        BuildingType.Infirmary => JobType.Healer,
        BuildingType.Tavern    => JobType.Cook,
        BuildingType.Recruiter => JobType.Recruiter,
        BuildingType.TrainingRoom => JobType.Trainer,
        BuildingType.Headquarters => JobType.Scout,
        _                      => JobType.None
    };

    // =========================================================================
    // Handlers internes
    // =========================================================================

    private void HandleUpgradeComplete(Building building) => OnBuildingUpgraded?.Invoke(building);

    private void HandleProductionComplete(Building building, ProductionTask task)
    {
        var worker = AdventurerManager.Instance.GetById(task.assignedAdventurerId);
        var definition = GetJobDefinition(task.craftedByJobType);
        var recipe = task.recipe;
        if (recipe?.ProducesEquipment == true)
        {
            int value = recipe.outputEquipment.GetGoldValue(task.rolledQuality);
            task.LastCommission = EconomyManager.Instance.PayCommission(worker, task.craftedByJobType, value);
            var item = new EquipmentInstance(recipe.outputEquipment, task.rolledQuality,
                task.assignedAdventurerId, task.craftedByJobType);
            GuildInventory.Instance.ReceiveCraftedEquipment(item);
        }
        else if (recipe?.outputResource != null)
        {
            float multiplier = worker?.GetJobInstance(task.craftedByJobType)?.GetOutputMultiplier(definition) ?? 1f;
            int amount = Mathf.RoundToInt(recipe.outputResourceAmount * multiplier);
            task.LastCommission = EconomyManager.Instance.PayCommission(worker, task.craftedByJobType,
                amount * recipe.outputResource.baseMarketValue);
            GuildInventory.Instance.AddResource(recipe.outputResource, amount);
        }

        if (worker != null)
        {
            bool leveledUp = worker.AddJobExperience(task.craftedByJobType, 50, definition);
            worker.AddExperience(20);
            if (leveledUp)
                OnWorkerJobLevelUp?.Invoke(worker, task.craftedByJobType,
                    worker.GetJobInstance(task.craftedByJobType)?.currentLevel ?? 0);
        }

        OnProductionCompleted?.Invoke(building, task);
        if (task.HasNextUnit && recipe != null && worker != null)
        {
            task.seriesUnit++;
            task.progress = 0f;
            task.awaitingInputs = true;
            building.AddTask(task);
        }
    }

    // =========================================================================
    // Post-load
    // =========================================================================

    public void OnAfterLoad()
    {
        _byType.Clear();
        _byId.Clear();

        foreach (var b in _buildings)
        {
            var def = FindDefinition(b.buildingType);
            if (def != null) b.OnAfterLoad(def);

            foreach (var task in b.activeTasks)
                if (_recipeByName.TryGetValue(task.recipeId, out var recipe))
                    task.OnAfterLoad(recipe);

            RegisterBuilding(b);
            foreach (var workerId in b.assignedAdventurerIds)
            {
                var worker = AdventurerManager.Instance.GetById(workerId);
                if (worker != null && _observedWorkers.Add(worker))
                    worker.OnStateChanged += HandleWorkerStateChanged;
            }
        }
    }

    private BuildingSO FindDefinition(BuildingType type) => buildingDefinitions.Find(d => d.buildingType == type);
}
