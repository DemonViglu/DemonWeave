using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace DemonWeave.Presentation
{
    // Compatibility for an editor session that still delivers legacy input after switching to Both.
    // Native EventSystem raycasts and handlers retain CanvasGroup/interactable/modal behaviour.
    [RequireComponent(typeof(EventSystem))]
    public sealed class LegacyUiPointerBridge : MonoBehaviour
    {
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private GameObject pressed;
        private PointerEventData pointer;
        private EventSystem events;
        private InputSystemUIInputModule nativeModule;
        private void Awake() { events = GetComponent<EventSystem>(); nativeModule = GetComponent<InputSystemUIInputModule>(); }
        private void LateUpdate()
        {
            #if ENABLE_LEGACY_INPUT_MANAGER
            bool nativePress = nativeModule && nativeModule.isActiveAndEnabled && nativeModule.leftClick?.action?.WasPressedThisFrame() == true;
            if (Input.GetMouseButtonDown(0) && !nativePress)
            {
                pointer = new PointerEventData(events) { position = Input.mousePosition, button = PointerEventData.InputButton.Left, clickCount = 1, eligibleForClick = true };
                hits.Clear(); events.RaycastAll(pointer, hits);
                var target = hits.Count > 0 ? hits[0].gameObject : null;
                pressed = target ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) : null;
                if (pressed)
                {
                    pointer.pressPosition = pointer.position; pointer.pointerPress = pressed;
                    ExecuteEvents.ExecuteHierarchy(pressed, pointer, ExecuteEvents.pointerDownHandler);
                }
            }
            if (pointer != null && Input.GetMouseButtonUp(0))
            {
                pointer.position = Input.mousePosition; hits.Clear(); events.RaycastAll(pointer, hits);
                var target = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
                if (pressed)
                {
                    ExecuteEvents.ExecuteHierarchy(pressed, pointer, ExecuteEvents.pointerUpHandler);
                    if (target == pressed) ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerClickHandler);
                }
                pointer = null; pressed = null;
            }
            #endif
        }
        private void OnDisable() { pointer = null; pressed = null; }
    }
}
