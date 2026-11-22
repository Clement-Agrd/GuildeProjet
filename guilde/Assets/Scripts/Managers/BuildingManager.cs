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

        // Salaire versé à la prise de poste (GDO 8.5 — hypothèse : on affinera si besoin)
        _jobByType.TryGetValue(jobType, out var jobDef);
        EconomyManager.Instance.PayWage(adventurer, jobDef);

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

    public string GetProductionSupplyIssue(CraftRecipeSO recipe, string componentBlockReason = null)
    {
        if (recipe == null) return "Recette indisponible";
        if (recipe.ProducesEquipment && !GuildInventory.Instance.CanReserveEquipmentSlot)
            return "En attente d’une place en réserve";
        if (!GuildInventory.Instance.HasResources(recipe.inputResources))
            return componentBlockReason ?? "En attente de composants";
        return null;
    }

    public bool CanQueueProduction(CraftRecipeSO recipe)
    {
        var stock = GuildInventory.Instance;
        if (recipe.ProducesEquipment && !stock.CanReserveEquipmentSlot) return false;
        return stock.HasResources(recipe.inputResources) || EconomyManager.Instance.AutoBuyComponents;
    }

    public bool StartProduction(Adventurer adventurer, CraftRecipeSO recipe, int quantity = 1)
    {
        if (adventurer == null || recipe == null) return false;
        if (quantity < 1 || quantity > MaxSeriesQuantity) return false;
        if (adventurer.currentStateType != AdventurerStateType.Working) return false;
        if (string.IsNullOrEmpty(adventurer.currentBuildingId) ||
            !_byId.TryGetValue(adventurer.currentBuildingId, out var building)) return false;
        if (!building.IsActive || !building.HasWorker(adventurer.id)) return false;
        if (!recipe.isUnlocked || recipe.requiredJob != adventurer.currentJobType) return false;
        if (!allRecipes.Contains(recipe) || GetTaskForWorker(adventurer.id) != null) return false;
        if (!recipe.ProducesEquipment && (recipe.outputResource == null || recipe.outputResourceAmount <= 0)) return false;
        if (!_jobByType.TryGetValue(adventurer.currentJobType, out var jobDef)) return false;

        var jobInst = adventurer.GetJobInstance(adventurer.currentJobType);
        if (jobInst == null || jobInst.currentLevel < recipe.requiredJobLevel) return false;
        if (!CanQueueProduction(recipe)) return false;

        var stock = GuildInventory.Instance;
        bool hasComponents = stock.HasResources(recipe.inputResources);

        var (duration, qualityBonus) = JobPerformance.GetCrafting(adventurer, recipe, jobDef);

        var task = new ProductionTask(recipe, adventurer.id, duration, qualityBonus)
        {
            seriesQuantity = quantity,
            awaitingInputs = !hasComponents
        };
        building.AddTask(task);
        if (hasComponents) stock.ConsumeResources(recipe.inputResources);
        return true;
    }

    private void StartWaitingUnits(Building building)
    {
        var stock = GuildInventory.Instance;
        foreach (var task in building.activeTasks)
        {
            if (!task.awaitingInputs || !building.HasWorker(task.assignedAdventurerId)) continue;
            var recipe = task.recipe;
            if (recipe == null || (recipe.ProducesEquipment && !stock.CanReserveEquipmentSlot)) continue;
            var worker = AdventurerManager.Instance.GetById(task.assignedAdventurerId);
            var job = worker?.GetJobInstance(task.craftedByJobType);
            if (job == null || worker.currentStateType != AdventurerStateType.Working) continue;

            if (!stock.HasResources(recipe.inputResources))
            {
                string blocked = EconomyManager.Instance.BuyMissingComponents(recipe.inputResources);
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
            var (duration, qualityBonus) = JobPerformance.GetCrafting(worker, recipe, GetJobDefinition(task.craftedByJobType));
            task.BeginUnit(duration, qualityBonus);
            stock.ConsumeResources(recipe.inputResources);
        }
    }

    public void CancelProduction(string buildingId, string taskId)
    {
        if (!_byId.TryGetValue(buildingId, out var building)) return;
        var task = building.activeTasks.Find(t => t.taskId == taskId);
        if (task == null) return;
        building.CancelTask(taskId);
        if (task.recipe == null || task.awaitingInputs) return;
        foreach (var cost in task.recipe.inputResources)
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
    public Building            GetBuildingById(string id)         => _byId.TryGetValue(id, out var b) ? b : null;
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
        var adventurer = AdventurerManager.Instance.GetById(task.assignedAdventurerId);

        if (adventurer != null)
        {
            _jobByType.TryGetValue(task.craftedByJobType, out var jobDef);
            bool leveledUp = adventurer.AddJobExperience(task.craftedByJobType, 50, jobDef);
            adventurer.AddExperience(20);

            if (leveledUp)
                OnWorkerJobLevelUp?.Invoke(adventurer, task.craftedByJobType,
                    adventurer.GetJobInstance(task.craftedByJobType)?.currentLevel ?? 0);
        }

        if (task.recipe != null)
        {
            if (task.recipe.ProducesEquipment)
            {
                var item = new EquipmentInstance(task.recipe.outputEquipment, task.rolledQuality,
                    task.assignedAdventurerId, task.craftedByJobType);
                GuildInventory.Instance.ReceiveCraftedEquipment(item);
            }
            else if (task.recipe.outputResource != null)
            {
                int amount = task.recipe.outputResourceAmount;
                if (adventurer != null)
                {
                    _jobByType.TryGetValue(task.craftedByJobType, out var jd);
                    var ji = adventurer.GetJobInstance(task.craftedByJobType);
                    if (ji != null && jd != null)
                        amount = Mathf.RoundToInt(amount * ji.GetOutputMultiplier(jd));
                }
                GuildInventory.Instance.AddResource(task.recipe.outputResource, amount);
            }
        }

        OnProductionCompleted?.Invoke(building, task);
        if (task.HasNextUnit && task.recipe != null && adventurer != null)
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
