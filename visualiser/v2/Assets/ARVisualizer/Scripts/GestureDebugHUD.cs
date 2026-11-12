using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Phone-only gesture simulator. Uses the real gesture mappings without faking Wi-Fi receipts.</summary>
    public sealed class GestureDebugHUD : MonoBehaviour
    {
        [SerializeField, HideInInspector] int gestureLayoutVersion;
        [SerializeField] GameObject root;
        [SerializeField] GameObject panel;
        [SerializeField] Text result;
        [SerializeField] Button toggle, leftHand, rightHand;
        [SerializeField] Color selectedColor = new Color(0.2f, 0.65f, 0.5f);
        [SerializeField] Color idleColor = new Color(0.08f, 0.16f, 0.19f, 0.97f);
        ARVisualizerApp app;
        VisualizerHUD hud;
        public TrackedHand SelectedHand { get; private set; } = TrackedHand.Right;
        public bool IsOpen => panel != null && panel.activeInHierarchy;

        public void Initialize(ARVisualizerApp application, VisualizerHUD owner)
        {
            app = application;
            hud = owner;
            SelectedHand = app.ControlHand;
            panel.SetActive(false);
            hud.VisibilityChanged += OnVisibilityChanged;
            Refresh();
        }

        void Update() => Refresh();
        void OnVisibilityChanged(bool visible) => Refresh();
        public void Refresh()
        {
            if (app == null || hud == null) return;
            bool visible = hud.IsHUDVisible && !app.GoggleModeEnabled;
            if (!visible) panel.SetActive(false);
            root.SetActive(visible);
            toggle.targetGraphic.color = IsOpen ? selectedColor : idleColor;
            leftHand.targetGraphic.color = SelectedHand == TrackedHand.Left ? selectedColor : idleColor;
            rightHand.targetGraphic.color = SelectedHand == TrackedHand.Right ? selectedColor : idleColor;
        }

        public void TogglePanel()
        {
            if (app == null || !hud.IsHUDVisible || app.GoggleModeEnabled) return;
            panel.SetActive(!panel.activeSelf);
            Refresh();
        }
        public void SelectLeftHand() { SelectedHand = TrackedHand.Left; Refresh(); }
        public void SelectRightHand() { SelectedHand = TrackedHand.Right; Refresh(); }
        public void TriggerGesture(string gesture)
        {
            if (app == null || !IsOpen || app.GoggleModeEnabled || !hud.IsHUDVisible) return;
            if (!CommandProtocol.TryParseGesture(gesture, SelectedHand == TrackedHand.Left ? "left" : "right", out var command, out var error))
            { result.text = error; return; }
            string label = (SelectedHand == TrackedHand.Left ? "L / " : "R / ") + gesture.Replace('_', ' ');
            app.Execute(command, reply =>
            {
                if (this != null && result != null) result.text = label + "\n" + reply.message;
            });
        }

        void OnDestroy() { if (hud != null) hud.VisibilityChanged -= OnVisibilityChanged; }
    }
}
