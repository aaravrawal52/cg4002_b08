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
            root.SetActive(!hud.IsHUDVisible);
            string editing = "";
            foreach (var hand in Hands)
            {
                using var scope = app.UseHand(hand);
                string mode = app.MoveModeEnabled ? "MOVE" : app.AdjustModeEnabled ? (app.EditMode == ScreenEditMode.None ? "ADJUST" : app.EditMode.ToString().ToUpperInvariant()) : "";
                if (mode.Length > 0) editing += (editing.Length == 0 ? "" : "   |   ") + hand + ": " + mode;
            }
            editLabel.gameObject.SetActive(!hud.IsHUDVisible && editing.Length > 0);
            editLabel.text = editing + "   /   Grab: confirm   /   Crumple: cancel";
        }

        public void RefreshPointer()
        {
            if (app == null || hud == null) return;
            RefreshVisibility();
            int index = app.CurrentHand == TrackedHand.Left ? 0 : 1;
            overGuide[index] = false;
            // Centre fallback cannot open the guide by itself.
            if (!root.activeInHierarchy || !app.PointerReady || !app.Hand.IsTracked) return;
            var canvas = GetComponent<Canvas>();
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
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            for (int i = 0; i < actions.Length && i < catalogue.gestures.Length; i++) actions[i].text = catalogue.gestures[i].guide;
            string context = "FREE MODE · Point at a screen or a flat surface";
            if (app.MoveModeEnabled) context = "MOVE · Aim at a surface, then grab to place";
            else if (app.EditMode == ScreenEditMode.Resize) context = "RESIZE · Swipe up / down · Grab saves · Crumple restores";
            else if (app.EditMode == ScreenEditMode.Rotate) context = "ROTATE · Swipe left / right · Grab saves · Crumple restores";
            else if (app.ScreenApps.IsModal) context = "LIBRARY · Left / right select · Up / down scroll · Grab opens";
            else if (app.PointedScreen != null)
            {
                string id = app.PointedScreen.GetComponent<ScreenMedia>().AppId;
                if (id == "timer") context = "TIMER · Left / right choose digit · Up / down set · Grab starts / pauses";
                if (id == "flashcards") context = "CARDS · Left / right browse · Up / down reveal / hide · Grab flips";
                if (id == "photos") context = "MEDIA · Left / right browse · Up opens library · Grab plays / pauses";
            }
            contextLabel.text = app.CurrentHand.ToString().ToUpperInvariant() + " HAND  /  " + context;
        }

        void OnDestroy() { if (hud != null) hud.VisibilityChanged -= OnVisibilityChanged; }
    }
}
