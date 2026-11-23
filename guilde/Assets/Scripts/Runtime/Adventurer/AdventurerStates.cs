using UnityEngine;

// ─── Base ─────────────────────────────────────────────────────────────────────

public abstract class AdventurerState
{
    protected readonly Adventurer Owner;
    public abstract AdventurerStateType StateType { get; }

    protected AdventurerState(Adventurer owner) => Owner = owner;

    public virtual void Enter() { }
    public virtual void Exit()  { }
    public virtual void Tick(float deltaTime) { }
}

// ─── Idle ─────────────────────────────────────────────────────────────────────

public class IdleState : AdventurerState
{
    public override AdventurerStateType StateType => AdventurerStateType.Idle;
    public IdleState(Adventurer owner) : base(owner) { }

    public override void Enter() => Owner.ClearAssignment();

    public override void Tick(float deltaTime)
    {
        // Récupération passive lente
        Owner.stats.fatigue -= 3f * deltaTime;
        Owner.stats.ClampSecondaryStats();
    }
}

// ─── Working ──────────────────────────────────────────────────────────────────

public class WorkingState : AdventurerState
{
    public override AdventurerStateType StateType => AdventurerStateType.Working;
    public WorkingState(Adventurer owner) : base(owner) { }
}

// ─── OnMission ────────────────────────────────────────────────────────────────

public class OnMissionState : AdventurerState
{
    public override AdventurerStateType StateType => AdventurerStateType.OnMission;
    public OnMissionState(Adventurer owner) : base(owner) { }

    public override void Enter()
    {
        // Légère montée de moral au départ
        Owner.stats.moral += 5f;
        Owner.stats.ClampSecondaryStats();
    }

    // La fatigue pendant la mission est gérée par MissionManager à la résolution
}

// ─── Resting ──────────────────────────────────────────────────────────────────

public class RestingState : AdventurerState
{
    public override AdventurerStateType StateType => AdventurerStateType.Resting;
    public RestingState(Adventurer owner) : base(owner) { }

    public override void Enter() => Owner.ClearAssignment();

    public override void Tick(float deltaTime)
    {
        Owner.stats.fatigue -= Owner.RestRecoveryPerHour * deltaTime;
        Owner.stats.ClampSecondaryStats();

        if (Owner.stats.fatigue <= 10f)
            Owner.TransitionToState(new IdleState(Owner));
    }
}

// ─── Injured ──────────────────────────────────────────────────────────────────

public class InjuredState : AdventurerState
{
    public override AdventurerStateType StateType => AdventurerStateType.Injured;

    private float _progress;
    private readonly float _totalTime;
    private readonly float _initialHealthFraction;

    public float HealthFraction => Mathf.Lerp(_initialHealthFraction, 1f, _totalTime > 0f ? _progress / _totalTime : 1f);

    public InjuredState(Adventurer owner, float healingTime = 60f, float initialHealthFraction = 0.25f) : base(owner)
    {
        _totalTime = Mathf.Max(0f, healingTime);
        _initialHealthFraction = Mathf.Clamp01(initialHealthFraction);
    }

    public override void Enter() => Owner.ClearAssignment();

    public override void Tick(float deltaTime)
    {
        _progress += deltaTime;
        if (_progress >= _totalTime)
            Owner.TransitionToState(new RestingState(Owner));
    }
}

// ─── Dead ─────────────────────────────────────────────────────────────────────

public class DeadState : AdventurerState
{
    public override AdventurerStateType StateType => AdventurerStateType.Dead;
    public DeadState(Adventurer owner) : base(owner) { }

    public override void Enter()
    {
        Owner.ClearAssignment();
        Owner.stats.moral = 0f;
    }
    // État terminal — pas de Tick
}
