using UnityEngine;
using UnityEngine.UIElements;

public class AdventurerDragManipulator : PointerManipulator
{
    private readonly GuildWorkshopUI _ui;
    private readonly string _adventurerId;
    private Vector2 _origin;
    private bool _pressed;
    private bool _moved;
    private bool _dragStarted;
    private int _pointerId;

    public AdventurerDragManipulator(GuildWorkshopUI ui, string adventurerId)
    {
        _ui = ui;
        _adventurerId = adventurerId;
    }
    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<PointerDownEvent>(OnDown);
        target.RegisterCallback<PointerMoveEvent>(OnMove);
        target.RegisterCallback<PointerUpEvent>(OnUp);
        target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
    }
    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<PointerDownEvent>(OnDown);
        target.UnregisterCallback<PointerMoveEvent>(OnMove);
        target.UnregisterCallback<PointerUpEvent>(OnUp);
        target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
    }
    private void OnDown(PointerDownEvent evt)
    {
        if (evt.button != 0) return;
        for (var element = evt.target as VisualElement; element != null && element != target; element = element.parent)
            if (element is Button) return;
        if (_ui.RosterPointerHeld) return;
        _origin = evt.position;
        _pointerId = evt.pointerId;
        _pressed = _ui.RosterPointerHeld = true;
        _moved = _dragStarted = false;
        target.CapturePointer(evt.pointerId);
        target.Focus();
    }
    private void OnMove(PointerMoveEvent evt)
    {
        if (!_pressed || evt.pointerId != _pointerId || !target.HasPointerCapture(evt.pointerId)) return;
        Vector2 position = evt.position;
        if (!_moved && Vector2.Distance(_origin, position) > 8f)
        {
            _moved = true;
            _dragStarted = _ui.BeginDrag(_adventurerId, position);
        }
        if (!_dragStarted) return;
        _ui.MoveDrag(position);
        evt.StopPropagation();
    }
    private void OnUp(PointerUpEvent evt)
    {
        if (!_pressed || evt.pointerId != _pointerId) return;
        _pressed = _ui.RosterPointerHeld = false;
        if (_dragStarted) _ui.EndDrag(_adventurerId, evt.position, true);
        else if (!_moved && target.worldBound.Contains(evt.position)) _ui.SelectRosterAdventurer(_adventurerId);
        if (target.HasPointerCapture(evt.pointerId)) target.ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }
    private void OnCaptureOut(PointerCaptureOutEvent evt)
    {
        if (!_pressed || evt.pointerId != _pointerId) return;
        _pressed = _ui.RosterPointerHeld = false;
        if (_dragStarted) _ui.EndDrag(_adventurerId, _origin, false);
    }
}
