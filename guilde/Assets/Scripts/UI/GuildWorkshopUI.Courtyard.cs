using System.Collections.Generic;
using UnityEngine.UIElements;

public partial class GuildWorkshopUI
{
    private readonly Dictionary<BuildingType, (Button Button, Label Title, Label Staff)> _courtyardTiles = new();

    private void BuildCourtyard()
    {
        var grid = _root.Q("map-container");
        grid.Clear();
        _courtyardTiles.Clear();
        foreach (var type in BuildingManager.GuildBuildings)
        {
            var tile = new Button(() => ShowBuilding(type)) { name = "building-" + type };
            tile.AddToClassList("guild-building");
            if (type == BuildingType.Headquarters) tile.AddToClassList("headquarters");

            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("building-icon");
            icon.AddToClassList($"building-icon--{type}");
            tile.Add(icon);
            var title = Text(tile, "", "building-title");
            var staff = Text(tile, "", "building-occupancy");
            title.pickingMode = PickingMode.Ignore;
            staff.pickingMode = PickingMode.Ignore;
            grid.Add(tile);
            _courtyardTiles.Add(type, (tile, title, staff));
        }

        _forge = _courtyardTiles[BuildingType.Forge].Button;
        _market = _courtyardTiles[BuildingType.Market].Button;
        _forge.name = "forge-drop";
        _market.name = "market-drop";
    }

    private void RefreshCourtyard()
    {
        foreach (var entry in _courtyardTiles)
        {
            var building = buildings.GetBuilding(entry.Key);
            var tile = entry.Value;
            tile.Button.SetEnabled(building != null);
            if (building == null)
            {
                tile.Title.text = entry.Key.ToString();
                tile.Staff.text = "Indisponible";
                continue;
            }

            tile.Title.text = building.definition.buildingName;
            tile.Staff.text = $"Niv. {building.currentLevel} · {building.assignedAdventurerIds.Count}/{building.MaxWorkers} en poste";
        }
        _root.Q<Label>("roster-count").text = $"{buildings.RosterOccupancy}/{buildings.RosterCapacity}";
    }

    private void RenderGuildBuilding(Building b)
    {
        Text(_content, b.definition.description, "card-meta");
        var sections = new VisualElement();
        sections.AddToClassList("service-columns");
        _content.Add(sections);
        var staff = new VisualElement();
        staff.AddToClassList("service-column");
        sections.Add(staff);
        RenderStaff(staff, b);
        var selected = adventurers.GetById(_selectedId);

        if (BuildingManager.HasService(b.buildingType))
        {
            var service = new VisualElement();
            service.AddToClassList("service-column");
            sections.Add(service);
            RenderService(service, b, selected);
        }
        if (b.buildingType == BuildingType.Headquarters) RenderMissions(b);
        RenderGuildUpgrade(b);
    }

    private void RenderTradeStaff(BuildingType type)
    {
        var b = buildings.GetBuilding(type);
        if (b == null) return;
        var panel = new VisualElement { name = "trade-staff" };
        panel.AddToClassList("service-column");
        _content.Add(panel);
        RenderStaff(panel, b);
    }

    private void RenderStaff(VisualElement panel, Building b)
    {
        var job = b.definition.associatedJobType;
        var definition = buildings.GetJobDefinition(job);
        Text(panel, $"POSTES DE TRAVAIL · {b.assignedAdventurerIds.Count}/{b.MaxWorkers}", "subheading");
        Text(panel, $"Métier · {JobLabel(job)}", "tag");
        foreach (string id in b.assignedAdventurerIds)
        {
            var worker = adventurers.GetById(id);
            if (worker == null) continue;
            var card = Card(panel, worker.adventurerName);
            Text(card, $"Niv. {worker.GetJobInstance(job)?.currentLevel ?? 0}", "card-meta");
            RenderJobPerformance(card, worker, job, b);
            ActionButton(card, "Libérer le poste", () => buildings.UnassignAdventurer(worker)).name = "release-" + id;
        }

        if (!b.IsActive) return;
        var selected = adventurers.GetById(_selectedId);
        if (selected?.IsAvailable != true)
        {
            if (b.HasFreeSlot) Text(panel, "Cliquez sur le nom d’un aventurier disponible à droite pour le sélectionner.", "card-meta");
            return;
        }

        Text(panel, $"Aperçu · {selected.adventurerName}", "tag");
        RenderJobPerformance(panel, selected, job, b);
        if (!b.HasFreeSlot) return;

        int salary = selected.CalculateSalary(definition);
        ActionButton(panel, $"Affecter {selected.adventurerName} · {salary} or",
            () => AssignToBuilding(selected.id, b.buildingType), true).name = "assign-" + b.buildingType;
    }

    private void RenderService(VisualElement service, Building b, Adventurer selected)
    {
        var title = b.buildingType switch
        {
            BuildingType.Infirmary => "PATIENTS",
            BuildingType.Tavern => "AVENTURIERS AU REPOS",
            _ => "AVENTURIERS À L’ENTRAÎNEMENT"
        };
        Text(service, title, "subheading");
        int count = 0;
        foreach (var a in adventurers.GetAllAdventurers())
        {
            if (a.currentService != b.buildingType) continue;
            count++;
            var card = Card(service, a.adventurerName);
            var status = Text(card, "", "card-meta");
            _liveUpdates.Add(() => status.text = $"Fatigue {a.stats.fatigue:0} · Moral {a.stats.moral:0} · XP {a.stats.experience}/{a.stats.experienceToNextLevel}");
            if (b.buildingType == BuildingType.TrainingRoom)
            {
                var progress = new ProgressBar { lowValue = 0, highValue = 100 };
                progress.AddToClassList("task-progress");
                card.Add(progress);
                _liveUpdates.Add(() =>
                {
                    progress.value = buildings.ServiceProgress(a) * 100f;
                    progress.title = $"Séance · {progress.value:0} %";
                });
            }
            ActionButton(card, "Quitter le service", () => buildings.StopService(a));
        }

        Text(service, $"{buildings.ServiceOccupancy(b.buildingType)}/{b.currentLevel} places de service occupées", "tag");
        if (b.buildingType == BuildingType.TrainingRoom && b.assignedAdventurerIds.Count == 0)
            Text(service, "Un entraîneur doit être en poste pour que les séances avancent.", "empty");
        if (buildings.CanStartService(selected, b))
        {
            var action = b.buildingType switch
            {
                BuildingType.Infirmary => "Soigner",
                BuildingType.Tavern => "Faire reposer",
                _ => "Entraîner"
            };
            ActionButton(service, $"{action} {selected.adventurerName}", () => buildings.StartService(selected, b), true);
        }
    }

    private void RenderMissions(Building b)
    {
        Subheading($"EFFECTIF DE LA GUILDE · {buildings.RosterOccupancy}/{buildings.RosterCapacity}");
        Subheading("TABLEAU DES MISSIONS");
        Text(_content, $"{buildings.MissionOffers.Count} offres · capacité de recherche {buildings.MissionCapacity}", "tag");
        Text(_content, $"Une recherche toutes les {buildings.MissionInterval:0.#} s de jeu · Contrat prestigieux {buildings.MissionPrestigiousChance:P1}", "card-meta");
        var progress = new ProgressBar { lowValue = 0, highValue = 100 };
        progress.AddToClassList("task-progress");
        _content.Add(progress);
        _liveUpdates.Add(() =>
        {
            progress.value = buildings.DiscoveryProgress * 100f;
            progress.title = !b.IsActive ? "Recherche en pause pendant les travaux"
                : buildings.MissionOffers.Count >= buildings.MissionCapacity ? "Tableau des missions plein"
                : $"Recherche de missions · {progress.value:0} %";
        });
        foreach (string offer in buildings.MissionOffers)
        {
            var card = Card(_content, offer);
            ActionButton(card, "Archiver", () => buildings.ArchiveMission(offer));
        }
    }

    private void RenderGuildUpgrade(Building b)
    {
        Subheading($"BÂTIMENT · NIVEAU {b.currentLevel}/{b.definition.maxLevel}");
        if (b.state == BuildingState.Upgrading)
        {
            var remaining = Text(_content, "", "card-meta");
            _liveUpdates.Add(() => remaining.text = $"Travaux en cours · {b.upgradeTimeRemaining:0} s");
        }
        else if (b.CanUpgrade())
        {
            var cost = b.GetNextLevelData();
            Text(_content, $"{cost.goldCost} or · {Costs(cost.resourceCosts)} · {cost.upgradeTime:0} s · {cost.maxWorkerSlots} postes au niveau suivant", "card-meta");
            var button = ActionButton(_content, "Améliorer le bâtiment", () =>
            {
                if (buildings.UpgradeBuilding(b.buildingType)) Notify("Les travaux ont commencé.");
            });
            button.SetEnabled(economy.CurrentGold >= cost.goldCost && inventory.HasResources(cost.resourceCosts));
            if (b.buildingType == BuildingType.Headquarters)
                Text(_content, $"Niveau suivant · {cost.maxWorkerSlots} postes et {buildings.RosterCapacity + 3} aventuriers maximum", "card-meta");
        }
    }

    private void RenderJewelryStock(VisualElement stock)
    {
        Text(stock, "BIJOUX EN RÉSERVE", "subheading");
        foreach (var item in equipment.GetStoredEquipment())
        {
            if (item.definition?.slot is EquipmentSlot.Bracelet or EquipmentSlot.Necklace or EquipmentSlot.Ring)
                Text(stock, $"{item.definition.itemName} · {QualityLabel(item.quality)}", "card-meta");
        }
    }
}
