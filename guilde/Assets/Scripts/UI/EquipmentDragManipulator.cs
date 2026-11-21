using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class EquipmentDragManipulator : PointerManipulator
{
    private readonly GuildEquipmentPanel _presenter;
    private readonly VisualElement _root;
    private readonly EquipmentInstance _item;
    private readonly IReadOnlyDictionary<EquipmentSlot, VisualElement> _slots;
    private readonly Action<EquipmentSlot> _drop;
    private readonly Action _refresh;
    private Vector2 _origin;
    private bool _pressed;
    private bool _moved;
    private int _pointer;
    private Label _ghost;

    public EquipmentDragManipulator(GuildEquipmentPanel presenter, VisualElement root, EquipmentInstance item,
        IReadOnlyDictionary<EquipmentSlot, VisualElement> slots, Action<EquipmentSlot> drop, Action refresh)
    {
        _presenter = presenter; _root = root; _item = item; _slots = slots; _drop = drop; _refresh = refresh;
    }
    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<PointerDownEvent>(Down);
        target.RegisterCallback<PointerMoveEvent>(Move);
        target.RegisterCallback<PointerUpEvent>(Up);
        target.RegisterCallback<PointerCaptureOutEvent>(LostCapture);
        target.RegisterCallback<DetachFromPanelEvent>(Detached);
    }
    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<PointerDownEvent>(Down);
        target.UnregisterCallback<PointerMoveEvent>(Move);
        target.UnregisterCallback<PointerUpEvent>(Up);
        target.UnregisterCallback<PointerCaptureOutEvent>(LostCapture);
        target.UnregisterCallback<DetachFromPanelEvent>(Detached);
    }
    private void Down(PointerDownEvent evt)
    {
        if (evt.button != 0) return;
        for (var element = evt.target as VisualElement; element != null && element != target; element = element.parent)
            if (element is Button) return;
        _pressed = true; _moved = false; _pointer = evt.pointerId; _origin = evt.position;
        target.CapturePointer(_pointer);
    }
    private void Move(PointerMoveEvent evt)
    {
        if (!_pressed || !target.HasPointerCapture(_pointer)) return;
        Vector2 position = evt.position;
        if (!_moved && Vector2.Distance(_origin, position) > 8f)
        {
            _moved = true; _presenter.IsDragging = true;
            _ghost = new Label(_item.definition.itemName) { pickingMode = PickingMode.Ignore };
            _ghost.AddToClassList("drag-ghost"); _ghost.style.display = DisplayStyle.Flex;
            _root.Add(_ghost);
        }
        if (!_moved) return;
        Vector2 local = _root.WorldToLocal(position);
        _ghost.style.left = local.x + 14; _ghost.style.top = local.y + 12;
        foreach (var slot in _slots)
        {
            slot.Value.EnableInClassList("gear-drop-ready", slot.Key == _item.definition.slot);
            slot.Value.EnableInClassList("gear-drop-invalid", slot.Key != _item.definition.slot && slot.Value.worldBound.Contains(position));
        }
        evt.StopPropagation();
    }
    private void Up(PointerUpEvent evt)
    {
        if (!_pressed || evt.pointerId != _pointer) return;
        _pressed = false;
        if (_moved)
        {
            foreach (var slot in _slots)
                if (slot.Value.worldBound.Contains(evt.position)) { _drop(slot.Key); break; }
            Cleanup();
            evt.StopPropagation();
        }
        if (target.HasPointerCapture(_pointer)) target.ReleasePointer(_pointer);
    }
    private void LostCapture(PointerCaptureOutEvent evt)
    {
        if (!_pressed) return;
        _pressed = false; Cleanup();
    }
    private void Detached(DetachFromPanelEvent evt) { _pressed = false; Cleanup(); }
    private void Cleanup()
    {
        if (!_moved) return;
        _moved = false;
        _presenter.IsDragging = false;
        _ghost?.RemoveFromHierarchy(); _ghost = null;
        foreach (var slot in _slots)
        {
            slot.Value.RemoveFromClassList("gear-drop-ready");
            slot.Value.RemoveFromClassList("gear-drop-invalid");
        }
        _refresh();
    }
}
