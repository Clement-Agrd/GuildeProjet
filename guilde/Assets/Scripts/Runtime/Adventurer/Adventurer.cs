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

    // IDs pour save/load (les SO refs ne se sérialisent pas)
    public string       classId;
    public string       raceId;
    public List<string> traitIds = new();

    // Refs runtime — non sérialisées, reconstruites au chargement par AdventurerManager
    [NonSerialized] public AdventurerClassSO adventurerClass;
    [NonSerialized] public AdventurerRaceSO  adventurerRace;
    [NonSerialized] public List<TraitSO>     traits = new();

    // ─── Stats ────────────────────────────────────────────────────────────────
    public AdventurerStats stats = new();

    // ─── Métiers ──────────────────────────────────────────────────────────────
    // Tous les aventuriers ont accès aux 6 métiers dès leur création (niveau 0)
    public List<JobInstance> jobInstanceList = new(); // sérialisable

    [NonSerialized] private Dictionary<JobType, JobInstance> _jobInstances = new();

    // ─── Assignment courant ───────────────────────────────────────────────────
    public AdventurerStateType currentStateType    = AdventurerStateType.Idle;
    public string              currentBuildingId;
    public string              currentMissionId;
    public JobType             currentJobType = JobType.None;

    // ─── Affinités ────────────────────────────────────────────────────────────
    public List<AffinityData> affinityList = new(); // sérialisable

    [NonSerialized] private Dictionary<string, AffinityData> _affinities = new();

    // ─── State machine (non sérialisée) ───────────────────────────────────────
    [NonSerialized] private AdventurerState _currentState;

    // ─── Events ───────────────────────────────────────────────────────────────
    [field: NonSerialized] public event Action<AdventurerStateType, AdventurerStateType> OnStateChanged;
    [field: NonSerialized] public event Action<JobType, int> OnJobLevelUp; // jobType, newLevel
    [field: NonSerialized] public event Action<int>          OnLevelUp;    // newLevel

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

        // Stats : base classe + modificateurs race
        stats = new AdventurerStats(classData?.baseStats ?? new AdventurerStats());
        raceData?.ApplyToStats(stats);
        stats.ClampSecondaryStats();

        InitializeJobs();

        // State machine
        _currentState = new IdleState(this);
        currentStateType = AdventurerStateType.Idle;
        _currentState.Enter();
    }

    // =========================================================================
    // Tick — appelé par TimeManager à chaque tick de jeu
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
        OnStateChanged?.Invoke(previous, currentStateType);
    }

    // ─── API publique pour les Managers ───────────────────────────────────────

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

    public void SetInjured(float healingTime = 60f)
    {
        if (currentStateType == AdventurerStateType.Dead) return;
        TransitionToState(new InjuredState(this, healingTime));
    }

    public void Die() => TransitionToState(new DeadState(this));

    // Appelé par les states dans leur Enter() quand ils libèrent l'aventurier
    public void ClearAssignment()
    {
        currentBuildingId = null;
        currentMissionId  = null;
        currentJobType    = JobType.None;
    }

    public bool IsAvailable => currentStateType == AdventurerStateType.Idle;

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

    /// <summary>
    /// Ajoute de l'XP au métier ciblé.
    /// Retourne true si l'aventurier monte de niveau de métier.
    /// </summary>
    public bool AddJobExperience(JobType jobType, int amount, JobDefinitionSO definition)
    {
        var instance = GetJobInstance(jobType);
        if (instance == null || definition == null) return false;

        int finalAmount = Mathf.RoundToInt(
            amount * GetTraitMultiplier(TraitEffectType.ExperienceGain_Multiplier));

        bool leveledUp = instance.AddExperience(definition, finalAmount);
        if (leveledUp)
            OnJobLevelUp?.Invoke(jobType, instance.currentLevel);

        SyncJobList();
        return leveledUp;
    }

    private void SyncJobList()
        => jobInstanceList = new List<JobInstance>(_jobInstances.Values);

    // =========================================================================
    // Traits — helpers
    // =========================================================================

    /// <summary>Produit cumulé des effets multiplicateurs de tous les traits actifs.</summary>
    public float GetTraitMultiplier(TraitEffectType effectType)
    {
        float result = 1f;
        foreach (var trait in traits)
        {
            float v = trait.GetEffectValue(effectType);
            if (v != 0f) result *= v;
        }
        return result;
    }

    /// <summary>Somme des effets plats de tous les traits actifs.</summary>
    public float GetTraitFlat(TraitEffectType effectType)
    {
        float result = 0f;
        foreach (var trait in traits)
            result += trait.GetEffectValue(effectType);
        return result;
    }

    // =========================================================================
    // Affinités
    // =========================================================================

    /// <summary>
    /// Modifie l'affinité avec un autre aventurier.
    /// Crée l'entrée si inexistante, avec valeur de départ basée sur les races.
    /// </summary>
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

    private void SyncAffinityList()
        => affinityList = new List<AffinityData>(_affinities.Values);

    // =========================================================================
    // Économie
    // =========================================================================

    /// <summary>
    /// Salaire courant. Passe activeJobDef si l'aventurier est en train de travailler.
    /// Base classe × niveau général × traits × prime de niveau métier.
    /// </summary>
    public int CalculateSalary(JobDefinitionSO activeJobDef = null)
    {
        if (adventurerClass == null) return 0;

        float salary = adventurerClass.baseSalary;
        salary *= 1f + (stats.level - 1) * 0.1f;
        salary *= GetTraitMultiplier(TraitEffectType.Salary_Multiplier);

        if (activeJobDef != null && currentJobType != JobType.None)
        {
            var jobInst = GetJobInstance(currentJobType);
            if (jobInst != null)
                salary *= jobInst.GetSalaryMultiplier(activeJobDef);
        }

        return Mathf.Max(1, Mathf.RoundToInt(salary));
    }

    /// <summary>
    /// Commission prélevée sur la vente d'un objet produit.
    /// Base 15% — réduite par passion naturelle (affinité de classe) et traits.
    /// </summary>
    public float CalculateCommission(float salePrice, JobDefinitionSO activeJobDef)
    {
        float rate = 0.15f;

        if (adventurerClass != null && currentJobType != JobType.None)
            rate -= adventurerClass.GetJobAffinityBonus(currentJobType) * 0.5f;

        rate *= GetTraitMultiplier(TraitEffectType.Commission_Multiplier);
        rate  = Mathf.Clamp(rate, 0.05f, 0.30f);

        return salePrice * rate;
    }

    // =========================================================================
    // XP général
    // =========================================================================

    public void AddExperience(int amount)
    {
        int final = Mathf.RoundToInt(
            amount * GetTraitMultiplier(TraitEffectType.ExperienceGain_Multiplier));

        if (stats.AddExperience(final))
            OnLevelUp?.Invoke(stats.level);
    }

    // =========================================================================
    // Post-load — à appeler par AdventurerManager après désérialisation
    // =========================================================================

    public void OnAfterLoad()
    {
        // Reconstruit les dictionnaires depuis les listes sérialisées
        _jobInstances = new Dictionary<JobType, JobInstance>();
        foreach (var j in jobInstanceList) _jobInstances[j.jobType] = j;

        _affinities = new Dictionary<string, AffinityData>();
        foreach (var a in affinityList) _affinities[a.targetAdventurerId] = a;

        // Reconstruit la state machine depuis l'état sérialisé
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