using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance { get; private set; }

    [SerializeField] private EconomyManager economy;
    [SerializeField] private BuildingManager buildings;
    [SerializeField] private AdventurerManager adventurers;
    [SerializeField] private GameSpeed initialSpeed = GameSpeed.Normal;

    public const int HoursPerDay = 24;
    public const int DaysPerMonth = 30;
    public const int MonthsPerYear = 12;
    public const int HoursPerMonth = HoursPerDay * DaysPerMonth;
    private const int StepsPerHour = 10;
    private const double Step = 1d / StepsPerHour;
    private double _accumulator;
    private long _elapsedSteps;
    private bool _initialized;
    private GameSpeed _resumeSpeed = GameSpeed.Normal;
    private InputAction _pauseShortcut;

    public GameSpeed CurrentSpeed { get; private set; } = GameSpeed.Paused;
    public double ElapsedGameTime => _elapsedSteps * Step;
    public long Year => _elapsedSteps / (StepsPerHour * HoursPerMonth * MonthsPerYear) + 1;
    public int Month => (int)(_elapsedSteps / (StepsPerHour * HoursPerMonth) % MonthsPerYear) + 1;
    public int Day => (int)(_elapsedSteps / (StepsPerHour * HoursPerDay) % DaysPerMonth) + 1;
    public int Hour => (int)(_elapsedSteps / StepsPerHour % HoursPerDay);
    public event Action<GameSpeed> OnSpeedChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        if (Instance != this) return;
        _pauseShortcut = new InputAction("Pause / Reprendre", InputActionType.Button, "<Keyboard>/space");
        _pauseShortcut.performed += HandlePauseShortcut;
        _pauseShortcut.Enable();
    }

    private void OnDisable()
    {
        if (_pauseShortcut == null) return;
        _pauseShortcut.performed -= HandlePauseShortcut;
        _pauseShortcut.Disable();
        _pauseShortcut.Dispose();
        _pauseShortcut = null;
    }

    public void ResetForNewGame()
    {
        _accumulator = 0;
        _elapsedSteps = 0;
        _initialized = true;
        _resumeSpeed = initialSpeed == GameSpeed.Paused ? GameSpeed.Normal : initialSpeed;
        SetSpeed(initialSpeed);
    }

    public void SetSpeed(GameSpeed speed)
    {
        if (!Enum.IsDefined(typeof(GameSpeed), speed) || CurrentSpeed == speed) return;
        if (speed == GameSpeed.Paused)
            _accumulator = 0;
        else
            _resumeSpeed = speed;
        CurrentSpeed = speed;
        OnSpeedChanged?.Invoke(speed);
    }

    public void TogglePause()
    {
        if (CurrentSpeed == GameSpeed.Paused) Resume();
        else SetSpeed(GameSpeed.Paused);
    }

    public void Resume() => SetSpeed(_resumeSpeed);

    private void HandlePauseShortcut(InputAction.CallbackContext context)
    {
        if (_initialized) TogglePause();
    }

    private void Update() => Advance(Time.unscaledDeltaTime);

    // À vitesse normale, une seconde réelle fait avancer la simulation d'une heure de jeu.
    public void Advance(float realSeconds)
    {
        if (!_initialized || CurrentSpeed == GameSpeed.Paused ||
            realSeconds <= 0f || float.IsNaN(realSeconds) || float.IsInfinity(realSeconds)) return;
        _accumulator += realSeconds * (int)CurrentSpeed;
        // Un événement du journal peut mettre la simulation en pause pendant un pas.
        while (CurrentSpeed != GameSpeed.Paused && _accumulator + 0.000001 >= Step)
        {
            _accumulator -= Step;
            _elapsedSteps++;
            if (_elapsedSteps % (StepsPerHour * HoursPerMonth) == 0)
                economy.PayMonthlyPayroll(adventurers.GetAllAdventurers());
            economy.Tick((float)Step);
            buildings.TrackWorkerActivity((float)Step);
            buildings.Tick((float)Step);
            adventurers.Tick((float)Step);
            buildings.ApplyWorkerFatigue();
        }
    }
}
