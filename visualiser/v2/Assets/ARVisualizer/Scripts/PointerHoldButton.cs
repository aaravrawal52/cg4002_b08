using UnityEngine;
using UnityEngine.EventSystems;

namespace ARVisualizer
{
    /// <summary>A phone HUD left-click button which can hold the ray on a scrollable app menu.</summary>
    public sealed class PointerHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        ARVisualizerApp app;
        bool hasCapturedPress;
        ScreenMenuInput capturedMenu;
        bool suppressNextClick;

        public void Initialize(ARVisualizerApp application) => app = application;

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || app == null) return;

            app.SendLocal("status");
            capturedMenu = app.ScreenApps.MenuInput;
            hasCapturedPress = capturedMenu.BeginRayPress(out _);
            suppressNextClick = hasCapturedPress;
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || !hasCapturedPress) return;

            hasCapturedPress = false;
            capturedMenu?.EndRayPress(out _);
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
            if (hasCapturedPress) capturedMenu?.CancelPress();
            capturedMenu = null;
            hasCapturedPress = false;
            suppressNextClick = false;
        }
    }
}
