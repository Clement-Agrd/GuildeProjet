using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Adventurer
{
    // ─── Identité ─────────────────────────────────────────────────────────────
    public string id;
    public string adventurerName;
    public int    age;

    public string       classId;
    public string       raceId;
    public List<string> traitIds = new();

    [NonSerialized] public AdventurerClassSO adventurerClass;
    [NonSerialized] public AdventurerRaceSO  adventurerRace;
    [NonSerialized] public List<TraitSO>     traits = new();

    // ─── Stats ────────────────────────────────────────────────────────────────
    public AdventurerStats stats = new();

    public List<EquippedItemSlot> equippedItems = new();
    [NonSerialized] private Func<string, EquipmentInstance> _resolveEquipment;

    public void BindEquipmentResolver(Func<string, EquipmentInstance> resolver) => _resolveEquipment = resolver;

    public string GetEquipmentId(EquipmentSlot slot)
        => equippedItems.Find(entry => entry.slot == slot)?.equipmentId;

    public void SetEquippedItem(EquipmentSlot slot, string equipmentId)
    {
        equippedItems.RemoveAll(entry => entry.slot == slot);
        if (!string.IsNullOrEmpty(equipmentId))
            equippedItems.Add(new EquippedItemSlot { slot = slot, equipmentId = equipmentId });
    }

    public int GetEffectivePrimary(PrimaryStat stat)
    {
        TraitEffectType effect = stat switch
        {
            PrimaryStat.Vitalite => TraitEffectType.Vitalite_Multiplier,
            PrimaryStat.Resistance => TraitEffectType.Resistance_Multiplier,
            PrimaryStat.Force => TraitEffectType.Force_Multiplier,
            PrimaryStat.Intelligence => TraitEffectType.Intelligence_Multiplier,
            PrimaryStat.Charisme => TraitEffectType.Charisme_Multiplier,
            PrimaryStat.Sagesse => TraitEffectType.Sagesse_Multiplier,
            PrimaryStat.Agilite => TraitEffectType.Agilite_Multiplier,
            _ => TraitEffectType.Chance_Multiplier
        };
        return Mathf.Clamp(Mathf.RoundToInt(stats.GetPrimary(stat) * GetTraitMultiplier(effect)), 1, 100);
    }

    public AdventurerStats GetEffectiveStats()
    {
        var result = new AdventurerStats(stats);
        foreach (PrimaryStat stat in Enum.GetValues(typeof(PrimaryStat)))
            result.AddToPrimary(stat, GetEffectivePrimary(stat) - result.GetPrimary(stat));
        result.moral += GetTraitFlat(TraitEffectType.Moral_Flat);
        result.fatigue += GetTraitFlat(TraitEffectType.Fatigue_Flat);
        result.loyaute += GetTraitFlat(TraitEffectType.Loyaute_Flat);
        result.ClampSecondaryStats();
        return result;
    }

    // replacement sert à prévisualiser un changement sans modifier l'équipement ni l'inventaire.
    public CombatStats GetCombatStats(EquipmentInstance replacement = null)
    {
        var result = CombatStats.FromPrimary(GetEffectiveStats());
        float damageMultiplier = 1f;
        float accuracyMultiplier = 1f;
        foreach (var entry in equippedItems)
        {
            if (replacement?.definition != null && entry.slot == replacement.definition.slot) continue;
            ApplyEquipment(_resolveEquipment?.Invoke(entry.equipmentId), result, ref damageMultiplier, ref accuracyMultiplier);
        }
        ApplyEquipment(replacement, result, ref damageMultiplier, ref accuracyMultiplier);
        result.attaquePhysique = Mathf.RoundToInt(result.attaquePhysique * damageMultiplier);
        result.attaqueMagique = Mathf.RoundToInt(result.attaqueMagique * damageMultiplier);
        result.precision *= accuracyMultiplier;
        result.Clamp();
        return result;
    }

    private void ApplyEquipment(EquipmentInstance item, CombatStats result, ref float damageMultiplier, ref float accuracyMultiplier)
    {
        if (item?.definition == null || item.IsBroken) return;
        item.ApplyToCombatStats(result);
        if (item.definition.slot != EquipmentSlot.MainWeapon || adventurerClass == null
            || adventurerClass.MastersWeapon(item.definition.weaponType)) return;
        damageMultiplier *= Mathf.Clamp01(item.definition.unmasteredDamageMultiplier);
        accuracyMultiplier *= Mathf.Clamp01(item.definition.unmasteredAccuracyMultiplier);
    }

    // ─── Métiers ──────────────────────────────────────────────────────────────
    public List<JobInstance> jobInstanceList = new();
    [NonSerialized] private Dictionary<JobType, JobInstance> _jobInstances = new();

    // ─── Assignment courant ───────────────────────────────────────────────────
    public AdventurerStateType currentStateType = AdventurerStateType.Idle;
    public string              currentBuildingId;
    public string              previousBuildingId;
    public string              currentMissionId;
    public JobType             currentJobType = JobType.None;

    // ─── Affinités ────────────────────────────────────────────────────────────
    public List<AffinityData> affinityList = new();
    [NonSerialized] private Dictionary<string, AffinityData> _affinities = new();

    // ─── State machine ────────────────────────────────────────────────────────
    [NonSerialized] private AdventurerState _currentState;

    // ─── Events — incluent toujours "this" en premier param (cf. Building) ────
    [field: NonSerialized] public event Action<Adventurer, AdventurerStateType, AdventurerStateType> OnStateChanged;
    [field: NonSerialized] public event Action<Adventurer, JobType, int> OnJobLevelUp;
    [field: NonSerialized] public event Action<Adventurer, int>          OnLevelUp;

    // =========================================================================
    // Constructeur
    // =========================================================================

    public Adventurer(string name, AdventurerClassSO classData, AdventurerRaceSO raceData,
                      List<TraitSO> assignedTraits, int age = 20)
    {
        id             = Guid.NewGuid().ToString();
        adventurerName = name;
        this.age       = age;

        adventurerClass = classData;
        adventurerRace  = raceData;
        traits          = assignedTraits ?? new List<TraitSO>();

        classId  = classData?.name ?? "";
        raceId   = raceData?.name  ?? "";
        traitIds = new List<string>();
        foreach (var t in traits) traitIds.Add(t.name);

        stats = AdventurerStats.RollStartingStats();
        if (classData != null)
        {
            foreach (var modifier in classData.startingStatModifiers)
                stats.AddToPrimary(modifier.stat, modifier.value);
        }

        InitializeJobs();

        _currentState     = new IdleState(this);
        currentStateType  = AdventurerStateType.Idle;
        _currentState.Enter();
    }

    // =========================================================================
    // Tick
    // =========================================================================

    public void Tick(float deltaTime)
    {
        if (currentStateType == AdventurerStateType.Dead) return;
        _currentState?.Tick(deltaTime);
    }

    // =========================================================================
    // State machine
    // =========================================================================

    public void TransitionToState(AdventurerState newState)
    {
        var previous = currentStateType;
        _currentState?.Exit();
        _currentState    = newState;
        currentStateType = newState.StateType;
        _currentState.Enter();
        OnStateChanged?.Invoke(this, previous, currentStateType);
    }

    public void AssignToBuilding(string buildingId, JobType jobType)
    {
        if (currentStateType == AdventurerStateType.Dead) return;
        currentBuildingId = buildingId;
        currentJobType    = jobType;
        TransitionToState(new WorkingState(this));
    }

    public void AssignToMission(string missionId)
    {
        if (currentStateType == AdventurerStateType.Dead) return;
        currentMissionId = missionId;
        TransitionToState(new OnMissionState(this));
    }

    public void SetResting()
    {
        if (currentStateType == AdventurerStateType.Dead) return;
        TransitionToState(new RestingState(this));
    }

    public void SetInjured(float healingTime = 60f, float initialHealthFraction = 0.25f)
    {
        if (currentStateType == AdventurerStateType.Dead) return;
        TransitionToState(new InjuredState(this, healingTime, initialHealthFraction));
    }

    public float HealthFraction => currentStateType == AdventurerStateType.Dead ? 0f
        : _currentState is InjuredState injury ? injury.HealthFraction : 1f;

    public float WorkFatiguePerHour => JobPerformance.GetEffect(this, JobEffectType.WorkFatigue).Total;
    public float RestRecoveryPerHour => 10f * GetTraitMultiplier(TraitEffectType.FatigueRecovery_Multiplier);

    internal void AddWorkFatigue(float workHours)
    {
        if (workHours <= 0f || currentStateType != AdventurerStateType.Working) return;
        stats.fatigue += WorkFatiguePerHour * workHours;
        stats.ClampSecondaryStats();
        if (IsExhausted) SetResting();
    }

    public bool IsExhausted => stats.fatigue >= 100f;

    public void Die() => TransitionToState(new DeadState(this));

    public void ClearAssignment()
    {
        if (!string.IsNullOrEmpty(currentBuildingId)) previousBuildingId = currentBuildingId;
        currentBuildingId = null;
        currentMissionId  = null;
        currentJobType    = JobType.None;
    }

    [NonSerialized] public BuildingType? currentService;
    public bool IsAvailable => currentStateType == AdventurerStateType.Idle && !currentService.HasValue;

    // =========================================================================
    // Métiers
    // =========================================================================

    private void InitializeJobs()
    {
        _jobInstances = new Dictionary<JobType, JobInstance>();
        foreach (JobType jobType in Enum.GetValues(typeof(JobType)))
        {
            if (jobType == JobType.None) continue;
            _jobInstances[jobType] = new JobInstance(jobType);
        }
        SyncJobList();
    }

    public JobInstance GetJobInstance(JobType jobType)
    {
        _jobInstances.TryGetValue(jobType, out var instance);
        return instance;
    }

    public bool AddJobExperience(JobType jobType, int amount, JobDefinitionSO definition)
    {
        var instance = GetJobInstance(jobType);
        if (instance == null || definition == null) return false;

        int finalAmount = Mathf.RoundToInt(
            amount * GetTraitMultiplier(TraitEffectType.ExperienceGain_Multiplier));

        bool leveledUp = instance.AddExperience(definition, finalAmount);
        if (leveledUp)
            OnJobLevelUp?.Invoke(this, jobType, instance.currentLevel);

        SyncJobList();
        return leveledUp;
    }

    private void SyncJobList() => jobInstanceList = new List<JobInstance>(_jobInstances.Values);

    // =========================================================================
    // Traits
    // =========================================================================

    public float GetTraitMultiplier(TraitEffectType effectType)
    {
        float result = 1f;
        foreach (var trait in traits)
        {
            if (trait == null) continue;
            foreach (var effect in trait.effects)
                if (effect.effectType == effectType) result *= effect.value;
        }
        if (adventurerClass != null)
            foreach (var effect in adventurerClass.classPassives)
                if (effect.effectType == effectType) result *= effect.value;
        return result;
    }

    public float GetTraitFlat(TraitEffectType effectType)
    {
        float result = 0f;
        foreach (var trait in traits)
            if (trait != null)
                foreach (var effect in trait.effects)
                    if (effect.effectType == effectType) result += effect.value;
        if (adventurerClass != null)
            foreach (var effect in adventurerClass.classPassives)
                if (effect.effectType == effectType) result += effect.value;
        return result;
    }

    // =========================================================================
    // Affinités
    // =========================================================================

    public void ChangeAffinity(Adventurer other, float delta)
    {
        if (other == null || other.id == id) return;

        if (!_affinities.TryGetValue(other.id, out var data))
        {
            float start = 50f;
            if (adventurerRace != null && other.adventurerRace != null)
                start += adventurerRace.GetAffinityModifierWith(other.adventurerRace.raceType);

            data = new AffinityData(other.id, Mathf.Clamp(start, 0f, 100f));
            _affinities[other.id] = data;
        }

        data.ChangeValue(delta);
        SyncAffinityList();
    }

    public AffinityData GetAffinityWith(string adventurerId)
    {
        _affinities.TryGetValue(adventurerId, out var data);
        return data;
    }

    public AffinityType GetAffinityTypeWith(string adventurerId)
        => GetAffinityWith(adventurerId)?.affinityType ?? AffinityType.Stranger;

    private void SyncAffinityList() => affinityList = new List<AffinityData>(_affinities.Values);

    // =========================================================================
    // Économie
    // =========================================================================

    public int CalculateSalary()
    {
        int totalJobLevels = 0;
        foreach (var job in jobInstanceList)
            totalJobLevels += job.currentLevel;
        float averageJobLevel = jobInstanceList.Count == 0 ? 0f : (float)totalJobLevels / jobInstanceList.Count;
        float salary = (adventurerClass?.baseSalary ?? 10) + 3f * (stats.level - 1) + 3f * averageJobLevel;
        salary *= GetTraitMultiplier(TraitEffectType.Salary_Multiplier);
        return Mathf.Max(1, Mathf.RoundToInt(salary));
    }

    public float GetCommissionRate(JobType job)
    {
        int level = GetJobInstance(job)?.currentLevel ?? 0;
        float rate = (0.05f + 0.01f * level) * GetTraitMultiplier(TraitEffectType.Commission_Multiplier);
        return Mathf.Clamp(rate, 0.05f, 0.30f);
    }

    public int CalculateCommission(int value, JobType job) => value <= 0 ? 0
        : Mathf.Max(1, Mathf.RoundToInt(value * GetCommissionRate(job)));

    // =========================================================================
    // XP général
    // =========================================================================

    public void AddExperience(int amount)
    {
        int final = Mathf.RoundToInt(
            amount * GetTraitMultiplier(TraitEffectType.ExperienceGain_Multiplier));

        if (stats.AddExperience(final))
            OnLevelUp?.Invoke(this, stats.level);
    }

    // =========================================================================
    // Post-load — les refs sont résolues par AdventurerManager puis passées ici
    // =========================================================================

    public void OnAfterLoad(AdventurerClassSO classData, AdventurerRaceSO raceData, List<TraitSO> traitsData)
    {
        adventurerClass = classData;
        adventurerRace  = raceData;
        traits          = traitsData ?? new List<TraitSO>();
        equippedItems ??= new List<EquippedItemSlot>();

        _jobInstances = new Dictionary<JobType, JobInstance>();
        foreach (var j in jobInstanceList) _jobInstances[j.jobType] = j;

        _affinities = new Dictionary<string, AffinityData>();
        foreach (var a in affinityList) _affinities[a.targetAdventurerId] = a;

        _currentState = currentStateType switch
        {
            AdventurerStateType.Working   => new WorkingState(this),
            AdventurerStateType.OnMission => new OnMissionState(this),
            AdventurerStateType.Resting   => new RestingState(this),
            AdventurerStateType.Injured   => new InjuredState(this),
            AdventurerStateType.Dead      => new DeadState(this),
            _                             => new IdleState(this)
        };
    }
}

[Serializable]
public class EquippedItemSlot
{
    public EquipmentSlot slot;
    public string equipmentId;
}
