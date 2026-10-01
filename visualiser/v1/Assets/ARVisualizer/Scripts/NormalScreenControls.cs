using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Phone-only controls, with a locked selection while editing a screen.</summary>
    public sealed class NormalScreenControls : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] GameObject panel;
        [SerializeField] Text selectionLabel;
        [SerializeField] Button settings;
        [SerializeField] Button leftClick;
        [SerializeField] Button rightClick;
        [SerializeField] Button grow;
        [SerializeField] Button shrink;
        [SerializeField] Button clockwise;
        [SerializeField] Button counterclockwise;

        ARVisualizerApp app;
        VisualizerHUD hud;
        ScreenSurface selected;

        public bool IsOpen => panel != null && panel.activeInHierarchy;

        public void Initialize(ARVisualizerApp application, VisualizerHUD visualizerHUD)
        {
            app = application;
            hud = visualizerHUD;
            panel.SetActive(false);
            hud.VisibilityChanged += OnVisibilityChanged;
            Refresh();
        }

        void Update() => Refresh();
        void OnVisibilityChanged(bool visible) => Refresh();

        public void Refresh()
        {
            if (app == null || root == null) return;
            bool visible = !app.GoggleModeEnabled && hud.IsHUDVisible;
            if (!visible || (panel.activeSelf && !HasValidSelection())) Close();
            root.SetActive(visible);

            leftClick.interactable = rightClick.interactable = app.PointerReady;
            bool canSelectScreen = app.PointerReady && app.PointedScreen != null;
            bool mediaPickerOpen = app.ScreenApps != null && app.ScreenApps.IsModal;
            settings.interactable = !mediaPickerOpen && (panel.activeSelf || app.AdjustModeEnabled || canSelectScreen);
            RefreshSelection();
        }

        bool HasValidSelection() => selected != null && selected.isActiveAndEnabled && app.AdjustedScreen == selected;

        void RefreshSelection()
        {
            if (!panel.activeSelf || selected == null) return;
            selectionLabel.text = "SCREEN " + selected.Id + "  /  " + (selected.Width * 100).ToString("0.#")
                + " x " + (selected.Height * 100).ToString("0.#") + " cm";
            grow.interactable = selected.SizeSteps < ScreenSurface.MaximumSizeSteps;
            shrink.interactable = selected.SizeSteps > 1;
            clockwise.interactable = counterclockwise.interactable = selected.CanRotate;
        }

        public void ToggleSettings()
        {
            if (app == null || app.GoggleModeEnabled) return;
            if (panel.activeSelf)
            {
                Close();
                return;
            }
            app.Execute(new VisualizerCommand { command = "adjust.enter" }, reply =>
            {
                if (!reply.ok) return;
                selected = app.AdjustedScreen;
                app.ScreenApps?.Close();
                panel.SetActive(true);
                Refresh();
            });
        }

        public void ChooseApp()
        {
            var screen = selected;
            Close();
            if (screen != null && screen.isActiveAndEnabled) app.ScreenApps?.ShowPinnedChooser(screen);
        }

        public void DeleteScreen()
        {
            var screen = selected;
            Close();
            if (app != null && screen != null) app.DeleteScreen(screen);
        }

        public void Grow() => app.SendLocal("adjust.grow");
        public void Shrink() => app.SendLocal("adjust.shrink");
        public void RotateClockwise() => app.SendLocal("adjust.rotate.cw");
        public void RotateCounterclockwise() => app.SendLocal("adjust.rotate.ccw");

        public void Close()
        {
            // Clear the local selection before adjust.exit refreshes the HUD again.
            var screen = selected;
            selected = null;
            if (panel != null) panel.SetActive(false);
            if (app != null && screen != null && app.AdjustedScreen == screen) app.SendLocal("adjust.exit");
        }

        void OnDisable()
        {
            if (app != null) Close();
        }

        void OnDestroy()
        {
            if (hud != null) hud.VisibilityChanged -= OnVisibilityChanged;
        }
    }
}
