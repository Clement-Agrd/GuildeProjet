using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

public static class GuildWorkshopValidation
{
    [Serializable] private class Report
    {
        public string checkedAtUtc;
        public string unityVersion;
        public bool passed;
        public List<string> checks = new();
        public string failure;
    }

    [MenuItem("Guildmaster/2 - Verifier la boucle (Play)")]
    public static void Run()
    {
        if (!Application.isPlaying || GameManager.Instance == null)
        {
            Debug.LogWarning("Ouvrez GuildWorkshop et lancez Play avant cette vérification.");
            return;
        }
        var report = new Report { checkedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        var clock = TimeManager.Instance;
        var ui = UnityEngine.Object.FindAnyObjectByType<GuildWorkshopUI>();
        var autoPause = ui?.Root.Q<Toggle>("auto-pause");
        bool initialAutoPause = autoPause?.value ?? false;
        var randomState = UnityEngine.Random.state;
        clock.enabled = false;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            report.checks.Add(name);
        }
        try
        {
            Check(autoPause != null && ui.Root.Q<Button>("open-journal") != null, "Les commandes du journal sont accessibles");
            autoPause.value = false;
            var game = GameManager.Instance;
            var roster = AdventurerManager.Instance;
            var buildings = BuildingManager.Instance;
            var inventory = GuildInventory.Instance;
            var economy = EconomyManager.Instance;
            game.StartNewGame();
            Check(VerifyPauseShortcut(clock), "Espace suspend la simulation puis reprend à la vitesse précédente");
            clock.SetSpeed(GameSpeed.Normal);
            Check(economy.CurrentGold == 500 && roster.GetAllAdventurers().Count == 0, "Nouvelle partie : 500 or, roster vide");
            Check(buildings.GetAllBuildings().Count == 8 && roster.GetRecruitPool().Count == 4 && !buildings.HasBuilding(BuildingType.Lodge), "Huit bâtiments et quatre candidats au départ");
            var offer = roster.GetRecruitPool()[0];
            var a = offer.candidate;
            Check(roster.HireAdventurer(a.id) && economy.CurrentGold == 500 - offer.hiringBonus, "Recrutement : prime débitée");
            Check(!roster.HireAdventurer(a.id), "Une candidature ne peut être embauchée deux fois");
            int beforeAssignment = economy.CurrentGold;
            int wage = a.CalculateSalary(buildings.GetJobDefinition(JobType.Blacksmith));
            Check(buildings.AssignAdventurer(a, BuildingType.Forge), "Affectation à la Forge");
            Check(economy.CurrentGold == beforeAssignment - wage, "Salaire annoncé payé exactement à la prise de poste");
            Check(!buildings.AssignAdventurer(a, BuildingType.Forge), "Affectation doublée refusée");
            var forge = buildings.GetBuilding(BuildingType.Forge);
            var recipe = buildings.GetRecipesForJob(JobType.Blacksmith)[0];
            var component = recipe.inputResources[0];
            int stockBefore = inventory.GetAmount(component.resource);
            a.stats.fatigue = 15f;
            clock.SetSpeed(GameSpeed.Normal);
            clock.Advance(2f);
            Check(a.stats.fatigue == 15f && forge.HasWorker(a.id), "Un forgeron sans commande ne se fatigue pas");
            Check(buildings.StartProduction(a, recipe), "Lancement d'une recette de Forge");
            Check(inventory.GetAmount(component.resource) == stockBefore - component.amount, "Composants consommés une fois");
            Check(!buildings.StartProduction(a, recipe) && forge.activeTasks.Count == 1, "Une seule commande par artisan");
            Check(inventory.GetAmount(component.resource) == stockBefore - component.amount, "Un second lancement refusé ne consomme rien");
            var task = forge.activeTasks[0];
            clock.SetSpeed(GameSpeed.Paused);
            float beforePause = task.progress;
            double beforeTime = clock.ElapsedGameTime;
            clock.Advance(30f);
            Check(task.progress == beforePause && clock.ElapsedGameTime == beforeTime, "Pause : production et temps immobiles");
            clock.SetSpeed(GameSpeed.Fast); clock.Advance(.5f);
            Check(Math.Abs(clock.ElapsedGameTime - beforeTime - 1f) < .001f && task.progress > beforePause && a.stats.fatigue > 15f, "Vitesse x2 : 0,5 seconde réelle donne 1 seconde de jeu");
            beforeTime = clock.ElapsedGameTime;
            clock.SetSpeed(GameSpeed.VeryFast); clock.Advance(.5f);
            Check(Math.Abs(clock.ElapsedGameTime - beforeTime - 1.5f) < .001f, "Vitesse x3 : 0,5 seconde réelle donne 1,5 seconde de jeu");
            clock.TogglePause();
            Check(clock.CurrentSpeed == GameSpeed.Paused, "Bascule pause depuis la vitesse x3");
            clock.TogglePause();
            Check(clock.CurrentSpeed == GameSpeed.VeryFast, "La reprise restaure la dernière vitesse x3");
            autoPause.value = true;
            beforeTime = clock.ElapsedGameTime;
            a.stats.fatigue = 99.99f;
            clock.Advance(2f);
            Check(a.IsExhausted && a.currentStateType == AdventurerStateType.Resting && !forge.HasWorker(a.id) && forge.HasFreeSlot,
                "Fatigue à 100 % : repos forcé et emplacement libéré");
            Check(clock.CurrentSpeed == GameSpeed.Paused && Math.Abs(clock.ElapsedGameTime - beforeTime - .1f) < .001f
                && ui.Root.Q<Label>("status").text.Contains("fatigue 100 %"), "Le journal signalant la fatigue arrête la simulation au pas courant");
            Check(buildings.CanStartService(a, buildings.GetBuilding(BuildingType.Tavern)), "L’aventurier épuisé peut être envoyé à la taverne");
            autoPause.value = false;
            float pausedProgress = task.progress;
            buildings.Tick(20f);
            Check(task.progress == pausedProgress && inventory.GetAllEquipment().Count == 0, "Une commande sans son artisan ne progresse pas");
            roster.Tick(20f);
            Check(ui.Root.Q<Label>("status").text.Contains("a terminé son repos"), "Le journal annonce la fin du repos naturel");
            Check(a.IsAvailable && buildings.AssignAdventurer(a, BuildingType.Forge), "L'artisan reposé peut retrouver son poste");
            clock.TogglePause();
            beforeTime = clock.ElapsedGameTime;
            clock.Advance(.1f);
            Check(Math.Abs(clock.ElapsedGameTime - beforeTime - .3f) < .001f, "La reprise ne rattrape pas les pas rejetés par la pause automatique");
            buildings.Tick(task.totalDuration);
            Check(forge.activeTasks.Count == 0 && inventory.GetAllEquipment().Count == 1, "Fin de fabrication : équipement ajouté une fois");
            float fatigueAfterCraft = a.stats.fatigue;
            clock.Advance(1f);
            Check(a.stats.fatigue == fatigueAfterCraft && forge.HasWorker(a.id),
                "La fatigue cesse en fin de fabrication même si le forgeron reste en poste");
            var item = inventory.GetAllEquipment()[0];
            Check(item.craftedByAdventurerId == a.id && item.craftedByJobType == JobType.Blacksmith, "Fabricant et métier de fabrication conservés");
            Check(a.stats.experience > 0 && a.GetJobInstance(JobType.Blacksmith).currentExperience > 0, "XP générale et XP métier attribuées");
            int goldBeforeSale = economy.CurrentGold;
            int expectedRevenue = economy.GetEquipmentSellPrice(item);
            Check(economy.SellEquipment(item.equipmentId) && economy.CurrentGold == goldBeforeSale + expectedRevenue
                && inventory.GetAllEquipment().Count == 0, "Vente : équipement retiré et revenu net commission crédité");
            Check(!economy.SellEquipment(item.equipmentId), "Une pièce vendue ne peut être vendue deux fois");
            stockBefore = inventory.GetAmount(component.resource);
            Check(buildings.StartProduction(a, recipe), "Nouvelle commande après la précédente");
            buildings.CancelProduction(forge.buildingId, forge.activeTasks[0].taskId);
            Check(forge.activeTasks.Count == 0 && inventory.GetAmount(component.resource) == stockBefore, "Annulation : commande retirée et composants restitués");
            buildings.UnassignAdventurer(a);
            Check(a.IsAvailable && !forge.HasWorker(a.id), "Libérer un poste rend l'aventurier immédiatement disponible");
            Check(buildings.AssignAdventurer(a, BuildingType.Market) && !buildings.StartProduction(a, recipe), "Un marchand ne peut pas fabriquer une recette de forgeron");
            buildings.UnassignAdventurer(a); a.Tick(20f);
            int goldBeforeUpgrade = economy.CurrentGold;
            var upgrade = forge.GetNextLevelData();
            Check(buildings.UpgradeBuilding(BuildingType.Forge) && economy.CurrentGold == goldBeforeUpgrade - upgrade.goldCost, "Amélioration : coûts débités");
            Check(!forge.IsActive, "La forge est inactive pendant son amélioration");
            buildings.Tick(upgrade.upgradeTime + .1f);
            Check(forge.currentLevel == 2 && forge.MaxWorkers == 2 && forge.IsActive, "Amélioration terminée : niveau et emplacements augmentés");
            var primary = new AdventurerStats();
            var increased = new AdventurerStats(primary); increased.resistance += 4;
            var expected = CombatStats.FromPrimary(increased);
            var combat = CombatStats.FromPrimary(primary); combat.AddPrimary(PrimaryStat.Resistance, 4);
            Check(combat.pointsDeVie == expected.pointsDeVie && combat.defensePhysique == expected.defensePhysique
                && combat.defenseMagique == expected.defenseMagique, "Résistance : conversion et bonus d'équipement cohérents");
            Check(new JobInstance(JobType.Blacksmith).GetSalaryMultiplier(null) == 1f, "Définition de métier absente : valeur neutre sans exception");
            inventory.AddResource(component.resource, component.resource.maxStackSize);
            int fullStockGold = economy.CurrentGold;
            Check(!economy.BuyResource(component.resource, 5) && economy.CurrentGold == fullStockGold, "Réserves pleines : achat refusé sans dépense");
            Check(ui != null && ui.Root.Q<Label>("gold") != null && ui.Root.Q<Button>("pause") != null
                && ui.Root.Q<Button>("guild-home") != null && ui.Root.Q<Button>("equipment-back") != null && ui.ForgeDropTarget != null, "Interface : HUD, contrôle du temps et cible de dépôt reliés");
            game.StartNewGame();
            Check(inventory.GetAllEquipment().Count == 0 && roster.GetAllAdventurers().Count == 0 && economy.CurrentGold == 500
                && buildings.GetBuilding(BuildingType.Forge).currentLevel == 1 && clock.ElapsedGameTime == 0, "Nouvelle partie : toutes les données runtime réinitialisées");
            economy.AddGold(5000);
            var trainer = roster.GetRecruitPool()[0].candidate;
            var trainee = roster.GetRecruitPool()[1].candidate;
            var scout = roster.GetRecruitPool()[2].candidate;
            Check(roster.HireAdventurer(trainer.id) && roster.HireAdventurer(trainee.id) &&
                roster.HireAdventurer(scout.id), "Recrutement des aventuriers des services");
            var training = buildings.GetBuilding(BuildingType.TrainingRoom);
            Check(buildings.StartService(trainee, training) && !buildings.AssignAdventurer(trainee, BuildingType.TrainingRoom)
                && !buildings.StartService(scout, training), "Entraînement : place distincte, capacité et affectation unique");
            buildings.Tick(9f);
            Check(trainee.stats.experience == 0, "Séance suspendue en l’absence d’un entraîneur");
            Check(buildings.AssignAdventurer(trainer, BuildingType.TrainingRoom) && !buildings.StartService(trainer, training),
                "L’entraîneur occupe un poste distinct de la place du stagiaire");
            buildings.Tick(9f);
            Check(trainee.stats.experience > 0 && trainee.IsAvailable && training.HasWorker(trainer.id),
                "Fin d’entraînement : XP délivrée et place de service libérée");
            var tavern = buildings.GetBuilding(BuildingType.Tavern);
            trainee.stats.fatigue = 40f;
            trainee.stats.moral = 50f;
            Check(buildings.StartService(trainee, tavern), "Taverne : placement en service");
            buildings.Tick(2f);
            Check(trainee.stats.fatigue < 40f && trainee.stats.moral > 50f && !tavern.HasWorker(trainee.id),
                "Taverne : fatigue réduite et moral restauré");
            trainee.stats.fatigue = 50f;
            trainee.stats.moral = 99f;
            buildings.Tick(.5f);
            Check(trainee.currentService == BuildingType.Tavern && ui.Root.Q<Label>("status").text.Contains("moral de 100 %"),
                "Le journal signale le moral maximal même si le repos continue");
            trainee.stats.fatigue = 2f;
            buildings.Tick(.5f);
            Check(trainee.IsAvailable && ui.Root.Q<Label>("status").text.Contains("a terminé son repos"),
                "Fin du repos à la taverne : le journal annonce le repos et le poste de service est libéré");
            var infirmary = buildings.GetBuilding(BuildingType.Infirmary);
            Check(!buildings.StartService(trainee, infirmary), "L’infirmerie accueille les blessés");
            trainee.SetInjured(12f);
            Check(trainee.HealthFraction > 0f && trainee.HealthFraction < 1f, "Un blessé affiche des PV partiels");
            trainee.Tick(1f);
            Check(trainee.HealthFraction > .25f && trainee.HealthFraction < 1f, "Les PV remontent au cours de la guérison");
            Check(buildings.StartService(trainee, infirmary) && buildings.AssignAdventurer(scout, BuildingType.Infirmary),
                "Patients et soigneurs occupent des places distinctes");
            buildings.Tick(9f);
            Check(trainee.currentStateType == AdventurerStateType.Resting && trainee.currentService == null && trainee.HealthFraction == 1f,
                "Les soins accélèrent la guérison et libèrent la place");
            trainee.Tick(20f);
            buildings.UnassignAdventurer(scout);
            scout.Tick(20f);
            var hq = buildings.GetBuilding(BuildingType.Headquarters);
            float passiveInterval = buildings.MissionInterval;
            float passiveQuality = buildings.MissionQualityBonus;
            int passiveCapacity = buildings.MissionCapacity;
            buildings.Tick(passiveInterval);
            Check(buildings.MissionOffers.Count == 1 && buildings.MissionOffers[0].Contains("contrat"),
                "Le quartier général trouve un contrat même sans éclaireur");
            buildings.ArchiveMission(buildings.MissionOffers[0]);
            Check(buildings.AssignAdventurer(scout, BuildingType.Headquarters) && buildings.MissionInterval < passiveInterval
                && buildings.MissionCapacity > passiveCapacity && buildings.MissionQualityBonus > passiveQuality,
                "Un éclaireur améliore la vitesse, la capacité et la qualité des missions");
            buildings.Tick(16f);
            Check(buildings.MissionOffers.Count == 1, "Un éclaireur découvre une mission");
            int capacity = buildings.MissionCapacity;
            int rosterCapacity = buildings.RosterCapacity;
            float missionInterval = buildings.MissionInterval;
            Check(buildings.UpgradeBuilding(BuildingType.Headquarters), "Amélioration du quartier général");
            buildings.Tick(hq.upgradeTimeRemaining + .1f);
            Check(buildings.RosterCapacity > rosterCapacity && buildings.MissionCapacity > capacity &&
                buildings.MissionInterval < missionInterval, "Le niveau augmente l’effectif, la capacité de recherche et la vitesse");
            capacity = buildings.MissionCapacity;
            missionInterval = buildings.MissionInterval;
            Check(buildings.AssignAdventurer(trainee, BuildingType.Headquarters) && buildings.MissionCapacity > capacity &&
                buildings.MissionInterval < missionInterval, "Un second éclaireur augmente la capacité et la vitesse de recherche");
            buildings.Tick(100f);
            Check(buildings.MissionOffers.Count == buildings.MissionCapacity, "Le tableau respecte sa capacité de découverte");
            buildings.ArchiveMission(buildings.MissionOffers[0]);
            Check(buildings.MissionOffers.Count == buildings.MissionCapacity - 1, "Archiver une mission libère une place");
            buildings.UnassignAdventurer(trainee);
            trainee.Tick(20f);
            var jewelry = buildings.GetRecipesForJob(JobType.Craftsman);
            Check(jewelry.Count == 3 && buildings.AssignAdventurer(trainee, BuildingType.Workshop),
                "Atelier : trois recettes de bijouterie et un poste d’artisan");
            var slots = new HashSet<EquipmentSlot>();
            foreach (var jewelryRecipe in jewelry)
            {
                Check(buildings.StartProduction(trainee, jewelryRecipe), "Fabrication : " + jewelryRecipe.recipeName);
                buildings.Tick(jewelryRecipe.baseCraftDuration + .1f);
                var produced = inventory.GetAllEquipment().Find(item => item.definition == jewelryRecipe.outputEquipment);
                Check(produced != null && EquipmentManager.Instance.TryEquip(trainee, produced.equipmentId, out _) &&
                    slots.Add(produced.definition.slot), "Équipement : " + jewelryRecipe.recipeName);
            }
            Check(slots.SetEquals(new[] { EquipmentSlot.Bracelet, EquipmentSlot.Necklace, EquipmentSlot.Ring })
                && trainee.equippedItems.Count == 3, "Les trois bijoux peuvent être portés simultanément");
            buildings.UnassignAdventurer(trainee);
            trainee.Tick(20f);
            buildings.UnassignAdventurer(trainer);
            foreach (var type in BuildingManager.GuildBuildings)
            {
                var building = buildings.GetBuilding(type);
                Check(buildings.AssignAdventurer(trainee, type) && trainee.currentJobType == building.definition.associatedJobType,
                    $"Poste et métier associés dans {building.definition.buildingName}");
                buildings.UnassignAdventurer(trainee);
                trainee.Tick(20f);
            }
            roster.RefreshRecruitPool();
            while (buildings.CanHire)
            {
                if (roster.GetRecruitPool().Count == 0) roster.RefreshRecruitPool();
                if (!roster.HireAdventurer(roster.GetRecruitPool()[0].candidate.id))
                    throw new InvalidOperationException("Impossible de remplir l’effectif pour vérifier la limite");
            }
            roster.RefreshRecruitPool();
            int goldAtCapacity = economy.CurrentGold;
            Check(!roster.HireAdventurer(roster.GetRecruitPool()[0].candidate.id) && economy.CurrentGold == goldAtCapacity,
                "Recrutement plein : candidature conservée et aucun or débité");
            VerifySeriesAndAutoSales(Check);
            VerifyAutoPurchasesAndJournal(Check, ui, clock, autoPause);
            VerifyJournalDecisions(Check, ui, clock, autoPause);
            VerifyJobPerformance(Check, autoPause);
            VerifyGuildJobPerformance(Check, autoPause);
            VerifyRacialJobEffects(Check);
            report.passed = true;
            Debug.Log($"Atelier : {report.checks.Count} vérifications réussies en mode Play.");
        }
        catch (Exception ex)
        {
            report.failure = ex.ToString();
            Debug.LogException(ex);
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            if (autoPause != null) autoPause.value = initialAutoPause;
            GameManager.Instance.StartNewGame();
            clock.SetSpeed(GameSpeed.Paused);
            clock.enabled = true;
            Directory.CreateDirectory("Docs/AI");
            File.WriteAllText("Docs/AI/WorkshopValidation.json", JsonUtility.ToJson(report, true));
        }
    }

    private static void VerifySeriesAndAutoSales(Action<bool, string> check)
    {
        GameManager.Instance.StartNewGame();
        var roster = AdventurerManager.Instance;
        var buildings = BuildingManager.Instance;
        var economy = EconomyManager.Instance;
        var inventory = GuildInventory.Instance;
        var worker = roster.GetRecruitPool()[0].candidate;
        var merchant = roster.GetRecruitPool()[1].candidate;
        check(!economy.ConfigureAutoSales(true, EquipmentQuality.Normal, true),
            "La vente automatique exige un marchand en poste");
        check(roster.HireAdventurer(worker.id) && roster.HireAdventurer(merchant.id)
            && buildings.AssignAdventurer(worker, BuildingType.Forge) && buildings.AssignAdventurer(merchant, BuildingType.Market),
            "Un artisan et un marchand peuvent travailler simultanément");
        var forge = buildings.GetBuilding(BuildingType.Forge);
        var recipe = buildings.GetRecipesForJob(JobType.Blacksmith)[0];
        var component = recipe.inputResources[0];
        int initialComponents = inventory.GetAmount(component.resource);
        var armor = inventory.GetEquipmentDefById("Equipment_ReinforcedArmor");
        var worn = new EquipmentInstance(armor, EquipmentQuality.Normal);
        var forSale = new EquipmentInstance(armor, EquipmentQuality.Normal);
        inventory.AddEquipment(worn);
        inventory.AddEquipment(forSale);
        check(EquipmentManager.Instance.TryEquip(worker, worn.equipmentId, out _), "Un objet éligible porté est protégé de la vente");
        while (inventory.EquipmentCount < inventory.EquipmentCapacity - 1)
            if (!inventory.AddEquipment(new EquipmentInstance(armor, EquipmentQuality.Rare)))
                throw new InvalidOperationException("Impossible de remplir le stock de test");

        check(buildings.StartProduction(worker, recipe, 3) && buildings.ReservedEquipmentSlots == 1
            && inventory.GetAmount(component.resource) == initialComponents - component.amount,
            "Une série réserve et paie uniquement la prochaine pièce");
        check(!inventory.AddEquipment(new EquipmentInstance(armor, EquipmentQuality.Normal)),
            "La capacité inclut les places réservées aux commandes");
        var task = forge.activeTasks[0];
        check(economy.ConfigureAutoSales(true, EquipmentQuality.Normal, true), "Le marchand peut régler le seuil et la qualité");
        economy.Tick(3f);
        check(economy.AutoSoldCount == 0, "La vente limitée au stock plein attend que l'inventaire soit rempli");
        buildings.Tick(task.totalDuration + .1f);
        check(inventory.IsEquipmentFull && task.awaitingInputs && task.seriesUnit == 2 && buildings.ReservedEquipmentSlots == 0
            && inventory.GetAmount(component.resource) == initialComponents - component.amount,
            "La série attend une place sans payer la pièce suivante");
        int beforeSale = economy.CurrentGold;
        int net = economy.GetEquipmentSellPrice(forSale);
        float merchantFatigue = merchant.stats.fatigue;
        economy.Tick(3f);
        check(economy.AutoSoldCount == 1 && economy.CurrentGold == beforeSale + net
            && inventory.GetEquipment(forSale.equipmentId) == null && inventory.GetEquipment(worn.equipmentId) == worn,
            "La vente automatique libère une place et respecte l'équipement porté");
        check(merchant.stats.fatigue > merchantFatigue, "La vente automatique fatigue le marchand");
        buildings.Tick(.1f);
        check(!task.awaitingInputs && buildings.ReservedEquipmentSlots == 1
            && inventory.GetAmount(component.resource) == initialComponents - 2 * component.amount,
            "La série reprend seule quand une place se libère");
        buildings.Tick(task.totalDuration + .1f);
        check(task.awaitingInputs && task.seriesUnit == 3 && inventory.IsEquipmentFull, "La pièce suivante remplit à nouveau le stock");
        int beforeCancel = inventory.GetAmount(component.resource);
        buildings.CancelProduction(forge.buildingId, task.taskId);
        check(inventory.GetAmount(component.resource) == beforeCancel && forge.activeTasks.Count == 0,
            "Annuler une série en attente ne restitue aucun composant non payé");
        buildings.UnassignAdventurer(merchant);
        economy.Tick(3f);
        check(economy.AutoSoldCount == 1 && inventory.IsEquipmentFull, "La vente automatique se suspend sans marchand");

        inventory.RemoveEquipment(inventory.GetAllEquipment()[1].equipmentId);
        inventory.RemoveEquipment(inventory.GetAllEquipment()[1].equipmentId);
        check(buildings.StartProduction(worker, recipe, 2), "Une série de deux pièces peut démarrer");
        buildings.Tick(forge.activeTasks[0].totalDuration + .1f);
        buildings.Tick(forge.activeTasks[0].totalDuration + .1f);
        check(inventory.IsEquipmentFull && forge.activeTasks.Count == 0 && buildings.ReservedEquipmentSlots == 0,
            "La fabrication s'arrête au nombre de pièces demandé");

        inventory.RemoveEquipment(inventory.GetAllEquipment()[1].equipmentId);
        inventory.RemoveEquipment(inventory.GetAllEquipment()[1].equipmentId);
        check(buildings.StartProduction(worker, recipe, BuildingManager.MaxSeriesQuantity), "La quantité maximale est acceptée");
        var stopping = forge.activeTasks[0];
        check(!buildings.ReplaceAdventurer(merchant, BuildingType.Forge, "absent") && forge.HasWorker(worker.id),
            "Un remplacement périmé conserve le titulaire actuel");
        check(buildings.ReplaceAdventurer(merchant, BuildingType.Forge, worker.id) && worker.IsAvailable
            && forge.HasWorker(merchant.id) && !forge.HasWorker(worker.id), "Le remplacement rend le titulaire disponible et installe son remplaçant");
        float pausedProgress = stopping.progress;
        buildings.Tick(stopping.totalDuration);
        check(stopping.progress == pausedProgress && forge.activeTasks.Count == 1,
            "La série d'un titulaire remplacé reste en pause");
        check(buildings.ReplaceAdventurer(worker, BuildingType.Forge, merchant.id)
            && buildings.StopSeriesAfterCurrentUnit(forge.buildingId, stopping.taskId), "L’arrêt différé conserve la pièce en fabrication");
        int countBeforeStop = inventory.EquipmentCount;
        int componentsBeforeStop = inventory.GetAmount(component.resource);
        buildings.Tick(stopping.totalDuration + .1f);
        check(inventory.EquipmentCount == countBeforeStop + 1 && forge.activeTasks.Count == 0
            && inventory.GetAmount(component.resource) == componentsBeforeStop && buildings.ReservedEquipmentSlots == 0,
            "L’arrêt différé livre exactement une pièce et ne démarre pas la suivante");

        check(buildings.StartProduction(worker, recipe, 10), "Une série peut être planifiée près de la limite du stock");
        var waiting = forge.activeTasks[0];
        buildings.Tick(waiting.totalDuration + .1f);
        check(waiting.awaitingInputs && buildings.StopSeriesAfterCurrentUnit(forge.buildingId, waiting.taskId)
            && forge.activeTasks.Count == 0, "L’arrêt différé clôt immédiatement une série sans pièce en cours");
    }

    private static void VerifyAutoPurchasesAndJournal(Action<bool, string> check, GuildWorkshopUI ui,
        TimeManager clock, Toggle autoPause)
    {
        GameManager.Instance.StartNewGame();
        var roster = AdventurerManager.Instance;
        var economy = EconomyManager.Instance;
        var buildings = BuildingManager.Instance;
        var stock = GuildInventory.Instance;
        var smith = roster.GetRecruitPool()[0].candidate;
        var merchant = roster.GetRecruitPool()[1].candidate;
        check(!economy.AutoBuyComponents && !economy.ConfigureAutoPurchases(true), "L’achat automatique demande un marchand");
        autoPause.value = true;
        clock.SetSpeed(GameSpeed.Normal);
        check(roster.HireAdventurer(smith.id) && roster.HireAdventurer(merchant.id)
            && buildings.AssignAdventurer(smith, BuildingType.Forge) && buildings.AssignAdventurer(merchant, BuildingType.Market),
            "Un forgeron et un marchand sont affectés pour tester le réapprovisionnement");
        smith.AddExperience(smith.stats.experienceToNextLevel * 2);
        check(clock.CurrentSpeed == GameSpeed.Normal && ui.Root.Q<Label>("status").text.Contains("atteint le niveau"),
            "Une montée de niveau apparaît dans le journal sans mettre le jeu en pause");

        var recipe = buildings.GetRecipesForJob(JobType.Blacksmith)[0];
        var first = recipe.inputResources[0];
        var second = recipe.inputResources[1];
        var manualSale = new EquipmentInstance(recipe.outputEquipment, EquipmentQuality.Normal);
        float fatigueBefore = merchant.stats.fatigue;
        check(economy.BuyResource(first.resource, 1) && economy.SellResource(first.resource, 1)
            && stock.AddEquipment(manualSale) && economy.SellEquipment(manualSale.equipmentId)
            && merchant.stats.fatigue == fatigueBefore, "Les achats et ventes manuels ne fatiguent pas le marchand");

        stock.AddResource(first.resource, -stock.GetAmount(first.resource));
        stock.AddResource(second.resource, -stock.GetAmount(second.resource));
        check(!buildings.StartProduction(smith, recipe, 2), "La commande sans stock est refusée tant que les achats automatiques sont désactivés");
        check(economy.ConfigureAutoPurchases(true) && buildings.StartProduction(smith, recipe, 2), "La commande est acceptée avec le réapprovisionnement activé");
        var task = buildings.GetTaskForWorker(smith.id);

        buildings.UnassignAdventurer(merchant);
        int goldBefore = economy.CurrentGold;
        buildings.Tick(.1f);
        check(task.awaitingInputs && economy.CurrentGold == goldBefore && clock.CurrentSpeed == GameSpeed.Paused,
            "Sans marchand, la commande attend et le journal signale un blocage");
        check(buildings.AssignAdventurer(merchant, BuildingType.Market), "Le marchand peut reprendre son poste");
        clock.SetSpeed(GameSpeed.Normal);
        int firstCost = economy.GetBuyPrice(first.resource, first.amount);
        economy.SpendGold(economy.CurrentGold - firstCost);
        buildings.Tick(.1f);
        check(task.awaitingInputs && economy.CurrentGold == firstCost && stock.GetAmount(first.resource) == 0
            && stock.GetAmount(second.resource) == 0 && merchant.stats.fatigue == fatigueBefore,
            "Si l’or manque, aucun achat partiel ni fatigue");

        stock.AddResource(second.resource, second.amount);
        economy.AddGold(100);
        goldBefore = economy.CurrentGold;
        clock.SetSpeed(GameSpeed.Normal);
        buildings.Tick(.1f);
        check(!task.awaitingInputs && economy.CurrentGold == goldBefore - firstCost && merchant.stats.fatigue > fatigueBefore
            && clock.CurrentSpeed == GameSpeed.Normal,
            "L’achat automatique paie seulement les composants manquants et fatigue le marchand sans interrompre le jeu");
        buildings.Tick(task.totalDuration + .1f);
        check(!task.awaitingInputs && task.seriesUnit == 2 && stock.EquipmentCount == 1,
            "Les pièces suivantes d’une série peuvent à leur tour être réapprovisionnées");
        buildings.Tick(task.totalDuration + .1f);
        check(stock.EquipmentCount == 2 && buildings.GetTaskForWorker(smith.id) == null && clock.CurrentSpeed == GameSpeed.Paused,
            "La fin de série déclenche la pause quand l’artisan attend une commande");
    }

    private static void VerifyJournalDecisions(Action<bool, string> check, GuildWorkshopUI ui, TimeManager clock, Toggle autoPause)
    {
        GameManager.Instance.StartNewGame();
        var roster = AdventurerManager.Instance;
        var buildings = BuildingManager.Instance;
        var forge = buildings.GetBuilding(BuildingType.Forge);
        var tavern = buildings.GetBuilding(BuildingType.Tavern);
        var smith = roster.GetRecruitPool()[0].candidate;
        var recipe = buildings.GetRecipesForJob(JobType.Blacksmith)[0];
        autoPause.value = true;
        clock.SetSpeed(GameSpeed.Fast);
        check(roster.HireAdventurer(smith.id) && buildings.AssignAdventurer(smith, BuildingType.Forge)
            && buildings.StartProduction(smith, recipe, 2), "Une série est prête pour vérifier les actions de pause");
        buildings.Tick(forge.activeTasks[0].totalDuration + .1f);
        check(clock.CurrentSpeed == GameSpeed.Fast, "La fabrication continue entre deux pièces d’une série");
        buildings.Tick(forge.activeTasks[0].totalDuration + .1f);
        check(clock.CurrentSpeed == GameSpeed.Paused && GetDecisionButton(ui, "Relancer une série")?.enabledInHierarchy == true
            && GetDecisionButton(ui, "Aller à la forge") != null && GetDecisionButton(ui, "Ignorer") != null,
            "Fin de série : les trois choix sont disponibles dans la barre du bas");
        smith.AddExperience(smith.stats.experienceToNextLevel * 2);
        check(GetDecisionButton(ui, "Relancer une série")?.enabledInHierarchy == true,
            "Une montée de niveau ne masque pas la décision en attente dans la barre du bas");
        SubmitDecision(ui, "Aller à la forge");
        check(clock.CurrentSpeed == GameSpeed.Paused && forge.activeTasks.Count == 0, "Ouvrir la Forge préserve la pause sans lancer de commande");
        SubmitDecision(ui, "Relancer une série");
        check(clock.CurrentSpeed == GameSpeed.Fast && forge.activeTasks.Count == 1 && forge.activeTasks[0].seriesQuantity == 2,
            "Relancer une série reprend la même recette, la même quantité et la vitesse précédente");

        smith.stats.fatigue = 100f;
        smith.stats.moral = 99f;
        smith.SetResting();
        check(clock.CurrentSpeed == GameSpeed.Paused && GetDecisionButton(ui, "Envoyer à la taverne") != null
            && GetDecisionButton(ui, "Ignorer") != null, "La fatigue propose la taverne ou d’ignorer l’alerte");
        SubmitDecision(ui, "Envoyer à la taverne");
        check(clock.CurrentSpeed == GameSpeed.Fast && smith.currentService == BuildingType.Tavern,
            "Envoyer l’aventurier à la taverne remet le jeu en marche");
        buildings.Tick(.5f);
        check(clock.CurrentSpeed == GameSpeed.Fast && smith.currentService == BuildingType.Tavern,
            "Le moral à 100 % reste informatif si l’aventurier est encore au repos");
        smith.stats.fatigue = 1f;
        buildings.Tick(1f);
        check(clock.CurrentSpeed == GameSpeed.Paused && smith.IsAvailable && buildings.ServiceOccupancy(tavern.buildingType) == 0
            && GetDecisionButton(ui, "Envoyer à son ancien poste")?.enabledInHierarchy == true,
            "La fin du repos suspend le temps et propose de reprendre l’ancien poste");
        SubmitDecision(ui, "Voir la fiche");
        check(clock.CurrentSpeed == GameSpeed.Paused && smith.IsAvailable, "Consulter la fiche préserve la pause");
        SubmitDecision(ui, "Envoyer à son ancien poste");
        check(clock.CurrentSpeed == GameSpeed.Fast && forge.HasWorker(smith.id) && forge.activeTasks.Count == 1,
            "Retourner à la Forge remet le jeu en marche et conserve la commande de l’artisan");
        buildings.Tick(forge.activeTasks[0].totalDuration + .1f);
        buildings.Tick(forge.activeTasks[0].totalDuration + .1f);
        SubmitDecision(ui, "Ignorer");
        check(clock.CurrentSpeed == GameSpeed.Fast && forge.activeTasks.Count == 0,
            "Ignorer la notification reprend le temps sans nouvelle commande");
    }

    private static void VerifyJobPerformance(Action<bool, string> check, Toggle autoPause)
    {
        GameManager.Instance.StartNewGame();
        autoPause.value = false;
        var roster = AdventurerManager.Instance;
        var buildings = BuildingManager.Instance;
        var economy = EconomyManager.Instance;
        var smith = roster.GetRecruitPool()[0].candidate;
        var worker = roster.GetRecruitPool()[1].candidate;
        check(roster.HireAdventurer(smith.id) && roster.HireAdventurer(worker.id), "Deux candidats sont disponibles pour comparer les métiers");

        var forge = buildings.GetBuilding(BuildingType.Forge);
        var recipe = buildings.GetRecipesForJob(JobType.Blacksmith)[0];
        var definition = buildings.GetJobDefinition(JobType.Blacksmith);
        smith.stats.force = 5;
        smith.stats.chance = 5;
        smith.stats.vitalite = 5;
        var initial = JobPerformance.GetCrafting(smith, recipe, definition);
        float fatiguePerSecond = smith.WorkFatiguePerSecond;
        smith.stats.force = 45;
        smith.stats.chance = 45;
        smith.stats.vitalite = 45;
        var improved = JobPerformance.GetCrafting(smith, recipe, definition);
        check(improved.Duration < initial.Duration && recipe.GetQualityChances(improved.QualityBonus).Normal < recipe.GetQualityChances(initial.QualityBonus).Normal
            && smith.WorkFatiguePerSecond < fatiguePerSecond, "Force, Chance et Vitalité améliorent les performances de la Forge");
        check(buildings.AssignAdventurer(smith, BuildingType.Forge) && buildings.StartProduction(smith, recipe)
            && Mathf.Approximately(forge.activeTasks[0].totalDuration, improved.Duration),
            "La fabrication utilise la durée figurant dans l’aperçu");

        var jewelry = buildings.GetRecipesForJob(JobType.Craftsman)[0];
        var craftsman = buildings.GetJobDefinition(JobType.Craftsman);
        worker.stats.intelligence = 5;
        worker.stats.chance = 5;
        initial = JobPerformance.GetCrafting(worker, jewelry, craftsman);
        worker.stats.intelligence = 45;
        worker.stats.chance = 45;
        improved = JobPerformance.GetCrafting(worker, jewelry, craftsman);
        check(improved.Duration < initial.Duration && improved.QualityBonus > initial.QualityBonus,
            "Intelligence et Chance améliorent la fabrication des bijoux");
        var odds = jewelry.GetQualityChances(10f);
        check(odds.Normal >= 0f && odds.Rare >= 0f && odds.Epic >= 0f && odds.Legendary >= 0f
            && Mathf.Approximately(odds.Normal + odds.Rare + odds.Epic + odds.Legendary, 1f), "Les probabilités de rareté restent valides aux valeurs extrêmes");

        worker.stats.charisme = 45;
        var iron = recipe.inputResources[0].resource;
        int unassistedPrice = economy.GetSellPrice(iron, 20);
        check(buildings.AssignAdventurer(worker, BuildingType.Market) && economy.GetSellPrice(iron, 20) > unassistedPrice,
            "Le Charisme affiché augmente le prix de vente réel au Marché");

        buildings.UnassignAdventurer(worker);
        worker.stats.sagesse = 5;
        float healingBonus = JobPerformance.HealingBonus(worker);
        worker.stats.sagesse = 45;
        check(JobPerformance.HealingBonus(worker) > healingBonus, "La Sagesse accélère les soins");

        buildings.UnassignAdventurer(smith);
        smith.stats.fatigue = 100f;
        smith.stats.moral = 99f;
        smith.SetResting();
        roster.Tick(1f);
        float recoveredOutside = 100f - smith.stats.fatigue;
        smith.stats.fatigue = 100f;
        check(buildings.StartService(smith, buildings.GetBuilding(BuildingType.Tavern)), "L’aventurier peut se reposer à la taverne sans tavernier");
        buildings.Tick(1f);
        check(100f - smith.stats.fatigue > recoveredOutside, "La taverne réduit la fatigue plus vite que le repos naturel");
    }

    private static void VerifyGuildJobPerformance(Action<bool, string> check, Toggle autoPause)
    {
        GameManager.Instance.StartNewGame();
        autoPause.value = false;
        var roster = AdventurerManager.Instance;
        var buildings = BuildingManager.Instance;
        var worker = roster.GetRecruitPool()[0].candidate;
        var guest = roster.GetRecruitPool()[1].candidate;
        check(roster.HireAdventurer(worker.id) && roster.HireAdventurer(guest.id)
            && buildings.AssignAdventurer(worker, BuildingType.Recruiter), "Un recruteur est affecté pour vérifier les nouvelles candidatures");
        worker.stats.charisme = 5;
        worker.stats.chance = 5;
        float refresh = roster.RecruitRefreshRemaining;
        float trained = roster.TrainedRecruitChance;
        worker.stats.charisme = 45;
        check(roster.RecruitRefreshRemaining < refresh && roster.TrainedRecruitChance == trained,
            "Le Charisme accélère le renouvellement des candidatures");
        worker.stats.chance = 45;
        check(roster.TrainedRecruitChance > trained, "La Chance augmente la probabilité d’une recrue expérimentée");
        var oldOffer = roster.GetRecruitPool()[0];
        roster.Tick(roster.RecruitRefreshRemaining + .1f);
        check(!roster.GetRecruitPool().Contains(oldOffer), "La liste se renouvelle selon le délai affiché");

        buildings.UnassignAdventurer(worker);
        check(buildings.AssignAdventurer(worker, BuildingType.Headquarters), "Le même aventurier peut passer au quartier général");
        worker.stats.agilite = 5;
        worker.stats.chance = 5;
        float interval = buildings.MissionInterval;
        float prestigeChance = buildings.MissionPrestigiousChance;
        worker.stats.agilite = 45;
        check(buildings.MissionInterval < interval && buildings.MissionPrestigiousChance == prestigeChance,
            "L’Agilité accélère la recherche de contrats");
        worker.stats.chance = 45;
        check(buildings.MissionPrestigiousChance > prestigeChance, "La Chance augmente la probabilité d’un contrat prestigieux");

        buildings.UnassignAdventurer(worker);
        check(buildings.AssignAdventurer(worker, BuildingType.TrainingRoom)
            && buildings.StartService(guest, buildings.GetBuilding(BuildingType.TrainingRoom)), "Une séance est prête à comparer la vitesse de l’entraîneur");
        worker.stats.resistance = 5;
        buildings.Tick(.5f);
        float progress = buildings.ServiceProgress(guest);
        worker.stats.resistance = 45;
        buildings.Tick(.5f);
        check(buildings.ServiceProgress(guest) - progress > progress, "La Résistance accélère réellement la séance");

        buildings.StopService(guest);
        buildings.UnassignAdventurer(worker);
        guest.stats.fatigue = 100f;
        guest.stats.moral = 0f;
        check(buildings.AssignAdventurer(worker, BuildingType.Tavern)
            && buildings.StartService(guest, buildings.GetBuilding(BuildingType.Tavern)), "Le tavernier accueille un aventurier fatigué");
        worker.stats.sagesse = 5;
        buildings.Tick(.5f);
        float fatigueRecovered = 100f - guest.stats.fatigue;
        float moraleRecovered = guest.stats.moral;
        guest.stats.fatigue = 100f;
        guest.stats.moral = 0f;
        worker.stats.sagesse = 45;
        buildings.Tick(.5f);
        check(100f - guest.stats.fatigue > fatigueRecovered && guest.stats.moral > moraleRecovered,
            "La Sagesse du tavernier accélère la récupération de la fatigue et du moral");
    }

    private static void VerifyRacialJobEffects(Action<bool, string> check)
    {
        var roster = AdventurerManager.Instance;
        var buildings = BuildingManager.Instance;
        var dwarf = roster.GetRaceById("Race_Dwarf");
        var human = roster.GetRaceById("Race_Human");
        check(dwarf != null && human != null, "Les races du prototype sont configurées pour l’aperçu de métier");
        var variants = new (string Id, Race Family)[]
        {
            ("Race_Dwarf_Hill", Race.Dwarf), ("Race_Elf_Wood", Race.Elf), ("Race_Elf_High", Race.Elf),
            ("Race_Elf_Drow", Race.Elf), ("Race_Gnome_Rock", Race.Gnome), ("Race_Gnome_Forest", Race.Gnome),
            ("Race_Halfling_Lightfoot", Race.Halfling), ("Race_Halfling_Stout", Race.Halfling),
            ("Race_HalfElf", Race.HalfElf), ("Race_Orc", Race.Orc), ("Race_Tiefling", Race.Tiefling)
        };
        foreach (var (id, family) in variants)
        {
            var race = roster.GetRaceById(id);
            check(race != null && race.raceType == family && race.jobEffects.Count > 0 && race.statModifiers.Count == 0,
                $"Recrutement · {id} est disponible avec des effets de métier");
        }
        var worker = new Adventurer("Aperçu", null, dwarf, null);
        worker.stats.force = 17;
        worker.GetJobInstance(JobType.Blacksmith).currentLevel = 2;
        var definition = buildings.GetJobDefinition(JobType.Blacksmith);
        var effect = JobPerformance.GetEffect(worker, JobEffectType.ForgeDurationReduction, definition);
        check(effect.StatValue == 17 && Mathf.Approximately(effect.StatBonus, .17f) && Mathf.Approximately(effect.RaceBonus, .10f)
            && Mathf.Approximately(effect.LevelBonus, worker.GetJobInstance(JobType.Blacksmith).GetEfficiencyBonus(definition)),
            "L’aperçu distingue la Force, le bonus racial direct et le niveau de métier");
        var recipe = buildings.GetRecipesForJob(JobType.Blacksmith)[0];
        var crafting = JobPerformance.GetCrafting(worker, recipe, definition);
        check(Mathf.Approximately(crafting.Duration, recipe.GetActualDuration(effect.Total)) && worker.stats.force == 17,
            "La production utilise l’effet affiché et la Force reste inchangée");
        worker.adventurerRace = human;
        var humanEffect = JobPerformance.GetEffect(worker, JobEffectType.ForgeDurationReduction, definition);
        check(Mathf.Approximately(humanEffect.StatBonus, effect.StatBonus) && humanEffect.Total < effect.Total
            && Mathf.Approximately(JobPerformance.GetEffect(worker, JobEffectType.RecruitmentSpeed).RaceBonus, .04f),
            "L’Humain a un bonus polyvalent indépendant des statistiques");
        worker.adventurerRace = roster.GetRaceById("Race_Elf_Wood");
        check(Mathf.Approximately(JobPerformance.GetEffect(worker, JobEffectType.ScoutingSpeed).RaceBonus, .10f)
            && Mathf.Approximately(JobPerformance.GetEffect(worker, JobEffectType.ScoutingQuality).RaceBonus, .02f)
            && JobPerformance.GetEffect(worker, JobEffectType.ForgeDurationReduction).RaceBonus == 0f,
            "Les bonus de l’Elfe des bois s’appliquent au quartier général");
        worker.adventurerRace = roster.GetRaceById("Race_Halfling_Stout");
        check(Mathf.Approximately(JobPerformance.GetEffect(worker, JobEffectType.WorkFatigue).RaceBonus, -.18f),
            "Le Halfelin robuste se fatigue moins au travail");
    }

    private static Button GetDecisionButton(GuildWorkshopUI ui, string label)
    {
        typeof(GuildWorkshopUI).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
        return ui.Root.Q("status-actions").Query<Button>().ToList().Find(button => button.text == label);
    }

    private static void SubmitDecision(GuildWorkshopUI ui, string label)
    {
        var button = GetDecisionButton(ui, label);
        if (button?.enabledInHierarchy != true) throw new InvalidOperationException($"Action du journal indisponible · {label}");
        using var submit = NavigationSubmitEvent.GetPooled();
        submit.target = button;
        button.SendEvent(submit);
    }

    private static bool VerifyPauseShortcut(TimeManager clock)
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        clock.enabled = true;
        try
        {
            clock.SetSpeed(GameSpeed.Fast);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            if (clock.CurrentSpeed != GameSpeed.Paused) return false;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            return clock.CurrentSpeed == GameSpeed.Fast;
        }
        finally
        {
            clock.enabled = false;
            InputSystem.RemoveDevice(keyboard);
        }
    }
}
