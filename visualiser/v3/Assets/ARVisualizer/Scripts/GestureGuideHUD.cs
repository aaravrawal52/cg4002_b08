using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>A quiet edge handle when the debug HUD is hidden. Geometry stays in the HUD prefab.</summary>
    public sealed class GestureGuideHUD : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] RectTransform strip, panel;
        [SerializeField] Text contextLabel, editLabel;
        [SerializeField] Text[] actions;
        [Tooltip("Time to move from the edge strip into the guide, or briefly lose tracking.")]
        [SerializeField, Range(0.2f, 3)] float closeDelay = 1.2f;
        ARVisualizerApp app;
        VisualizerHUD hud;
        readonly bool[] overGuide = new bool[2];
        static readonly TrackedHand[] Hands = { TrackedHand.Left, TrackedHand.Right };
        float lastHover = float.NegativeInfinity;
        Canvas canvas;
        CommandProtocol.Catalogue catalogue;
        int shownEditing = -1;
        public bool IsOpen => panel != null && panel.gameObject.activeInHierarchy;
        public bool BlocksPointer(TrackedHand hand) => root != null && root.activeInHierarchy && overGuide[hand == TrackedHand.Left ? 0 : 1];

        public void Initialize(ARVisualizerApp application, VisualizerHUD owner)
        {
            app = application; hud = owner;
            panel.gameObject.SetActive(false);
            hud.VisibilityChanged += OnVisibilityChanged;
            RefreshVisibility();
        }

        void OnVisibilityChanged(bool visible)
        {
            panel.gameObject.SetActive(false);
            lastHover = float.NegativeInfinity;
            overGuide[0] = overGuide[1] = false;
            RefreshVisibility();
        }

        void Update()
        {
            if (app == null) return;
            RefreshVisibility();
            if (IsOpen && Time.unscaledTime - lastHover > closeDelay) panel.gameObject.SetActive(false);
        }

        void RefreshVisibility()
        {
            if (hud == null) return;
            bool hidden = !hud.IsHUDVisible;
            if (root.activeSelf != hidden) root.SetActive(hidden);
            // Called several times a frame: only write the label when a hand's edit mode has changed.
            int editing = 0;
            foreach (var hand in Hands)
            {
                using var scope = app.UseHand(hand);
                int mode = app.MoveModeEnabled ? 1 : !app.AdjustModeEnabled ? 0 : 2 + (int)app.EditMode;
                editing = editing * 16 + mode;
            }
            bool show = hidden && editing != 0;
            if (editLabel.gameObject.activeSelf != show) editLabel.gameObject.SetActive(show);
            if (editing == shownEditing) return;
            shownEditing = editing;
            string text = "";
            foreach (var hand in Hands)
            {
                using var scope = app.UseHand(hand);
                string mode = app.MoveModeEnabled ? "MOVE" : app.AdjustModeEnabled ? (app.EditMode == ScreenEditMode.None ? "ADJUST" : app.EditMode.ToString().ToUpperInvariant()) : "";
                if (mode.Length > 0) text += (text.Length == 0 ? "" : "   |   ") + hand + ": " + mode;
            }
            editLabel.text = text + "   /   Grab: confirm   /   Crumple: cancel";
        }

        public void RefreshPointer()
        {
            if (app == null || hud == null) return;
            RefreshVisibility();
            int index = app.CurrentHand == TrackedHand.Left ? 0 : 1;
            overGuide[index] = false;
            // Centre fallback cannot open the guide by itself.
            if (!root.activeInHierarchy || !app.PointerReady || !app.Hand.IsTracked) return;
            if (canvas == null) canvas = GetComponent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 point = app.ARCamera.ViewportToScreenPoint(app.PointerViewportPoint);
            bool onStrip = RectTransformUtility.RectangleContainsScreenPoint(strip, point, camera);
            bool onPanel = IsOpen && RectTransformUtility.RectangleContainsScreenPoint(panel, point, camera);
            if (!onStrip && !onPanel) return;
            if (!IsOpen) UpdateGuide();
            panel.gameObject.SetActive(true);
            overGuide[index] = true;
            lastHover = Time.unscaledTime;
        }

        void UpdateGuide()
        {
            catalogue ??= JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            for (int i = 0; i < actions.Length && i < catalogue.gestures.Length; i++) actions[i].text = catalogue.gestures[i].guide;
            string context = "FREE MODE · Point at a screen or a flat surface";
            if (app.MoveModeEnabled) context = "MOVE · Aim at a surface, then grab to place";
            else if (app.EditMode == ScreenEditMode.Resize) context = "RESIZE · Swipe up / down · Grab saves · Crumple restores";
            else if (app.EditMode == ScreenEditMode.Rotate) context = "ROTATE · Swipe left / right · Grab saves · Crumple restores";
            else if (app.ScreenApps.IsModal) context = "LIBRARY · Swipe to browse · Click opens the selection";
            else if (app.ScreenApps.IsChooserOpen || app.ScreenApps.IsSettingsOpen) context = "MENU · Swipe to select · Click opens · Crumple closes";
            else if (app.PointedScreen != null)
            {
                string id = app.PointedScreen.GetComponent<ScreenMedia>().AppId;
                if (id == "timer") context = "TIMER · Swipe to set digit / look · Click starts / pauses";
                if (id == "browser") context = "BROWSER · Click selects · Up / down scroll · Left / right history";
                if (id == "pdf") context = "PDF · Click selects · Up / down scroll · Files chooses another document";
                if (id == "puzzle") context = "PUZZLE · Click starts / pauses · Swipes slide tiles · Paused: up / down size, left reset";
                if (id == "flashcards") context = "CARDS · Left / right browse · Up / down reveal / hide · Click flips";
                if (id == "photos") context = "MEDIA · Left / right browse · Up opens library · Click plays / pauses";
            }
            contextLabel.text = app.CurrentHand.ToString().ToUpperInvariant() + " HAND  /  " + context;
        }

        void OnDestroy() { if (hud != null) hud.VisibilityChanged -= OnVisibilityChanged; }
    }
}
