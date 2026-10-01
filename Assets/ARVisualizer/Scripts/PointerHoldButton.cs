using UnityEngine;
using UnityEngine.EventSystems;

namespace ARVisualizer
{
    /// <summary>A phone HUD left-click button which can hold the ray on a scrollable app menu.</summary>
    public sealed class PointerHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        ARVisualizerApp app;
        bool hasCapturedPress;
        bool suppressNextClick;

        public void Initialize(ARVisualizerApp application) => app = application;

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || app == null) return;

            app.SendLocal("status");
            hasCapturedPress = app.ScreenApps.MenuInput.BeginRayPress(out _);
            suppressNextClick = hasCapturedPress;
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || !hasCapturedPress) return;

            hasCapturedPress = false;
            app.ScreenApps.MenuInput.EndRayPress(out _);
        }

        public void Click()
        {
            // Unity also sends Button.onClick after releasing a captured press.
            if (suppressNextClick)
            {
                suppressNextClick = false;
                return;
            }

            if (app != null) app.SendLocal("ui.click");
        }

        void OnDisable()
        {
            if (hasCapturedPress && app != null) app.ScreenApps.MenuInput.CancelPress();
            hasCapturedPress = false;
            suppressNextClick = false;
        }
    }
}
