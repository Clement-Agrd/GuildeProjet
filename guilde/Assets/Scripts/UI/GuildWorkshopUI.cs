using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public partial class GuildWorkshopUI : MonoBehaviour
{
    [SerializeField] private GameManager game;
    [SerializeField] private TimeManager clock;
    [SerializeField] private AdventurerManager adventurers;
    [SerializeField] private BuildingManager buildings;
    [SerializeField] private EconomyManager economy;
    [SerializeField] private GuildInventory inventory;
    [SerializeField] private EquipmentManager equipment;
    private GuildEquipmentPanel _equipmentPanel;

    private VisualElement _root;
    private VisualElement _content;
    private Button _forge;
    private Button _market;
    private string _view = "courtyard";
    private BuildingType _buildingType = BuildingType.Forge;
    private string _selectedId;
    private AdventurerStateType? _selectedState;
    private bool _dirty = true;
    private bool _dragging;
    internal bool RosterPointerHeld { get; set; }
    private bool _bound;
    private float _nextRefresh;
    private readonly List<Action> _liveUpdates = new();
    private readonly Dictionary<ResourceSO, Label> _resourceLabels = new();
    private bool _hasCrafted;
    private bool _hasSold;
    private readonly HashSet<string> _expandedRecruits = new();

    public VisualElement Root => _root;
    public Button ForgeDropTarget => _forge;
    public Button MarketDropTarget => _market;

    private void OnEnable()
    {
        _root = GetComponent<UIDocument>().rootVisualElement;
        _content = _root.Q<ScrollView>("detail-content");
        _root.Q<Button>("open-equipment").clicked += ShowEquipment;
        _root.Q<Button>("equipment-back").clicked += ShowCourtyard;
        _root.Q<Button>("guild-home").clicked += ShowCourtyard;
        _root.Q<Button>("pause").clicked += clock.TogglePause;
        BindJournalControls();
        BindSpeed("speed-1", GameSpeed.Normal);
        BindSpeed("speed-2", GameSpeed.Fast);
        BindSpeed("speed-3", GameSpeed.VeryFast);
        _root.Q<Button>("new-game").clicked += () => SetResetVisible(true);
        _root.Q<Button>("cancel-reset").clicked += () => SetResetVisible(false);
        _root.Q<Button>("confirm-reset").clicked += () => { SetResetVisible(false); game.StartNewGame(); };
        BuildCourtyard();
        _resourceLabels.Clear();
        _root.Q("resources").Clear();
        _dirty = true;
        if (game && game.IsInitialized) BindManagers();
    }

    private void Start() => BindManagers();

    private void BindManagers()
    {
        if (_bound) return;
        if (!equipment) return;
        _bound = true;
        clock.OnSpeedChanged += HandleJournalSpeed;
        _equipmentPanel = new GuildEquipmentPanel(this, equipment, inventory, adventurers, Notify, MarkDirty);
        equipment.OnEquipmentChanged += MarkDirty;
        game.OnNewGameStarted += HandleNewGame;
        buildings.OnGuildActivity += Notify;
        buildings.OnTavernRecovery += HandleTavernRecovery;
        adventurers.OnAdventurerHired += HandleHired;
        adventurers.OnAdventurerRested += HandleRested;
        adventurers.OnAdventurerFired += HandleRosterChanged;
        adventurers.OnAdventurerDied += HandleRosterChanged;
        adventurers.OnAdventurerLevelUp += HandleLevelUp;
        adventurers.OnAdventurerJobLevelUp += HandleJobLevelUp;
        adventurers.OnRecruitPoolRefreshed += MarkDirty;
        adventurers.OnRecruitSourced += HandleRecruitSourced;
        buildings.OnAdventurerAssigned += HandleAssigned;
        buildings.OnAdventurerUnassigned += HandleUnassigned;
        buildings.OnProductionCompleted += HandleProduction;
        buildings.OnProductionBlocked += HandleProductionBlocked;
        buildings.OnBuildingUpgraded += HandleUpgrade;
        economy.OnGoldChanged += HandleGold;
        economy.OnMonthlyPayrollPaid += HandlePayroll;
        economy.OnBankruptcyStarted += HandleBankruptcy;
        economy.OnBankruptcyEnded += MarkDirty;
        inventory.OnResourceChanged += HandleResource;
        inventory.OnEquipmentAdded += HandleEquipment;
        inventory.OnEquipmentRemoved += HandleEquipmentRemoved;
        AddJournal("La charte est signée. Votre guilde dispose de 500 pièces d'or et de ses premières réserves.");
        MarkDirty();
    }

    private void OnDisable()
    {
        HideJobPreviewTooltip();
        if (!_bound) return;
        _bound = false;
        clock.OnSpeedChanged -= HandleJournalSpeed;
        equipment.OnEquipmentChanged -= MarkDirty;
        game.OnNewGameStarted -= HandleNewGame;
        buildings.OnGuildActivity -= Notify;
        buildings.OnTavernRecovery -= HandleTavernRecovery;
        adventurers.OnAdventurerHired -= HandleHired;
        adventurers.OnAdventurerRested -= HandleRested;
        adventurers.OnAdventurerFired -= HandleRosterChanged;
        adventurers.OnAdventurerDied -= HandleRosterChanged;
        adventurers.OnAdventurerLevelUp -= HandleLevelUp;
        adventurers.OnAdventurerJobLevelUp -= HandleJobLevelUp;
        adventurers.OnRecruitPoolRefreshed -= MarkDirty;
        adventurers.OnRecruitSourced -= HandleRecruitSourced;
        buildings.OnAdventurerAssigned -= HandleAssigned;
        buildings.OnAdventurerUnassigned -= HandleUnassigned;
        buildings.OnProductionCompleted -= HandleProduction;
        buildings.OnProductionBlocked -= HandleProductionBlocked;
        buildings.OnBuildingUpgraded -= HandleUpgrade;
        economy.OnGoldChanged -= HandleGold;
        economy.OnMonthlyPayrollPaid -= HandlePayroll;
        economy.OnBankruptcyStarted -= HandleBankruptcy;
        economy.OnBankruptcyEnded -= MarkDirty;
        inventory.OnResourceChanged -= HandleResource;
        inventory.OnEquipmentAdded -= HandleEquipment;
        inventory.OnEquipmentRemoved -= HandleEquipmentRemoved;
        CloseReplacement();
        _liveUpdates.Clear();
        _dragging = false;
        RosterPointerHeld = false;
    }

    private void Update()
    {
        if (!_bound || !game.IsInitialized) return;
        var selectedState = adventurers.GetById(_selectedId)?.currentStateType;
        if (_selectedState != selectedState) { _selectedState = selectedState; MarkDirty(); }
        if (_dirty && !RosterPointerHeld && !_dragging && !_forgeWorkerDragging && !(_equipmentPanel?.IsDragging ?? false)) Render();
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.15f;
        RefreshHud();
        foreach (var update in _liveUpdates) update();
    }

    private void BindSpeed(string name, GameSpeed speed)
        => _root.Q<Button>(name).clicked += () => clock.SetSpeed(speed);
    private void ShowCourtyard() => SetView("courtyard");
    public void ShowRecruitment() => SetView("recruits");
    public void ShowMarket() => SetView("market");
    public void ShowEquipment() => SetView("equipment");
    public void SelectEquipmentAdventurer(string id) { _selectedId = id; SetView("equipment"); }
    public void ShowBuilding(BuildingType type)
    {
        if (type == BuildingType.Recruiter) { ShowRecruitment(); return; }
        if (type == BuildingType.Market) { ShowMarket(); return; }
        if (_buildingType != type && _content is ScrollView scroll) scroll.scrollOffset = Vector2.zero;
        _buildingType = type; SetView("building");
    }
    public void SelectAdventurer(string id) { _selectedId = id; SetView("adventurer"); }
    private void SetView(string view)
    {
        if (_view != view && _content is ScrollView scroll) scroll.scrollOffset = Vector2.zero;
        _view = view; MarkDirty();
    }

    public bool AssignToBuilding(string id, BuildingType type)
    {
        bool result = buildings.AssignAdventurer(adventurers.GetById(id), type);
        if (result) { _selectedId = id; ShowBuilding(type); }
        else Notify("Affectation impossible : vérifiez la disponibilité et les emplacements.");
        return result;
    }

    private void Render()
    {
        HideJobPreviewTooltip();
        _dirty = false;
        var screen = _root.Q("workshop-screen");
        screen.EnableInClassList("equipment-view", _view == "equipment");
        screen.EnableInClassList("recruitment-view", _view == "recruits");
        screen.EnableInClassList("courtyard-view", _view == "courtyard");
        _liveUpdates.Clear();
        RenderRoster();
        _content.Clear();
        switch (_view)
        {
            case "market": RenderMarket(); break;
            case "building": RenderBuilding(); break;
            case "adventurer": RenderAdventurer(); break;
            case "equipment":
                Heading("L’ARMURERIE DE LA GUILDE", "Préparer les aventuriers");
                _equipmentPanel.Render(_content, _selectedId);
                break;
            case "recruits": RenderRecruitment(); break;
        }
        RenderJournal();
        RefreshHud();
    }

    private void RenderRoster()
    {
        var list = _root.Q<ScrollView>("roster");
        list.Clear();
        var roster = adventurers.GetAllAdventurers();
        if (roster.Count == 0)
            Text(list, "Les bancs sont encore vides. Rendez-vous chez le recruteur.", "empty");
        foreach (var a in roster)
        {
            var card = new VisualElement { name = "roster-" + a.id, focusable = true, tabIndex = 0 };
            card.AddToClassList("hero-card");
            card.EnableInClassList("selected", a.id == _selectedId);
            list.Add(card);

            var header = new VisualElement();
            header.AddToClassList("hero-card-header");
            card.Add(header);
            Text(header, a.adventurerName, "hero-name");
            var detail = ActionButton(header, "Détail", () => SelectAdventurer(a.id));
            detail.name = "detail-" + a.id;
            detail.AddToClassList("hero-detail");

            Text(card, Identity(a), "hero-identity");
            var status = Text(card, "", "hero-status");
            var meters = new VisualElement();
            meters.AddToClassList("hero-meters");
            card.Add(meters);
            var health = AddRosterMeter(meters, "PV", "hero-health");
            var fatigue = AddRosterMeter(meters, "Fatigue", "hero-fatigue");
            var morale = AddRosterMeter(meters, "Moral", "hero-morale");
            var loyalty = AddRosterMeter(meters, "Loyauté", "hero-loyalty");

            void RefreshCard()
            {
                status.text = a.currentStateType == AdventurerStateType.Working
                    ? $"{JobLabel(a.currentJobType)} · en poste"
                    : StateLabel(a);
                health.style.width = Length.Percent(a.HealthFraction * 100f);
                fatigue.style.width = Length.Percent(a.stats.fatigue);
                morale.style.width = Length.Percent(a.stats.moral);
                loyalty.style.width = Length.Percent(a.stats.loyaute);
            }
            RefreshCard();
            _liveUpdates.Add(RefreshCard);
            card.RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (evt.target != card) return;
                SelectRosterAdventurer(a.id);
                evt.StopPropagation();
            });
            card.AddManipulator(new AdventurerDragManipulator(this, a.id));
        }
    }

    internal void SelectRosterAdventurer(string id)
    {
        if (_selectedId == id) return;
        _selectedId = id;
        MarkDirty();
    }

    private static VisualElement AddRosterMeter(VisualElement card, string title, string style)
    {
        var row = new VisualElement();
        row.AddToClassList("hero-meter-row");
        card.Add(row);
        Text(row, title, "hero-meter-label");
        var track = new VisualElement();
        track.AddToClassList("hero-meter-track");
        row.Add(track);
        var fill = new VisualElement();
        fill.AddToClassList("hero-meter-fill");
        fill.AddToClassList(style);
        track.Add(fill);
        return fill;
    }

    private void RenderRecruitment()
    {
        Heading("CANDIDATURES", "Le recruteur");
        RenderTradeStaff(BuildingType.Recruiter);
        Text(_content, $"Aventuriers · {buildings.RosterOccupancy}/{buildings.RosterCapacity}", "tag");
        if (!buildings.CanHire) Text(_content, "Recrutement fermé. Vérifiez la capacité au quartier général.", "empty");
        var offers = adventurers.GetRecruitPool();
        _expandedRecruits.RemoveWhere(id => !offers.Exists(offer => offer.candidate.id == id));
        Subheading("CANDIDATURES SPONTANÉES");
        var refresh = Text(_content, "", "card-meta");
        _liveUpdates.Add(() => refresh.text = $"Renouvellement dans {adventurers.RecruitRefreshRemaining:0.#} h.");
        RenderRecruitOffers(offers, false);
        Subheading("AVENTURIERS TROUVÉS PAR VOS RECRUTEURS");
        RenderRecruitOffers(offers, true);
    }

    private void RenderRecruitOffers(List<RecruitOffer> offers, bool sourced)
    {
        var grid = new VisualElement();
        grid.AddToClassList("recruit-grid");
        _content.Add(grid);
        int count = 0;
        foreach (var offer in offers)
        {
            if (offer.IsSourced != sourced) continue;
            count++;
            var a = offer.candidate;
            var card = Card(grid, a.adventurerName);
            card.AddToClassList("recruit-card");
            Text(card, Identity(a) + $" · {a.age} ans", "card-meta");
            if (sourced) Text(card, $"Trouvé par {offer.recruiterName}", "card-meta");
            Text(card, Traits(a), "tag");
            var attributes = new List<PrimaryStat>();
            foreach (PrimaryStat stat in Enum.GetValues(typeof(PrimaryStat))) attributes.Add(stat);
            attributes.Sort((left, right) =>
            {
                int comparison = a.GetEffectivePrimary(right).CompareTo(a.GetEffectivePrimary(left));
                return comparison != 0 ? comparison : left.CompareTo(right);
            });
            Text(card, $"{RecruitAttribute(a, attributes[0])} · {RecruitAttribute(a, attributes[1])}", "card-meta");
            var learnedJobs = a.jobInstanceList.FindAll(job => job.currentLevel > 0);
            learnedJobs.Sort((left, right) => right.currentLevel.CompareTo(left.currentLevel));
            Text(card, learnedJobs.Count == 0 ? "Aucun métier acquis" : string.Join(" · ", learnedJobs.ConvertAll(RecruitJob)), "tag");
            Text(card, $"Salaire {a.CalculateSalary()} or/mois · Prime {offer.hiringBonus} or", "card-meta");

            var details = new Foldout { text = "Voir le profil complet", value = _expandedRecruits.Contains(a.id) };
            details.AddToClassList("recruit-details");
            details.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) _expandedRecruits.Add(a.id);
                else _expandedRecruits.Remove(a.id);
            });
            card.Add(details);
            Text(details, "ATTRIBUTS EFFECTIFS", "subheading");
            foreach (PrimaryStat stat in Enum.GetValues(typeof(PrimaryStat)))
                Text(details, RecruitAttribute(a, stat), "card-meta");
            Text(details, "NIVEAUX DE MÉTIER", "subheading");
            foreach (var job in a.jobInstanceList)
                Text(details, RecruitJob(job), "card-meta");
            if (a.traits.Count > 0)
            {
                Text(details, "EFFETS DES TRAITS", "subheading");
                foreach (var trait in a.traits)
                    Text(details, string.IsNullOrWhiteSpace(trait.description)
                        ? trait.traitName : $"{trait.traitName} — {trait.description}", "card-meta");
            }
            var hire = ActionButton(card, $"Embaucher · {offer.hiringBonus} or", () =>
            {
                if (!adventurers.HireAdventurer(a.id)) Notify("Votre trésorerie ne permet pas ce recrutement.");
                else { _selectedId = a.id; MarkDirty(); }
            }, true);
            hire.name = "hire-" + a.id;
            hire.SetEnabled(buildings.CanHire && economy.CurrentGold >= offer.hiringBonus);
            ActionButton(card, "Refuser", () =>
            {
                if (adventurers.DeclineRecruit(a.id)) MarkDirty();
            }).AddToClassList("quiet");
        }
        if (count == 0)
            Text(_content, sourced ? "Aucun aventurier trouvé pour le moment." : "Aucune candidature spontanée disponible.", "empty");
    }

    private string JobLabel(JobType job) => buildings.GetJobDefinition(job)?.jobName ?? job.ToString();
    private string RecruitJob(JobInstance job) => $"{JobLabel(job.jobType)} niv. {job.currentLevel}";
    private static string RecruitAttribute(Adventurer a, PrimaryStat stat)
        => $"{GuildEquipmentPanel.StatLabel((StatType)(int)stat)} {a.GetEffectivePrimary(stat)}";

    private void RenderBuilding()
    {
        var b = buildings.GetBuilding(_buildingType);
        Heading("LE CŒUR DE L’ARTISANAT", b?.definition?.buildingName ?? "Bâtiment");
        if (b == null) return;
        if (b.buildingType != BuildingType.Forge && b.buildingType != BuildingType.Workshop) { RenderGuildBuilding(b); return; }
        if (b.buildingType == BuildingType.Forge)
        {
            RenderForge(b);
            return;
        }
        Text(_content, b.definition.description, "card-meta");
        Text(_content, $"Niveau {b.currentLevel} / {b.definition.maxLevel} · {b.assignedAdventurerIds.Count} / {b.MaxWorkers} artisans", "tag");
        var columns = new VisualElement();
        columns.AddToClassList("building-columns");
        _content.Add(columns);
        var staff = new VisualElement();
        staff.AddToClassList("building-staff");
        columns.Add(staff);
        var recipes = new VisualElement();
        recipes.AddToClassList("building-recipes");
        columns.Add(recipes);
        var stock = new VisualElement();
        stock.AddToClassList("building-stock");
        columns.Add(stock);

        RenderStaff(staff, b);
        RenderProductionOrders(recipes, b);
        Text(recipes, "CARNET DE RECETTES", "subheading");
        foreach (var recipe in buildings.GetRecipesForJob(b.definition.associatedJobType))
            RenderProductionRecipe(recipes, b, recipe);
        RenderProductionStock(stock);
        RenderJewelryStock(stock);
        RenderGuildUpgrade(b);
    }

    private void RenderProductionOrders(VisualElement parent, Building building)
    {
        bool forge = building.buildingType == BuildingType.Forge;
        if (!forge && building.activeTasks.Count == 0) return;
        Text(parent, "COMMANDES EN COURS", "subheading");
        if (building.activeTasks.Count == 0) Text(parent, "Aucune commande en cours.", "card-meta");
        foreach (var task in building.activeTasks)
        {
            var card = Card(parent, task.recipe?.recipeName ?? task.recipeId);
            var details = card;
            var actions = card;
            if (forge)
            {
                var layout = new VisualElement();
                layout.AddToClassList("row");
                layout.AddToClassList("forge-order-layout");
                card.Add(layout);
                details = new VisualElement();
                details.AddToClassList("forge-order-details");
                layout.Add(details);
                actions = new VisualElement();
                actions.AddToClassList("forge-order-actions");
                layout.Add(actions);
            }

            string name = adventurers.GetById(task.assignedAdventurerId)?.adventurerName ?? "Absent";
            string technique = forge ? $" · {ForgeTechniqueLabel(task.forgeTechnique)}" : "";
            Text(details, name + technique, "card-meta");
            if (task.IsSeries)
            {
                var units = Text(details, SeriesStatus(task), "tag");
                _liveUpdates.Add(() => units.text = SeriesStatus(task));
            }

            var progress = new ProgressBar { lowValue = 0, highValue = 100 };
            progress.AddToClassList("task-progress");
            details.Add(progress);
            void RefreshProgress()
            {
                progress.value = task.ProgressPercent;
                progress.title = !building.IsActive ? "Suspendue pendant l’amélioration" : !building.HasWorker(task.assignedAdventurerId)
                    ? "En pause · réaffectez cet aventurier ici" : task.awaitingInputs
                        ? buildings.GetProductionSupplyIssue(task.recipe, task.ComponentBlockReason, task.forgeTechnique) ?? "Prête à reprendre"
                        : $"Fabrication · {task.ProgressPercent:0} %";
            }
            RefreshProgress();
            _liveUpdates.Add(RefreshProgress);

            ActionButton(actions, task.IsSeries ? "Arrêter maintenant" : task.awaitingInputs ? "Annuler la commande" : "Annuler et récupérer les composants",
                () => { buildings.CancelProduction(building.buildingId, task.taskId); MarkDirty(); });
            if (task.IsSeries && !task.stopAfterCurrentUnit && (task.HasNextUnit || task.awaitingInputs))
            {
                var stop = ActionButton(actions, "Arrêter après cette pièce", () =>
                {
                    if (!buildings.StopSeriesAfterCurrentUnit(building.buildingId, task.taskId)) return;
                    if (task.stopAfterCurrentUnit) MarkDirty();
                    else Notify($"Série arrêtée · {task.recipe?.recipeName ?? task.recipeId} · {task.seriesUnit - 1} pièce(s) fabriquée(s).");
                });
                stop.name = "stop-series-after-current";
                if (task.awaitingInputs) stop.tooltip = "Aucune pièce en cours. La série s’arrêtera maintenant.";
            }
        }
    }

    private void RenderProductionStock(VisualElement stock)
    {
        Text(stock, "MATÉRIAUX EN RÉSERVE", "subheading");
        Text(stock, $"Équipement · {inventory.EquipmentCount}/{inventory.EquipmentCapacity}", "tag");
        foreach (var material in inventory.ResourceDefinitions)
            Text(stock, $"{material.resourceName} · {inventory.GetAmount(material)}", "card-meta");
    }

    private Adventurer FindFreeWorker(Building b, CraftRecipeSO recipe)
    {
        foreach (var id in b.assignedAdventurerIds)
        {
            var a = adventurers.GetById(id);
            if (a == null || a.currentStateType != AdventurerStateType.Working || buildings.GetTaskForWorker(id) != null) continue;
            var job = a.GetJobInstance(recipe.requiredJob);
            if (job != null && job.currentLevel >= recipe.requiredJobLevel) return a;
        }
        return null;
    }

    private void RenderMarket()
    {
        Heading("LES AFFAIRES REPRENNENT", "Le marché");
        RenderTradeStaff(BuildingType.Market);
        if (economy.HasActiveMerchant)
            Text(_content, $"Prix de vente brut +{economy.MarketSaleBonus:P0} · Les prix affichés déduisent la commission du marchand.", "card-meta");
        RenderMarketAutomation();
        Subheading("LES RÉSERVES");
        foreach (var resource in inventory.ResourceDefinitions)
        {
            var card = Card(_content, resource.resourceName);
            Text(card, $"En réserve : {inventory.GetAmount(resource)} / {resource.maxStackSize}", "card-meta");
            var row = new VisualElement(); row.AddToClassList("row"); card.Add(row);
            var buy = ActionButton(row, $"Acheter 5 · {economy.GetBuyPrice(resource, 5)} or", () =>
            {
                if (economy.BuyResource(resource, 5)) Notify($"Achat de 5 {resource.resourceName.ToLowerInvariant()}.");
                else Notify("Achat impossible : or insuffisant ou réserves pleines.");
            });
            buy.SetEnabled(economy.CurrentGold >= economy.GetBuyPrice(resource, 5) && inventory.GetRemainingCapacity(resource) >= 5);
            var sell = ActionButton(row, $"Vendre 5 · {economy.GetSellPrice(resource, 5)} or", () => economy.SellResource(resource, 5));
            sell.SetEnabled(inventory.GetAmount(resource) >= 5);
        }
        Subheading("LES PIÈCES À VENDRE");
        Text(_content, $"Inventaire · {inventory.EquipmentCount}/{inventory.EquipmentCapacity} équipements (portés inclus)", "tag");
        foreach (var item in equipment.GetStoredEquipment())
        {
            var card = Card(_content, item.definition?.itemName ?? item.definitionId);
            var quality = Text(card, QualityLabel(item.quality), "tag");
            quality.AddToClassList(item.quality.ToString().ToLowerInvariant());
            Text(card, $"{GuildEquipmentPanel.StatLabel(item.definition.mainStatType)} +{item.mainStatValue} · durabilité {item.currentDurability}/{item.maxDurability}", "card-meta");
            Text(card, "Artisan : " + (adventurers.GetById(item.craftedByAdventurerId)?.adventurerName ?? "inconnu"), "card-meta");
            foreach (var sub in item.subStats) Text(card, $"+{sub.value:0.#} {sub.statType}", "card-meta");
            var sell = ActionButton(card, $"Vendre · {economy.GetEquipmentSellPrice(item)} or nets", () =>
            {
                int revenue = economy.GetEquipmentSellPrice(item);
                if (!economy.SellEquipment(item.equipmentId)) return;
                _hasSold = true;
                Notify($"{item.definition.itemName} vendu · +{revenue} or nets.");
            }, true);
            sell.name = "sell-" + item.equipmentId;
        }
        if (equipment.StoredEquipmentCount == 0)
            Text(_content, "Votre étal est vide. Lancez une fabrication à la Forge pour préparer votre première vente.", "empty");
    }

    private void RefreshHud()
    {
        _root.Q<Label>("gold").text = $"{economy.CurrentGold:N0} or";
        foreach (var resource in inventory.ResourceDefinitions)
        {
            if (resource.resourceType != ResourceType.Basic) continue;
            if (!_resourceLabels.TryGetValue(resource, out var value))
            {
                var block = new VisualElement(); block.AddToClassList("resource");
                Text(block, resource.resourceName.ToUpperInvariant(), "eyebrow");
                value = Text(block, "", "resource-value");
                _resourceLabels.Add(resource, value); _root.Q("resources").Add(block);
            }
            value.text = inventory.GetAmount(resource).ToString();
        }
        var date = _root.Q<Label>("clock");
        date.text = $"An {clock.Year} · Mois {clock.Month:00} · Jour {clock.Day:00} · {clock.Hour:00} h";
        date.tooltip = $"À ×1, 1 seconde réelle = 1 heure de jeu. Paie le 1er du mois · {economy.MonthlyPayroll} or.";
        var pause = _root.Q<Button>("pause");
        pause.text = clock.CurrentSpeed == GameSpeed.Paused ? "Reprendre" : "Pause";
        string[] names = { "pause", "speed-1", "speed-2", "speed-3" };
        for (int i = 0; i < names.Length; i++) _root.Q<Button>(names[i]).EnableInClassList("active-speed", (int)clock.CurrentSpeed == i);
        var forge = buildings.GetBuilding(BuildingType.Forge);
        RefreshCourtyard();
        _root.Q<Label>("objective").text = _hasSold ? "PREMIÈRE COMMANDE HONORÉE / Votre guilde prend vie."
            : _hasCrafted ? "PREMIERS PAS / Votre pièce est prête. Relancez une recette pour poursuivre."
            : forge != null && forge.assignedAdventurerIds.Count > 0 ? "PREMIERS PAS / Lancez une recette dans le carnet de la Forge."
            : adventurers.GetAllAdventurers().Count > 0 ? "PREMIERS PAS / Glissez un aventurier sur la Forge."
            : "PREMIERS PAS / Recrutez votre premier artisan.";
    }

    internal bool BeginDrag(string id, Vector2 position)
    {
        var a = adventurers.GetById(id);
        if (_view != "courtyard" || a?.IsAvailable != true) return false;

        _dragging = true;
        SelectRosterAdventurer(id);
        var ghost = _root.Q<Label>("drag-ghost");
        ghost.text = a.adventurerName;
        ghost.style.display = DisplayStyle.Flex;
        foreach (var entry in _courtyardTiles)
        {
            var b = buildings.GetBuilding(entry.Key);
            entry.Value.Button.EnableInClassList("drop-target", b?.IsActive == true && b.HasFreeSlot);
        }
        MoveDrag(position);
        return true;
    }

    internal void MoveDrag(Vector2 position)
    {
        if (!_dragging) return;
        var local = _root.WorldToLocal(position);
        var ghost = _root.Q("drag-ghost");
        ghost.style.left = local.x + 16;
        ghost.style.top = local.y + 12;
        foreach (var tile in _courtyardTiles.Values)
        {
            var button = tile.Button;
            button.EnableInClassList("drop-hover", button.ClassListContains("drop-target") && button.worldBound.Contains(position));
        }
    }

    internal void EndDrag(string id, Vector2 position, bool accept)
    {
        if (!_dragging) return;
        _dragging = false;
        _root.Q("drag-ghost").style.display = DisplayStyle.None;
        foreach (var tile in _courtyardTiles.Values)
        {
            tile.Button.RemoveFromClassList("drop-target");
            tile.Button.RemoveFromClassList("drop-hover");
        }
        if (accept && _view == "courtyard")
        {
            foreach (var entry in _courtyardTiles)
            {
                if (!entry.Value.Button.worldBound.Contains(position)) continue;
                AssignToBuilding(id, entry.Key);
                break;
            }
        }
        MarkDirty();
    }

    private void Heading(string eyebrow, string title)
    {
        _root.Q<Label>("detail-eyebrow").text = eyebrow;
        _root.Q<Label>("detail-title").text = title;
    }
    private void Subheading(string text) => Text(_content, text, "subheading");
    private static Label Text(VisualElement parent, string text, string style)
    {
        var label = new Label(text); label.AddToClassList(style); parent.Add(label); return label;
    }
    private static VisualElement Card(VisualElement parent, string title)
    {
        var card = new VisualElement(); card.AddToClassList("card"); parent.Add(card);
        Text(card, title, "card-title"); return card;
    }
    private static Button ActionButton(VisualElement parent, string text, Action action, bool primary = false)
    {
        var button = new Button(action) { text = text };
        if (primary) button.AddToClassList("primary"); parent.Add(button); return button;
    }
    private static string Identity(Adventurer a) => $"{a.adventurerRace?.raceName} · {a.adventurerClass?.className} · niv. {a.stats.level}";
    private static string Traits(Adventurer a) => a.traits.Count == 0 ? "Aucun trait particulier" : string.Join(" · ", a.traits.ConvertAll(t => t.traitName));
    private static string Costs(List<ResourceCost> costs) => costs.Count == 0 ? "Sans composants" : string.Join(" + ", costs.ConvertAll(c => $"{c.amount} {c.resource.resourceName.ToLowerInvariant()}"));
    public static string QualityLabel(EquipmentQuality q) => q switch
    {
        EquipmentQuality.Rare => "Rare", EquipmentQuality.Epic => "Épique", EquipmentQuality.Legendary => "Légendaire", _ => "Normal"
    };
    private static string StateLabel(Adventurer a) => a.currentService switch
    {
        BuildingType.Infirmary => "En soins",
        BuildingType.Tavern => "À la taverne",
        BuildingType.TrainingRoom => "À l’entraînement",
        _ => BaseStateLabel(a)
    };
    private static string BaseStateLabel(Adventurer a) => a.currentStateType switch
    {
        AdventurerStateType.Working => "Au travail", AdventurerStateType.Resting => "Au repos", AdventurerStateType.Injured => "Blessé",
        AdventurerStateType.OnMission => "En mission", AdventurerStateType.Dead => "Mort", _ => "Disponible"
    };
    private void SetResetVisible(bool visible) => _root.Q("reset-overlay").style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    private void MarkDirty() => _dirty = true;
    private void HandleNewGame()
    {
        _selectedId = null; _view = "courtyard"; _journal.Clear(); _hasCrafted = false; _hasSold = false;
        _journalPausedClock = false;
        _stockFullNotified = false;
        _recipeToReveal = null;
        _seriesQuantities.Clear();
        _forgeSelections.Clear();
        _forgeFilter = null;
        CloseReplacement();
        if (_content is ScrollView scroll) scroll.scrollOffset = Vector2.zero;
        _root.Q<Label>("status").text = "Votre première commande vous attend.";
        AddJournal("Une nouvelle charte, un nouveau départ. Votre guilde vous attend.");
        MarkDirty();
    }
    private void HandleHired(Adventurer a) => Notify($"{a.adventurerName} rejoint la guilde.");
    private void HandleRosterChanged(Adventurer a) => MarkDirty();
    private void HandleLevelUp(Adventurer a, int level) => Notify($"{a.adventurerName} atteint le niveau {level}.");
    private void HandleJobLevelUp(Adventurer a, JobType job, int level) => Notify($"{a.adventurerName} : {buildings.GetJobDefinition(job)?.jobName} niveau {level}.");
    private void HandleAssigned(Building b, Adventurer a) => Notify($"{a.adventurerName} prend son poste : {b.definition.buildingName}.");
    private void HandleUnassigned(Building b, Adventurer a)
    {
        if (a.currentStateType == AdventurerStateType.Resting && a.IsExhausted)
            NotifyExhaustion(a);
        else
            Notify($"{a.adventurerName} quitte son poste. Son emplacement est libéré.");
    }
    private void HandleProduction(Building b, ProductionTask task)
    {
        _hasCrafted = true;
        MarkDirty();
        if (task.recipe == null) return;
        string commission = task.LastCommission > 0 ? $" Commission de fabrication · {task.LastCommission} or." : "";
        if (task.HasNextUnit)
        {
            if (task.LastCommission > 0)
                Notify($"{task.recipe.recipeName} · pièce {task.seriesUnit}/{task.seriesQuantity} terminée.{commission}");
            return;
        }
        var recipe = task.recipe;
        string message = task.stopAfterCurrentUnit ? $"Série arrêtée après fabrication · {recipe.recipeName} ×{task.seriesUnit}."
            : task.IsSeries ? $"Série terminée · {recipe.recipeName} ×{task.seriesQuantity}."
            : $"{recipe.recipeName} · fabrication terminée (qualité {QualityLabel(task.rolledQuality)}).";
        var worker = adventurers.GetById(task.assignedAdventurerId);
        var technique = task.forgeTechnique;
        int quantity = task.IsSeries ? task.seriesQuantity
            : _seriesQuantities.TryGetValue(recipe, out int saved) && saved >= 2 ? saved : 5;
        bool WorkerNeedsOrder() => worker != null && adventurers.GetById(worker.id) == worker
            && b.HasWorker(worker.id) && buildings.GetTaskForWorker(worker.id) == null;
        AddDecision(message + commission, WorkerNeedsOrder, new JournalAction
        {
            Label = "Relancer une série",
            GetBlockedReason = () => !WorkerNeedsOrder() || !b.IsActive ? "Artisan ou bâtiment indisponible."
                : buildings.GetProductionStartIssue(worker, b, recipe, technique),
            Resolve = () => StartCraft(b, recipe, quantity, worker, technique)
        }, new JournalAction
        {
            Label = b.buildingType == BuildingType.Forge ? "Aller à la forge" : "Aller à l’atelier",
            Execute = () => OpenProductionRecipe(b.buildingType, recipe, worker, technique)
        }, IgnoreDecision());
    }
    private void HandleProductionBlocked(Building building, ProductionTask task, string reason)
    {
        Notify($"{building.definition.buildingName} · {task.recipe.recipeName} en attente : {reason}.", new JournalAction
        {
            Label = "Ouvrir le marché",
            Execute = ShowMarket
        }, blocksProgress: true, needsDecision: () => building.activeTasks.Contains(task) && task.awaitingInputs
            && !inventory.HasResources(task.InputResources));
    }
    private void HandleUpgrade(Building b) => Notify($"{b.definition.buildingName} atteint le niveau {b.currentLevel}.");
    private void HandleGold(int amount) => MarkDirty();
    private void HandlePayroll(int amount) => Notify($"Paie du mois {clock.Month} · {amount} or versés aux aventuriers.");
    private void HandleRecruitSourced(RecruitOffer offer, int commission)
        => Notify($"{offer.recruiterName} a trouvé {offer.candidate.adventurerName} · niv. {offer.candidate.stats.level} · {commission} or de commission.");
    private void HandleResource(ResourceSO resource, int amount) => MarkDirty();
    private void HandleEquipment(EquipmentInstance item) { CheckEquipmentStock(); MarkDirty(); }
    private void HandleEquipmentRemoved(string id) { CheckEquipmentStock(); MarkDirty(); }
    private void HandleBankruptcy() => Notify("La trésorerie est vide. Le moral baisse tant que la guilde est en faillite.", null,
        blocksProgress: true, needsDecision: () => economy.IsBankrupt);
}
