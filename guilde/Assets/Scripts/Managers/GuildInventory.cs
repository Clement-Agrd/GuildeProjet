using System;
using System.Collections.Generic;
using UnityEngine;

public class GuildInventory : MonoBehaviour
{
    public static GuildInventory Instance { get; private set; }

    [Header("Définitions (résolution des refs au chargement)")]
    [SerializeField] private List<ResourceSO>  allResources  = new();
    [SerializeField] private List<EquipmentSO> allEquipments = new();
    [SerializeField, Min(1)] private int equipmentCapacity = 12;

    // ─── Ressources ───────────────────────────────────────────────────────────
    private readonly List<ResourceStack>               _resourceStacks = new();
    private readonly Dictionary<string, ResourceStack>  _stockById      = new();

    // ─── Équipements ──────────────────────────────────────────────────────────
    private readonly List<EquipmentInstance>               _equipment     = new();
    private readonly Dictionary<string, EquipmentInstance> _equipmentById = new();

    // ─── Events ───────────────────────────────────────────────────────────────
    public event Action<ResourceSO, int>   OnResourceChanged; // ressource, nouveau total
    public event Action<EquipmentInstance> OnEquipmentAdded;
    public event Action<string>            OnEquipmentRemoved;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ResetForNewGame()
    {
        _resourceStacks.Clear();
        _stockById.Clear();
        _equipment.Clear();
        _equipmentById.Clear();
    }

    // =========================================================================
    // Ressources
    // =========================================================================

    public bool HasResources(List<ResourceCost> costs)
    {
        if (costs == null) return true;
        foreach (var cost in costs)
        {
            if (cost == null || cost.resource == null || cost.amount < 0) return false;
            int required = 0;
            foreach (var other in costs)
                if (other != null && other.resource == cost.resource) required += Mathf.Max(0, other.amount);
            if (GetAmount(cost.resource) < required) return false;
        }
        return true;
    }

    public void ConsumeResources(List<ResourceCost> costs)
    {
        if (costs == null || !HasResources(costs)) return;
        foreach (var cost in costs)
        {
            if (cost.resource == null) continue;
            AddResource(cost.resource, -cost.amount);
        }
    }

    public void AddResource(ResourceSO resource, int amount)
    {
        if (resource == null || amount == 0) return;

        if (!_stockById.TryGetValue(resource.name, out var stack))
        {
            stack = new ResourceStack { resourceId = resource.name, amount = 0 };
            _stockById[resource.name] = stack;
            _resourceStacks.Add(stack);
        }

        stack.amount = Mathf.Clamp(stack.amount + amount, 0, resource.maxStackSize);
        OnResourceChanged?.Invoke(resource, stack.amount);
    }

    public int GetAmount(ResourceSO resource)
        => resource != null && _stockById.TryGetValue(resource.name, out var s) ? s.amount : 0;

    public List<ResourceStack> GetAllStock() => _resourceStacks;
    public IReadOnlyList<ResourceSO> ResourceDefinitions => allResources;

    public int GetRemainingCapacity(ResourceSO resource)
        => resource == null ? 0 : Mathf.Max(0, resource.maxStackSize - GetAmount(resource));

    // =========================================================================
    // Équipements
    // =========================================================================

    public int EquipmentCapacity => Mathf.Max(1, equipmentCapacity);
    public int EquipmentCount => _equipment.Count;
    public bool IsEquipmentFull => EquipmentCount >= EquipmentCapacity;
    public bool CanReserveEquipmentSlot
        => EquipmentCount + (BuildingManager.Instance?.ReservedEquipmentSlots ?? 0) < EquipmentCapacity;

    public bool AddEquipment(EquipmentInstance item)
        => CanReserveEquipmentSlot && AddEquipmentToInventory(item);

    // La commande a réservé sa place avant de consommer ses composants.
    internal void ReceiveCraftedEquipment(EquipmentInstance item) => AddEquipmentToInventory(item);

    private bool AddEquipmentToInventory(EquipmentInstance item)
    {
        if (item == null || string.IsNullOrEmpty(item.equipmentId) || _equipmentById.ContainsKey(item.equipmentId)) return false;
        _equipment.Add(item);
        _equipmentById[item.equipmentId] = item;
        OnEquipmentAdded?.Invoke(item);
        return true;
    }

    public bool RemoveEquipment(string equipmentId)
    {
        if (!_equipmentById.TryGetValue(equipmentId, out var item)) return false;
        _equipment.Remove(item);
        _equipmentById.Remove(equipmentId);
        OnEquipmentRemoved?.Invoke(equipmentId);
        return true;
    }

    public EquipmentInstance       GetEquipment(string equipmentId) => equipmentId != null && _equipmentById.TryGetValue(equipmentId, out var i) ? i : null;
    public List<EquipmentInstance> GetAllEquipment()                => _equipment;
    public List<EquipmentInstance> GetEquipmentBySlot(EquipmentSlot slot)
        => _equipment.FindAll(e => e.definition != null && e.definition.slot == slot);

    // =========================================================================
    // Résolution de définitions
    // =========================================================================

    public ResourceSO  GetResourceById(string id)     => allResources.Find(r => r.name == id);
    public EquipmentSO GetEquipmentDefById(string id) => allEquipments.Find(e => e.name == id);

    // =========================================================================
    // Save / Load
    // =========================================================================

    public void LoadInventory(List<ResourceStack> stacks, List<EquipmentInstance> items)
    {
        _resourceStacks.Clear();
        _stockById.Clear();
        foreach (var s in stacks)
        {
            _resourceStacks.Add(s);
            _stockById[s.resourceId] = s;
        }

        _equipment.Clear();
        _equipmentById.Clear();
        foreach (var item in items)
        {
            item.OnAfterLoad(GetEquipmentDefById(item.definitionId));
            _equipment.Add(item);
            _equipmentById[item.equipmentId] = item;
        }
    }
}

[Serializable]
public class ResourceStack
{
    public string resourceId;
    public int    amount;
}
