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

    private void OpenProductionRecipe(BuildingType type, CraftRecipeSO recipe)
    {
        _recipeToReveal = recipe;
        ShowBuilding(type);
    }

    private void RenderProductionRecipe(VisualElement parent, Building building, CraftRecipeSO recipe)
    {
        var card = Card(parent, recipe.recipeName);
        if (_recipeToReveal == recipe && _content is ScrollView scroll)
        {
            _recipeToReveal = null;
            card.AddToClassList("recipe-highlight");
            card.schedule.Execute(() => scroll.ScrollTo(card)).StartingIn(1);
        }

        var availableWorker = FindFreeWorker(building, recipe);
        Text(card, $"{Costs(recipe.inputResources)} · métier niv. {recipe.requiredJobLevel}", "card-meta");
        if (availableWorker == null)
            Text(card, $"{recipe.baseCraftDuration:0.##} s par pièce (durée de base)", "card-meta");
        else
        {
            var crafting = JobPerformance.GetCrafting(availableWorker, recipe, buildings.GetJobDefinition(recipe.requiredJob));
            float duration = crafting.Duration / building.ProductionSpeedMultiplier;
            string odds = recipe.ProducesEquipment ? $" · Rare ou mieux {(1f - recipe.GetQualityChances(crafting.QualityBonus).Normal):P1}" : "";
            Text(card, $"{availableWorker.adventurerName} · {duration:0.##} s par pièce{odds}", "card-meta");
        }
        var controls = new VisualElement();
        controls.AddToClassList("row");
        card.Add(controls);

        var craft = ActionButton(controls, "Lancer la fabrication", () => StartCraft(building, recipe, 1), true);
        craft.name = "craft-" + recipe.name;

        var quantity = new IntegerField("Qté") { name = "quantity-" + recipe.name };
        quantity.AddToClassList("series-quantity");
        quantity.value = _seriesQuantities.TryGetValue(recipe, out int saved) ? saved : 5;
        controls.Add(quantity);
        var series = ActionButton(controls, "Fabriquer en série", () => StartCraft(building, recipe, ClampSeriesQuantity(quantity.value)));
        series.name = "series-" + recipe.name;
        bool canStart = building.IsActive && availableWorker != null && buildings.CanQueueProduction(recipe);
        craft.SetEnabled(canStart);
        series.SetEnabled(canStart && quantity.value >= 2);
        quantity.RegisterValueChangedCallback(evt =>
        {
            int value = ClampSeriesQuantity(evt.newValue);
            _seriesQuantities[recipe] = value;
            series.SetEnabled(canStart && value >= 2);
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
    }

    private static int ClampSeriesQuantity(int quantity) => Mathf.Clamp(quantity, 0, BuildingManager.MaxSeriesQuantity);

    private static string SeriesStatus(ProductionTask task)
    {
        string status = $"Série · {task.seriesUnit - 1}/{task.seriesQuantity} pièces terminées";
        if (task.stopAfterCurrentUnit) return status + " · arrêt après cette pièce";
        return task.awaitingInputs ? status + " · en attente" : status;
    }

    private bool StartCraft(Building building, CraftRecipeSO recipe, int quantity, Adventurer worker = null)
    {
        worker ??= FindFreeWorker(building, recipe);
        if (worker != null && buildings.StartProduction(worker, recipe, quantity))
        {
            string amount = quantity > 1 ? $" ×{quantity}" : "";
            Notify($"Commande lancée · {recipe.recipeName}{amount}.");
            return true;
        }
        Notify(buildings.GetProductionSupplyIssue(recipe) ?? "Aucun artisan qualifié et libre.");
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
