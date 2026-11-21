using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BuildingManager
{
    public static readonly IReadOnlyList<BuildingType> GuildBuildings = Array.AsReadOnly(new[]
    {
        BuildingType.Headquarters, BuildingType.Recruiter, BuildingType.Market,
        BuildingType.Infirmary, BuildingType.Tavern, BuildingType.Workshop,
        BuildingType.Forge, BuildingType.TrainingRoom
    });

    private readonly List<ScriptableObject> _generatedDefinitions = new();
    private readonly List<ServiceSession> _sessions = new();
    private readonly List<(Adventurer Worker, float Duration)> _workerActivity = new();
    private readonly List<string> _missionOffers = new();
    private float _discoveryProgress;
    private int _missionsFound;

    private sealed class ServiceSession
    {
        public Adventurer Adventurer;
        public BuildingType Type;
        public float Progress;
        public bool RestReported;
        public bool MoraleReported;
    }

    public event Action<string> OnGuildActivity;
    public event Action<Adventurer, bool, bool> OnTavernRecovery;
    public IReadOnlyList<string> MissionOffers => _missionOffers;
    public int RosterCapacity => 6 + 3 * (GetBuilding(BuildingType.Headquarters)?.currentLevel - 1 ?? 0);
    public int RosterOccupancy
    {
        get
        {
            int count = 0;
            foreach (var a in AdventurerManager.Instance.GetAllAdventurers())
                if (a.currentStateType != AdventurerStateType.Dead) count++;
            return count;
        }
    }
    public bool CanHire => GetBuilding(BuildingType.Recruiter)?.IsActive == true && RosterOccupancy < RosterCapacity;
    public float DiscoveryProgress => Mathf.Clamp01(_discoveryProgress);
    public float MissionInterval
    {
        get
        {
            var hq = GetBuilding(BuildingType.Headquarters);
            return hq == null
                ? float.PositiveInfinity
                : 30f / (1f + hq.assignedAdventurerIds.Count + 0.5f * (hq.currentLevel - 1));
        }
    }

    public float MissionQualityBonus
    {
        get
        {
            var hq = GetBuilding(BuildingType.Headquarters);
            return hq == null ? 0f : 0.25f * hq.assignedAdventurerIds.Count + 0.1f * (hq.currentLevel - 1);
        }
    }

    public int MissionCapacity
    {
        get
        {
            var hq = GetBuilding(BuildingType.Headquarters);
            return hq == null ? 0 : hq.currentLevel + 2 * hq.assignedAdventurerIds.Count;
        }
    }

    private void ConfigureGuildBuildings()
    {
        EnsureBuilding(BuildingType.Headquarters, JobType.Scout, "Quartier général", "Éclaireur", "Le quartier général trouve des missions. Les éclaireurs améliorent la capacité, la vitesse et la qualité de la recherche.");
        EnsureBuilding(BuildingType.Recruiter, JobType.Recruiter, "Recruteur", "Recruteur", "Consultez les candidatures et engagez de nouveaux aventuriers.");
        EnsureBuilding(BuildingType.Infirmary, JobType.Healer, "Infirmerie", "Soigneur", "Confiez les blessés aux soigneurs pour accélérer leur guérison.");
        EnsureBuilding(BuildingType.Tavern, JobType.Cook, "Taverne", "Tavernier", "Réduisez la fatigue et remontez le moral.");
        EnsureBuilding(BuildingType.Workshop, JobType.Craftsman, "Atelier", "Artisan", "Fabriquez des bracelets, des colliers et des bagues.");
        EnsureBuilding(BuildingType.TrainingRoom, JobType.Trainer, "Salle d’entraînement", "Entraîneur", "Gagnez de l’expérience avec un entraîneur.");

        if (GetRecipesForJob(JobType.Craftsman).Count > 0) return;
        ResourceSO metal = null;
        foreach (var resource in GuildInventory.Instance.ResourceDefinitions)
            if (resource != null && resource.name == "Resource_Iron") { metal = resource; break; }
        if (metal == null) return;
        AddJewelry("Bracelet de fer", EquipmentSlot.Bracelet, StatType.Vitalite, metal, 3);
        AddJewelry("Collier de fer", EquipmentSlot.Necklace, StatType.Intelligence, metal, 4);
        AddJewelry("Bague de fer", EquipmentSlot.Ring, StatType.Chance, metal, 2);
    }

    public int ServiceOccupancy(BuildingType type)
    {
        int count = 0;
        foreach (var session in _sessions)
            if (session.Type == type) count++;
        return count;
    }

    public float ServiceProgress(Adventurer a)
    {
        var session = _sessions.Find(s => s.Adventurer == a);
        return session == null ? 0f : Mathf.Clamp01(session.Progress / 8f);
    }
    private void EnsureBuilding(BuildingType type, JobType job, string title, string jobName, string description)
    {
        if (!_jobByType.ContainsKey(job))
        {
            var definition = CreateInstance<JobDefinitionSO>("Job_" + job);
            definition.jobType = job;
            definition.associatedBuilding = type;
            definition.jobName = jobName;
            _jobByType.Add(job, definition);
        }

        var building = FindDefinition(type);
        if (building == null)
        {
            building = CreateInstance<BuildingSO>("Building_" + type);
            building.buildingName = title;
            building.buildingType = type;
            building.description = description;
            building.associatedJobType = job;
            building.maxLevel = 3;
            for (int level = 1; level <= building.maxLevel; level++)
                building.levelData.Add(new BuildingLevelData { maxWorkerSlots = level, goldCost = 100 * level, upgradeTime = 10f * level });
            buildingDefinitions.Add(building);
        }
        UnlockBuilding(building);
    }

    private T CreateInstance<T>(string id) where T : ScriptableObject
    {
        var asset = ScriptableObject.CreateInstance<T>();
        asset.name = id;
        _generatedDefinitions.Add(asset);
        return asset;
    }

    private void AddJewelry(string title, EquipmentSlot slot, StatType stat, ResourceSO metal, int cost)
    {
        var item = CreateInstance<EquipmentSO>("Equipment_Iron" + slot);
        item.itemName = title;
        item.slot = slot;
        item.mainStatType = stat;
        item.baseMainStatValue = 5;
        item.baseGoldValue = 18 * cost;
        var recipe = CreateInstance<CraftRecipeSO>("Recipe_Iron" + slot);
        recipe.recipeName = title;
        recipe.requiredJob = JobType.Craftsman;
        recipe.baseCraftDuration = 4f + cost;
        recipe.inputResources.Add(new ResourceCost { resource = metal, amount = cost });
        recipe.outputEquipment = item;
        allRecipes.Add(recipe);
        _recipeByName.Add(recipe.name, recipe);
    }

    public static bool HasService(BuildingType type)
        => type is BuildingType.Infirmary or BuildingType.Tavern or BuildingType.TrainingRoom;

    public bool CanStartService(Adventurer a, Building b)
    {
        if (a == null || b == null || !b.IsActive || !HasService(b.buildingType) || a.currentService.HasValue)
            return false;
        if (ServiceOccupancy(b.buildingType) >= b.currentLevel) return false;
        return b.buildingType switch
        {
            BuildingType.Infirmary => a.currentStateType == AdventurerStateType.Injured,
            BuildingType.Tavern => a.IsAvailable || a.currentStateType == AdventurerStateType.Resting,
            _ => a.IsAvailable
        };
    }

    public bool StartService(Adventurer a, Building b)
    {
        if (!CanStartService(a, b) || AdventurerManager.Instance.GetById(a.id) != a) return false;
        a.currentService = b.buildingType;
        _sessions.Add(new ServiceSession { Adventurer = a, Type = b.buildingType });
        OnGuildActivity?.Invoke($"{a.adventurerName} entre dans le service « {b.definition.buildingName} ».");
        return true;
    }

    public void StopService(Adventurer a)
    {
        var session = _sessions.Find(s => s.Adventurer == a);
        if (session == null) return;
        EndService(session);
        OnGuildActivity?.Invoke($"{a.adventurerName} termine sa prise en charge.");
    }

    private void EndService(ServiceSession session)
    {
        _sessions.Remove(session);
        session.Adventurer.currentService = null;
    }

    public void ArchiveMission(string offer)
    {
        if (_missionOffers.Remove(offer)) OnGuildActivity?.Invoke($"Mission archivée · {offer}");
    }

    private void ResetGuildServices()
    {
        foreach (var session in _sessions) session.Adventurer.currentService = null;
        _sessions.Clear();
        _workerActivity.Clear();
        _missionOffers.Clear();
        _discoveryProgress = 0f;
        _missionsFound = 0;
    }

    internal void TrackWorkerActivity(float deltaTime)
    {
        foreach (var b in _buildings)
        {
            if (!b.IsActive || b.assignedAdventurerIds.Count == 0) continue;
            if (b.buildingType is BuildingType.Forge or BuildingType.Workshop)
            {
                float speed = 1f + b.GetCurrentLevelData().productionSpeedBonus;
                if (speed <= 0f) continue;
                foreach (var task in b.activeTasks)
                {
                    if (task.awaitingInputs || task.IsComplete || !b.HasWorker(task.assignedAdventurerId)) continue;
                    float remaining = (1f - task.progress) * task.totalDuration / speed;
                    TrackWorker(task.assignedAdventurerId, Mathf.Min(deltaTime, remaining));
                }
                continue;
            }

            float workDuration = b.buildingType == BuildingType.Headquarters
                ? Mathf.Min(deltaTime, Mathf.Max(0f, (MissionCapacity - _missionOffers.Count - _discoveryProgress) * MissionInterval))
                : HasService(b.buildingType) && ServiceOccupancy(b.buildingType) > 0 ? deltaTime : 0f;
            if (workDuration <= 0f) continue;
            foreach (string id in b.assignedAdventurerIds)
                TrackWorker(id, workDuration);
        }
    }

    private void TrackWorker(string id, float duration)
    {
        var worker = AdventurerManager.Instance.GetById(id);
        if (worker != null) _workerActivity.Add((worker, duration));
    }

    internal void ApplyWorkerFatigue()
    {
        foreach (var activity in _workerActivity)
            activity.Worker.AddWorkFatigue(activity.Duration);
        _workerActivity.Clear();
    }

    // Un achat, une vente ou une embauche est traité par le premier employé en poste.
    internal void RecordTransactionWork(BuildingType type)
    {
        var b = GetBuilding(type);
        if (b == null || !b.IsActive || b.assignedAdventurerIds.Count == 0) return;
        AdventurerManager.Instance.GetById(b.assignedAdventurerIds[0])?.AddWorkFatigue(1f);
    }

    private void TickGuildServices(float deltaTime)
    {
        for (int i = _sessions.Count - 1; i >= 0; i--)
        {
            var session = _sessions[i];
            var a = session.Adventurer;
            var b = GetBuilding(session.Type);
            if (AdventurerManager.Instance.GetById(a.id) != a ||
                a.currentStateType is AdventurerStateType.Dead or AdventurerStateType.OnMission or AdventurerStateType.Working ||
                (session.Type == BuildingType.Infirmary && a.currentStateType != AdventurerStateType.Injured) ||
                (session.Type != BuildingType.Infirmary && a.currentStateType == AdventurerStateType.Injured))
            {
                StopService(a);
                continue;
            }

            if (!b.IsActive) continue;
            int staff = b.assignedAdventurerIds.Count;
            float rate = deltaTime * (1f + 0.25f * (b.currentLevel - 1) + 0.5f * staff);
            switch (session.Type)
            {
                case BuildingType.Infirmary:
                    a.Tick(rate);
                    if (a.currentStateType != AdventurerStateType.Injured) StopService(a);
                    break;
                case BuildingType.Tavern:
                    bool wasTired = a.stats.fatigue > 0f;
                    bool hadLowerMorale = a.stats.moral < 100f;
                    a.stats.fatigue -= 8f * rate * a.GetTraitMultiplier(TraitEffectType.FatigueRecovery_Multiplier);
                    a.stats.moral += 4f * rate;
                    a.stats.ClampSecondaryStats();
                    bool rested = !session.RestReported && wasTired && a.stats.fatigue <= 0f;
                    bool moraleRestored = !session.MoraleReported && hadLowerMorale && a.stats.moral >= 100f;
                    session.RestReported |= rested;
                    session.MoraleReported |= moraleRestored;
                    if (a.stats.fatigue <= 0f && a.currentStateType == AdventurerStateType.Resting)
                        a.TransitionToState(new IdleState(a));
                    if (a.stats.fatigue <= 0f && a.stats.moral >= 100f)
                    {
                        EndService(session);
                        if (!rested && !moraleRestored) OnGuildActivity?.Invoke($"{a.adventurerName} quitte la taverne.");
                    }
                    if (rested || moraleRestored) OnTavernRecovery?.Invoke(a, rested, moraleRestored);
                    break;
                case BuildingType.TrainingRoom:
                    if (staff == 0) break;
                    session.Progress += rate;
                    if (session.Progress < 8f) break;
                    a.AddExperience(40);
                    StopService(a);
                    break;
            }
        }

        var hq = GetBuilding(BuildingType.Headquarters);
        if (hq == null || !hq.IsActive || _missionOffers.Count >= MissionCapacity) return;
        _discoveryProgress += deltaTime / MissionInterval;
        while (_discoveryProgress >= 1f && _missionOffers.Count < MissionCapacity)
        {
            _discoveryProgress -= 1f;
            float qualityScore = UnityEngine.Random.value + MissionQualityBonus;
            string quality = qualityScore >= 1.15f ? "Prestigieuse" : qualityScore >= 0.8f ? "Remarquable" : "Ordinaire";
            string offer = $"Reconnaissance des environs · contrat {++_missionsFound} · {quality}";
            _missionOffers.Add(offer);
            if (_missionOffers.Count >= MissionCapacity) _discoveryProgress = 0f;
            OnGuildActivity?.Invoke($"Nouvelle mission disponible · {offer}");
        }
    }
}
