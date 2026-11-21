using System;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private AdventurerManager adventurers;
    [SerializeField] private BuildingManager buildings;
    [SerializeField] private EconomyManager economy;
    [SerializeField] private GuildInventory inventory;
    [SerializeField] private TimeManager clock;
    [SerializeField] private List<BuildingType> startingBuildings = new() { BuildingType.Forge, BuildingType.Market };
    [SerializeField] private List<ResourceCost> startingResources = new();

    public bool IsInitialized { get; private set; }
    public event Action OnNewGameStarted;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start() => StartNewGame();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void StartNewGame()
    {
        if (!adventurers || !buildings || !economy || !inventory || !clock)
        {
            Debug.LogError("Nouvelle partie impossible : les managers doivent être reliés dans la scène.", this);
            return;
        }

        IsInitialized = false;
        clock.SetSpeed(GameSpeed.Paused);
        buildings.ResetForNewGame();
        inventory.ResetForNewGame();
        adventurers.ResetForNewGame();
        economy.ResetForNewGame();
        buildings.InitializeDefaultBuildings(startingBuildings);
        foreach (var cost in startingResources)
            if (cost.resource != null) inventory.AddResource(cost.resource, cost.amount);
        clock.ResetForNewGame();
        IsInitialized = true;
        OnNewGameStarted?.Invoke();
    }
}
