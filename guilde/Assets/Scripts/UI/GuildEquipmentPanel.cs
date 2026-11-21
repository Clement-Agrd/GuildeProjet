using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Présentation de l'armurerie ; l'état du jeu reste dans Adventurer, l'inventaire et EquipmentManager.
public class GuildEquipmentPanel
{
    private readonly GuildWorkshopUI _ui;
    private readonly EquipmentManager _equipment;
    private readonly GuildInventory _inventory;
    private readonly AdventurerManager _adventurers;
    private readonly Action<string> _notify;
    private readonly Action _refresh;
    private EquipmentSlot _selectedSlot = EquipmentSlot.MainWeapon;
    private bool _showStats;
    public bool IsDragging { get; internal set; }

    public GuildEquipmentPanel(GuildWorkshopUI ui, EquipmentManager equipment, GuildInventory inventory,
        AdventurerManager adventurers, Action<string> notify, Action refresh)
    {
        _ui = ui; _equipment = equipment; _inventory = inventory; _adventurers = adventurers;
        _notify = notify; _refresh = refresh;
    }

    public void Render(VisualElement parent, string selectedId)
    {
        var roster = _adventurers.GetAllAdventurers().FindAll(a => a.currentStateType != AdventurerStateType.Dead);
        var a = roster.Find(candidate => candidate.id == selectedId) ?? (roster.Count > 0 ? roster[0] : null);
        if (a == null)
        {
            Label(parent, "Recrutez un aventurier pour lui attribuer les ouvrages de la guilde.", "empty");
            foreach (var item in _equipment.GetStoredEquipment()) ItemCard(parent, item);
            return;
        }
        var choices = roster.ConvertAll(candidate => candidate.adventurerName);
        var picker = new DropdownField("Aventurier", choices, roster.IndexOf(a));
        picker.name = "equipment-adventurer";
        picker.AddToClassList("equipment-picker");
        picker.RegisterValueChangedCallback(_ =>
        {
            if (picker.index >= 0 && picker.index < roster.Count) _ui.SelectEquipmentAdventurer(roster[picker.index].id);
        });
        parent.Add(picker);
        var tabs = new VisualElement(); tabs.AddToClassList("row"); parent.Add(tabs);
        Button(tabs, "Équipement", () => { _showStats = false; _refresh(); }).EnableInClassList("primary", !_showStats);
        Button(tabs, "Statistiques", () => { _showStats = true; _refresh(); }).EnableInClassList("primary", _showStats);
        if (_showStats) { RenderStats(parent, a); return; }

        var columns = new VisualElement(); columns.AddToClassList("equipment-columns"); parent.Add(columns);
        var loadout = new ScrollView(); loadout.AddToClassList("equipment-loadout"); columns.Add(loadout);
        var reserves = new ScrollView(); reserves.AddToClassList("equipment-reserves"); columns.Add(reserves);
        loadout.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        reserves.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        var combat = a.GetCombatStats();
        Label(loadout, $"Vie {combat.pointsDeVie} · Attaque {combat.attaquePhysique} · Magie {combat.attaqueMagique}", "equipment-summary");
        Label(loadout, "Choisissez un emplacement ou déposez-y une pièce depuis les réserves.", "card-meta");
        var slots = new VisualElement(); slots.AddToClassList("equipment-slots"); loadout.Add(slots);
        var dropTargets = new Dictionary<EquipmentSlot, VisualElement>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            var item = _inventory.GetEquipment(a.GetEquipmentId(slot));
            var button = Button(slots, SlotLabel(slot) + "\n" + (item?.definition?.itemName ?? "Vide"), () =>
            { _selectedSlot = slot; _refresh(); });
            button.name = "equipment-slot-" + slot;
            button.AddToClassList("equipment-slot");
            button.EnableInClassList("selected-slot", slot == _selectedSlot);
            dropTargets[slot] = button;
        }
        var current = _inventory.GetEquipment(a.GetEquipmentId(_selectedSlot));
        if (current != null)
        {
            Label(loadout, "PORTÉ · " + SlotLabel(_selectedSlot).ToUpperInvariant(), "subheading");
            var card = ItemCard(loadout, current);
            var remove = Button(card, "Retirer vers les réserves", () =>
            {
                if (_equipment.TryUnequip(a, _selectedSlot, out var reason))
                    _notify($"{current.definition.itemName} est de retour dans les réserves.");
                else _notify(reason);
            });
            remove.name = "unequip-" + _selectedSlot;
            remove.SetEnabled(a.currentStateType != AdventurerStateType.OnMission);
        }
        Label(reserves, "RÉSERVES · " + SlotLabel(_selectedSlot).ToUpperInvariant(), "subheading");
        int available = 0;
        foreach (var item in _equipment.GetStoredEquipment())
        {
            if (item.definition == null || item.definition.slot != _selectedSlot) continue;
            available++;
            var card = ItemCard(reserves, item);
            card.name = "equipment-item-" + item.equipmentId;
            bool canEquip = _equipment.CanEquip(a, item, out var reason);
            if (item.definition.slot == EquipmentSlot.MainWeapon && a.adventurerClass != null && !a.adventurerClass.MastersWeapon(item.definition.weaponType))
                Label(card, $"Arme non maîtrisée : dégâts ×{item.definition.unmasteredDamageMultiplier:0.##}, précision ×{item.definition.unmasteredAccuracyMultiplier:0.##}, usure ×{item.definition.unmasteredWearMultiplier:0.##}.", "equipment-warning");
            var preview = a.GetCombatStats(item);
            Label(card, $"Aperçu · attaque {combat.attaquePhysique} → {preview.attaquePhysique}\nMagie {combat.attaqueMagique} → {preview.attaqueMagique} · vie {combat.pointsDeVie} → {preview.pointsDeVie}\nDéfense {combat.defensePhysique} → {preview.defensePhysique} · précision {combat.precision:0.#} → {preview.precision:0.#} %", "equipment-preview");
            var equip = Button(card, current != null ? "Remplacer la pièce portée" : "Équiper", () =>
            {
                if (_equipment.TryEquip(a, item.equipmentId, out var failure))
                    _notify($"{a.adventurerName} équipe {item.definition.itemName}.");
                else _notify(failure);
            });
            equip.name = "equip-" + item.equipmentId;
            equip.AddToClassList("primary"); equip.SetEnabled(canEquip);
            if (!canEquip) Label(card, reason, "equipment-warning");
            card.AddManipulator(new EquipmentDragManipulator(this, _ui.Root, item, dropTargets, destination =>
            {
                if (destination != item.definition.slot) { _notify("Cette pièce correspond à un autre emplacement."); return; }
                if (_equipment.TryEquip(a, item.equipmentId, out var failure))
                    _notify($"{a.adventurerName} équipe {item.definition.itemName}.");
                else _notify(failure);
            }, _refresh));
        }
        if (available == 0) Label(reserves, "Aucune pièce disponible pour cet emplacement. Les ouvrages fabriqués à la Forge apparaissent ici.", "empty");
    }

    internal static void RenderStats(VisualElement parent, Adventurer a)
    {
        var columns = new VisualElement(); columns.AddToClassList("equipment-columns"); parent.Add(columns);
        var attributes = new VisualElement(); attributes.AddToClassList("equipment-loadout"); columns.Add(attributes);
        var combat = new VisualElement(); combat.AddToClassList("equipment-reserves"); columns.Add(combat);
        Label(attributes, "ATTRIBUTS · CLASSE, RACE ET TRAITS", "subheading");
        foreach (PrimaryStat stat in Enum.GetValues(typeof(PrimaryStat)))
            StatRow(attributes, StatLabel((StatType)(int)stat), a.GetEffectivePrimary(stat).ToString());
        var c = a.GetCombatStats();
        Label(combat, "STATS DE COMBAT · ÉQUIPEMENT INCLUS", "subheading");
        StatRow(combat, "Points de vie", c.pointsDeVie.ToString());
        StatRow(combat, "Attaque physique", c.attaquePhysique.ToString());
        StatRow(combat, "Attaque magique", c.attaqueMagique.ToString());
        StatRow(combat, "Défense physique", c.defensePhysique.ToString());
        StatRow(combat, "Défense magique", c.defenseMagique.ToString());
        StatRow(combat, "Chance de critique", $"{c.chanceCrit:0.#} %");
        StatRow(combat, "Dégâts critiques", $"{c.degatsCrit:0.#} %");
        StatRow(combat, "Résistance critique", $"{c.resistanceCrit:0.#} %");
        StatRow(combat, "Esquive", $"{c.esquive:0.#} %");
        StatRow(combat, "Précision", $"{c.precision:0.#} %");
        StatRow(combat, "Puissance des soins", c.puissanceSoin.ToString());
        StatRow(combat, "Récupération", $"{c.tauxRecuperation:0.#} %");
        Label(attributes, "Une pièce cassée reste portée, mais n’apporte plus de bonus. Les armes non maîtrisées appliquent leurs malus au résultat final.", "empty");
    }

    public void RenderRepairs(VisualElement parent)
    {
        Label(parent, "LES RÉPARATIONS", "subheading");
        Label(parent, "Réparation immédiate en or, avec un forgeron libre en poste.", "card-meta");
        int damaged = 0;
        foreach (var item in _inventory.GetAllEquipment())
        {
            int cost = _equipment.GetRepairCost(item);
            if (cost == 0) continue;
            damaged++;
            var card = ItemCard(parent, item);
            var owner = _equipment.GetOwner(item.equipmentId);
            Label(card, owner != null ? "Porté par " + owner.adventurerName : "Dans les réserves", "card-meta");
            var button = Button(card, $"Réparer · {cost} or", () =>
            {
                if (_equipment.TryRepair(item.equipmentId, out var reason)) _notify($"{item.definition.itemName} est réparé.");
                else _notify(reason);
            });
            button.name = "repair-" + item.equipmentId;
            bool allowed = _equipment.CanRepair(item, out var failure);
            button.SetEnabled(allowed);
            if (!allowed) Label(card, failure, "equipment-warning");
        }
        if (damaged == 0) Label(parent, "Toutes les pièces de la guilde sont en bon état.", "tag");
    }

    private static VisualElement ItemCard(VisualElement parent, EquipmentInstance item)
    {
        var card = new VisualElement(); card.AddToClassList("card"); parent.Add(card);
        Label(card, item.definition?.itemName ?? item.definitionId, "card-title");
        var quality = Label(card, GuildWorkshopUI.QualityLabel(item.quality), "tag");
        quality.AddToClassList(item.quality.ToString().ToLowerInvariant());
        if (item.definition != null) Label(card, $"+{item.mainStatValue} {StatLabel(item.definition.mainStatType)}", "card-meta");
        foreach (var sub in item.subStats) Label(card, $"+{sub.value:0.#} {StatLabel((StatType)((int)sub.statType + 8))}", "card-meta");
        var condition = Label(card, $"Durabilité {item.currentDurability} / {item.maxDurability}" + (item.IsBroken ? " · Cassé" : item.NeedsRepair ? " · À réparer" : ""), "card-meta");
        if (item.IsBroken || item.NeedsRepair) condition.AddToClassList("equipment-warning");
        return card;
    }

    private static void StatRow(VisualElement parent, string title, string value)
    {
        var row = new VisualElement(); row.AddToClassList("equipment-stat-row"); parent.Add(row);
        Label(row, title, "card-meta"); Label(row, value, "equipment-stat-value");
    }
    private static Label Label(VisualElement parent, string text, string style)
    {
        var label = new Label(text); label.AddToClassList(style); parent.Add(label); return label;
    }
    private static Button Button(VisualElement parent, string text, Action action)
    {
        var button = new Button(action) { text = text }; parent.Add(button); return button;
    }
    public static string SlotLabel(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Bracelet => "Bracelet", EquipmentSlot.Necklace => "Collier", EquipmentSlot.Ring => "Bague",
        EquipmentSlot.MainWeapon => "Arme principale", EquipmentSlot.OffHand => "Main secondaire",
        EquipmentSlot.Armor => "Armure", EquipmentSlot.Helmet => "Casque", EquipmentSlot.Gloves => "Gants",
        EquipmentSlot.Boots => "Bottes", _ => "Accessoire"
    };
    public static string StatLabel(StatType stat) => stat switch
    {
        StatType.Vitalite => "Vitalité", StatType.Resistance => "Résistance", StatType.Force => "Force",
        StatType.Intelligence => "Intelligence", StatType.Charisme => "Charisme", StatType.Sagesse => "Sagesse",
        StatType.Agilite => "Agilité", StatType.Chance => "Chance", StatType.PointsDeVie => "Points de vie",
        StatType.DefensePhysique => "Défense physique", StatType.DefenseMagique => "Défense magique",
        StatType.AttaquePhysique => "Attaque physique", StatType.AttaqueMagique => "Attaque magique",
        StatType.ChanceCrit => "Chance de critique", StatType.DegatsCrit => "Dégâts critiques",
        StatType.ResistanceCrit => "Résistance critique", StatType.Esquive => "Esquive", StatType.Precision => "Précision",
        StatType.PuissanceSoin => "Puissance des soins", _ => "Récupération"
    };
}
