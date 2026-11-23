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
        Text(panel, $"POSTES DE TRAVAIL · {b.assignedAdventurerIds.Count}/{b.MaxWorkers}", "subheading");
        Text(panel, $"Métier · {JobLabel(job)}", "tag");
        foreach (string id in b.assignedAdventurerIds)
        {
            var worker = adventurers.GetById(id);
            if (worker == null) continue;
            var card = Card(panel, worker.adventurerName);
            Text(card, $"Niv. {worker.GetJobInstance(job)?.currentLevel ?? 0}", "card-meta");
            RenderJobPerformance(card, worker, job, b);
            if (job == JobType.Recruiter)
            {
                var search = Text(card, "", "card-meta");
                _liveUpdates.Add(() => search.text = !b.IsActive ? "Prospection en pause pendant les travaux"
                    : adventurers.GetSourcedOfferCount(worker) >= AdventurerManager.SourcedOffersPerRecruiter
                        ? "Offres trouvées en attente de décision"
                        : $"Prochaine prospection · {adventurers.GetRecruitSearchRemaining(worker):0.#} h");
            }
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

        ActionButton(panel, $"Affecter {selected.adventurerName}",
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
            _liveUpdates.Add(() => status.text = b.buildingType == BuildingType.Infirmary
                ? $"PV · {a.HealthFraction:P0}" : $"Fatigue {a.stats.fatigue:0} · Moral {a.stats.moral:0} · XP {a.stats.experience}/{a.stats.experienceToNextLevel}");
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
        if (selected == null)
        {
            Text(service, "Sélectionnez un aventurier pour voir le prix de la prise en charge.", "card-meta");
            return;
        }

        string action = b.buildingType switch
        {
            BuildingType.Infirmary => "Soigner",
            BuildingType.Tavern => "Payer le repas et le repos de",
            _ => "Entraîner"
        };
        var price = Text(service, "", "card-meta");
        var start = ActionButton(service, "", () =>
        {
            if (!buildings.StartService(selected, b))
                Notify(buildings.GetServiceBlockedReason(selected, b) ?? "Service indisponible.");
        }, true);
        void RefreshPrice()
        {
            int total = buildings.GetServicePrice(selected, b, out int commission);
            start.text = $"{action} {selected.adventurerName} · {total} or";
            string reason = buildings.GetServiceBlockedReason(selected, b);
            string breakdown = commission == 0 ? "Tarif du service" : $"Tarif {total - commission} or · Commission {commission} or";
            price.text = reason ?? breakdown;
            start.SetEnabled(reason == null);
        }
        RefreshPrice();
        _liveUpdates.Add(RefreshPrice);
    }

    private void RenderMissions(Building b)
    {
        Subheading($"EFFECTIF DE LA GUILDE · {buildings.RosterOccupancy}/{buildings.RosterCapacity}");
        Text(_content, $"Prochaine paie mensuelle · {economy.MonthlyPayroll} or", "card-meta");
        Subheading($"REQUÊTES DES VILLAGEOIS · {buildings.MissionCount(false)}/{buildings.VillageRequestCapacity}");
        var progress = new ProgressBar { lowValue = 0, highValue = 100 };
        progress.AddToClassList("task-progress");
        _content.Add(progress);
        _liveUpdates.Add(() =>
        {
            progress.value = buildings.RequestProgress * 100f;
            progress.title = !b.IsActive ? "Requêtes en pause pendant les travaux"
                : buildings.MissionCount(false) >= buildings.VillageRequestCapacity ? "Tableau des requêtes plein"
                : $"Prochaine requête · {(1f - buildings.RequestProgress) * buildings.VillageRequestInterval:0.#} h";
        });
        RenderMissionOffers(false);

        Subheading($"DÉCOUVERTES DES ÉCLAIREURS · {buildings.MissionCount(true)}/{buildings.ScoutedMissionCapacity}");
        if (b.assignedAdventurerIds.Count == 0)
            Text(_content, "Affectez un éclaireur pour découvrir des missions supplémentaires.", "card-meta");
        foreach (string id in b.assignedAdventurerIds)
        {
            var scout = adventurers.GetById(id);
            if (scout == null) continue;
            var deadline = Text(_content, "", "card-meta");
            _liveUpdates.Add(() => deadline.text = !b.IsActive ? $"{scout.adventurerName} · recherche en pause"
                : buildings.MissionCount(true) >= buildings.ScoutedMissionCapacity ? $"{scout.adventurerName} · tableau plein"
                : $"{scout.adventurerName} · prochaine découverte dans {buildings.ScoutHoursRemaining(scout):0.#} h");
        }
        RenderMissionOffers(true);
    }

    private void RenderMissionOffers(bool scouted)
    {
        foreach (var offer in buildings.MissionOffers)
        {
            if (offer.IsScouted != scouted) continue;
            var card = Card(_content, offer.Title);
            Text(card, scouted ? $"Niv. {offer.Level} · Trouvée par {offer.ScoutName}" : $"Niv. {offer.Level}", "card-meta");
            ActionButton(card, "Archiver", () => buildings.ArchiveMission(offer));
        }
    }

    private void RenderGuildUpgrade(Building b)
    {
        Subheading($"BÂTIMENT · NIVEAU {b.currentLevel}/{b.definition.maxLevel}");
        if (b.state == BuildingState.Upgrading)
        {
            var remaining = Text(_content, "", "card-meta");
            _liveUpdates.Add(() => remaining.text = $"Travaux en cours · {b.upgradeTimeRemaining:0.#} h");
        }
        else if (b.CanUpgrade())
        {
            var cost = b.GetNextLevelData();
            Text(_content, $"{cost.goldCost} or · {Costs(cost.resourceCosts)} · {cost.upgradeTime:0.#} h · {cost.maxWorkerSlots} postes au niveau suivant", "card-meta");
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
