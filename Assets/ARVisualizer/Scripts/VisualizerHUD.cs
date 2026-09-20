using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Updates the saved HUD's content. Layout and button events are authored in the prefab.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AR Visualizer/HUD")]
    public sealed class VisualizerHUD : MonoBehaviour
    {
        [Header("Application")]
        [Tooltip("Assigned by ARVisualizerApp at startup. The prefab can be edited on its own.")]
        [SerializeField] ARVisualizerApp app;
        [Tooltip("How often status text refreshes. Aim movement updates every frame.")]
        [SerializeField, Min(0.02f)] float refreshInterval = 0.1f;

        [Header("HUD visibility")]
        [Tooltip("Panels and aim marker hidden by the toggle. Keep the toggle button outside these objects.")]
        [SerializeField] GameObject[] hideableContent = new GameObject[0];
        public bool IsHUDVisible { get; private set; } = true;
        public event System.Action<bool> VisibilityChanged;
        bool[] contentWasActive;

        [Header("Live status labels")]
        [SerializeField] Text tracking;
        [SerializeField] Text hand;
        [SerializeField] Text network;
        [SerializeField] Text counter;
        [SerializeField] Text section;
        [SerializeField] Text target;
        [SerializeField] Text detail;
        [SerializeField] Text feedback;
        [SerializeField] Text sizeLabel;

        [Header("Controls")]
        [Tooltip("Buttons use their Inspector On Click events. These references update labels and availability only.")]
        [SerializeField] Text pointerLabel;
        [SerializeField] Text modeLabel;
        [SerializeField] Button place;
        [SerializeField] Button undo;

        [Header("Aim marker")]
        [Tooltip("Only this empty wrapper follows the hand. Edit the RectTransforms and Images inside it to style the marker.")]
        [SerializeField] RectTransform aimPosition;
        [SerializeField] GameObject cursorVisual;
        [SerializeField] Graphic cursorIndicator;

        [Header("State colors")]
        [Tooltip("Disable to use the target Text component's own color in every state.")]
        [SerializeField] bool tintTargetStatus = true;
        [SerializeField] Color readyColor = new Color(0.4f, 1, 0.8f);
        [SerializeField] Color idleColor = Color.white;
        [Tooltip("Disable to use the aim marker's Image colors in every state.")]
        [SerializeField] bool tintAimIndicator = true;
        [SerializeField] Color noSurfaceColor = new Color(1, 0.72f, 0.3f);

        [Header("Status messages")]
        [SerializeField] string viewHeading = "VIEW MODE";
        [SerializeField] string placeHeading = "PLACE MODE";
        [SerializeField] string enterPlaceMessage = "Enter place mode";
        [SerializeField] string readyMessage = "Ready to place";
        [SerializeField] string scanMessage = "Scan a flat surface";
        [SerializeField] string showHandMessage = "Show your index knuckle";

        [Header("Button captions")]
        [SerializeField] string pointerOnCaption = "POINTER  ON";
        [SerializeField] string pointerOffCaption = "POINTER  OFF";
        [SerializeField] string enterPlaceCaption = "ENTER PLACE";
        [SerializeField] string exitPlaceCaption = "EXIT PLACE";

        float refreshAt;
        bool pointerWasLocked;
        bool pointerWasInteractable;

        public bool HasRequiredReferences => tracking != null && hand != null && network != null && counter != null
            && section != null && target != null && detail != null && feedback != null && sizeLabel != null
            && pointerLabel != null && modeLabel != null && place != null && undo != null
            && aimPosition != null && cursorVisual != null && cursorIndicator != null;

        public void Initialize(ARVisualizerApp value)
        {
            app = value;
            refreshAt = 0;
            RefreshStatus();
        }

        // These methods appear in Button > On Click in Unity's Inspector.
        public void ToggleHUD() => SetHUDVisible(!IsHUDVisible);
        public void SetHUDVisible(bool visible)
        {
            if (visible == IsHUDVisible) return;
            IsHUDVisible = visible;
            if (!visible) contentWasActive = new bool[hideableContent.Length];
            for (int i = 0; i < hideableContent.Length; ++i)
            {
                var item = hideableContent[i];
                if (item == null || item == gameObject || item.GetComponentInChildren<HUDVisibilityToggle>(true) != null) continue;
                if (!visible) contentWasActive[i] = item.activeSelf;
                item.SetActive(visible && contentWasActive != null && i < contentWasActive.Length && contentWasActive[i]);
            }
            VisibilityChanged?.Invoke(visible);
        }

        public void TogglePointer() => Send(app != null && app.Hand != null && app.Hand.PointerEnabled ? "pointer.off" : "pointer.on");
        public void TogglePlaceMode() => Send(app != null && app.PlaceModeEnabled ? "place.exit" : "place.enter");
        public void PlaceScreen() => Send("screen.place");
        public void UndoScreen() => Send("screen.undo");
        public void ClearScreens() => Send("screens.clear");
        public void EnterAdjustMode() => Send("adjust.enter");
        public void ExitAdjustMode() => Send("adjust.exit");
        public void GrowScreen() => Send("adjust.grow");
        public void ShrinkScreen() => Send("adjust.shrink");
        public void RotateScreenClockwise() => Send("adjust.rotate.cw");
        public void RotateScreenCounterclockwise() => Send("adjust.rotate.ccw");
        // Existing custom HUD prefabs keep their saved button events working.
        public void PlaceCube() => PlaceScreen();
        public void UndoCube() => UndoScreen();
        public void ClearCubes() => ClearScreens();
        public void EnterGoggleMode() => Send("goggle.enter");
        public void ExitGoggleMode() => Send("goggle.exit");

        void Send(string command)
        {
            if (!Application.isPlaying || app == null) return;
            app.SendLocal(command);
            RefreshStatus();
        }

        void Update()
        {
            if (app == null || app.Hand == null) return;
            Vector2 point = app.Hand.PointerEnabled ? app.Hand.ViewportPoint : Vector2.one * 0.5f;
            bool visible = app.Hand.PointerEnabled ? app.Hand.IsTracked : app.PlaceModeEnabled;
            if (app.GoggleModeEnabled) visible &= app.GoggleHUD.ViewportToPanel(point, out point);
            SetAim(visible, point, app.HasTarget || app.RayScreen != null || (app.GoggleModeEnabled && app.GoggleHUD.HoveredButton != null));
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + refreshInterval;
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            if (app == null || app.Hand == null || app.Communication == null) return;
            SetText(tracking, app.TrackingStatus);
            SetText(hand, app.Hand.Status);
            SetText(network, "WI-FI  " + app.Communication.Status + "  /  " + app.Communication.Endpoint);
            Vector2 size = app.AdjustedScreen != null ? new Vector2(app.AdjustedScreen.Width, app.AdjustedScreen.Height) : app.DefaultScreenSize;
            SetText(sizeLabel, (size.x * 100).ToString("0.#") + " x " + (size.y * 100).ToString("0.#") + " x 0.5 cm  /  16:9");
            ApplyState(app.PlaceModeEnabled, app.Hand.PointerEnabled, app.Hand.IsTracked,
                app.HasTarget, app.IsPlacing, app.ScreenCount, app.SurfaceCount, app.Feedback);
            if (app.AdjustModeEnabled) { SetText(section, "ADJUST MODE"); SetText(target, "Screen " + app.AdjustedScreen.Id + " selected"); }
            if (pointerLabel != null)
            {
                var button = pointerLabel.GetComponentInParent<Button>();
                if (button != null)
                {
                    if (app.GoggleModeEnabled && !pointerWasLocked) pointerWasInteractable = button.interactable;
                    if (app.GoggleModeEnabled) button.interactable = false;
                    else if (pointerWasLocked) button.interactable = pointerWasInteractable;
                }
            }
            pointerWasLocked = app.GoggleModeEnabled;
            if (app.GoggleModeEnabled) SetText(section, "GOGGLE / " + (app.AdjustModeEnabled ? "ADJUST" : app.PlaceModeEnabled ? "PLACE" : "VIEW"));
            if (app.InputScreen != null)
            {
                var screen = app.InputScreen;
                if (!app.AdjustModeEnabled) SetText(target, "Screen " + screen.Id + " / " + screen.InputKind.ToString().ToLowerInvariant());
                SetText(detail, screen.InputKind + "  /  u " + screen.InputUV.x.ToString("0.000") + "  v " + screen.InputUV.y.ToString("0.000"));
            }
        }

        void ApplyState(bool placeMode, bool pointerEnabled, bool handTracked, bool hasTarget,
            bool placing, int cubes, int surfaces, string message)
        {
            SetText(counter, cubes.ToString("00") + " SCREENS");
            SetText(section, placeMode ? placeHeading : viewHeading);
            SetText(target, !placeMode ? enterPlaceMessage : hasTarget ? readyMessage
                : pointerEnabled && !handTracked ? showHandMessage : scanMessage);
            if (target != null && tintTargetStatus) target.color = placeMode && hasTarget ? readyColor : idleColor;
            SetText(detail, surfaces + " surfaces  /  " + (!placeMode ? "highlights hidden" : pointerEnabled ? "hand aim" : "centre aim"));
            SetText(feedback, message);
            SetText(pointerLabel, pointerEnabled ? pointerOnCaption : pointerOffCaption);
            SetText(modeLabel, placeMode ? exitPlaceCaption : enterPlaceCaption);
            if (place != null) place.interactable = placeMode && hasTarget && !placing;
            if (undo != null) undo.interactable = cubes > 0 || placing;
        }

        void SetAim(bool visible, Vector2 viewportPoint, bool hasTarget)
        {
            if (cursorVisual != null) cursorVisual.SetActive(visible);
            if (aimPosition != null) aimPosition.anchorMin = aimPosition.anchorMax = viewportPoint;
            if (cursorIndicator != null && tintAimIndicator) cursorIndicator.color = hasTarget ? readyColor : noSurfaceColor;
        }

        static void SetText(Text label, string text)
        {
            if (label != null) label.text = text;
        }

#if UNITY_EDITOR
        public enum PreviewState { View, Scanning, HandMissing, ReadyToPlace }

        /// <summary>Explicit editor preview; never runs automatically or changes authored geometry.</summary>
        public void Preview(PreviewState state)
        {
            if (Application.isPlaying) return;
            bool placing = state != PreviewState.View;
            bool ready = state == PreviewState.ReadyToPlace;
            bool missing = state == PreviewState.HandMissing;
            SetText(tracking, "AR tracking");
            SetText(hand, missing ? showHandMessage : "Pointer off / centre aim");
            SetText(network, "WI-FI  Listening  /  192.168.1.42:7777");
            SetText(sizeLabel, "16 x 9 x 0.5 cm  /  16:9");
            ApplyState(placing, missing, false, ready, false, ready ? 2 : 0, placing ? 4 : 0,
                ready ? "Screen placed / 16 x 9 cm" : missing ? "Show your knuckle to aim at a surface"
                : placing ? "Move the phone slowly to find a flat surface" : "Enter place mode to highlight surfaces and add screens");
            SetAim(placing && !missing, Vector2.one * 0.5f, ready);
        }
#endif
    }
}
