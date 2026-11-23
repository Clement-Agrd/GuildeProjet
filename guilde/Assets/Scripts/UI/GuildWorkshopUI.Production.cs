using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GuildWorkshopUI
{
    private readonly Dictionary<CraftRecipeSO, int> _seriesQuantities = new();
    private CraftRecipeSO _recipeToReveal;
    private bool _stockFullNotified;
    private static readonly List<string> AutoSaleQualities = new()
    {
        "Normale", "Jusqu’à rare", "Jusqu’à épique", "Toutes les qualités"
    };

    private void OpenProductionRecipe(BuildingType type, CraftRecipeSO recipe, Adventurer worker, ForgeTechnique technique)
    {
        _recipeToReveal = recipe;
        if (type == BuildingType.Forge)
        {
            if (_forgeFilter.HasValue && !MatchesForgeCategory(recipe, _forgeFilter)) _forgeFilter = null;
            _forgeSelections[recipe] = (worker?.id, technique);
        }
        ShowBuilding(type);
    }

    private void RenderProductionRecipe(VisualElement parent, Building building, CraftRecipeSO recipe)
    {
        var card = Card(parent, recipe.recipeName);
        RevealRecipe(card, recipe);
        var availableWorker = FindFreeWorker(building, recipe);
        Text(card, $"{Costs(recipe.inputResources)} · métier niv. {recipe.requiredJobLevel}", "card-meta");
        if (availableWorker == null)
            Text(card, $"{recipe.baseCraftDuration:0.##} h par pièce (durée de base)", "card-meta");
        else
        {
            var crafting = JobPerformance.GetCrafting(availableWorker, recipe, buildings.GetJobDefinition(recipe.requiredJob));
            float duration = crafting.Duration / building.ProductionSpeedMultiplier;
            string odds = recipe.ProducesEquipment ? $" · Rare ou mieux {(1f - recipe.GetQualityChances(crafting.QualityBonus).Normal):P1}" : "";
            Text(card, $"{availableWorker.adventurerName} · {duration:0.##} h par pièce{odds}", "card-meta");
            if (recipe.ProducesEquipment)
            {
                int minimum = availableWorker.CalculateCommission(recipe.outputEquipment.GetGoldValue(EquipmentQuality.Normal), recipe.requiredJob);
                int maximum = availableWorker.CalculateCommission(recipe.outputEquipment.GetGoldValue(EquipmentQuality.Legendary), recipe.requiredJob);
                string fee = minimum == maximum ? $"{minimum}" : $"{minimum}–{maximum}";
                Text(card, $"Commission à la fin de chaque pièce · {fee} or selon la qualité", "card-meta");
            }
        }
        var updateControls = RenderCraftControls(card, recipe, amount => StartCraft(building, recipe, amount, availableWorker));
        updateControls(buildings.GetProductionStartIssue(availableWorker, building, recipe));
    }

    private void RevealRecipe(VisualElement card, CraftRecipeSO recipe)
    {
        if (_recipeToReveal != recipe || _content is not ScrollView scroll) return;
        _recipeToReveal = null;
        card.AddToClassList("recipe-highlight");
        card.schedule.Execute(() => scroll.ScrollTo(card)).StartingIn(1);
    }

    private System.Action<string> RenderCraftControls(VisualElement card, CraftRecipeSO recipe, System.Func<int, bool> launch)
    {
        var controls = new VisualElement();
        controls.AddToClassList("row");
        card.Add(controls);

        var craft = ActionButton(controls, "Lancer la fabrication", () => launch(1), true);
        craft.name = "craft-" + recipe.name;

        var quantity = new IntegerField("Qté") { name = "quantity-" + recipe.name };
        quantity.AddToClassList("series-quantity");
        quantity.value = _seriesQuantities.TryGetValue(recipe, out int saved) ? saved : 5;
        controls.Add(quantity);
        var series = ActionButton(controls, "Fabriquer en série", () => launch(ClampSeriesQuantity(quantity.value)));
        series.name = "series-" + recipe.name;
        string blocked = null;
        void UpdateControls(string reason)
        {
            blocked = reason;
            craft.SetEnabled(reason == null);
            series.SetEnabled(reason == null && quantity.value >= 2);
            craft.tooltip = reason;
            series.tooltip = reason ?? (quantity.value < 2 ? "Choisissez au moins deux pièces." : null);
        }
        quantity.RegisterValueChangedCallback(evt =>
        {
            _seriesQuantities[recipe] = ClampSeriesQuantity(evt.newValue);
            UpdateControls(blocked);
        });
        quantity.RegisterCallback<FocusOutEvent>(_ => quantity.SetValueWithoutNotify(ClampSeriesQuantity(quantity.value)));

        var shortcuts = new VisualElement();
        shortcuts.AddToClassList("row");
        shortcuts.AddToClassList("series-shortcuts");
        card.Add(shortcuts);
        ActionButton(shortcuts, "0", () => quantity.value = 0).tooltip = "Remettre la quantité à zéro";
        ActionButton(shortcuts, "+10", () => quantity.value = ClampSeriesQuantity(ClampSeriesQuantity(quantity.value) + 10));
        ActionButton(shortcuts, "+100", () => quantity.value = ClampSeriesQuantity(ClampSeriesQuantity(quantity.value) + 100));
        ActionButton(shortcuts, "Max", () => quantity.value = BuildingManager.MaxSeriesQuantity).tooltip = $"Prévoir {BuildingManager.MaxSeriesQuantity} pièces";
        return UpdateControls;
    }

    private static int ClampSeriesQuantity(int quantity) => Mathf.Clamp(quantity, 0, BuildingManager.MaxSeriesQuantity);

    private static string SeriesStatus(ProductionTask task)
    {
        string status = $"Série · {task.seriesUnit - 1}/{task.seriesQuantity} pièces terminées";
        if (task.stopAfterCurrentUnit) return status + " · arrêt après cette pièce";
        return task.awaitingInputs ? status + " · en attente" : status;
    }

    private bool StartCraft(Building building, CraftRecipeSO recipe, int quantity, Adventurer worker = null,
        ForgeTechnique technique = ForgeTechnique.Standard)
    {
        if (worker == null && building.buildingType == BuildingType.Workshop)
            worker = FindFreeWorker(building, recipe);
        if (worker != null && buildings.StartProduction(worker, recipe, quantity, technique))
        {
            string amount = quantity > 1 ? $" ×{quantity}" : "";
            string mode = building.buildingType == BuildingType.Forge ? $" · {ForgeTechniqueLabel(technique)}" : "";
            Notify($"Commande lancée · {recipe.recipeName}{amount}{mode}.");
            return true;
        }
        Notify(buildings.GetProductionStartIssue(worker, building, recipe, technique) ?? "Commande indisponible.");
        return false;
    }

    private void RenderMarketAutomation()
    {
        Subheading("TRANSACTIONS AUTOMATIQUES");
        if (!economy.HasActiveMerchant)
        {
            Text(_content, "Affectez un marchand pour automatiser les achats et les ventes.", "card-meta");
            return;
        }

        var panel = new VisualElement();
        panel.AddToClassList("card");
        _content.Add(panel);
        var autoBuy = new Toggle("Acheter les composants manquants") { name = "auto-buy-components", value = economy.AutoBuyComponents };
        panel.Add(autoBuy);
        autoBuy.RegisterValueChangedCallback(evt =>
        {
            economy.ConfigureAutoPurchases(evt.newValue);
            MarkDirty();
        });
        Text(panel, "Forge et Atelier · achète au prix du marché le nécessaire pour la prochaine pièce.", "card-meta");
        var options = new VisualElement();
        options.AddToClassList("row");
        panel.Add(options);
        var enabled = new Toggle("Vente automatique") { name = "auto-sell", value = economy.AutoSellEquipment };
        options.Add(enabled);
        var onlyFull = new Toggle("Seulement si le stock est plein") { name = "auto-sell-full", value = economy.AutoSellOnlyWhenFull };
        options.Add(onlyFull);
        var rarity = new DropdownField("Qualité", AutoSaleQualities, (int)economy.AutoSellMaxQuality) { name = "auto-sell-quality" };
        panel.Add(rarity);

        void Configure()
        {
            economy.ConfigureAutoSales(enabled.value, (EquipmentQuality)rarity.index, onlyFull.value);
            MarkDirty();
        }

        enabled.RegisterValueChangedCallback(_ => Configure());
        onlyFull.RegisterValueChangedCallback(_ => Configure());
        rarity.RegisterValueChangedCallback(_ => Configure());
        Text(panel, $"Ventes automatiques · {economy.AutoSoldCount} pièce(s) · {economy.AutoSaleRevenue} or net", "card-meta");
    }

    private void CheckEquipmentStock()
    {
        if (!inventory.IsEquipmentFull)
        {
            _stockFullNotified = false;
            return;
        }
        if (_stockFullNotified || !game.IsInitialized) return;
        _stockFullNotified = true;
        Notify($"Inventaire d’équipement plein ({inventory.EquipmentCount}/{inventory.EquipmentCapacity}). Libérez de la place au marché.",
            new JournalAction { Label = "Ouvrir le marché", Execute = ShowMarket },
            blocksProgress: !economy.CanFreeEquipmentSlotAutomatically,
            needsDecision: () => inventory.IsEquipmentFull && !economy.CanFreeEquipmentSlotAutomatically);
    }
}
