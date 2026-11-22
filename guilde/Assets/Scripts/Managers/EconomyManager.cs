using System;
using System.Collections.Generic;
using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }

    [Header("Trésorerie")]
    [SerializeField] private int startingGold = 500;
    private int _currentGold;

    [Header("Marché — ressources")]
    [SerializeField] private float   buyMarkup          = 1.15f; // acheter coûte plus cher que la valeur de base
    [SerializeField] private float   sellMarkdown        = 0.85f; // vendre rapporte moins que la valeur de base
    [SerializeField] private float   priceDriftPerUnit   = 0.01f;
    [SerializeField] private Vector2 priceMultiplierRange = new Vector2(0.5f, 2f);

    private readonly List<MarketPriceEntry>               _priceEntries = new();
    private readonly Dictionary<string, MarketPriceEntry> _priceById    = new();
    private readonly Dictionary<ResourceSO, int> _componentPurchase = new();
    private const float AutoSaleInterval = 2f;
    private float _autoSaleRemaining;
    public bool AutoSellEquipment { get; private set; }
    public bool AutoBuyComponents { get; private set; }
    public bool AutoSellOnlyWhenFull { get; private set; }
    public EquipmentQuality AutoSellMaxQuality { get; private set; }
    public int AutoSoldCount { get; private set; }
    public int AutoSaleRevenue { get; private set; }

    public bool HasActiveMerchant
    {
        get
        {
            var market = BuildingManager.Instance?.GetBuilding(BuildingType.Market);
            return market?.IsActive == true && market.assignedAdventurerIds.Count > 0;
        }
    }

    // ─── Events ───────────────────────────────────────────────────────────────
    public event Action<int>               OnGoldChanged;
    public event Action                    OnBankruptcyStarted;
    public event Action                    OnBankruptcyEnded;
    public event Action<ResourceSO, float> OnResourcePriceChanged;

    public int  CurrentGold => _currentGold;
    public bool IsBankrupt  => _currentGold <= 0;

    private bool _wasBankrupt;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _currentGold = startingGold;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ResetForNewGame()
    {
        AutoSellEquipment = false;
        AutoBuyComponents = false;
        AutoSellOnlyWhenFull = false;
        AutoSellMaxQuality = EquipmentQuality.Normal;
        AutoSoldCount = 0;
        AutoSaleRevenue = 0;
        _autoSaleRemaining = AutoSaleInterval;
        _priceEntries.Clear();
        _priceById.Clear();
        SetGold(startingGold);
    }

    // =========================================================================
    // Tick — appelé par TimeManager
    // =========================================================================

    public void Tick(float deltaTime)
    {
        if (IsBankrupt) ApplyBankruptcyPenalty(deltaTime);
        TickAutoSales(deltaTime);
    }

    private void ApplyBankruptcyPenalty(float deltaTime)
    {
        // Léger malus de moral sur toute la guilde tant que la faillite dure (GDO 8.5)
        foreach (var a in AdventurerManager.Instance.GetAllAdventurers())
        {
            a.stats.moral -= 2f * deltaTime;
            a.stats.ClampSecondaryStats();
        }
    }

    // =========================================================================
    // Or — dépenses "bloquantes" (achats volontaires) vs "forcées" (salaires)
    // =========================================================================

    public bool SpendGold(int amount)
    {
        if (amount <= 0) return true;
        if (_currentGold < amount) return false;
        SetGold(_currentGold - amount);
        return true;
    }

    public void ForceSpendGold(int amount)
    {
        if (amount <= 0) return;
        SetGold(_currentGold - amount);
    }

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        SetGold(_currentGold + amount);
    }

    private void SetGold(int newValue)
    {
        _currentGold = newValue;
        OnGoldChanged?.Invoke(_currentGold);

        bool bankrupt = IsBankrupt;
        if (bankrupt && !_wasBankrupt) OnBankruptcyStarted?.Invoke();
        if (!bankrupt && _wasBankrupt) OnBankruptcyEnded?.Invoke();
        _wasBankrupt = bankrupt;
    }

    // =========================================================================
    // Salaires
    // =========================================================================

    /// <summary>Verse le salaire d'un aventurier. Toujours payé, quitte à entrer en faillite.</summary>
    public void PayWage(Adventurer adventurer, JobDefinitionSO activeJobDef = null)
    {
        if (adventurer == null) return;
        ForceSpendGold(adventurer.CalculateSalary(activeJobDef));
    }

    // =========================================================================
    // Marché — ressources
    // =========================================================================

    public float GetPriceMultiplier(ResourceSO resource)
        => _priceById.TryGetValue(resource.name, out var e) ? e.priceMultiplier : 1f;

    public int GetBuyPrice(ResourceSO resource, int amount)
        => Mathf.CeilToInt(resource.baseMarketValue * GetPriceMultiplier(resource) * buyMarkup * amount);

    public int GetSellPrice(ResourceSO resource, int amount)
        => Mathf.FloorToInt(resource.baseMarketValue * GetPriceMultiplier(resource) * sellMarkdown * amount * (1f + MarketSaleBonus));

    public float MarketSaleBonus => JobPerformance.SaleBonus(GetSellingMerchant());

    private Adventurer GetSellingMerchant()
    {
        var market = BuildingManager.Instance?.GetBuilding(BuildingType.Market);
        if (market?.IsActive != true) return null;

        Adventurer best = null;
        float bestBonus = 0f;
        foreach (string id in market.assignedAdventurerIds)
        {
            var candidate = AdventurerManager.Instance.GetById(id);
            float bonus = JobPerformance.SaleBonus(candidate);
            if (bonus <= bestBonus) continue;
            best = candidate;
            bestBonus = bonus;
        }
        return best;
    }

    public bool BuyResource(ResourceSO resource, int amount)
    {
        if (resource == null || amount <= 0) return false;
        if (GuildInventory.Instance.GetRemainingCapacity(resource) < amount) return false;
        if (!SpendGold(GetBuyPrice(resource, amount))) return false;

        GuildInventory.Instance.AddResource(resource, amount);
        DriftPrice(resource, amount * priceDriftPerUnit * resource.priceVolatility);
        return true;
    }

    public bool SellResource(ResourceSO resource, int amount)
    {
        if (resource == null || amount <= 0) return false;
        if (GuildInventory.Instance.GetAmount(resource) < amount) return false;

        int revenue = GetSellPrice(resource, amount);
        GuildInventory.Instance.AddResource(resource, -amount);
        AddGold(revenue);
        DriftPrice(resource, -amount * priceDriftPerUnit * resource.priceVolatility);
        return true;
    }

    public bool ConfigureAutoPurchases(bool enabled)
    {
        if (enabled && !HasActiveMerchant) return false;
        AutoBuyComponents = enabled;
        return true;
    }

    internal string BuyMissingComponents(List<ResourceCost> costs)
    {
        if (!AutoBuyComponents) return "composants manquants";
        if (!HasActiveMerchant) return "aucun marchand disponible pour l’achat automatique";

        var stock = GuildInventory.Instance;
        _componentPurchase.Clear();
        foreach (var cost in costs)
        {
            if (cost?.resource == null || cost.amount < 0) return "recette invalide";
            _componentPurchase.TryGetValue(cost.resource, out int required);
            _componentPurchase[cost.resource] = required + cost.amount;
        }

        long total = 0;
        bool hasDeficit = false;
        foreach (var requirement in _componentPurchase)
        {
            var resource = requirement.Key;
            if (requirement.Value > resource.maxStackSize) return "capacité des réserves insuffisante";
            int missing = requirement.Value - stock.GetAmount(resource);
            if (missing <= 0) continue;
            hasDeficit = true;
            total += GetBuyPrice(resource, missing);
        }
        if (!hasDeficit) return null;
        if (total > int.MaxValue || !SpendGold((int)total)) return "or insuffisant pour l’achat automatique";

        foreach (var requirement in _componentPurchase)
        {
            var resource = requirement.Key;
            int missing = requirement.Value - stock.GetAmount(resource);
            if (missing <= 0) continue;
            stock.AddResource(resource, missing);
            DriftPrice(resource, missing * priceDriftPerUnit * resource.priceVolatility);
        }
        BuildingManager.Instance.RecordTransactionWork(BuildingType.Market);
        return null;
    }

    private void DriftPrice(ResourceSO resource, float delta)
    {
        if (!_priceById.TryGetValue(resource.name, out var entry))
        {
            entry = new MarketPriceEntry { resourceId = resource.name, priceMultiplier = 1f };
            _priceById[resource.name] = entry;
            _priceEntries.Add(entry);
        }

        entry.priceMultiplier = Mathf.Clamp(entry.priceMultiplier + delta, priceMultiplierRange.x, priceMultiplierRange.y);
        OnResourcePriceChanged?.Invoke(resource, entry.priceMultiplier);
    }

    // =========================================================================
    // Marché — équipement (vente uniquement — l'achat viendra avec le MarketPanel)
    // =========================================================================

    public bool SellEquipment(string equipmentId)
    {
        var item = GuildInventory.Instance.GetEquipment(equipmentId);
        if (item?.definition == null) return false;
        if (EquipmentManager.Instance != null && EquipmentManager.Instance.GetOwner(equipmentId) != null) return false;

        int netToGuild = GetEquipmentSellPrice(item);
        if (!GuildInventory.Instance.RemoveEquipment(equipmentId)) return false;
        AddGold(netToGuild);
        return true;
    }

    public bool ConfigureAutoSales(bool enabled, EquipmentQuality maximumQuality, bool onlyWhenFull)
    {
        if (!HasActiveMerchant || !Enum.IsDefined(typeof(EquipmentQuality), maximumQuality)) return false;
        if (enabled && !AutoSellEquipment) _autoSaleRemaining = AutoSaleInterval;
        AutoSellEquipment = enabled;
        AutoSellMaxQuality = maximumQuality;
        AutoSellOnlyWhenFull = onlyWhenFull;
        return true;
    }

    private void TickAutoSales(float deltaTime)
    {
        if (!AutoSellEquipment || !HasActiveMerchant) return;
        if (AutoSellOnlyWhenFull && !GuildInventory.Instance.IsEquipmentFull) return;
        _autoSaleRemaining -= deltaTime;
        if (_autoSaleRemaining > 0f) return;
        _autoSaleRemaining = AutoSaleInterval;

        var item = FindAutoSaleItem();
        if (item == null) return;
        var merchant = GetSellingMerchant();
        int revenue = GetEquipmentSellPrice(item);
        if (!SellEquipment(item.equipmentId)) return;
        AutoSoldCount++;
        AutoSaleRevenue += revenue;
        merchant?.AddWorkFatigue(1f);
    }

    public bool CanFreeEquipmentSlotAutomatically => AutoSellEquipment && HasActiveMerchant && FindAutoSaleItem() != null;

    private EquipmentInstance FindAutoSaleItem()
    {
        var equipment = EquipmentManager.Instance;
        if (equipment == null) return null;
        foreach (var item in equipment.GetStoredEquipment())
            if (item.definition != null && (int)item.quality <= (int)AutoSellMaxQuality && GetEquipmentSellPrice(item) > 0)
                return item;
        return null;
    }

    public int GetEquipmentSellPrice(EquipmentInstance item)
    {
        if (item?.definition == null) return 0;

        int salePrice = Mathf.RoundToInt(item.definition.GetGoldValue(item.quality) * (1f + MarketSaleBonus));
        int netToGuild = salePrice;

        if (!string.IsNullOrEmpty(item.craftedByAdventurerId))
        {
            var crafter = AdventurerManager.Instance.GetById(item.craftedByAdventurerId);
            if (crafter != null)
            {
                var jobDef    = BuildingManager.Instance.GetJobDefinition(item.craftedByJobType);
                float commission = crafter.CalculateCommission(salePrice, item.craftedByJobType, jobDef);
                netToGuild -= Mathf.RoundToInt(commission);
            }
        }

        return Mathf.Max(0, netToGuild);
    }

    // =========================================================================
    // Save / Load
    // =========================================================================

    public void LoadEconomy(int gold, List<MarketPriceEntry> prices)
    {
        SetGold(gold);
        _priceEntries.Clear();
        _priceById.Clear();
        foreach (var p in prices)
        {
            _priceEntries.Add(p);
            _priceById[p.resourceId] = p;
        }
    }

    public List<MarketPriceEntry> GetAllPrices() => _priceEntries;
}

[Serializable]
public class MarketPriceEntry
{
    public string resourceId;
    public float  priceMultiplier = 1f;
}
