using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Trait_New", menuName = "Guildmaster/Adventurer/Trait")]
public class TraitSO : ScriptableObject
{
    [Header("Infos")]
    public string traitName;
    [TextArea(2, 4)] public string description;
    public TraitPolarity polarity;
    public Sprite icon;

    [Header("Effets")]
    public List<TraitEffect> effects = new();

    public float GetEffectValue(TraitEffectType type)
    {
        foreach (var e in effects)
            if (e.effectType == type) return e.value;
        return 0f;
    }

    public bool HasEffect(TraitEffectType type)
    {
        foreach (var e in effects)
            if (e.effectType == type) return true;
        return false;
    }
}

[Serializable]
public class TraitEffect
{
    public TraitEffectType effectType;
    [Tooltip("Multiplicateur : 1.2 = +20% | Modificateur plat : 10 = +10 points")]
    public float value = 1f;
}