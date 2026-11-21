using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GuildWorkshopUI
{
    private static readonly PrimaryStat[] ProfileAttributes =
    {
        PrimaryStat.Force, PrimaryStat.Agilite, PrimaryStat.Vitalite, PrimaryStat.Intelligence,
        PrimaryStat.Sagesse, PrimaryStat.Charisme, PrimaryStat.Resistance, PrimaryStat.Chance
    };

    private VisualElement _replacementOverlay;

    private void RenderAdventurer()
    {
        var adventurer = adventurers.GetById(_selectedId);
        if (adventurer == null) { ShowCourtyard(); return; }

        Heading("FICHE D’AVENTURIER", adventurer.adventurerName);
        var overview = ProfileGroup(_content, "profile-columns");
        RenderProfileIdentity(ProfileSection(overview, "Identité", "profile-left"), adventurer);
        var attributes = ProfileGroup(ProfileSection(overview, "Statistiques de base", "profile-right"), "profile-grid");
        foreach (PrimaryStat stat in ProfileAttributes)
            AddProfileStat(attributes, (StatType)stat, adventurer.GetEffectivePrimary(stat).ToString(), "profile-attribute");

        var loadout = ProfileGroup(_content, "profile-columns");
        RenderProfileEquipment(ProfileSection(loadout, "Équipement", "profile-left"), adventurer);
        RenderProfileCombat(ProfileSection(loadout, "Statistiques de combat", "profile-right"), adventurer.GetCombatStats());

        RenderProfileJobs(ProfileSection(_content, "Métiers"), adventurer);
        var traits = ProfileSection(_content, "Traits");
        bool hasTrait = false;
        foreach (var trait in adventurer.traits)
        {
            if (trait == null) continue;
            hasTrait = true;
            var row = ProfileGroup(traits, "profile-trait");
            Text(row, trait.traitName, "profile-emphasis");
            Text(row, trait.description, "profile-secondary");
        }
        if (!hasTrait) Text(traits, "Aucun trait particulier.", "profile-secondary");
    }

    private void RenderProfileIdentity(VisualElement parent, Adventurer adventurer)
    {
        var identity = ProfileGroup(parent, "profile-grid");
        AddProfileField(identity, "Race", adventurer.adventurerRace?.raceName ?? "—");
        AddProfileField(identity, "Classe", adventurer.adventurerClass?.className ?? "—");
        AddProfileField(identity, "Niveau", adventurer.stats.level.ToString());
        AddProfileField(identity, "Âge", $"{adventurer.age} ans");

        var state = Text(parent, StateLabel(adventurer), "profile-state");
        _liveUpdates.Add(() => state.text = StateLabel(adventurer));
        AddProfileMeter(parent, "Moral", "profile-meter-moral", () => adventurer.stats.moral);
        AddProfileMeter(parent, "Fatigue", "profile-meter-fatigue", () => adventurer.stats.fatigue);
        AddProfileMeter(parent, "Loyauté", "profile-meter-loyalty", () => adventurer.stats.loyaute);
        AddProfileMeter(parent, "XP", "profile-meter-xp", () => adventurer.stats.experience,
            () => adventurer.stats.experienceToNextLevel);
    }

    private static void AddProfileField(VisualElement parent, string label, string value)
    {
        var field = ProfileGroup(parent, "profile-field");
        Text(field, label, "profile-secondary");
        Text(field, value, "profile-emphasis");
    }

    private void AddProfileMeter(VisualElement parent, string label, string style,
        Func<float> value, Func<float> maximum = null)
    {
        var row = ProfileGroup(parent, "profile-meter-row");
        Text(row, label, "profile-meter-label");
        var meter = new ProgressBar { name = style };
        meter.AddToClassList("profile-meter");
        meter.AddToClassList(style);
        row.Add(meter);

        void Refresh()
        {
            float max = Mathf.Max(1f, maximum?.Invoke() ?? 100f);
            float current = value();
            meter.highValue = max;
            meter.value = Mathf.Clamp(current, 0f, max);
            meter.title = $"{current:0} / {max:0}";
        }
        Refresh();
        _liveUpdates.Add(Refresh);
    }

    private void RenderProfileEquipment(VisualElement parent, Adventurer adventurer)
    {
        var slots = ProfileGroup(parent, "profile-grid");
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            var item = inventory.GetEquipment(adventurer.GetEquipmentId(slot));
            var cell = ProfileGroup(slots, "profile-slot");
            Text(cell, GuildEquipmentPanel.SlotLabel(slot), "profile-secondary");
            var itemName = Text(cell, item?.definition?.itemName ?? "Vide", item == null ? "profile-empty-slot" : "profile-emphasis");
            if (item == null) continue;
            itemName.tooltip = $"{QualityLabel(item.quality)} · {item.currentDurability}/{item.maxDurability} durabilité";
            if (item.IsBroken) itemName.AddToClassList("profile-broken-slot");
        }
        ActionButton(parent, "Gérer l’équipement", () => SelectEquipmentAdventurer(adventurer.id));
    }

    private static void RenderProfileCombat(VisualElement parent, CombatStats combat)
    {
        var grid = ProfileGroup(parent, "profile-grid");
        AddProfileStat(grid, StatType.PointsDeVie, combat.pointsDeVie.ToString());
        AddProfileStat(grid, StatType.DefensePhysique, combat.defensePhysique.ToString());
        AddProfileStat(grid, StatType.DefenseMagique, combat.defenseMagique.ToString());
        AddProfileStat(grid, StatType.AttaquePhysique, combat.attaquePhysique.ToString());
        AddProfileStat(grid, StatType.AttaqueMagique, combat.attaqueMagique.ToString());
        AddProfileStat(grid, StatType.ChanceCrit, $"{combat.chanceCrit:0.#} %");
        AddProfileStat(grid, StatType.DegatsCrit, $"{combat.degatsCrit:0.#} %");
        AddProfileStat(grid, StatType.ResistanceCrit, $"{combat.resistanceCrit:0.#} %");
        AddProfileStat(grid, StatType.Esquive, $"{combat.esquive:0.#} %");
        AddProfileStat(grid, StatType.Precision, $"{combat.precision:0.#} %");
        AddProfileStat(grid, StatType.PuissanceSoin, combat.puissanceSoin.ToString());
        AddProfileStat(grid, StatType.TauxRecuperation, $"{combat.tauxRecuperation:0.#} %");
    }

    private static void AddProfileStat(VisualElement parent, StatType stat, string value, string size = "profile-combat")
    {
        string label = GuildEquipmentPanel.StatLabel(stat);
        var tile = ProfileGroup(parent, size);
        tile.tooltip = $"{label} · {value}";
        var icon = ProfileGroup(tile, "profile-stat-icon");
        icon.AddToClassList($"profile-stat-icon--{stat}");
        var text = ProfileGroup(tile, "profile-stat-text");
        Text(text, value, "profile-stat-value");
        Text(text, label, "profile-stat-label");
    }

    private void RenderProfileJobs(VisualElement parent, Adventurer adventurer)
    {
        if (adventurer.currentService == BuildingType.Tavern)
            ActionButton(parent, "Quitter la taverne", () => buildings.StopService(adventurer));

        var grid = ProfileGroup(parent, "profile-grid");
        var production = buildings.GetTaskForWorker(adventurer.id);
        foreach (var job in adventurer.jobInstanceList)
        {
            var building = buildings.GetAllBuildings().Find(b => b.definition?.associatedJobType == job.jobType);
            var row = ProfileGroup(grid, "profile-job");
            var text = ProfileGroup(row, "profile-job-text");
            Text(text, JobLabel(job.jobType), "profile-emphasis");
            var level = Text(text, $"Niv. {job.currentLevel} · {job.currentExperience} XP", "profile-secondary");
            if (production?.IsSeries == true && production.craftedByJobType == job.jobType)
            {
                level.name = "profile-job-series";
                void RefreshSeries()
                {
                    string activity = building?.HasWorker(adventurer.id) == true && building.IsActive ? "" : " (en pause)";
                    level.text = $"Niv. {job.currentLevel} · {job.currentExperience} XP · {SeriesStatus(production)}{activity}";
                }
                RefreshSeries();
                _liveUpdates.Add(RefreshSeries);
            }

            if (building != null && building.HasWorker(adventurer.id))
            {
                ActionButton(row, "Libérer", () => buildings.UnassignAdventurer(adventurer));
                continue;
            }

            bool replace = building?.HasFreeSlot == false;
            var assign = ActionButton(row, replace ? "Remplacer" : "Affecter", () =>
            {
                if (building == null) return;
                if (!building.HasFreeSlot) { OpenReplacement(adventurer, building); return; }
                if (!buildings.AssignAdventurer(adventurer, building.buildingType))
                    Notify("Ce poste n’est plus disponible.");
            });
            assign.name = "profile-assign-" + job.jobType;
            assign.SetEnabled(adventurer.IsAvailable && building?.IsActive == true);
            assign.tooltip = building == null ? "Bâtiment indisponible"
                : !adventurer.IsAvailable ? "Aventurier indisponible"
                : !building.IsActive ? "Bâtiment indisponible"
                : replace ? "Choisir le titulaire à remplacer"
                : $"{building.definition.buildingName} · {adventurer.CalculateSalary(buildings.GetJobDefinition(job.jobType))} or";
        }
    }

    private void OpenReplacement(Adventurer incoming, Building building)
    {
        CloseReplacement();
        _replacementOverlay = ProfileGroup(_root.Q("workshop-screen"), "overlay");
        _replacementOverlay.name = "replacement-overlay";
        _replacementOverlay.style.display = DisplayStyle.Flex;
        var panel = ProfileGroup(_replacementOverlay, "modal");
        panel.AddToClassList("panel");
        Text(panel, $"Remplacer au poste · {building.definition.buildingName}", "hero-title");
        int wage = incoming.CalculateSalary(buildings.GetJobDefinition(building.definition.associatedJobType));
        Text(panel, $"{incoming.adventurerName} prendra le poste pour {wage} or. Le titulaire choisi redeviendra disponible et sa fabrication éventuelle sera mise en pause.", "card-meta");

        foreach (string id in building.assignedAdventurerIds)
        {
            var outgoing = adventurers.GetById(id);
            if (outgoing == null) continue;
            var confirm = ActionButton(panel, $"Remplacer {outgoing.adventurerName}", () =>
            {
                CloseReplacement();
                if (!buildings.ReplaceAdventurer(incoming, building.buildingType, id))
                    Notify("Le poste a changé. Vérifiez les affectations.");
            });
            confirm.name = "confirm-replacement-" + id;
        }
        var cancel = ActionButton(panel, "Annuler", CloseReplacement);
        cancel.name = "cancel-replacement";
        cancel.Focus();
    }

    private void CloseReplacement()
    {
        _replacementOverlay?.RemoveFromHierarchy();
        _replacementOverlay = null;
    }

    private static VisualElement ProfileSection(VisualElement parent, string heading, string width = null)
    {
        var panel = ProfileGroup(parent, "profile-section");
        if (width != null) panel.AddToClassList(width);
        Text(panel, heading, "profile-heading");
        return panel;
    }

    private static VisualElement ProfileGroup(VisualElement parent, string style)
    {
        var group = new VisualElement();
        group.AddToClassList(style);
        parent.Add(group);
        return group;
    }
}
