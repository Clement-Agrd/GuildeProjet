using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

public partial class GuildWorkshopUI
{
    private sealed class JournalAction
    {
        public string Label;
        public Func<string> GetBlockedReason;
        public Action Execute;
    }

    private readonly List<(string Message, JournalAction Action)> _journal = new();
    private Toggle _autoPause;

    private void BindJournalControls()
    {
        _autoPause = _root.Q<Toggle>("auto-pause");
        _root.Q<Button>("open-journal").clicked += () => SetView("journal");
    }

    private void AddJournal(string message, JournalAction action = null, bool pause = true)
    {
        _journal.Insert(0, (message, action));
        if (_journal.Count > 30) _journal.RemoveAt(_journal.Count - 1);
        if (pause && _autoPause.value && game.IsInitialized) clock.SetSpeed(GameSpeed.Paused);
    }

    private void Notify(string message) => Notify(message, null);

    private void Notify(string message, JournalAction action)
    {
        _root.Q<Label>("status").text = message;
        AddJournal(message, action);
        MarkDirty();
    }

    private void HandleRested(Adventurer adventurer) => NotifyProfile(adventurer, "a terminé son repos");

    private void HandleTavernRecovery(Adventurer adventurer, bool rested, bool moraleRestored)
    {
        string update = rested && moraleRestored ? "a terminé son repos et retrouve un moral de 100 %"
            : rested ? "a terminé son repos" : "retrouve un moral de 100 %";
        NotifyProfile(adventurer, update);
    }

    private void NotifyProfile(Adventurer adventurer, string update)
    {
        Notify($"{adventurer.adventurerName} {update}.", new JournalAction
        {
            Label = "Voir la fiche",
            GetBlockedReason = () => adventurers.GetById(adventurer.id) == adventurer ? null : "Aventurier indisponible.",
            Execute = () => SelectAdventurer(adventurer.id)
        });
    }

    private void RenderJournal()
    {
        var entries = _root.Q("journal-entries");
        entries.Clear();
        if (_view == "journal")
        {
            Heading("LA GUILDE", "Journal de la guilde");
            entries = _content;
        }

        foreach (var entry in _journal)
        {
            var item = new VisualElement();
            item.AddToClassList("journal-entry");
            entries.Add(item);
            Text(item, entry.Message, "journal-line");
            var action = entry.Action;
            if (action == null) continue;
            var button = ActionButton(item, action.Label, action.Execute);
            button.AddToClassList("journal-action");
            if (action.GetBlockedReason == null) continue;

            var status = Text(item, "", "journal-action-status");
            void RefreshAction()
            {
                string reason = action.GetBlockedReason();
                button.SetEnabled(reason == null);
                status.text = reason;
                status.style.display = reason == null ? DisplayStyle.None : DisplayStyle.Flex;
            }

            RefreshAction();
            _liveUpdates.Add(RefreshAction);
        }
    }

    private void NotifyExhaustion(Adventurer adventurer)
    {
        bool sent = false;
        string BlockedReason()
        {
            if (sent || adventurer.currentService == BuildingType.Tavern) return "Envoyé à la taverne.";
            if (adventurers.GetById(adventurer.id) != adventurer || adventurer.currentStateType == AdventurerStateType.Dead)
                return "Aventurier indisponible.";
            if (adventurer.currentStateType != AdventurerStateType.Resting)
                return "Cet aventurier a quitté le repos.";
            var tavern = buildings.GetBuilding(BuildingType.Tavern);
            if (tavern?.IsActive != true) return "Taverne indisponible.";
            return buildings.CanStartService(adventurer, tavern) ? null : "Aucune place à la taverne.";
        }

        Notify($"{adventurer.adventurerName} est épuisé (fatigue 100 %). Son poste est libre.",
            new JournalAction
            {
                Label = "Envoyer à la taverne",
                GetBlockedReason = BlockedReason,
                Execute = () =>
                {
                    if (BlockedReason() != null) return;
                    sent = buildings.StartService(adventurer, buildings.GetBuilding(BuildingType.Tavern));
                }
            });
    }
}
