using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ARVisualizer
{
    /// <summary>Converts touches in empty phone space into ray clicks or gallery drags.</summary>
    [DefaultExecutionOrder(100), DisallowMultipleComponent]
    [AddComponentMenu("AR Visualizer/Phone Pointer Clicks")]
    public sealed class PhonePointerClicks : MonoBehaviour
    {
        const int NoHeldTouch = -1;

        ARVisualizerApp app;
        VisualizerHUD hud;
        PointerEventData uiProbe;
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        int heldTouchId = NoHeldTouch;
        Vector2 touchStartPosition;

        bool CanUsePhonePointer => isActiveAndEnabled && app != null && app.isActiveAndEnabled
            && !app.GoggleModeEnabled && app.Hand.PointerEnabled;

        public void Initialize(ARVisualizerApp application, VisualizerHUD visualizerHUD)
        {
            app = application;
            hud = visualizerHUD;
        }

        public bool TryClickAt(Vector2 position)
        {
            if (!CanUsePhonePointer || !IsInsideScreen(position) || IsOverGUI(position)) return false;
            app.SendLocal(IsLeftHalf(position) ? "ui.click" : "ui.rightclick");
            return true;
        }

        public bool IsOverGUI(Vector2 position)
        {
            if (app != null && app.ModeButton != null && app.ModeButton.ContainsScreenPoint(position)) return true;
            // Include informational panels even when their Graphics don't consume Unity clicks.
            if (hud != null && hud.ContainsPhoneUI(position)) return true;
            if (EventSystem.current == null) return true;

            uiProbe ??= new PointerEventData(EventSystem.current);
            uiProbe.position = position;
            uiHits.Clear();
            EventSystem.current.RaycastAll(uiProbe, uiHits);
            foreach (var hit in uiHits)
            {
                if (hit.module is GraphicRaycaster) return true;
            }
            return false;
        }

        void LateUpdate()
        {
            if (!CanUsePhonePointer)
            {
                CancelHold();
                return;
            }

            foreach (var touch in Touch.activeTouches)
            {
                if (touch.touchId == heldTouchId) UpdateHeldTouch(touch);
                else if (touch.phase == TouchPhase.Began) BeginTouch(touch);
            }
        }

        void BeginTouch(Touch touch)
        {
            bool canHold = heldTouchId == NoHeldTouch
                && IsLeftHalf(touch.screenPosition) && !IsOverGUI(touch.screenPosition);
            if (canHold && app.ScreenApps.MenuInput.BeginRayPress(out _))
            {
                heldTouchId = touch.touchId;
                touchStartPosition = touch.screenPosition;
                return;
            }

            TryClickAt(touch.screenPosition);
        }

        void UpdateHeldTouch(Touch touch)
        {
            switch (touch.phase)
            {
                case TouchPhase.Canceled:
                    CancelHold();
                    break;
                case TouchPhase.Ended:
                    heldTouchId = NoHeldTouch;
                    app.ScreenApps.MenuInput.EndRayPress(out _);
                    break;
                default:
                    app.ScreenApps.MenuInput.DragFromPhone(touch.screenPosition - touchStartPosition);
                    break;
            }
        }

        void CancelHold()
        {
            if (heldTouchId != NoHeldTouch && app != null && app.ScreenApps != null)
            {
                app.ScreenApps.MenuInput.CancelPress();
            }
            heldTouchId = NoHeldTouch;
        }

        static bool IsLeftHalf(Vector2 position) => position.x < Screen.width * 0.5f;

        static bool IsInsideScreen(Vector2 position)
        {
            return position.x >= 0 && position.y >= 0
                && position.x < Screen.width && position.y < Screen.height;
        }

        void OnEnable() => EnhancedTouchSupport.Enable();

        void OnDisable()
        {
            CancelHold();
            EnhancedTouchSupport.Disable();
        }
    }
}
