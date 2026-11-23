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
    private readonly List<MissionOffer> _missionOffers = new();
    private readonly Dictionary<string, float> _scoutProgress = new();
    private const float PrestigiousMissionThreshold = 0.85f;
    private float _requestProgress;
    private int _missionsFound;

    private sealed class ServiceSession
    {
        public Adventurer Adventurer;
        public BuildingType Type;
        public string WorkerId;
        public float Progress;
        public bool RestReported;
        public bool MoraleReported;
    }

    public event Action<string> OnGuildActivity;
    public event Action<Adventurer, bool, bool> OnTavernRecovery;
    public IReadOnlyList<MissionOffer> MissionOffers => _missionOffers;
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
    public float RequestProgress => Mathf.Clamp01(_requestProgress);
    public int VillageRequestCapacity => GetBuilding(BuildingType.Headquarters)?.currentLevel ?? 0;
    public int ScoutedMissionCapacity => VillageRequestCapacity * 2;
    private float HeadquartersSpeed => 1f + 0.5f * Mathf.Max(0, VillageRequestCapacity - 1);
    public float VillageRequestInterval => 30f / HeadquartersSpeed;

    public float ScoutInterval(Adventurer scout)
        => 30f / (HeadquartersSpeed * (1f + JobPerformance.Scouting(scout).SpeedBonus));

    public float ScoutHoursRemaining(Adventurer scout)
    {
        _scoutProgress.TryGetValue(scout.id, out float progress);
        return (1f - progress) * ScoutInterval(scout);
    }

    public static float ScoutPrestigiousChance(Adventurer scout)
        => Mathf.Clamp01(1f + JobPerformance.Scouting(scout).QualityBonus - PrestigiousMissionThreshold);

    public int MissionCount(bool scouted)
    {
        int count = 0;
        foreach (var offer in _missionOffers)
            if (offer.IsScouted == scouted) count++;
        return count;
    }

    private void ConfigureGuildBuildings()
    {
        EnsureBuilding(BuildingType.Headquarters, JobType.Scout, "Quartier général", "Éclaireur", "Les villageois déposent leurs requêtes au quartier général. Les éclaireurs rapportent leurs propres découvertes.");
        EnsureBuilding(BuildingType.Recruiter, JobType.Recruiter, "Recruteur", "Recruteur", "Consultez les candidatures spontanées et les aventuriers trouvés par vos recruteurs.");
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

    private Adventurer GetServiceWorker(Building building)
    {
        Adventurer best = null;
        int leastSessions = int.MaxValue;
        foreach (string id in building.assignedAdventurerIds)
        {
            var worker = AdventurerManager.Instance.GetById(id);
            if (worker == null) continue;
            int sessions = 0;
            foreach (var session in _sessions)
                if (session.WorkerId == id) sessions++;
            if (sessions >= leastSessions) continue;
            best = worker;
            leastSessions = sessions;
        }
        return best;
    }

    public int GetServicePrice(Adventurer a, Building b, out int commission)
        => GetServicePrice(a, b, GetServiceWorker(b), out commission);

    private static int GetServicePrice(Adventurer a, Building b, Adventurer worker, out int commission)
    {
        int basePrice = b.buildingType switch
        {
            BuildingType.Infirmary => Mathf.Max(1, Mathf.CeilToInt((1f - a.HealthFraction) * a.GetCombatStats().pointsDeVie / 10f)),
            BuildingType.TrainingRoom => 8 + 2 * a.stats.level,
            BuildingType.Tavern => 5,
            _ => 0
        };
        commission = worker?.CalculateCommission(basePrice, b.definition.associatedJobType) ?? 0;
        return basePrice + commission;
    }

    public string GetServiceBlockedReason(Adventurer a, Building b)
    {
        if (a == null || b == null || !b.IsActive || !HasService(b.buildingType))
            return "Service indisponible.";
        if (AdventurerManager.Instance.GetById(a.id) != a || a.currentService.HasValue)
            return "Aventurier indisponible.";
        if (ServiceOccupancy(b.buildingType) >= b.currentLevel) return "Toutes les places sont occupées.";

        switch (b.buildingType)
        {
            case BuildingType.Infirmary when a.currentStateType != AdventurerStateType.Injured:
                return "L’infirmerie accueille les blessés.";
            case BuildingType.Tavern when !a.IsAvailable && a.currentStateType != AdventurerStateType.Resting:
            case BuildingType.TrainingRoom when !a.IsAvailable:
                return "Aventurier indisponible.";
            case BuildingType.Tavern when a.stats.fatigue <= 0f && a.stats.moral >= 100f:
                return "Cet aventurier est reposé et son moral est au maximum.";
            case BuildingType.TrainingRoom when GetServiceWorker(b) == null:
                return "Un entraîneur doit être en poste.";
        }

        return EconomyManager.Instance.CurrentGold >= GetServicePrice(a, b, out _) ? null : "Or insuffisant.";
    }

    public bool CanStartService(Adventurer a, Building b) => GetServiceBlockedReason(a, b) == null;

    public bool StartService(Adventurer a, Building b)
    {
        if (!CanStartService(a, b)) return false;
        var worker = GetServiceWorker(b);
        int price = GetServicePrice(a, b, worker, out int commission);
        if (!EconomyManager.Instance.SpendGold(price)) return false;
        a.currentService = b.buildingType;
        _sessions.Add(new ServiceSession { Adventurer = a, Type = b.buildingType, WorkerId = worker?.id });
        string commissionNote = commission > 0 ? $" dont {commission} or de commission" : "";
        OnGuildActivity?.Invoke($"{a.adventurerName} · {b.definition.buildingName} · {price} or réglés{commissionNote}.");
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

    public void ArchiveMission(MissionOffer offer)
    {
        if (_missionOffers.Remove(offer)) OnGuildActivity?.Invoke($"Mission archivée · {offer.Title}");
    }

    private void ResetGuildServices()
    {
        foreach (var session in _sessions) session.Adventurer.currentService = null;
        _sessions.Clear();
        _workerActivity.Clear();
        _missionOffers.Clear();
        _requestProgress = 0f;
        _scoutProgress.Clear();
        _missionsFound = 0;
    }

    internal void TrackWorkerActivity(float deltaTime)
    {
        foreach (var b in _buildings)
        {
            if (!b.IsActive || b.assignedAdventurerIds.Count == 0) continue;
            if (b.buildingType is BuildingType.Forge or BuildingType.Workshop)
            {
                float speed = b.ProductionSpeedMultiplier;
                if (speed <= 0f) continue;
                foreach (var task in b.activeTasks)
                {
                    if (task.awaitingInputs || task.IsComplete || !b.HasWorker(task.assignedAdventurerId)) continue;
                    float remaining = (1f - task.progress) * task.totalDuration / speed;
                    TrackWorker(task.assignedAdventurerId, Mathf.Min(deltaTime, remaining));
                }
            }
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
            var worker = session.WorkerId != null && b.HasWorker(session.WorkerId)
                ? AdventurerManager.Instance.GetById(session.WorkerId) : null;
            if (session.Type == BuildingType.TrainingRoom && worker == null)
            {
                worker = GetServiceWorker(b);
                if (worker == null) continue;
                session.WorkerId = worker.id;
            }
            float buildingSpeed = 1f + 0.25f * (b.currentLevel - 1);
            float bonus = worker == null ? 0f : session.Type switch
            {
                BuildingType.Infirmary => JobPerformance.HealingBonus(worker),
                BuildingType.Tavern => JobPerformance.TavernRecoveryBonus(worker),
                _ => JobPerformance.TrainingBonus(worker)
            };
            float speed = buildingSpeed * (1f + bonus);
            if (worker != null) TrackWorker(worker.id, deltaTime);
            float rate = deltaTime * speed;
            switch (session.Type)
            {
                case BuildingType.Infirmary:
                    a.Tick(rate);
                    if (a.currentStateType != AdventurerStateType.Injured)
                    {
                        AwardServiceExperience(worker, JobType.Healer);
                        StopService(a);
                    }
                    break;
                case BuildingType.Tavern:
                    bool wasTired = a.stats.fatigue > 0f;
                    bool hadLowerMorale = a.stats.moral < 100f;
                    a.stats.fatigue -= 1.8f * rate * a.RestRecoveryPerHour;
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
                        AwardServiceExperience(worker, JobType.Cook);
                        EndService(session);
                        if (!rested && !moraleRestored) OnGuildActivity?.Invoke($"{a.adventurerName} quitte la taverne.");
                    }
                    if (rested || moraleRestored) OnTavernRecovery?.Invoke(a, rested, moraleRestored);
                    break;
                case BuildingType.TrainingRoom:
                    session.Progress += rate;
                    if (session.Progress < 8f) break;
                    a.AddExperience(40);
                    AwardServiceExperience(worker, JobType.Trainer);
                    StopService(a);
                    break;
            }
        }

        TickMissionOffers(deltaTime);
    }

    private void AwardServiceExperience(Adventurer worker, JobType job)
        => worker?.AddJobExperience(job, 25, GetJobDefinition(job));

    private void TickMissionOffers(float hours)
    {
        var headquarters = GetBuilding(BuildingType.Headquarters);
        if (headquarters?.IsActive != true) return;

        if (MissionCount(false) < VillageRequestCapacity)
        {
            _requestProgress += hours / VillageRequestInterval;
            while (_requestProgress >= 1f && MissionCount(false) < VillageRequestCapacity)
            {
                _requestProgress -= 1f;
                AddMissionOffer(null);
            }
            if (MissionCount(false) >= VillageRequestCapacity) _requestProgress = 0f;
        }

        for (int i = headquarters.assignedAdventurerIds.Count - 1; i >= 0 && MissionCount(true) < ScoutedMissionCapacity; i--)
        {
            var scout = AdventurerManager.Instance.GetById(headquarters.assignedAdventurerIds[i]);
            if (scout == null) continue;
            _scoutProgress.TryGetValue(scout.id, out float progress);
            progress += hours / ScoutInterval(scout);
            while (progress >= 1f && MissionCount(true) < ScoutedMissionCapacity)
            {
                progress -= 1f;
                AddMissionOffer(scout);
            }
            _scoutProgress[scout.id] = MissionCount(true) >= ScoutedMissionCapacity ? 0f : Mathf.Clamp01(progress);
        }
    }

    private void AddMissionOffer(Adventurer scout)
    {
        float score = UnityEngine.Random.value + (scout == null
            ? 0.1f * (VillageRequestCapacity - 1)
            : JobPerformance.Scouting(scout).QualityBonus);
        int level = score >= PrestigiousMissionThreshold ? 3 : score >= 0.5f ? 2 : 1;
        string title = scout == null ? "Requête des villageois" : level switch
        {
            3 => "Donjon découvert",
            2 => "Monstres repérés",
            _ => "Traces à examiner"
        };
        var offer = new MissionOffer($"{title} · contrat {++_missionsFound}", level, scout?.adventurerName);
        _missionOffers.Add(offer);
        if (scout == null)
        {
            OnGuildActivity?.Invoke($"Requête déposée · {offer.Title} · niv. {level}");
            return;
        }

        int commission = EconomyManager.Instance.PayCommission(scout, JobType.Scout, 40 * level);
        scout.AddJobExperience(JobType.Scout, 25, GetJobDefinition(JobType.Scout));
        TrackWorker(scout.id, 1f);
        OnGuildActivity?.Invoke($"{scout.adventurerName} · {offer.Title} · niv. {level} · {commission} or de commission.");
    }
}

public sealed class MissionOffer
{
    public string Title { get; }
    public int Level { get; }
    public string ScoutName { get; }
    public bool IsScouted => ScoutName != null;

    public MissionOffer(string title, int level, string scoutName)
    {
        Title = title;
        Level = level;
        ScoutName = scoutName;
    }
}
