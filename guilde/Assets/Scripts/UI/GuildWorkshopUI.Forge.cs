using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GuildWorkshopUI
{
    private static readonly (EquipmentSlot Slot, string Label)[] ForgeCategories =
    {
        (EquipmentSlot.MainWeapon, "Armes"), (EquipmentSlot.Armor, "Armures"),
        (EquipmentSlot.Helmet, "Casques"), (EquipmentSlot.Gloves, "Gants"), (EquipmentSlot.Boots, "Bottes")
    };
    private readonly Dictionary<CraftRecipeSO, (string WorkerId, ForgeTechnique Technique)> _forgeSelections = new();
    private readonly Dictionary<VisualElement, Action<Adventurer>> _forgeWorkerDrops = new();
    private EquipmentSlot? _forgeFilter;
    private bool _forgeWorkerDragging;

    private void RenderForge(Building building)
    {
        _forgeWorkerDrops.Clear();
        var columns = new VisualElement();
        columns.AddToClassList("forge-columns");
        _content.Add(columns);

        var staff = new VisualElement();
        staff.AddToClassList("forge-staff");
        columns.Add(staff);
        Text(staff, $"FORGERONS · {building.assignedAdventurerIds.Count}/{building.MaxWorkers}", "subheading");
        var workers = new List<Adventurer>();
        foreach (string id in building.assignedAdventurerIds)
        {
            var worker = adventurers.GetById(id);
            if (worker == null) continue;
            workers.Add(worker);
            var card = new VisualElement();
            card.AddToClassList("card");
            staff.Add(card);
            var handle = new VisualElement();
            handle.AddToClassList("forge-worker-handle");
            handle.tooltip = "Glisser cet aventurier vers le choix de forgeron d’une recette.";
            card.Add(handle);
            Text(handle, worker.adventurerName, "profile-emphasis");
            var activity = Text(handle, "", "card-meta");
            void RefreshActivity()
            {
                var task = buildings.GetTaskForWorker(id);
                string status = task == null ? "Disponible" : !building.IsActive ? "Fabrication suspendue"
                    : task.awaitingInputs ? "En attente" : "Fabrication en cours";
                activity.text = $"Forgeron niv. {worker.GetJobInstance(JobType.Blacksmith)?.currentLevel ?? 0} · {status}";
            }
            RefreshActivity();
            _liveUpdates.Add(RefreshActivity);
            BindForgeWorkerDrag(handle, worker);
            ActionButton(card, "Libérer le poste", () => buildings.UnassignAdventurer(worker)).name = "release-" + id;
        }

        var selected = adventurers.GetById(_selectedId);
        if (building.HasFreeSlot && building.IsActive && selected?.IsAvailable == true)
            ActionButton(staff, $"Affecter {selected.adventurerName}", () => AssignToBuilding(selected.id, BuildingType.Forge));
        else if (workers.Count == 0)
            Text(staff, "Sélectionnez un aventurier disponible pour l’affecter.", "card-meta");
        RenderProductionStock(staff);

        var catalogue = new VisualElement();
        catalogue.AddToClassList("forge-catalogue");
        columns.Add(catalogue);
        RenderProductionOrders(catalogue, building);
        var toolbar = new VisualElement();
        toolbar.AddToClassList("row");
        toolbar.AddToClassList("forge-toolbar");
        catalogue.Add(toolbar);
        Text(toolbar, "CARNET DE RECETTES", "subheading");
        AddForgeFilter(toolbar, null, "Toutes les recettes");
        foreach (var category in ForgeCategories)
            AddForgeFilter(toolbar, category.Slot, category.Label);

        int shown = 0;
        foreach (var recipe in buildings.GetRecipesForJob(JobType.Blacksmith))
        {
            if (!MatchesForgeCategory(recipe, _forgeFilter)) continue;
            RenderForgeRecipe(catalogue, building, recipe, workers);
            shown++;
        }
        if (shown == 0) Text(catalogue, "Aucune recette dans cette catégorie.", "empty");
        _equipmentPanel.RenderRepairs(_content);
        RenderGuildUpgrade(building);
    }

    private static string ForgeTechniqueLabel(ForgeTechnique technique) => technique switch
    {
        ForgeTechnique.Refined => "Rare",
        ForgeTechnique.Masterwork => "Épique",
        _ => "Normale"
    };

    private static bool MatchesForgeCategory(CraftRecipeSO recipe, EquipmentSlot? category)
        => category == null || recipe.outputEquipment?.slot == category
            || (category == EquipmentSlot.MainWeapon && recipe.outputEquipment?.slot == EquipmentSlot.OffHand);

    private void AddForgeFilter(VisualElement toolbar, EquipmentSlot? slot, string title)
    {
        var button = ActionButton(toolbar, slot.HasValue ? "" : "Tout", () =>
        {
            _forgeFilter = slot;
            MarkDirty();
        });
        button.name = "forge-filter-" + (slot?.ToString() ?? "all");
        button.tooltip = title;
        button.EnableInClassList("forge-choice-active", _forgeFilter == slot);
        if (!slot.HasValue) return;

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        button.AddToClassList("forge-filter-icon");
        icon.AddToClassList("forge-glyph");
        var shape = slot.Value;
        icon.generateVisualContent += context => DrawForgeGlyph(context.painter2D, shape);
        button.Add(icon);
    }

    private void RenderForgeRecipe(VisualElement parent, Building building, CraftRecipeSO recipe, List<Adventurer> workers)
    {
        var card = Card(parent, recipe.recipeName);
        card.AddToClassList("forge-recipe");
        RevealRecipe(card, recipe);
        _forgeSelections.TryGetValue(recipe, out var selection);
        var worker = workers.Find(candidate => candidate.id == selection.WorkerId);
        var technique = selection.Technique;

        var odds = new VisualElement();
        odds.AddToClassList("row");
        odds.AddToClassList("forge-odds");
        card.Add(odds);
        var common = Text(odds, "", "forge-odd");
        var rare = Text(odds, "", "forge-odd");
        var epic = Text(odds, "", "forge-odd");
        var legendary = Text(odds, "", "forge-odd");
        rare.AddToClassList("rare"); epic.AddToClassList("epic"); legendary.AddToClassList("legendary");

        var columns = new VisualElement();
        columns.AddToClassList("row");
        columns.AddToClassList("forge-recipe-details");
        card.Add(columns);
        var workerBox = new VisualElement();
        workerBox.AddToClassList("forge-worker-slot");
        columns.Add(workerBox);

        var choices = new List<string> { "Choisir un forgeron" };
        foreach (var candidate in workers)
        {
            string label = candidate.adventurerName;
            int duplicate = 1;
            while (choices.Contains(label)) label = $"{candidate.adventurerName} ({++duplicate})";
            choices.Add(label);
        }
        var picker = new DropdownField(choices, worker == null ? 0 : workers.IndexOf(worker) + 1)
        { name = "forge-worker-" + recipe.name };
        workerBox.Add(picker);
        picker.SetEnabled(workers.Count > 0);
        var bonuses = Text(workerBox, "", "card-meta");

        var costs = new VisualElement();
        costs.AddToClassList("forge-recipe-costs");
        columns.Add(costs);
        Text(costs, "PAR PIÈCE", "subheading");
        var ingredients = Text(costs, "", "card-meta");
        var duration = Text(costs, "", "card-meta");
        var commission = Text(costs, "", "card-meta");

        var modes = new VisualElement();
        modes.AddToClassList("row");
        modes.AddToClassList("forge-modes");
        card.Add(modes);
        var warning = Text(card, "", "card-meta");
        var updateControls = RenderCraftControls(card, recipe, quantity => StartCraft(building, recipe, quantity, worker, technique));
        var modesByTechnique = new Dictionary<ForgeTechnique, Button>();
        foreach (ForgeTechnique mode in Enum.GetValues(typeof(ForgeTechnique)))
        {
            if (!recipe.SupportsTechnique(mode)) continue;
            var button = ActionButton(modes, ForgeTechniqueLabel(mode), () =>
            {
                technique = mode;
                RefreshPreview();
            });
            button.name = $"forge-technique-{recipe.name}-{mode}";
            modesByTechnique.Add(mode, button);
        }

        void RefreshPreview()
        {
            var job = worker?.GetJobInstance(JobType.Blacksmith);
            int level = job?.currentLevel ?? -1;
            if (!recipe.SupportsTechnique(technique) || (technique != ForgeTechnique.Standard && level < recipe.GetRequiredJobLevel(technique)))
                technique = ForgeTechnique.Standard;
            _forgeSelections[recipe] = (worker?.id, technique);

            foreach (var entry in modesByTechnique)
            {
                int required = recipe.GetRequiredJobLevel(entry.Key);
                bool unlocked = entry.Key == ForgeTechnique.Standard || level >= required;
                entry.Value.SetEnabled(unlocked);
                entry.Value.EnableInClassList("forge-choice-active", entry.Key == technique);
                entry.Value.text = unlocked ? ForgeTechniqueLabel(entry.Key) : $"{ForgeTechniqueLabel(entry.Key)} · niv. {required}";
                entry.Value.tooltip = unlocked ? null : $"Forgeron de niveau {required} requis.";
            }

            var (hours, bonus) = worker == null ? (recipe.GetActualDuration(0f, technique), 0f)
                : JobPerformance.GetCrafting(worker, recipe, buildings.GetJobDefinition(JobType.Blacksmith), technique);
            var chances = recipe.GetQualityChances(bonus, technique);
            odds.style.display = recipe.ProducesEquipment ? DisplayStyle.Flex : DisplayStyle.None;
            common.text = $"Commun {chances.Normal:P1}";
            rare.text = $"Rare {chances.Rare:P1}";
            epic.text = $"Épique {chances.Epic:P1}";
            legendary.text = $"Légendaire {chances.Legendary:P1}";

            if (worker == null)
                bonuses.text = "Glissez un aventurier ici ou choisissez-le dans la liste.";
            else
            {
                var efficiency = JobPerformance.GetEffect(worker, JobEffectType.ForgeDurationReduction,
                    buildings.GetJobDefinition(JobType.Blacksmith));
                bonuses.text = $"Niv. {level} · Vitesse {1f / (1f - efficiency.Total):P0}\nBonus de rareté +{bonus * 100f:0.#} pts";
            }
            ingredients.text = Costs(recipe.GetInputs(technique));
            float buildingSpeed = building.ProductionSpeedMultiplier;
            duration.text = buildingSpeed > 0f ? $"Durée estimée · {hours / buildingSpeed:0.##} h" : "Production suspendue";
            commission.text = worker == null ? "Commission · —" : $"Commission · {ForgeCommissionPreview(recipe, worker, chances)}";
            string reason = buildings.GetProductionStartIssue(worker, building, recipe, technique);
            warning.text = reason ?? "";
            warning.style.display = reason == null ? DisplayStyle.None : DisplayStyle.Flex;
            updateControls(reason);
        }

        picker.RegisterValueChangedCallback(change =>
        {
            int index = choices.IndexOf(change.newValue);
            worker = index > 0 ? workers[index - 1] : null;
            RefreshPreview();
        });
        _forgeWorkerDrops.Add(workerBox, candidate =>
        {
            int index = workers.IndexOf(candidate);
            if (index < 0) return;
            worker = candidate;
            picker.SetValueWithoutNotify(choices[index + 1]);
            RefreshPreview();
        });
        RefreshPreview();
    }

    private static string ForgeCommissionPreview(CraftRecipeSO recipe, Adventurer worker,
        (float Normal, float Rare, float Epic, float Legendary) chances)
    {
        if (!recipe.ProducesEquipment)
        {
            int amount = Mathf.RoundToInt(recipe.outputResourceAmount
                * (worker.GetJobInstance(JobType.Blacksmith)?.GetOutputMultiplier(
                    BuildingManager.Instance.GetJobDefinition(JobType.Blacksmith)) ?? 1f));
            return recipe.outputResource == null ? "—" : $"{worker.CalculateCommission(amount * recipe.outputResource.baseMarketValue, JobType.Blacksmith)} or";
        }

        int min = int.MaxValue, max = 0;
        void Include(EquipmentQuality quality, float probability)
        {
            if (probability <= 0f) return;
            int cost = worker.CalculateCommission(recipe.outputEquipment.GetGoldValue(quality), JobType.Blacksmith);
            min = Mathf.Min(min, cost);
            max = Mathf.Max(max, cost);
        }
        Include(EquipmentQuality.Normal, chances.Normal);
        Include(EquipmentQuality.Rare, chances.Rare);
        Include(EquipmentQuality.Epic, chances.Epic);
        Include(EquipmentQuality.Legendary, chances.Legendary);
        return min == max ? $"{min} or" : $"{min}–{max} or selon la qualité";
    }

    private void BindForgeWorkerDrag(VisualElement handle, Adventurer worker)
    {
        int pointer = -1;
        Vector2 origin = default;
        bool moved = false;
        VisualElement hovered = null;
        var ghost = _root.Q<Label>("drag-ghost");

        void Cleanup()
        {
            if (pointer < 0) return;
            int captured = pointer;
            pointer = -1;
            _forgeWorkerDragging = false;
            if (handle.HasPointerCapture(captured)) handle.ReleasePointer(captured);
            if (moved) ghost.style.display = DisplayStyle.None;
            moved = false;
            hovered?.RemoveFromClassList("forge-worker-drop");
            hovered = null;
        }
        handle.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || _forgeWorkerDragging) return;
            pointer = evt.pointerId;
            origin = evt.position;
            _forgeWorkerDragging = true;
            handle.CapturePointer(pointer);
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (evt.pointerId != pointer) return;
            Vector2 position = evt.position;
            if (!moved && Vector2.Distance(origin, position) < 8f) return;
            moved = true;
            ghost.text = worker.adventurerName;
            ghost.style.display = DisplayStyle.Flex;
            ghost.BringToFront();
            Vector2 local = _root.WorldToLocal(position);
            ghost.style.left = local.x + 16;
            ghost.style.top = local.y + 12;

            var target = _root.panel.Pick(position);
            while (target != null && !_forgeWorkerDrops.ContainsKey(target)) target = target.parent;
            if (hovered != target)
            {
                hovered?.RemoveFromClassList("forge-worker-drop");
                hovered = target;
                hovered?.AddToClassList("forge-worker-drop");
            }
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.pointerId != pointer) return;
            if (moved && hovered != null && _forgeWorkerDrops.TryGetValue(hovered, out var drop)) drop(worker);
            Cleanup();
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerCaptureOutEvent>(evt => { if (evt.pointerId == pointer) Cleanup(); });
        handle.RegisterCallback<DetachFromPanelEvent>(_ => Cleanup());
    }

    private static void DrawForgeGlyph(Painter2D pen, EquipmentSlot slot)
    {
        pen.strokeColor = new Color32(102, 57, 24, 255);
        pen.fillColor = new Color32(217, 160, 80, 255);
        pen.lineWidth = 1.4f;

        void Shape(params Vector2[] points)
        {
            pen.BeginPath();
            pen.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) pen.LineTo(points[i]);
            pen.ClosePath();
            pen.Fill();
            pen.Stroke();
        }

        switch (slot)
        {
            case EquipmentSlot.MainWeapon:
                Shape(new(5, 20), new(10, 14), new(8, 12), new(10, 10), new(12, 12),
                    new(18, 4), new(22, 3), new(21, 8), new(15, 15), new(17, 17), new(15, 19), new(13, 17), new(8, 23));
                break;
            case EquipmentSlot.Armor:
                Shape(new(9, 4), new(11, 8), new(15, 8), new(17, 4), new(22, 7), new(24, 13),
                    new(19, 15), new(19, 22), new(7, 22), new(7, 15), new(2, 13), new(4, 7));
                break;
            case EquipmentSlot.Helmet:
                Shape(new(4, 21), new(4, 10), new(8, 5), new(13, 3), new(18, 5), new(22, 10),
                    new(22, 21), new(15, 21), new(15, 13), new(8, 13), new(8, 21));
                break;
            case EquipmentSlot.Gloves:
                Shape(new(8, 23), new(7, 17), new(3, 13), new(5, 10), new(8, 12), new(8, 5),
                    new(11, 5), new(11, 10), new(12, 3), new(15, 3), new(15, 10), new(17, 5),
                    new(20, 6), new(20, 12), new(22, 10), new(23, 12), new(19, 19), new(19, 23));
                break;
            case EquipmentSlot.Boots:
                Shape(new(9, 4), new(19, 4), new(19, 14), new(23, 17), new(23, 22),
                    new(3, 22), new(3, 18), new(9, 14));
                break;
        }
    }
}
