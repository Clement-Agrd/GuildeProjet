using System;
using System.Collections.Generic;
using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public static EquipmentManager Instance { get; private set; }

    [SerializeField] private GuildInventory inventory;
    [SerializeField] private AdventurerManager adventurers;
    [SerializeField] private BuildingManager buildings;
    [SerializeField] private EconomyManager economy;
    [Range(0.05f, 1f)] [SerializeField] private float fullRepairValueFraction = 0.4f;

    public event Action OnEquipmentChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    private void OnEnable()
    {
        adventurers.OnAdventurerFired += ReleaseAll;
        adventurers.OnAdventurerDied += ReleaseAll;
    }
    private void OnDisable()
    {
        adventurers.OnAdventurerFired -= ReleaseAll;
        adventurers.OnAdventurerDied -= ReleaseAll;
    }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // L'inventaire conserve chaque instance une seule fois. Les IDs du loadout font autorité
    // pour l'appartenance : aucun second état "propriétaire" à synchroniser sur les objets.
    public Adventurer GetOwner(string equipmentId)
    {
        if (string.IsNullOrEmpty(equipmentId)) return null;
        foreach (var a in adventurers.GetAllAdventurers())
            foreach (var slot in a.equippedItems)
                if (slot.equipmentId == equipmentId) return a;
        return null;
    }

    public IEnumerable<EquipmentInstance> GetStoredEquipment()
    {
        foreach (var item in inventory.GetAllEquipment())
            if (GetOwner(item.equipmentId) == null) yield return item;
    }

    public int StoredEquipmentCount
    {
        get
        {
            int count = 0;
            foreach (var item in GetStoredEquipment()) count++;
            return count;
        }
    }

    public bool CanEquip(Adventurer a, EquipmentInstance item, out string reason)
    {
        if (!CanChangeLoadout(a, out reason)) return false;
        if (item?.definition == null || inventory.GetEquipment(item.equipmentId) != item)
        { reason = "Cet objet n’est plus dans les réserves."; return false; }
        var owner = GetOwner(item.equipmentId);
        if (owner != null)
        { reason = owner == a ? "Cet objet est déjà équipé." : $"Objet porté par {owner.adventurerName}."; return false; }
        if (!string.IsNullOrEmpty(item.definition.requiredClassId) && item.definition.requiredClassId != a.classId)
        { reason = "Cet objet est réservé à une autre classe."; return false; }
        reason = null;
        return true;
    }

    public bool TryEquip(Adventurer a, string equipmentId, out string reason)
    {
        var item = inventory.GetEquipment(equipmentId);
        if (!CanEquip(a, item, out reason)) return false;
        a.SetEquippedItem(item.definition.slot, equipmentId);
        OnEquipmentChanged?.Invoke();
        return true;
    }

    public bool TryUnequip(Adventurer a, EquipmentSlot slot, out string reason)
    {
        if (!CanChangeLoadout(a, out reason)) return false;
        if (string.IsNullOrEmpty(a.GetEquipmentId(slot)))
        { reason = "Cet emplacement est vide."; return false; }
        a.SetEquippedItem(slot, null);
        OnEquipmentChanged?.Invoke();
        return true;
    }

    private bool CanChangeLoadout(Adventurer a, out string reason)
    {
        if (a == null || adventurers.GetById(a.id) != a)
        { reason = "Sélectionnez un aventurier de la guilde."; return false; }
        if (a.currentStateType == AdventurerStateType.OnMission || a.currentStateType == AdventurerStateType.Dead)
        { reason = "L’équipement ne peut être modifié que lorsque l’aventurier est à la guilde."; return false; }
        reason = null;
        return true;
    }

    private void ReleaseAll(Adventurer a)
    {
        if (a.equippedItems.Count == 0) return;
        a.equippedItems.Clear();
        OnEquipmentChanged?.Invoke();
    }

    // À appeler lors de la résolution d'une mission ou d'un combat, jamais avec le temps d'atelier.
    public int ApplyWear(Adventurer a, int baseWear)
    {
        if (a == null || adventurers.GetById(a.id) != a || baseWear <= 0) return 0;
        int affected = 0;
        foreach (var slot in a.equippedItems)
        {
            var item = inventory.GetEquipment(slot.equipmentId);
            if (item?.definition == null || item.IsBroken) continue;
            float multiplier = 1f;
            if (item.definition.slot == EquipmentSlot.MainWeapon && a.adventurerClass != null
                && !a.adventurerClass.MastersWeapon(item.definition.weaponType))
                multiplier = Mathf.Max(1f, item.definition.unmasteredWearMultiplier);
            item.TakeDamage(Mathf.CeilToInt(baseWear * multiplier));
            affected++;
        }
        if (affected > 0) OnEquipmentChanged?.Invoke();
        return affected;
    }

    public int GetRepairCost(EquipmentInstance item)
    {
        if (item?.definition == null || item.maxDurability <= 0 || item.currentDurability >= item.maxDurability) return 0;
        float missingFraction = 1f - Mathf.Clamp01((float)item.currentDurability / item.maxDurability);
        return Mathf.Max(1, Mathf.CeilToInt(item.definition.GetGoldValue(item.quality) * fullRepairValueFraction * missingFraction));
    }

    public bool CanRepair(EquipmentInstance item, out string reason)
    {
        if (item?.definition == null || inventory.GetEquipment(item.equipmentId) != item)
        { reason = "Cet objet n’est plus dans les réserves."; return false; }
        if (GetRepairCost(item) == 0)
        { reason = "Cet objet est déjà en parfait état."; return false; }
        var owner = GetOwner(item.equipmentId);
        if (owner?.currentStateType == AdventurerStateType.OnMission)
        { reason = "Cet objet est actuellement en mission."; return false; }
        var forge = buildings.GetBuilding(BuildingType.Forge);
        if (forge == null || !forge.IsActive)
        { reason = "La Forge doit être active pour réparer."; return false; }
        bool hasArtisan = false;
        foreach (var id in forge.assignedAdventurerIds)
        {
            var a = adventurers.GetById(id);
            if (a != null && a.currentStateType == AdventurerStateType.Working
                && a.currentJobType == JobType.Blacksmith && buildings.GetTaskForWorker(id) == null)
            { hasArtisan = true; break; }
        }
        if (!hasArtisan)
        { reason = "Affectez un forgeron libre à la Forge."; return false; }
        if (economy.CurrentGold < GetRepairCost(item))
        { reason = "La trésorerie ne suffit pas pour cette réparation."; return false; }
        reason = null;
        return true;
    }

    public bool TryRepair(string equipmentId, out string reason)
    {
        var item = inventory.GetEquipment(equipmentId);
        if (!CanRepair(item, out reason)) return false;
        if (!economy.SpendGold(GetRepairCost(item)))
        { reason = "La trésorerie ne suffit pas pour cette réparation."; return false; }
        item.FullRepair();
        OnEquipmentChanged?.Invoke();
        return true;
    }
}
