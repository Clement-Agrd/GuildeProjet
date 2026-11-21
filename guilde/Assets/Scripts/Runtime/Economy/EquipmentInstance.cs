using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EquipmentInstance
{
    // ─── Identité ─────────────────────────────────────────────────────────────
    public string          equipmentId;
    public string          definitionId;
    [NonSerialized] public EquipmentSO definition;

    // ─── Qualité & durabilité ────────────────────────────────────────────────
    public EquipmentQuality quality;
    public int              currentDurability;
    public int              maxDurability;

    // ─── Stats tirées à la création ───────────────────────────────────────────
    public int               mainStatValue;
    public List<SubStatRoll> subStats = new();

    // ─── Traçabilité fabrication (pour la commission à la vente) ─────────────
    public string  craftedByAdventurerId;
    public JobType craftedByJobType = JobType.None;

    // ─── Propriétés ───────────────────────────────────────────────────────────
    public bool IsBroken    => currentDurability <= 0;
    public bool NeedsRepair => currentDurability < maxDurability * 0.25f;

    // ─── Constructeurs ────────────────────────────────────────────────────────
    public EquipmentInstance() { }

    public EquipmentInstance(EquipmentSO def, EquipmentQuality quality,
                              string craftedByAdventurerId = null,
                              JobType craftedByJobType = JobType.None)
    {
        equipmentId              = Guid.NewGuid().ToString();
        definition                = def;
        definitionId              = def.name;
        this.quality               = quality;
        maxDurability             = def.baseDurability;
        currentDurability         = maxDurability;
        mainStatValue             = def.GetScaledMainStat(quality);
        this.craftedByAdventurerId = craftedByAdventurerId;
        this.craftedByJobType      = craftedByJobType;
        RollSubStats(def, quality);
    }

    private void RollSubStats(EquipmentSO def, EquipmentQuality quality)
    {
        subStats = new List<SubStatRoll>();
        int count = EquipmentSO.SubStatCountFor(quality);
        if (count == 0 || def.possibleSubStats.Count == 0) return;

        float baseValue = def.GetScaledSubStat(quality);
        var   pool      = new List<CombatStat>(def.possibleSubStats);

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int   idx   = UnityEngine.Random.Range(0, pool.Count);
            float value = Mathf.Round(baseValue * UnityEngine.Random.Range(0.85f, 1.15f) * 10f) / 10f;
            subStats.Add(new SubStatRoll(pool[idx], value));
            pool.RemoveAt(idx);
        }
    }

    // ─── Application aux CombatStats ─────────────────────────────────────────
    // Workflow : CombatStats.FromPrimary(stats) → equip1.Apply(combat) → equip2.Apply(combat) → combat.Clamp()

    public void ApplyToCombatStats(CombatStats combat)
    {
        if (definition == null || IsBroken) return;

        if (definition.IsMainStatPrimary)
            combat.AddPrimary(definition.GetMainAsPrimary(), mainStatValue);
        else
            combat.AddCombatStat(definition.GetMainAsCombat(), mainStatValue);

        foreach (var sub in subStats)
            combat.AddCombatStat(sub.statType, sub.value);
    }

    // ─── Durabilité ───────────────────────────────────────────────────────────
    public void TakeDamage(int amount) => currentDurability = Mathf.Max(0, currentDurability - Mathf.Max(0, amount));
    public void Repair(int amount)     => currentDurability = Mathf.Min(maxDurability, currentDurability + Mathf.Max(0, amount));
    public void FullRepair()           => currentDurability = maxDurability;

    // ─── Post-load ────────────────────────────────────────────────────────────
    public void OnAfterLoad(EquipmentSO def) => definition = def;
}

[Serializable]
public class SubStatRoll
{
    public CombatStat statType;
    public float      value;

    public SubStatRoll() { }
    public SubStatRoll(CombatStat statType, float value)
    {
        this.statType = statType;
        this.value    = value;
    }
}
