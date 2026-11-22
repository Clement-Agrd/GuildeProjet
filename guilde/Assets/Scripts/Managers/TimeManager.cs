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

    private const float Step = 0.1f;
    private double _accumulator;
    private bool _initialized;
    private GameSpeed _resumeSpeed = GameSpeed.Normal;
    private InputAction _pauseShortcut;

    public GameSpeed CurrentSpeed { get; private set; } = GameSpeed.Paused;
    public double ElapsedGameTime { get; private set; }
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
        ElapsedGameTime = 0;
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

    // Pas fixe commun aux vitesses x1/x2/x3 : la fatigue ne saute pas une fabrication.
    public void Advance(float realSeconds)
    {
        if (!_initialized || CurrentSpeed == GameSpeed.Paused ||
            realSeconds <= 0f || float.IsNaN(realSeconds) || float.IsInfinity(realSeconds)) return;
        _accumulator += realSeconds * (int)CurrentSpeed;
        // Un événement du journal peut mettre la simulation en pause pendant un pas.
        while (CurrentSpeed != GameSpeed.Paused && _accumulator + 0.000001 >= Step)
        {
            _accumulator -= Step;
            economy.Tick(Step);
            buildings.TrackWorkerActivity(Step);
            buildings.Tick(Step);
            adventurers.Tick(Step);
            buildings.ApplyWorkerFatigue();
            ElapsedGameTime += Step;
        }
    }
}
