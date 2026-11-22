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
        public Func<bool> Resolve;
    }

    private sealed class JournalEntry
    {
        public string Message;
        public JournalAction[] Actions;
        public Func<bool> NeedsDecision;
        public bool HoldsPause;
        public bool Resolved;
        public bool IsPending => !Resolved && NeedsDecision?.Invoke() == true;
    }

    private readonly List<JournalEntry> _journal = new();
    private Toggle _autoPause;
    private bool _journalPausedClock;

    private void BindJournalControls()
    {
        _autoPause = _root.Q<Toggle>("auto-pause");
        _root.Q<Button>("open-journal").clicked += () => SetView("journal");
    }

    private void AddJournal(string message, JournalAction action = null, bool blocksProgress = false, Func<bool> needsDecision = null)
    {
        var actions = action == null ? Array.Empty<JournalAction>() : new[] { action };
        if (!blocksProgress)
        {
            AddJournalEntry(new JournalEntry { Message = message, Actions = actions }, false);
            return;
        }
        AddDecision(message, needsDecision ?? (() => true), action == null
            ? new[] { IgnoreDecision() }
            : new[] { action, IgnoreDecision() });
    }

    private void AddDecision(string message, Func<bool> needsDecision, params JournalAction[] actions)
        => AddJournalEntry(new JournalEntry { Message = message, Actions = actions, NeedsDecision = needsDecision }, needsDecision());

    private void AddJournalEntry(JournalEntry entry, bool blocksProgress)
    {
        _journal.Insert(0, entry);
        if (blocksProgress && _autoPause.value && game.IsInitialized)
        {
            _journalPausedClock |= clock.CurrentSpeed != GameSpeed.Paused;
            entry.HoldsPause = _journalPausedClock && entry.NeedsDecision != null;
            clock.SetSpeed(GameSpeed.Paused);
        }
        if (_journal.Count > 30)
        {
            int oldest = _journal.FindLastIndex(e => !e.HoldsPause || !e.IsPending);
            if (oldest >= 0) _journal.RemoveAt(oldest);
        }
        UpdateStatusText();
        MarkDirty();
    }

    private void HandleJournalSpeed(GameSpeed speed)
    {
        if (speed == GameSpeed.Paused || !_journalPausedClock) return;
        _journalPausedClock = false;
        foreach (var entry in _journal) entry.HoldsPause = false;
        UpdateStatusText();
        MarkDirty();
    }

    private void Notify(string message) => Notify(message, null);

    private void Notify(string message, JournalAction action, bool blocksProgress = false, Func<bool> needsDecision = null)
        => AddJournal(message, action, blocksProgress, needsDecision);

    private void HandleRested(Adventurer adventurer) => NotifyProfile(adventurer, "a terminé son repos");

    private void HandleTavernRecovery(Adventurer adventurer, bool rested, bool moraleRestored)
    {
        string update = rested && moraleRestored ? "a terminé son repos et retrouve un moral de 100 %"
            : rested ? "a terminé son repos" : "retrouve un moral de 100 %";
        NotifyProfile(adventurer, update);
    }

    private void NotifyProfile(Adventurer adventurer, string update)
    {
        var profile = new JournalAction
        {
            Label = "Voir la fiche",
            GetBlockedReason = () => adventurers.GetById(adventurer.id) == adventurer ? null : "Aventurier indisponible.",
            Execute = () => SelectAdventurer(adventurer.id)
        };
        string message = $"{adventurer.adventurerName} {update}.";
        if (!adventurer.IsAvailable)
        {
            Notify(message, profile);
            return;
        }

        string previousBuildingId = adventurer.previousBuildingId;
        bool IsAvailable() => adventurer.IsAvailable && adventurers.GetById(adventurer.id) == adventurer;
        string ReturnBlockedReason()
        {
            if (!IsAvailable()) return "Aventurier indisponible.";
            if (string.IsNullOrEmpty(previousBuildingId)) return "Aucun ancien poste.";
            var previousBuilding = buildings.GetBuildingById(previousBuildingId);
            if (previousBuilding?.IsActive != true) return "Ancien poste indisponible.";
            return previousBuilding.HasFreeSlot ? null : "L’ancien poste est occupé.";
        }

        AddDecision(message, IsAvailable, new JournalAction
        {
            Label = "Envoyer à son ancien poste",
            GetBlockedReason = ReturnBlockedReason,
            Resolve = () => buildings.AssignAdventurer(adventurer, buildings.GetBuildingById(previousBuildingId).buildingType)
        }, profile, IgnoreDecision());
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
            RenderJournalActions(item, entry);
        }
        var statusActions = _root.Q("status-actions");
        statusActions.Clear();
        var latest = UpdateStatusText();
        if (latest != null) RenderJournalActions(statusActions, latest);
    }

    private JournalEntry UpdateStatusText()
    {
        var latest = _journal.Find(entry => entry.HoldsPause && entry.IsPending);
        if (latest == null && _journal.Count != 0) latest = _journal[0];
        if (latest != null) _root.Q<Label>("status").text = latest.Message;
        return latest;
    }

    private static JournalAction IgnoreDecision() => new() { Label = "Ignorer", Resolve = () => true };

    private void RenderJournalActions(VisualElement parent, JournalEntry entry)
    {
        if (entry.Actions.Length == 0) return;
        var row = new VisualElement();
        row.AddToClassList("row");
        parent.Add(row);
        foreach (var action in entry.Actions)
        {
            if (action.Resolve != null && !entry.IsPending) continue;
            var button = ActionButton(row, action.Label, () => ExecuteJournalAction(entry, action));
            button.AddToClassList("journal-action");
            if (action.GetBlockedReason == null) continue;
            var status = Text(row, "", "journal-action-status");
            void RefreshAction()
            {
                string reason = action.GetBlockedReason();
                button.SetEnabled(reason == null);
                button.tooltip = reason;
                status.text = reason;
                status.style.display = reason == null ? DisplayStyle.None : DisplayStyle.Flex;
            }
            RefreshAction();
            _liveUpdates.Add(RefreshAction);
        }
    }

    private void ExecuteJournalAction(JournalEntry entry, JournalAction action)
    {
        if (!_journal.Contains(entry) || action.GetBlockedReason?.Invoke() != null) return;
        if (action.Resolve == null)
        {
            action.Execute();
            return;
        }
        if (!entry.IsPending || !action.Resolve()) return;
        entry.Resolved = true;
        if (clock.CurrentSpeed == GameSpeed.Paused && !_journal.Exists(other => other.HoldsPause && other.IsPending))
            clock.Resume();
        UpdateStatusText();
        MarkDirty();
    }

    private void NotifyExhaustion(Adventurer adventurer)
    {
        bool StillExhausted() => adventurers.GetById(adventurer.id) == adventurer
            && adventurer.currentStateType == AdventurerStateType.Resting && adventurer.currentService != BuildingType.Tavern;
        string BlockedReason()
        {
            if (!StillExhausted()) return "Aventurier indisponible.";
            var tavern = buildings.GetBuilding(BuildingType.Tavern);
            if (tavern?.IsActive != true) return "Taverne indisponible.";
            return buildings.CanStartService(adventurer, tavern) ? null : "Aucune place à la taverne.";
        }

        AddDecision($"{adventurer.adventurerName} est épuisé (fatigue 100 %). Son poste est libre.", StillExhausted,
            new JournalAction
            {
                Label = "Envoyer à la taverne",
                GetBlockedReason = BlockedReason,
                Resolve = () => buildings.StartService(adventurer, buildings.GetBuilding(BuildingType.Tavern))
            }, IgnoreDecision());
    }
}
