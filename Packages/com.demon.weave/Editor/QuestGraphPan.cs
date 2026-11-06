using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    // Right-click opens the menu; a right-button drag pans without opening it.
    internal sealed class QuestGraphPan : MouseManipulator
    {
        private bool pressed, dragging, suppressMenu;
        private Vector2 origin;
        private Vector3 viewOrigin;
        private GraphView Graph => (GraphView)target;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(Down, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseMoveEvent>(Move, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseUpEvent>(Up, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseCaptureOutEvent>(LostCapture);
            target.RegisterCallback<ContextualMenuPopulateEvent>(Menu, TrickleDown.TrickleDown);
        }
        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(Down, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseMoveEvent>(Move, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseUpEvent>(Up, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseCaptureOutEvent>(LostCapture);
            target.UnregisterCallback<ContextualMenuPopulateEvent>(Menu, TrickleDown.TrickleDown);
        }
        private void Down(MouseDownEvent evt)
        {
            if (evt.button != 1) return;
            pressed = true; dragging = false; suppressMenu = false;
            origin = evt.mousePosition; viewOrigin = Graph.viewTransform.position;
        }
        private void Move(MouseMoveEvent evt)
        {
            if (!pressed) return;
            if ((evt.pressedButtons & 2) == 0) { End(); return; }
            var delta = evt.mousePosition - origin;
            if (!dragging && delta.sqrMagnitude < 16) return;
            if (!dragging) { dragging = true; suppressMenu = true; target.CaptureMouse(); }
            Graph.UpdateViewTransform(viewOrigin + (Vector3)delta, Graph.viewTransform.scale);
            evt.StopImmediatePropagation();
        }
        private void Up(MouseUpEvent evt)
        {
            if (evt.button != 1 || !pressed) return;
            bool moved = dragging;
            End();
            if (moved) evt.StopImmediatePropagation();
        }
        private void End()
        {
            pressed = false; dragging = false;
            if (target.HasMouseCapture()) target.ReleaseMouse();
        }
        private void LostCapture(MouseCaptureOutEvent evt) { pressed = false; dragging = false; }
        private void Menu(ContextualMenuPopulateEvent evt)
        {
            if (!suppressMenu) return;
            evt.menu.ClearItems(); evt.StopImmediatePropagation();
        }
    }
}
