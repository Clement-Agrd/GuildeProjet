using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Equipment_New", menuName = "Guildmaster/Economy/Equipment")]
public class EquipmentSO : ScriptableObject
{
    [Header("Identité")]
    public string itemName;
    [TextArea(1, 3)] public string description;
    public Sprite icon;

    [Header("Slot & restrictions")]
    public EquipmentSlot slot;
    [Tooltip("Slot MainWeapon uniquement")]
    public WeaponType weaponType;
    [Tooltip("Vide = toutes classes")]
    public string requiredClassId;

    [Header("Arme non maîtrisée (valeurs provisoires)")]
    [Range(0f, 1f)] public float unmasteredDamageMultiplier = 0.75f;
    [Range(0f, 1f)] public float unmasteredAccuracyMultiplier = 0.8f;
    [Min(1f)] public float unmasteredWearMultiplier = 1.5f;

    [Header("Stat principale (primaire ou de combat)")]
    public StatType mainStatType;
    public int baseMainStatValue = 20;

    [Header("Pool de substats (stats de combat uniquement)")]
    [Tooltip("Les substats réelles sont tirées au sort à la création de l'instance")]
    public List<CombatStat> possibleSubStats = new();
    public float baseSubStatValue = 10f;

    [Header("Durabilité & valeur")]
    public int baseDurability = 100;
    public int baseGoldValue  = 50;

    [Header("Multiplicateurs par rareté")]
    public float rareMultiplier      = 1.5f;
    public float epicMultiplier      = 2.5f;
    public float legendaryMultiplier = 4.0f;

    // ─── Helpers ──────────────────────────────────────────────────────────────

    public static int SubStatCountFor(EquipmentQuality q) => q switch
    {
        EquipmentQuality.Normal    => 0,
        EquipmentQuality.Rare      => 2,
        EquipmentQuality.Epic      => 3,
        EquipmentQuality.Legendary => 4,
        _                          => 0
    };

    public float GetQualityMultiplier(EquipmentQuality q) => q switch
    {
        EquipmentQuality.Rare      => rareMultiplier,
        EquipmentQuality.Epic      => epicMultiplier,
        EquipmentQuality.Legendary => legendaryMultiplier,
        _                          => 1f
    };

    public int   GetScaledMainStat(EquipmentQuality q) => Mathf.RoundToInt(baseMainStatValue * GetQualityMultiplier(q));
    public float GetScaledSubStat(EquipmentQuality q)  => baseSubStatValue * GetQualityMultiplier(q);
    public int   GetGoldValue(EquipmentQuality q)      => Mathf.RoundToInt(baseGoldValue * GetQualityMultiplier(q));

    // La stat principale est primaire si son index StatType est 0–7
    public bool        IsMainStatPrimary => (int)mainStatType < 8;
    public PrimaryStat GetMainAsPrimary() => (PrimaryStat)(int)mainStatType;
    public CombatStat  GetMainAsCombat()  => (CombatStat)((int)mainStatType - 8);
}
