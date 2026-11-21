using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class GuildEquipmentValidation
{
    [Serializable] private class Report
    {
        public string checkedAtUtc;
        public bool passed;
        public List<string> checks = new();
        public string failure;
    }

    [MenuItem("Guildmaster/3 - Verifier l'equipement (Play)")]
    public static void Run()
    {
        if (!Application.isPlaying || EquipmentManager.Instance == null)
        { Debug.LogWarning("Ouvrez GuildWorkshop avec ses managers et lancez Play."); return; }
        var report = new Report { checkedAtUtc = DateTime.UtcNow.ToString("O") };
        var temporary = new List<UnityEngine.Object>();
        var clock = TimeManager.Instance;
        var random = UnityEngine.Random.state;
        clock.enabled = false;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            report.checks.Add(name);
        }
        try
        {
            GameManager.Instance.StartNewGame();
            var roster = AdventurerManager.Instance;
            var inventory = GuildInventory.Instance;
            var equipment = EquipmentManager.Instance;
            var economy = EconomyManager.Instance;
            var buildings = BuildingManager.Instance;
            var warriorClass = roster.GetClassById("Class_Warrior");
            var mageClass = roster.GetClassById("Class_Mage");
            var human = roster.GetRaceById("Race_Human");
            var warrior = new Adventurer("Aldric · validation", warriorClass, human, new());
            var mage = new Adventurer("Liora · validation", mageClass, human, new());
            roster.LoadRoster(new List<Adventurer> { warrior, mage }); roster.OnAfterLoad();
            var swordDef = inventory.GetEquipmentDefById("Equipment_IronSword");
            var armorDef = inventory.GetEquipmentDefById("Equipment_ReinforcedArmor");
            var daggerDef = inventory.GetEquipmentDefById("Equipment_IronDagger");
            var sword = new EquipmentInstance(swordDef, EquipmentQuality.Normal);
            var armor = new EquipmentInstance(armorDef, EquipmentQuality.Rare);
            var dagger = new EquipmentInstance(daggerDef, EquipmentQuality.Normal);
            inventory.AddEquipment(sword); inventory.AddEquipment(armor); inventory.AddEquipment(dagger);
            inventory.AddEquipment(sword);
            Check(inventory.GetAllEquipment().Count == 3, "Une instance d'équipement n'est jamais dupliquée");
            var baseCombat = warrior.GetCombatStats();
            Check(equipment.TryEquip(warrior, sword.equipmentId, out _), "Une épée peut être équipée par un guerrier");
            Check(equipment.GetOwner(sword.equipmentId) == warrior && equipment.StoredEquipmentCount == 2, "Appartenance et réserves disponibles concordent");
            Check(warrior.GetCombatStats().attaquePhysique == baseCombat.attaquePhysique + sword.mainStatValue, "Arme maîtrisée : bonus physique appliqué");
            Check(!equipment.TryEquip(mage, sword.equipmentId, out _) && !equipment.TryEquip(warrior, sword.equipmentId, out _), "Double attribution et équipement répété refusés");
            int beforeSale = economy.CurrentGold;
            Check(!economy.SellEquipment(sword.equipmentId) && economy.CurrentGold == beforeSale, "Vendre une pièce portée est interdit, sans mouvement d'or");
            int itemsBefore = inventory.GetAllEquipment().Count;
            Check(equipment.TryEquip(warrior, armor.equipmentId, out _), "Une armure peut être portée avec une arme");
            Check(warrior.GetCombatStats().defensePhysique >= baseCombat.defensePhysique + armor.mainStatValue, "Stat principale et substats d'armure appliquées");
            var beforePreview = warrior.GetCombatStats();
            var preview = warrior.GetCombatStats(dagger);
            Check(warrior.GetEquipmentId(EquipmentSlot.MainWeapon) == sword.equipmentId
                && equipment.GetOwner(dagger.equipmentId) == null && warrior.GetCombatStats().attaquePhysique == beforePreview.attaquePhysique,
                "La prévisualisation ne modifie ni équipement, ni propriétaire, ni statistiques de base");
            Check(equipment.TryEquip(warrior, dagger.equipmentId, out _), "Remplacement d'une arme dans le même emplacement");
            Check(equipment.GetOwner(sword.equipmentId) == null && inventory.GetAllEquipment().Count == itemsBefore,
                "L'ancienne pièce retourne en réserve sans perte ni duplication");
            Check(warrior.GetCombatStats().attaquePhysique == preview.attaquePhysique
                && Mathf.Abs(warrior.GetCombatStats().precision - preview.precision) < .01f, "La prévisualisation correspond aux stats après équipement");
            Check(equipment.TryEquip(mage, sword.equipmentId, out _), "Une arme non maîtrisée reste équipable");
            var mageBase = CombatStats.FromPrimary(mage.GetEffectiveStats());
            var mageCombat = mage.GetCombatStats();
            Check(mageCombat.attaquePhysique == Mathf.RoundToInt((mageBase.attaquePhysique + sword.mainStatValue) * swordDef.unmasteredDamageMultiplier)
                && mageCombat.attaqueMagique == Mathf.RoundToInt(mageBase.attaqueMagique * swordDef.unmasteredDamageMultiplier),
                "Maîtrise : malus de dégâts physiques et magiques appliqué");
            Check(Mathf.Abs(mageCombat.precision - Mathf.Clamp(mageBase.precision * swordDef.unmasteredAccuracyMultiplier, 0, 100)) < .01f,
                "Maîtrise : malus de précision appliqué");
            Check(equipment.ApplyWear(mage, 10) == 1 && sword.currentDurability == 100 - Mathf.CeilToInt(10 * swordDef.unmasteredWearMultiplier),
                "L'arme non maîtrisée s'use plus vite");
            int wearBefore = sword.currentDurability;
            equipment.ApplyWear(mage, -10); sword.TakeDamage(-5);
            Check(sword.currentDurability == wearBefore, "Une usure négative ne répare jamais un objet");
            sword.Repair(-5);
            Check(sword.currentDurability == wearBefore, "Une réparation négative n'endommage pas l'objet");
            mage.AssignToMission("validation");
            Check(!equipment.TryUnequip(mage, EquipmentSlot.MainWeapon, out _), "Retrait de l'équipement bloqué pendant une mission");
            Check(!equipment.TryRepair(sword.equipmentId, out _), "Réparation d'un objet parti en mission refusée");
            mage.SetResting(); mage.Tick(20);
            Check(equipment.TryUnequip(mage, EquipmentSlot.MainWeapon, out _), "Retrait possible au retour à la guilde");
            Check(equipment.TryEquip(warrior, sword.equipmentId, out _), "Reprise d'une arme maîtrisée");
            int beforeWear = sword.currentDurability;
            equipment.ApplyWear(warrior, 10);
            Check(sword.currentDurability == beforeWear - 10 && armor.currentDurability == armor.maxDurability - 10,
                "Usure standard appliquée à l'arme maîtrisée et à l'armure");
            Check(!equipment.TryRepair(sword.equipmentId, out _), "Réparation refusée sans forgeron en poste");
            Check(buildings.AssignAdventurer(warrior, BuildingType.Forge), "Un forgeron peut prendre en charge les réparations");
            var recipe = buildings.GetRecipesForJob(JobType.Blacksmith)[0];
            Check(buildings.StartProduction(warrior, recipe) && !equipment.TryRepair(sword.equipmentId, out _),
                "Un forgeron occupé par une commande ne peut pas réparer");
            var forge = buildings.GetBuilding(BuildingType.Forge);
            buildings.CancelProduction(forge.buildingId, forge.activeTasks[0].taskId);
            economy.LoadEconomy(0, new());
            int damagedDurability = sword.currentDurability;
            Check(!equipment.TryRepair(sword.equipmentId, out _) && sword.currentDurability == damagedDurability,
                "Réparation refusée faute d'or, sans modification de durabilité");
            economy.AddGold(500);
            int repairCost = equipment.GetRepairCost(sword);
            int beforeRepairGold = economy.CurrentGold;
            Check(equipment.TryRepair(sword.equipmentId, out _) && sword.currentDurability == sword.maxDurability
                && economy.CurrentGold == beforeRepairGold - repairCost, "Réparation complète et coût exact en or");
            Check(!equipment.TryRepair(sword.equipmentId, out _) && equipment.GetRepairCost(sword) == 0
                && economy.CurrentGold == beforeRepairGold - repairCost, "Un objet intact ne peut pas être facturé à nouveau");
            equipment.ApplyWear(warrior, 10);
            Check(buildings.UpgradeBuilding(BuildingType.Forge) && !equipment.TryRepair(sword.equipmentId, out _),
                "Une Forge en travaux ne peut pas réparer");
            buildings.Tick(forge.upgradeTimeRemaining + .1f);
            equipment.ApplyWear(warrior, 1000);
            var brokenCombat = warrior.GetCombatStats();
            Check(sword.IsBroken && armor.IsBroken && brokenCombat.attaquePhysique == baseCombat.attaquePhysique
                && brokenCombat.defensePhysique == baseCombat.defensePhysique, "Les pièces cassées n'apportent aucun bonus ni substat");
            Check(equipment.TryRepair(sword.equipmentId, out _) && warrior.GetCombatStats().attaquePhysique > brokenCombat.attaquePhysique,
                "Réparer une arme cassée rétablit ses bonus");

            var accessoryDef = UnityEngine.Object.Instantiate(swordDef); temporary.Add(accessoryDef);
            accessoryDef.name = "Validation_Resistance"; accessoryDef.slot = EquipmentSlot.Accessory;
            accessoryDef.mainStatType = StatType.Resistance; accessoryDef.baseMainStatValue = 3;
            var accessory = new EquipmentInstance(accessoryDef, EquipmentQuality.Normal);
            inventory.AddEquipment(accessory);
            var beforeAccessory = warrior.GetCombatStats();
            Check(equipment.TryEquip(warrior, accessory.equipmentId, out _) && warrior.GetCombatStats().pointsDeVie == beforeAccessory.pointsDeVie + 6
                && warrior.GetCombatStats().defensePhysique == beforeAccessory.defensePhysique + 15
                && warrior.GetCombatStats().defenseMagique == beforeAccessory.defenseMagique + 12,
                "Une stat primaire d'équipement se convertit en toutes ses stats de combat");
            var restrictedDef = UnityEngine.Object.Instantiate(accessoryDef); temporary.Add(restrictedDef);
            restrictedDef.name = "Validation_Classe"; restrictedDef.requiredClassId = mage.classId;
            var restricted = new EquipmentInstance(restrictedDef, EquipmentQuality.Normal); inventory.AddEquipment(restricted);
            Check(!equipment.TryEquip(warrior, restricted.equipmentId, out _) && warrior.GetEquipmentId(EquipmentSlot.Accessory) == accessory.equipmentId,
                "Restriction de classe respectée sans retirer la pièce précédente");
            Check(equipment.TryEquip(mage, restricted.equipmentId, out _), "La classe autorisée peut porter l'objet restreint");

            var passiveClass = UnityEngine.Object.Instantiate(warriorClass); temporary.Add(passiveClass);
            passiveClass.baseStats = new AdventurerStats { force = 10 };
            passiveClass.classPassives = new() { new TraitEffect { effectType = TraitEffectType.Force_Multiplier, value = 1.5f },
                new TraitEffect { effectType = TraitEffectType.Moral_Flat, value = 5f } };
            var trait1 = ScriptableObject.CreateInstance<TraitSO>(); temporary.Add(trait1);
            trait1.effects = new() { new TraitEffect { effectType = TraitEffectType.Force_Multiplier, value = 1.2f },
                new TraitEffect { effectType = TraitEffectType.Moral_Flat, value = 10f } };
            var trait2 = ScriptableObject.CreateInstance<TraitSO>(); temporary.Add(trait2);
            trait2.effects = new() { new TraitEffect { effectType = TraitEffectType.Force_Multiplier, value = 1.1f },
                new TraitEffect { effectType = TraitEffectType.Fatigue_Flat, value = 15f } };
            var subject = new Adventurer("Validation", passiveClass, human, new() { trait1, trait2 });
            var effective = subject.GetEffectiveStats();
            Check(effective.force == 20 && effective.moral == 85 && effective.fatigue == 15,
                "Traits et passifs : multiplicateurs composés et bonus plats additionnés");
            subject.GetEffectiveStats(); subject.GetCombatStats(); subject.GetCombatStats();
            Check(subject.stats.force == 10 && subject.stats.moral == 70 && subject.stats.fatigue == 0,
                "Les calculs répétés ne modifient jamais les stats de base");
            var detached = new Adventurer("Hors roster", warriorClass, human, new());
            Check(!equipment.TryEquip(detached, dagger.equipmentId, out _), "Un aventurier hors de la guilde ne peut pas prendre un objet");
            var restored = JsonUtility.FromJson<Adventurer>(JsonUtility.ToJson(warrior));
            restored.OnAfterLoad(warriorClass, human, new()); restored.BindEquipmentResolver(inventory.GetEquipment);
            Check(restored.GetEquipmentId(EquipmentSlot.MainWeapon) == sword.equipmentId
                && restored.GetCombatStats().attaquePhysique == warrior.GetCombatStats().attaquePhysique,
                "Les IDs des emplacements se reconstruisent après sérialisation");
            int beforeDeparture = inventory.GetAllEquipment().Count;
            Check(roster.FireAdventurer(mage.id) && mage.equippedItems.Count == 0 && equipment.GetOwner(restricted.equipmentId) == null
                && inventory.GetAllEquipment().Count == beforeDeparture, "Le renvoi restitue les pièces sans les dupliquer");
            roster.KillAdventurer(warrior.id);
            Check(warrior.equippedItems.Count == 0 && equipment.GetOwner(sword.equipmentId) == null, "La mort libère l'équipement porté");
            Check(economy.SellEquipment(sword.equipmentId), "Une pièce libérée peut être vendue");
            GameManager.Instance.StartNewGame();
            Check(inventory.GetAllEquipment().Count == 0 && equipment.StoredEquipmentCount == 0 && roster.GetAllAdventurers().Count == 0,
                "Nouvelle partie : aucune pièce ni appartenance résiduelle");
            report.passed = true;
            Debug.Log($"Équipement : {report.checks.Count} vérifications réussies en mode Play.");
        }
        catch (Exception ex) { report.failure = ex.ToString(); Debug.LogException(ex); }
        finally
        {
            GameManager.Instance.StartNewGame(); clock.SetSpeed(GameSpeed.Paused); clock.enabled = true;
            UnityEngine.Random.state = random;
            foreach (var item in temporary) UnityEngine.Object.DestroyImmediate(item);
            Directory.CreateDirectory("Docs/AI");
            File.WriteAllText("Docs/AI/EquipmentValidation.json", JsonUtility.ToJson(report, true));
        }
    }

    [MenuItem("Guildmaster/4 - Demo equipement et reparation (Play)")]
    public static void PrepareDemo()
    {
        if (!Application.isPlaying || EquipmentManager.Instance == null) return;
        GameManager.Instance.StartNewGame();
        var roster = AdventurerManager.Instance;
        var inventory = GuildInventory.Instance;
        var human = roster.GetRaceById("Race_Human");
        var warrior = new Adventurer("Aldric Briseforge", roster.GetClassById("Class_Warrior"), human, new());
        var mage = new Adventurer("Liora Boisclair", roster.GetClassById("Class_Mage"), human, new());
        roster.LoadRoster(new List<Adventurer> { warrior, mage }); roster.OnAfterLoad();
        var sword = new EquipmentInstance(inventory.GetEquipmentDefById("Equipment_IronSword"), EquipmentQuality.Normal);
        var armor = new EquipmentInstance(inventory.GetEquipmentDefById("Equipment_ReinforcedArmor"), EquipmentQuality.Rare);
        var dagger = new EquipmentInstance(inventory.GetEquipmentDefById("Equipment_IronDagger"), EquipmentQuality.Epic);
        inventory.AddEquipment(sword); inventory.AddEquipment(armor); inventory.AddEquipment(dagger);
        EquipmentManager.Instance.TryEquip(warrior, sword.equipmentId, out _);
        EquipmentManager.Instance.ApplyWear(warrior, 80);
        TimeManager.Instance.SetSpeed(GameSpeed.Paused);
        UnityEngine.Object.FindAnyObjectByType<GuildWorkshopUI>().SelectEquipmentAdventurer(warrior.id);
    }
}
