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
        public bool IsGoggleMode => app != null && app.GoggleModeEnabled;
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
        [Tooltip("Buttons use their Inspector On Click events and authored normal-mode layout. Goggle mode arranges the visible controls into a row.")]
        [SerializeField] Text pointerLabel;
        [SerializeField] Text modeLabel;
        [SerializeField] Button place;
        [SerializeField] Button undo;
        [Tooltip("Spacing between visible bottom buttons in goggle mode. Normal-mode RectTransforms are restored on exit.")]
        [SerializeField, Min(0)] float goggleButtonSpacing = 12;

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
        [SerializeField] string placeScreenCaption = "PLACE SCREEN";
        [SerializeField] string placeHereCaption = "PLACE HERE";
        [SerializeField] Color placeHereColor = new Color(0.25f, 0.9f, 0.55f);
        [SerializeField] Color placeHereTextColor = new Color(0.035f, 0.055f, 0.075f);

        float refreshAt;
        Graphic placementGraphic;
        Color placementIdleColor, placementIdleTextColor;
        bool placementStyleCaptured;
        bool pointerWasLocked;
        bool pointerWasInteractable;
        ControlState[] normalControls;
        float goggleRowLeft, goggleRowWidth;

        sealed class ControlState
        {
            public readonly RectTransform Rect;
            readonly bool active;
            readonly Vector2 anchorMin, anchorMax, pivot, size, position;

            public ControlState(Button button)
            {
                Rect = (RectTransform)button.transform;
                active = button.gameObject.activeSelf;
                anchorMin = Rect.anchorMin;
                anchorMax = Rect.anchorMax;
                pivot = Rect.pivot;
                size = Rect.sizeDelta;
                position = Rect.anchoredPosition;
            }

            public void Restore()
            {
                if (Rect == null) return;
                Rect.anchorMin = anchorMin;
                Rect.anchorMax = anchorMax;
                Rect.pivot = pivot;
                Rect.sizeDelta = size;
                Rect.anchoredPosition = position;
                Rect.gameObject.SetActive(active);
            }
        }

        public bool HasRequiredReferences => tracking != null && hand != null && network != null && counter != null
            && section != null && target != null && detail != null && feedback != null && sizeLabel != null
            && pointerLabel != null && modeLabel != null && place != null && undo != null
            && aimPosition != null && cursorVisual != null && cursorIndicator != null;

        public void Initialize(ARVisualizerApp value)
        {
            app = value;
            refreshAt = 0;
            RefreshStatus();
            GetComponent<NormalScreenControls>()?.Initialize(app, this);
            foreach (var button in GetComponentsInChildren<PointerHoldButton>(true)) button.Initialize(app);
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
                if (item == null || item == gameObject || item.GetComponentInChildren<HUDVisibilityToggle>(true) != null
                    || item.GetComponentInChildren<DisplayModeButton>(true) != null) continue;
                if (!visible) contentWasActive[i] = item.activeSelf;
                item.SetActive(visible && contentWasActive != null && i < contentWasActive.Length && contentWasActive[i]);
            }
            VisibilityChanged?.Invoke(visible);
        }

        public void TogglePointer() => Send(app != null && app.Hand != null && app.Hand.PointerEnabled ? "pointer.off" : "pointer.on");
        public void LeftClick() => Send("ui.click");
        public void RightClick() => Send("ui.rightclick");
        public void TogglePlaceMode() => Send(app != null && app.PlaceModeEnabled ? "place.exit" : "place.enter");
        public void PlacementAction()
        {
            if (IsGoggleMode) TogglePlaceMode();
            else Send(app != null && app.PlaceModeEnabled ? "screen.place" : "place.enter");
        }
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
        public void ToggleGoggleMode() => Send(IsGoggleMode ? "goggle.exit" : "goggle.enter");

        public bool ContainsPhoneUI(Vector2 position)
        {
            if (!isActiveAndEnabled || IsGoggleMode) return false;
            foreach (var graphic in GetComponentsInChildren<Graphic>())
            {
                if (!graphic.isActiveAndEnabled || graphic.color.a <= 0 || (aimPosition != null && graphic.transform.IsChildOf(aimPosition))) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, position, null)) return true;
            }
            return false;
        }

        void Send(string command)
        {
            if (!Application.isPlaying || app == null) return;
            app.SendLocal(command);
            RefreshStatus();
        }

        void Update()
        {
            if (app == null || app.Hand == null) return;
            RefreshAim();
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + refreshInterval;
            RefreshStatus();
        }

        void RefreshAim()
        {
            bool pointerEnabled = app.Hand.PointerEnabled;
            Vector2 point = pointerEnabled ? app.PointerViewportPoint : Vector2.one * 0.5f;
            bool visible = pointerEnabled ? app.PointerReady : app.PlaceModeEnabled;
            if (IsGoggleMode) visible &= app.GoggleHUD.ViewportToPanel(point, out point);

            bool overScreenButton = app.ScreenApps != null && app.ScreenApps.HoveredButton != null;
            bool overHUDButton = IsGoggleMode && app.GoggleHUD.HoveredButton != null;
            bool hasTarget = app.HasTarget || app.RayScreen != null || overScreenButton || overHUDButton;
            SetAim(visible, point, hasTarget);
        }

        public void RefreshStatus()
        {
            if (app == null || app.Hand == null || app.Communication == null) return;
            SetText(tracking, app.TrackingStatus);
            SetText(hand, app.UsesCentrePointer && app.Hand.PointerEnabled ? "Centre pointer / no hand" : app.Hand.Status);
            SetText(network, "WI-FI  " + app.Communication.Status + "  /  " + app.Communication.Endpoint);
            RefreshScreenSize();
            ApplyState(app.PlaceModeEnabled, app.Hand.PointerEnabled, app.Hand.IsTracked || app.UsesCentrePointer,
                app.HasTarget, app.IsPlacing, app.ScreenCount, app.SurfaceCount, app.Feedback);
            RefreshPointerButton();
            RefreshGoggleControls();
            if (place != null) place.gameObject.SetActive(IsGoggleMode && app.PlaceModeEnabled);
            if (undo != null) undo.gameObject.SetActive(IsGoggleMode);
            RefreshInteractionStatus();
            GetComponent<NormalScreenControls>()?.Refresh();
        }

        void RefreshScreenSize()
        {
            var screen = app.AdjustedScreen;
            if (screen == null) screen = app.InputScreen;
            if (screen == null) screen = app.PointedScreen;

            Vector2 size = screen != null ? new Vector2(screen.Width, screen.Height) : app.DefaultScreenSize;
            bool mediaAspect = screen != null && !Mathf.Approximately(screen.AspectRatio, ScreenSurface.DefaultAspectRatio);
            SetText(sizeLabel, (size.x * 100).ToString("0.#") + " x " + (size.y * 100).ToString("0.#")
                + " cm  /  FLAT " + (mediaAspect ? "MEDIA ASPECT" : "16:9"));
        }

        void RefreshPointerButton()
        {
            var button = ParentButton(pointerLabel);
            if (button != null)
            {
                if (IsGoggleMode && !pointerWasLocked) pointerWasInteractable = button.interactable;
                if (IsGoggleMode) button.interactable = false;
                else if (pointerWasLocked) button.interactable = pointerWasInteractable;
            }
            pointerWasLocked = IsGoggleMode;
        }

        void RefreshInteractionStatus()
        {
            if (app.AdjustModeEnabled)
            {
                SetText(section, "ADJUST MODE");
                SetText(target, "Screen " + app.AdjustedScreen.Id + " selected");
            }
            if (app.UsesCentrePointer) SetText(detail, app.SurfaceCount + " surfaces  /  centre aim");
            if (IsGoggleMode)
            {
                string mode = app.AdjustModeEnabled ? "ADJUST" : app.PlaceModeEnabled ? "PLACE" : "VIEW";
                SetText(section, "GOGGLE / " + mode);
            }

            var screen = app.InputScreen;
            if (screen == null) return;
            if (!app.AdjustModeEnabled) SetText(target, "Screen " + screen.Id + " / " + screen.InputKind.ToString().ToLowerInvariant());
            SetText(detail, screen.InputKind + "  /  u " + screen.InputUV.x.ToString("0.000") + "  v " + screen.InputUV.y.ToString("0.000"));
        }

        void ApplyState(bool placeMode, bool pointerEnabled, bool handTracked, bool hasTarget,
            bool placing, int screens, int surfaces, string message)
        {
            SetText(counter, screens.ToString("00") + " SCREENS");
            SetText(section, placeMode ? placeHeading : viewHeading);
            SetText(target, TargetStatus(placeMode, hasTarget, pointerEnabled && !handTracked));
            if (target != null && tintTargetStatus) target.color = placeMode && hasTarget ? readyColor : idleColor;
            SetText(detail, surfaces + " surfaces  /  " + (!placeMode ? "highlights hidden" : pointerEnabled ? "hand aim" : "centre aim"));
            SetText(feedback, message);
            SetText(pointerLabel, pointerEnabled ? pointerOnCaption : pointerOffCaption);
            RefreshPlacementButton(placeMode, hasTarget, placing);
            if (place != null) place.interactable = placeMode && hasTarget && !placing;
            if (undo != null) undo.interactable = screens > 0 || placing;
        }

        string TargetStatus(bool placeMode, bool hasTarget, bool handMissing)
        {
            if (!placeMode) return enterPlaceMessage;
            if (hasTarget) return readyMessage;
            return handMissing ? showHandMessage : scanMessage;
        }

        void RefreshPlacementButton(bool placeMode, bool hasTarget, bool placing)
        {
            SetText(modeLabel, IsGoggleMode ? (placeMode ? exitPlaceCaption : enterPlaceCaption)
                : (placeMode ? placeHereCaption : placeScreenCaption));
            if (modeLabel == null) return;

            var button = ParentButton(modeLabel);
            if (!placementStyleCaptured && button != null)
            {
                placementGraphic = button.targetGraphic;
                placementIdleColor = placementGraphic != null ? placementGraphic.color : Color.white;
                placementIdleTextColor = modeLabel.color;
                placementStyleCaptured = true;
            }

            bool confirm = !IsGoggleMode && placeMode;
            if (button != null) button.interactable = !confirm || (hasTarget && !placing);
            if (placementGraphic != null) placementGraphic.color = confirm ? placeHereColor : placementIdleColor;
            modeLabel.color = confirm ? placeHereTextColor : placementIdleTextColor;
        }

        void RefreshGoggleControls()
        {
            if (!IsGoggleMode)
            {
                RestoreNormalControls();
                return;
            }
            if (normalControls == null && !CaptureNormalControls()) return;

            normalControls[0].Rect.gameObject.SetActive(false); // The pointer stays on in goggle mode.
            place.gameObject.SetActive(app.PlaceModeEnabled);
            undo.gameObject.SetActive(true);
            ArrangeGoggleButtons();
        }

        bool CaptureNormalControls()
        {
            var pointer = ParentButton(pointerLabel);
            var mode = ParentButton(modeLabel);
            if (pointer == null || mode == null || place == null || undo == null) return false;

            // Preserve authored phone layout and visibility before changing the goggle row.
            normalControls = new[] { new ControlState(pointer), new ControlState(mode), new ControlState(place), new ControlState(undo) };
            var parent = (RectTransform)mode.transform.parent;
            goggleRowLeft = 0;
            goggleRowWidth = parent.rect.width;
            return true;
        }

        void RestoreNormalControls()
        {
            if (normalControls == null) return;
            foreach (var control in normalControls) control.Restore();
            normalControls = null;
        }

        void ArrangeGoggleButtons()
        {
            int count = 0;
            for (int i = 1; i < normalControls.Length; ++i)
                if (normalControls[i].Rect.gameObject.activeSelf) ++count;
            if (count == 0) return;

            float spacing = Mathf.Min(goggleButtonSpacing, goggleRowWidth / (count * 2));
            float width = (goggleRowWidth - spacing * (count - 1)) / count;
            int column = 0;
            for (int i = 1; i < normalControls.Length; ++i)
            {
                var rect = normalControls[i].Rect;
                if (!rect.gameObject.activeSelf) continue;
                rect.anchorMin = new Vector2(0, rect.anchorMin.y);
                rect.anchorMax = new Vector2(0, rect.anchorMax.y);
                rect.pivot = new Vector2(0, rect.pivot.y);
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
                rect.anchoredPosition = new Vector2(goggleRowLeft + column++ * (width + spacing), rect.anchoredPosition.y);
            }
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

        static Button ParentButton(Text label) => label != null ? label.GetComponentInParent<Button>(true) : null;

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
            SetText(sizeLabel, "16 x 9 cm  /  FLAT 16:9");
            ApplyState(placing, missing, false, ready, false, ready ? 2 : 0, placing ? 4 : 0,
                ready ? "Screen placed / 16 x 9 cm" : missing ? "Show your knuckle to aim at a surface"
                : placing ? "Move the phone slowly to find a flat surface" : "Enter place mode to highlight surfaces and add screens");
            if (place != null) place.gameObject.SetActive(false);
            if (undo != null) undo.gameObject.SetActive(false);
            SetAim(placing || missing, Vector2.one * 0.5f, ready);
        }
#endif
    }
}
