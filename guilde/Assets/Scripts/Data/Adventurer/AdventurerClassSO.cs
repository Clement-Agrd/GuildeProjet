using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Class_New", menuName = "Guildmaster/Adventurer/Class")]
public class AdventurerClassSO : ScriptableObject
{
    [Header("Identité")]
    public string className;
    [TextArea(2, 4)] public string description;
    public Sprite icon;

    [Header("Stats de base (bonus additif appliqué à la création)")]
    public AdventurerStats baseStats = new();

    [Header("Armes maîtrisées")]
    public List<WeaponType> masteredWeapons = new();

    [Header("Passifs de classe")]
    public List<TraitEffect> classPassives = new();

    [Header("Affinités métier de classe")]
    [Tooltip("Aptitudes naturelles de cette classe pour certains métiers")]
    public List<ClassJobAffinity> jobAffinities = new();

    [Header("Économie")]
    [Tooltip("Salaire de base avant modificateurs (traits, niveau, réputation)")]
    public int baseSalary = 10;

    public bool MastersWeapon(WeaponType weapon) => masteredWeapons.Contains(weapon);

    public float GetJobAffinityBonus(JobType jobType)
    {
        foreach (var a in jobAffinities)
            if (a.jobType == jobType) return a.performanceBonus;
        return 0f;
    }
}


[Serializable]
public class ClassJobAffinity
{
    public JobType jobType;
    [Range(0f, 0.5f)]
    [Tooltip("Bonus de performance dans ce métier (0.25 = +25%)")]
    public float performanceBonus = 0.1f;
}