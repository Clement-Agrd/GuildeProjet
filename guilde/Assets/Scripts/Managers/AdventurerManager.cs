using System;
using System.Collections.Generic;
using UnityEngine;

public class AdventurerManager : MonoBehaviour
{
    public static AdventurerManager Instance { get; private set; }

    [Header("Définitions (pool de génération)")]
    [SerializeField] private List<AdventurerClassSO> allClasses = new();
    [SerializeField] private List<AdventurerRaceSO>  allRaces   = new();
    [SerializeField] private List<TraitSO>           allTraits  = new();

    [Header("Recrutement")]
    [SerializeField] private int   recruitPoolSize    = 4;
    [SerializeField] private float recruitRefreshTime = 300f; // secondes de jeu
    [SerializeField] private int   baseHiringBonus    = 20;

    // ─── Roster ───────────────────────────────────────────────────────────────
    private readonly List<Adventurer>               _roster = new();
    private readonly Dictionary<string, Adventurer>  _byId   = new();

    // ─── Recrutement ──────────────────────────────────────────────────────────
    private readonly List<RecruitOffer> _recruitPool = new();
    private float _recruitTimer;

    // ─── Events ───────────────────────────────────────────────────────────────
    public event Action<Adventurer>               OnAdventurerHired;
    public event Action<Adventurer>               OnAdventurerFired;
    public event Action<Adventurer>               OnAdventurerDied;
    public event Action<Adventurer>               OnAdventurerRested;
    public event Action<Adventurer, int>          OnAdventurerLevelUp;
    public event Action<Adventurer, JobType, int> OnAdventurerJobLevelUp;
    public event Action                           OnRecruitPoolRefreshed;

    // =========================================================================
    // Unity lifecycle
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (_recruitPool.Count == 0 && allClasses.Count > 0 && allRaces.Count > 0)
            RefreshRecruitPool();
    }

    private void OnDestroy()
    {
        foreach (var a in _roster)
        {
            a.OnLevelUp -= HandleLevelUp;
            a.OnJobLevelUp -= HandleJobLevelUp;
        }
        if (Instance == this) Instance = null;
    }

    public void ResetForNewGame()
    {
        foreach (var a in _roster)
        {
            a.OnLevelUp -= HandleLevelUp;
            a.OnJobLevelUp -= HandleJobLevelUp;
        }
        _roster.Clear();
        _byId.Clear();
        _recruitTimer = 0f;
        RefreshRecruitPool();
    }

    public float RecruitRefreshRemaining => Mathf.Max(0f, recruitRefreshTime - _recruitTimer);

    // =========================================================================
    // Tick — appelé par TimeManager
    // =========================================================================

    public void Tick(float deltaTime)
    {
        foreach (var a in _roster)
        {
            if (a.currentService == BuildingType.Tavern) continue;
            bool wasResting = a.currentStateType == AdventurerStateType.Resting;
            a.Tick(deltaTime);
            if (wasResting && a.IsAvailable) OnAdventurerRested?.Invoke(a);
        }

        _recruitTimer += deltaTime;
        if (_recruitTimer >= recruitRefreshTime)
        {
            _recruitTimer = 0f;
            RefreshRecruitPool();
        }
    }

    // =========================================================================
    // Roster
    // =========================================================================

    public bool HireAdventurer(string recruitId)
    {
        if (BuildingManager.Instance != null && !BuildingManager.Instance.CanHire) return false;
        var offer = _recruitPool.Find(r => r.candidate.id == recruitId);
        if (offer == null) return false;
        if (!EconomyManager.Instance.SpendGold(offer.hiringBonus)) return false;

        _recruitPool.Remove(offer);
        RegisterAdventurer(offer.candidate);
        BuildingManager.Instance?.RecordTransactionWork(BuildingType.Recruiter);
        OnAdventurerHired?.Invoke(offer.candidate);
        return true;
    }

    public bool FireAdventurer(string adventurerId)
    {
        if (!_byId.TryGetValue(adventurerId, out var adventurer)) return false;
        if (adventurer.currentStateType == AdventurerStateType.OnMission) return false;

        if (adventurer.currentStateType == AdventurerStateType.Working)
            BuildingManager.Instance.UnassignAdventurer(adventurer);

        _roster.Remove(adventurer);
        _byId.Remove(adventurerId);
        BuildingManager.Instance.StopService(adventurer);
        BuildingManager.Instance.CancelTasksForWorker(adventurerId);
        adventurer.OnLevelUp -= HandleLevelUp;
        adventurer.OnJobLevelUp -= HandleJobLevelUp;
        OnAdventurerFired?.Invoke(adventurer);
        return true;
    }

    private void RegisterAdventurer(Adventurer a)
    {
        if (!_roster.Contains(a)) _roster.Add(a);
        _byId[a.id] = a;
        a.BindEquipmentResolver(GuildInventory.Instance.GetEquipment);

        a.OnLevelUp    -= HandleLevelUp;
        a.OnLevelUp    += HandleLevelUp;
        a.OnJobLevelUp -= HandleJobLevelUp;
        a.OnJobLevelUp += HandleJobLevelUp;
    }

    // =========================================================================
    // Recrutement
    // =========================================================================

    public void RefreshRecruitPool()
    {
        _recruitPool.Clear();
        _recruitTimer = 0f;
        if (allClasses.Count == 0 || allRaces.Count == 0)
        {
            OnRecruitPoolRefreshed?.Invoke();
            return;
        }
        for (int i = 0; i < recruitPoolSize; i++)
            _recruitPool.Add(GenerateRecruit());

        OnRecruitPoolRefreshed?.Invoke();
    }

    private RecruitOffer GenerateRecruit()
    {
        var cls  = allClasses[UnityEngine.Random.Range(0, allClasses.Count)];
        var race = allRaces[UnityEngine.Random.Range(0, allRaces.Count)];

        var traits = new List<TraitSO>(race.racialTraits);
        int extraTraits = UnityEngine.Random.Range(0, 3);
        for (int i = 0; i < extraTraits && allTraits.Count > 0; i++)
        {
            var t = allTraits[UnityEngine.Random.Range(0, allTraits.Count)];
            if (!traits.Contains(t)) traits.Add(t);
        }

        string[] names = { "Aldric", "Liora", "Borin", "Maëlys", "Ewen", "Sélène", "Garrick", "Nora", "Théa", "Oswin", "Elian", "Runa" };
        string[] surnames = { "Briseforge", "Boisclair", "Duvallon", "Flamberge", "Hauterive", "Desrosiers", "Courtebraise", "Pierrebrune" };
        string name = names[UnityEngine.Random.Range(0, names.Length)] + " " + surnames[UnityEngine.Random.Range(0, surnames.Length)];
        var candidate = new Adventurer(name, cls, race, traits, age: UnityEngine.Random.Range(18, 45));

        // Les recrues proposées peuvent déjà avoir du niveau dans un métier
        if (UnityEngine.Random.value < 0.4f)
        {
            var jobType = (JobType)UnityEngine.Random.Range(1, Enum.GetValues(typeof(JobType)).Length);
            var jobInst = candidate.GetJobInstance(jobType);
            var jobDef  = BuildingManager.Instance.GetJobDefinition(jobType);
            if (jobInst != null && jobDef != null)
            {
                int preLevels = UnityEngine.Random.Range(1, 4);
                for (int i = 0; i < preLevels; i++)
                    jobInst.AddExperience(jobDef, jobDef.GetLevelData(jobInst.currentLevel + 1).experienceRequired);
            }
        }

        int bonus = Mathf.RoundToInt(baseHiringBonus * (1f + candidate.stats.level * 0.2f));
        return new RecruitOffer(candidate, bonus);
    }

    public List<RecruitOffer> GetRecruitPool() => _recruitPool;

    public bool DeclineRecruit(string recruitId)
        => _recruitPool.RemoveAll(offer => offer.candidate.id == recruitId) > 0;

    // =========================================================================
    // Affinités — pont entre 2 IDs (Adventurer.ChangeAffinity veut une ref directe)
    // =========================================================================

    public void ChangeAffinityBetween(string idA, string idB, float delta)
    {
        if (!_byId.TryGetValue(idA, out var a)) return;
        if (!_byId.TryGetValue(idB, out var b)) return;
        a.ChangeAffinity(b, delta);
        b.ChangeAffinity(a, delta);
    }

    // =========================================================================
    // Mort — sera surtout utilisé par MissionManager
    // =========================================================================

    public void KillAdventurer(string adventurerId)
    {
        if (!_byId.TryGetValue(adventurerId, out var adventurer)) return;
        if (adventurer.currentStateType == AdventurerStateType.Working)
            BuildingManager.Instance.UnassignAdventurer(adventurer);

        adventurer.Die();
        BuildingManager.Instance.StopService(adventurer);
        BuildingManager.Instance.CancelTasksForWorker(adventurerId);
        OnAdventurerDied?.Invoke(adventurer);
    }

    // =========================================================================
    // Queries
    // =========================================================================

    public Adventurer       GetById(string id)                => id != null && _byId.TryGetValue(id, out var a) ? a : null;
    public List<Adventurer> GetAllAdventurers()                => _roster;
    public List<Adventurer> GetAvailableAdventurers()          => _roster.FindAll(a => a.IsAvailable);
    public List<Adventurer> GetAdventurersInBuilding(string id) => _roster.FindAll(a => a.currentBuildingId == id);

    public AdventurerClassSO GetClassById(string id) => allClasses.Find(c => c.name == id);
    public AdventurerRaceSO  GetRaceById(string id)  => allRaces.Find(r => r.name == id);
    public TraitSO           GetTraitById(string id) => allTraits.Find(t => t.name == id);

    // =========================================================================
    // Handlers internes
    // =========================================================================

    private void HandleLevelUp(Adventurer a, int newLevel) => OnAdventurerLevelUp?.Invoke(a, newLevel);
    private void HandleJobLevelUp(Adventurer a, JobType job, int newLevel) => OnAdventurerJobLevelUp?.Invoke(a, job, newLevel);

    // =========================================================================
    // Save / Load
    // =========================================================================

    public void LoadRoster(List<Adventurer> loadedRoster)
    {
        _roster.Clear();
        _roster.AddRange(loadedRoster);
    }

    public void OnAfterLoad()
    {
        _byId.Clear();
        foreach (var a in _roster)
        {
            var cls    = GetClassById(a.classId);
            var race   = GetRaceById(a.raceId);
            var traits = new List<TraitSO>();
            foreach (var tid in a.traitIds)
            {
                var t = GetTraitById(tid);
                if (t != null) traits.Add(t);
            }

            a.OnAfterLoad(cls, race, traits);
            RegisterAdventurer(a);
        }

        RefreshRecruitPool();
    }
}

[Serializable]
public class RecruitOffer
{
    public Adventurer candidate;
    public int         hiringBonus;

    public RecruitOffer() { }
    public RecruitOffer(Adventurer candidate, int hiringBonus)
    {
        this.candidate   = candidate;
        this.hiringBonus = hiringBonus;
    }
}
