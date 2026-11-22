using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class GuildWorkshopSetup
{
    public const string ScenePath = "Assets/Scenes/GuildWorkshop.unity";
    private const string DataRoot = "Assets/Data/Workshop";

    [MenuItem("Guildmaster/1 - Creer ou ouvrir l'atelier")]
    public static void CreateWorkshop()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Arrêtez le mode Play avant de créer la scène.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolder(DataRoot);
        EnsureFolder("Assets/UI/Workshop");
        bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        // NewScene décharge les assets inutilisés : le faire AVANT d'en conserver les références.
        var scene = sceneExists ? EditorSceneManager.OpenScene(ScenePath)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var iron = Asset<ResourceSO>("Resource_Iron", r => { r.resourceName = "Fer"; r.baseMarketValue = 9; });
        var wood = Asset<ResourceSO>("Resource_Wood", r => { r.resourceName = "Bois"; r.baseMarketValue = 4; });
        var leather = Asset<ResourceSO>("Resource_Leather", r => { r.resourceName = "Cuir"; r.baseMarketValue = 6; });
        var hardworking = Asset<TraitSO>("Trait_Hardworking", t =>
        {
            t.traitName = "Travailleur"; t.polarity = TraitPolarity.Positive;
            t.description = "Récupère plus vite après une journée de travail.";
            t.effects.Add(new TraitEffect { effectType = TraitEffectType.FatigueRecovery_Multiplier, value = 1.2f });
        });
        var gifted = Asset<TraitSO>("Trait_Gifted", t =>
        {
            t.traitName = "Apprend vite"; t.polarity = TraitPolarity.Positive;
            t.description = "Gagne 20 % d’expérience supplémentaire.";
            t.effects.Add(new TraitEffect { effectType = TraitEffectType.ExperienceGain_Multiplier, value = 1.2f });
        });
        var greedy = Asset<TraitSO>("Trait_Greedy", t =>
        {
            t.traitName = "Cupide"; t.polarity = TraitPolarity.Negative;
            t.description = "Demande un salaire et une commission plus élevés.";
            t.effects.Add(new TraitEffect { effectType = TraitEffectType.Salary_Multiplier, value = 1.15f });
            t.effects.Add(new TraitEffect { effectType = TraitEffectType.Commission_Multiplier, value = 1.2f });
        });
        var warrior = Asset<AdventurerClassSO>("Class_Warrior", c =>
        {
            c.className = "Guerrier"; c.baseSalary = 10;
            c.baseStats.force = 16; c.baseStats.vitalite = 14; c.baseStats.resistance = 13;
            c.masteredWeapons = new() { WeaponType.Sword, WeaponType.Axe, WeaponType.Mace };
            c.jobAffinities.Add(new ClassJobAffinity { jobType = JobType.Blacksmith, performanceBonus = .25f });
        });
        var mage = Asset<AdventurerClassSO>("Class_Mage", c =>
        {
            c.className = "Mage"; c.baseSalary = 12;
            c.baseStats.intelligence = 17; c.baseStats.sagesse = 14; c.baseStats.charisme = 12;
            c.masteredWeapons = new() { WeaponType.Staff, WeaponType.Grimoire };
            c.jobAffinities.Add(new ClassJobAffinity { jobType = JobType.Blacksmith, performanceBonus = .1f });
        });
        Asset<AdventurerRaceSO>("Race_Human", r => { r.raceName = "Humain"; r.raceType = Race.Human; });
        Asset<AdventurerRaceSO>("Race_Dwarf", r =>
        {
            r.raceName = "Nain des montagnes"; r.raceType = Race.Dwarf;
            r.statModifiers.Add(new PrimaryStatModifier { stat = PrimaryStat.Vitalite, value = 3 });
            r.statModifiers.Add(new PrimaryStatModifier { stat = PrimaryStat.Resistance, value = 2 });
            r.jobEffects.Add(new RacialJobEffect { effect = JobEffectType.ForgeDurationReduction, bonus = .10f });
            r.jobEffects.Add(new RacialJobEffect { effect = JobEffectType.TrainingSpeed, bonus = .08f });
        });
        var jobs = new List<JobDefinitionSO>();
        string[] jobNames = { "", "Forgeron", "Artisan", "Marchand", "Soigneur", "Cuisinier", "Recruteur", "Entraîneur", "Éclaireur" };
        foreach (JobType type in Enum.GetValues(typeof(JobType)))
        {
            if (type == JobType.None) continue;
            jobs.Add(Asset<JobDefinitionSO>("Job_" + type, j =>
            {
                j.jobName = jobNames[(int)type]; j.jobType = type; j.maxLevel = 10;
                j.associatedBuilding = type switch
                {
                    JobType.Blacksmith => BuildingType.Forge, JobType.Craftsman => BuildingType.Workshop,
                    JobType.Merchant => BuildingType.Market, JobType.Healer => BuildingType.Infirmary,
                    JobType.Cook => BuildingType.Tavern, JobType.Trainer => BuildingType.TrainingRoom,
                    JobType.Scout => BuildingType.Headquarters, _ => BuildingType.Recruiter
                };
                j.primaryStatWeights.Add(new StatWeight { stat = PrimaryStat.Force, weight = .6f });
                j.primaryStatWeights.Add(new StatWeight { stat = PrimaryStat.Vitalite, weight = .4f });
                for (int level = 1; level <= 10; level++)
                    j.levelData.Add(new JobLevelData { experienceRequired = 100 + (level - 1) * 60,
                        qualityBonus = level * .025f, efficiencyBonus = level * .05f,
                        outputMultiplier = 1f + level * .05f, salaryMultiplier = 1f + level * .1f });
            }));
        }
        var forge = Asset<BuildingSO>("Building_Forge", b =>
        {
            b.buildingName = "La Forge"; b.buildingType = BuildingType.Forge; b.associatedJobType = JobType.Blacksmith;
            b.description = "Au rythme du marteau, vos réserves deviennent les premières richesses de la guilde.";
            AddBuildingLevels(b, wood, iron);
        });
        var market = Asset<BuildingSO>("Building_Market", b =>
        {
            b.buildingName = "Le Marché"; b.buildingType = BuildingType.Market; b.associatedJobType = JobType.Merchant;
            b.description = "Composants et ouvrages changent de mains sous les auvents de la cour.";
            AddBuildingLevels(b, wood, iron);
        });
        var sword = Equipment("Equipment_IronSword", "Épée de fer", EquipmentSlot.MainWeapon, WeaponType.Sword, StatType.AttaquePhysique, 24, 65);
        var dagger = Equipment("Equipment_IronDagger", "Dague de fer", EquipmentSlot.MainWeapon, WeaponType.Dagger, StatType.AttaquePhysique, 14, 32);
        var armor = Equipment("Equipment_ReinforcedArmor", "Armure renforcée", EquipmentSlot.Armor, WeaponType.Sword, StatType.DefensePhysique, 28, 90);
        var recipes = new List<CraftRecipeSO>
        {
            Recipe("Recipe_IronSword", "Épée de fer", sword, 7f, Cost(iron, 2), Cost(wood, 1)),
            Recipe("Recipe_IronDagger", "Dague de fer", dagger, 5f, Cost(iron, 1), Cost(wood, 1)),
            Recipe("Recipe_ReinforcedArmor", "Armure renforcée", armor, 10f, Cost(iron, 3), Cost(leather, 2))
        };
        AssetDatabase.SaveAssets();
        // Réouvrir préserve les réglages manuels d'une scène déjà créée.
        if (sceneExists)
        {
            RepairMissingReferences();
            return;
        }
        var managers = new GameObject("Guild Systems");
        var inventory = Add<GuildInventory>(managers.transform, "Inventory");
        var economy = Add<EconomyManager>(managers.transform, "Economy");
        var buildings = Add<BuildingManager>(managers.transform, "Buildings");
        var adventurers = Add<AdventurerManager>(managers.transform, "Adventurers");
        var clock = Add<TimeManager>(managers.transform, "Time");
        var game = Add<GameManager>(managers.transform, "Game");
        var equipment = Add<EquipmentManager>(managers.transform, "Equipment");
        SetRef(equipment, "inventory", inventory); SetRef(equipment, "adventurers", adventurers);
        SetRef(equipment, "buildings", buildings); SetRef(equipment, "economy", economy);
        SetArray(inventory, "allResources", iron, wood, leather);
        SetArray(inventory, "allEquipments", sword, dagger, armor);
        SetArray(buildings, "buildingDefinitions", forge, market);
        SetArray(buildings, "jobDefinitions", jobs.ToArray());
        SetArray(buildings, "allRecipes", recipes.ToArray());
        SetArray(adventurers, "allClasses", warrior, mage);
        RestoreArray<AdventurerRaceSO>(adventurers, "allRaces");
        SetArray(adventurers, "allTraits", hardworking, gifted, greedy);
        SetRef(clock, "economy", economy); SetRef(clock, "buildings", buildings); SetRef(clock, "adventurers", adventurers);
        SetRef(game, "adventurers", adventurers); SetRef(game, "buildings", buildings);
        SetRef(game, "economy", economy); SetRef(game, "inventory", inventory); SetRef(game, "clock", clock);
        var settings = new SerializedObject(game);
        var resources = settings.FindProperty("startingResources"); resources.arraySize = 3;
        SetCost(resources.GetArrayElementAtIndex(0), iron, 18);
        SetCost(resources.GetArrayElementAtIndex(1), wood, 20);
        SetCost(resources.GetArrayElementAtIndex(2), leather, 12);
        settings.ApplyModifiedPropertiesWithoutUndo();

        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/Workshop/GuildPanelSettings.asset");
        if (!panel)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = .5f;
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/Workshop/GuildTheme.tss");
            AssetDatabase.CreateAsset(panel, "Assets/UI/Workshop/GuildPanelSettings.asset");
        }
        var uiObject = new GameObject("Guild Workshop UI");
        var document = uiObject.AddComponent<UIDocument>();
        var documentSettings = new SerializedObject(document);
        documentSettings.FindProperty("m_PanelSettings").objectReferenceValue = panel;
        documentSettings.FindProperty("sourceAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Workshop/GuildWorkshop.uxml");
        documentSettings.ApplyModifiedPropertiesWithoutUndo();
        var ui = uiObject.AddComponent<GuildWorkshopUI>();
        SetRef(ui, "game", game); SetRef(ui, "clock", clock); SetRef(ui, "adventurers", adventurers);
        SetRef(ui, "buildings", buildings); SetRef(ui, "economy", economy); SetRef(ui, "inventory", inventory);
        SetRef(ui, "equipment", equipment);
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera"; camera.orthographic = true;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.05f, .1f, .12f);
        EditorSceneManager.SaveScene(scene, ScenePath);
        var oldScenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        oldScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = oldScenes.ToArray();
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = uiObject;
        Debug.Log("Atelier prêt : Assets/Scenes/GuildWorkshop.unity. Lancez Play pour recruter, fabriquer et vendre.");
    }

    private static void RepairMissingReferences()
    {
        var inventory = UnityEngine.Object.FindAnyObjectByType<GuildInventory>();
        var buildings = UnityEngine.Object.FindAnyObjectByType<BuildingManager>();
        var adventurers = UnityEngine.Object.FindAnyObjectByType<AdventurerManager>();
        var game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (!inventory || !buildings || !adventurers || !game)
            throw new InvalidOperationException("La scène d'atelier doit contenir ses managers.");
        var equipment = UnityEngine.Object.FindAnyObjectByType<EquipmentManager>();
        if (!equipment)
        {
            equipment = Add<EquipmentManager>(game.transform.parent, "Equipment");
            Undo.RegisterCreatedObjectUndo(equipment.gameObject, "Ajouter la gestion de l'équipement");
        }
        SetRef(equipment, "inventory", inventory); SetRef(equipment, "adventurers", adventurers);
        SetRef(equipment, "buildings", buildings);
        SetRef(equipment, "economy", UnityEngine.Object.FindAnyObjectByType<EconomyManager>());
        var workshopUi = UnityEngine.Object.FindAnyObjectByType<GuildWorkshopUI>();
        if (workshopUi) SetRef(workshopUi, "equipment", equipment);
        RestoreArray<ResourceSO>(inventory, "allResources");
        RestoreArray<EquipmentSO>(inventory, "allEquipments");
        RestoreArray<BuildingSO>(buildings, "buildingDefinitions");
        RestoreArray<JobDefinitionSO>(buildings, "jobDefinitions");
        RestoreArray<CraftRecipeSO>(buildings, "allRecipes");
        RestoreArray<AdventurerClassSO>(adventurers, "allClasses");
        RestoreArray<AdventurerRaceSO>(adventurers, "allRaces");
        RestoreArray<TraitSO>(adventurers, "allTraits");
        var serialized = new SerializedObject(game);
        var costs = serialized.FindProperty("startingResources");
        string[] ids = { "Resource_Iron", "Resource_Wood", "Resource_Leather" };
        for (int i = 0; i < Math.Min(ids.Length, costs.arraySize); i++)
        {
            var reference = costs.GetArrayElementAtIndex(i).FindPropertyRelative("resource");
            if (!reference.objectReferenceValue)
                reference.objectReferenceValue = AssetDatabase.LoadAssetAtPath<ResourceSO>(DataRoot + "/" + ids[i] + ".asset");
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("Atelier : références vérifiées et scène enregistrée.");
    }

    private static void RestoreArray<T>(UnityEngine.Object target, string field) where T : UnityEngine.Object
    {
        var serialized = new SerializedObject(target);
        var array = serialized.FindProperty(field);
        bool needsRepair = array.arraySize == 0;
        for (int i = 0; i < array.arraySize; i++)
            needsRepair |= !array.GetArrayElementAtIndex(i).objectReferenceValue;
        if (!needsRepair) return;
        var assets = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { DataRoot })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<T>).Cast<UnityEngine.Object>().ToArray();
        SetArray(target, field, assets);
    }

    private static void AddBuildingLevels(BuildingSO b, ResourceSO wood, ResourceSO iron)
    {
        b.maxLevel = 4;
        for (int level = 1; level <= 4; level++)
            b.levelData.Add(new BuildingLevelData { goldCost = level == 1 ? 0 : (level - 1) * 120,
                resourceCosts = level == 1 ? new() : new() { Cost(wood, (level - 1) * 8), Cost(iron, (level - 1) * 6) },
                upgradeTime = level == 1 ? 0 : 12f * (level - 1), maxWorkerSlots = level,
                productionSpeedBonus = (level - 1) * .15f });
    }
    private static EquipmentSO Equipment(string id, string title, EquipmentSlot slot, WeaponType weapon, StatType stat, int value, int gold)
        => Asset<EquipmentSO>(id, e =>
        {
            e.itemName = title; e.slot = slot; e.weaponType = weapon; e.mainStatType = stat;
            e.baseMainStatValue = value; e.baseGoldValue = gold;
            e.possibleSubStats = new() { CombatStat.AttaquePhysique, CombatStat.DefensePhysique, CombatStat.ChanceCrit, CombatStat.Precision };
        });
    private static CraftRecipeSO Recipe(string id, string title, EquipmentSO output, float duration, params ResourceCost[] costs)
        => Asset<CraftRecipeSO>(id, r =>
        {
            r.recipeName = title; r.requiredJob = JobType.Blacksmith; r.requiredJobLevel = 0;
            r.baseCraftDuration = duration; r.outputEquipment = output; r.inputResources = new List<ResourceCost>(costs);
        });
    private static ResourceCost Cost(ResourceSO resource, int amount) => new() { resource = resource, amount = amount };
    private static T Asset<T>(string id, Action<T> initialize) where T : ScriptableObject
    {
        string path = DataRoot + "/" + id + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) return existing;
        var asset = ScriptableObject.CreateInstance<T>(); asset.name = id; initialize(asset);
        AssetDatabase.CreateAsset(asset, path); return asset;
    }
    private static T Add<T>(Transform parent, string name) where T : Component
    {
        var go = new GameObject(name); go.transform.SetParent(parent); return go.AddComponent<T>();
    }
    private static void SetRef(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(UnityEngine.Object target, string field, params UnityEngine.Object[] values)
    {
        var serialized = new SerializedObject(target);
        var array = serialized.FindProperty(field); array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetCost(SerializedProperty property, ResourceSO resource, int amount)
    {
        property.FindPropertyRelative("resource").objectReferenceValue = resource;
        property.FindPropertyRelative("amount").intValue = amount;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
}
