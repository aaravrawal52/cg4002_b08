using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Serialization;

namespace ARVisualizer
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HandPointerSource), typeof(CommunicationManager))]
    [AddComponentMenu("AR Visualizer/Application")]
    public sealed class ARVisualizerApp : MonoBehaviour
    {
        [Header("Screen placement")]
        [Tooltip("Editable 16:9 screen prefab. Its Size Steps control the initial dimensions.")]
        [SerializeField] ScreenSurface screenPrefab;
        [FormerlySerializedAs("maximumCubes")]
        [SerializeField, Min(1)] int maximumScreens = 100;
        [Tooltip("Farthest surface that can be targeted, in metres.")]
        [SerializeField, Min(0.1f)] float maximumPlacementDistance = 5;
        [Tooltip("The sample scene's filled surface visualization, shown in place mode.")]
        [SerializeField] GameObject surfaceVisualizationPrefab;

        [Header("Editable HUD")]
        [Tooltip("The saved Landscape HUD prefab instance. Edit its children with the Rect tool (T).")]
        [SerializeField] VisualizerHUD hud;

        [Header("Hand ray appearance")]
        [SerializeField] bool pointerInitiallyEnabled = true;
        [SerializeField, Min(0.0001f)] float rayWidth = 0.0025f;
        [SerializeField, Min(0.0001f)] float footprintWidth = 0.002f;
        [SerializeField] Color validRayColor = new Color(0.27f, 1, 0.77f);
        [SerializeField] Color noSurfaceRayColor = new Color(1, 0.72f, 0.3f);

        [Header("Real-world occlusion")]
        [Tooltip("Human: use recognized people, including hands, to hide screens behind them. Environment: use LiDAR depth for hands and other objects. No Occlusion: draw screens over the camera image. The available depth source is used if the preferred source is unsupported.")]
        [SerializeField] OcclusionPreferenceMode occlusionPreference = OcclusionPreferenceMode.PreferHumanOcclusion;
        [Tooltip("Smooth LiDAR depth over time to reduce flicker. Turn off to compare responsiveness for moving hands when using environment occlusion.")]
        [SerializeField] bool smoothEnvironmentDepth = true;
        [Header("Screen interaction")]
        [Tooltip("Fingertip distance from the screen face needed to begin a touch. LiDAR contact is approximate.")]
        [SerializeField, Range(0.003f, 0.03f)] float touchToleranceMetres = 0.012f;
        [Tooltip("Angle per rotate command, for screens on horizontal surfaces only.")]
        [SerializeField, Range(1, 90)] float rotationStepDegrees = 15;
        public HandPointerSource Hand { get; private set; }
        public CommunicationManager Communication { get; private set; }
        public Camera ARCamera { get; private set; }
        public GoggleHUD GoggleHUD { get; private set; }
        public bool GoggleModeEnabled => GoggleHUD != null && GoggleHUD.IsActive;
        public bool HasTarget { get; private set; }
        public bool IsPlacing { get; private set; }
        public bool PlaceModeEnabled { get; private set; }
        public int ScreenCount => placed.Count;
        public int CubeCount => ScreenCount; // Compatibility with earlier controllers.
        public IReadOnlyList<ScreenSurface> Screens => placed;
        public ScreenSurface RayScreen { get; private set; }
        public ScreenSurface InputScreen { get; private set; }
        public ScreenSurface AdjustedScreen { get; private set; }
        public bool AdjustModeEnabled => AdjustedScreen != null;
        public Vector2 DefaultScreenSize => screenPrefab != null ? new Vector2(screenPrefab.Width, screenPrefab.Height) : new Vector2(0.16f, 0.09f);
        public int SurfaceCount { get; private set; }
        public string Feedback { get; private set; } = "Enter place mode to highlight surfaces and add screens";
        public string TrackingStatus => ARSession.state == ARSessionState.SessionTracking
            ? "AR tracking" : ARSession.state + " / " + ARSession.notTrackingReason;

        ARPlaneManager planes;
        ARRaycastManager raycasts;
        ARAnchorManager anchors;
        AROcclusionManager occlusion;
        bool occlusionSettingsDirty;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly List<ScreenSurface> placed = new List<ScreenSurface>();
        readonly List<InputAction> poseActions = new List<InputAction>();
        Material lineMaterial;
        LineRenderer beam, footprint;
        Pose targetPose;
        bool targetOnHorizontal;
        Vector2 rayScreenUV;
        bool refreshingScreenInput;
        int nextScreenId = 1;
        int placementGeneration;
        bool pointerBeforeGoggles;
        ARPlane retainedPlane;
        Vector3 retainedLocalPoint;

        void Awake()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;

            lineMaterial = new Material(Resources.Load<Shader>("VisualizerLine"));
            var sessionObject = new GameObject("AR Session");
            sessionObject.transform.SetParent(transform);
            sessionObject.SetActive(false);
            var session = sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();

            var originObject = new GameObject("XR Origin");
            originObject.transform.SetParent(transform);
            originObject.SetActive(false);
            var origin = originObject.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            var cameraObject = new GameObject("AR Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            ARCamera = cameraObject.GetComponent<Camera>();
            ARCamera.nearClipPlane = 0.05f;
            ARCamera.farClipPlane = 30;
            ARCamera.clearFlags = CameraClearFlags.SolidColor;
            ARCamera.backgroundColor = new Color(0.025f, 0.04f, 0.06f);
            cameraObject.AddComponent<AudioListener>();
            var cameraManager = cameraObject.AddComponent<ARCameraManager>();
            cameraManager.requestedFacingDirection = CameraFacingDirection.World;
            // Write real-world depth before opaque screens are drawn, so normal depth testing hides them.
            cameraManager.requestedBackgroundRenderingMode = CameraBackgroundRenderingMode.BeforeOpaques;
            // ARCameraBackground caches this component in Awake: create it before the background is activated.
            occlusion = cameraObject.AddComponent<AROcclusionManager>();
            ApplyOcclusionSettings();
            cameraObject.AddComponent<ARCameraBackground>();
            var pose = cameraObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput = Action("Position", "<HandheldARInputDevice>/devicePosition", "Vector3");
            pose.rotationInput = Action("Rotation", "<HandheldARInputDevice>/deviceRotation", "Quaternion");
            // AR Foundation 6.6's handheld layout exposes pose only; ARSession gates placement.
            pose.ignoreTrackingState = true;
            origin.Camera = ARCamera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0;
            planes = originObject.AddComponent<ARPlaneManager>();
            planes.planePrefab = surfaceVisualizationPrefab;
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            raycasts = originObject.AddComponent<ARRaycastManager>();
            anchors = originObject.AddComponent<ARAnchorManager>();
            originObject.SetActive(true);
            sessionObject.SetActive(true);

            Hand = GetComponent<HandPointerSource>();
            Hand.Session = session;
            Hand.TrackingOrigin = offset.transform;
            Hand.ARCamera = ARCamera;
            Hand.SetPointerEnabled(pointerInitiallyEnabled);
            beam = CreateLine("Hand ray", rayWidth, validRayColor, false, transform);
            footprint = CreateLine("Screen footprint", footprintWidth, validRayColor, true, transform);
            Communication = GetComponent<CommunicationManager>();
            if (Communication == null) Communication = gameObject.AddComponent<CommunicationManager>();
            Communication.CommandReceived += Execute;
            if (hud == null) hud = GetComponentInChildren<VisualizerHUD>(true);
            if (hud != null)
            {
                GoggleHUD = hud.GetComponent<GoggleHUD>();
                if (GoggleHUD == null) GoggleHUD = hud.gameObject.AddComponent<GoggleHUD>();
                GoggleHUD.Initialize(this);
                hud.Initialize(this);
            }
            else Debug.LogError("Assign a saved HUD. Use Tools > AR Visualizer > Add editable HUD to current scene.", this);
        }

        InputActionProperty Action(string name, string binding, string control)
        {
            var action = new InputAction(name, InputActionType.Value, binding, expectedControlType: control);
            poseActions.Add(action);
            return new InputActionProperty(action);
        }

        LineRenderer CreateLine(string name, float width, Color color, bool loop, Transform parent)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.loop = loop;
            line.useWorldSpace = true;
            line.numCapVertices = 3;
            line.positionCount = 0;
            return line;
        }

        void LateUpdate()
        {
            if (occlusionSettingsDirty) ApplyOcclusionSettings();
            UpdateSurfaceVisuals();
            GoggleHUD?.RefreshPointer();
            RefreshScreenInput();
            RefreshTarget();
            DrawPointer();
        }

        void OnValidate() => occlusionSettingsDirty = true;

        void ApplyOcclusionSettings()
        {
            if (occlusion == null) return;
            occlusionSettingsDirty = false;
            bool useDepth = occlusionPreference != OcclusionPreferenceMode.NoOcclusion;
            // Request both sources; ARKit selects one according to preference and device support.
            // A stencil alone would hide even screens in front of the hand, so request human depth too.
            occlusion.requestedHumanStencilMode = useDepth ? HumanSegmentationStencilMode.Best : HumanSegmentationStencilMode.Disabled;
            occlusion.requestedHumanDepthMode = useDepth ? HumanSegmentationDepthMode.Best : HumanSegmentationDepthMode.Disabled;
            occlusion.requestedEnvironmentDepthMode = useDepth ? EnvironmentDepthMode.Best : EnvironmentDepthMode.Disabled;
            occlusion.environmentDepthTemporalSmoothingRequested = smoothEnvironmentDepth;
            // A headset needs scene depth across the whole video image for per-eye reprojection.
            occlusion.requestedOcclusionPreferenceMode = GoggleModeEnabled && useDepth
                ? OcclusionPreferenceMode.PreferEnvironmentOcclusion : occlusionPreference;
            occlusion.enabled = useDepth;
        }

        void UpdateSurfaceVisuals()
        {
            SurfaceCount = 0;
            foreach (var plane in planes.trackables)
            {
                bool tracked = plane.trackingState == TrackingState.Tracking && plane.subsumedBy == null;
                if (tracked) ++SurfaceCount;
                var renderer = plane.GetComponent<MeshRenderer>();
                // Keep ARPlane and its mesh updater active so tracking continues in view mode.
                // forceRenderingOff also takes precedence over ARPlaneMeshVisualizer's visibility updates.
                if (renderer != null) renderer.forceRenderingOff = !PlaceModeEnabled || !tracked;
            }
        }

        void SetPlaceMode(bool enabled)
        {
            if (enabled) ExitAdjustMode();
            if (PlaceModeEnabled == enabled) return;
            PlaceModeEnabled = enabled;
            retainedPlane = null;
            if (!enabled) ++placementGeneration; // Cancel any anchor request still in flight.
            footprint.enabled = enabled && HasTarget;
            UpdateSurfaceVisuals();
        }

        void RefreshTarget()
        {
            HasTarget = false;
            if (ARSession.state != ARSessionState.SessionTracking || (Hand.PointerEnabled && !Hand.IsTracked))
            { retainedPlane = null; return; }
            if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD)
            {
                // Keep the last world target while aiming at Place Screen on the floating panel.
                if (PlaceModeEnabled && retainedPlane != null && retainedPlane.trackingState == TrackingState.Tracking && retainedPlane.subsumedBy == null)
                {
                    var position = retainedPlane.transform.TransformPoint(retainedLocalPoint);
                    var normal = retainedPlane.transform.up;
                    if (Vector3.Dot(normal, ARCamera.transform.position - position) < 0) normal = -normal;
                    if (Vector3.Distance(position, ARCamera.transform.position) <= maximumPlacementDistance)
                    {
                        targetPose = ScreenSurface.PlacementPose(position, normal, ARCamera.transform.position, out targetOnHorizontal);
                        HasTarget = true;
                    }
                }
                return;
            }
            retainedPlane = null;
            if (RayScreen != null) return;
            Vector2 viewport = Hand.PointerEnabled ? Hand.ViewportPoint : new Vector2(0.5f, 0.5f);
            if (!raycasts.Raycast(new Vector2(viewport.x * Screen.width, viewport.y * Screen.height), hits, TrackableType.PlaneWithinPolygon)) return;
            foreach (var hit in hits)
            {
                var plane = planes.GetPlane(hit.trackableId);
                if (plane == null || plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null || hit.distance > maximumPlacementDistance) continue;
                Vector3 normal = plane.transform.up;
                if (Vector3.Dot(normal, ARCamera.transform.position - hit.pose.position) < 0) normal = -normal;
                targetPose = ScreenSurface.PlacementPose(hit.pose.position, normal, ARCamera.transform.position, out targetOnHorizontal);
                HasTarget = true;
                if (GoggleModeEnabled && PlaceModeEnabled)
                {
                    retainedPlane = plane;
                    retainedLocalPoint = plane.transform.InverseTransformPoint(hit.pose.position);
                }
                break;
            }
        }

        void DrawPointer()
        {
            beam.enabled = Hand.PointerEnabled && Hand.IsTracked && ARSession.state == ARSessionState.SessionTracking;
            if (beam.enabled)
            {
                Vector2 point = Hand.ViewportPoint;
                var ray = ARCamera.ViewportPointToRay(point);
                // The ray tracks the index MCP (base knuckle); fingertip depth is reserved for touch.
                Vector3 start = Hand.RayOrigin;
                beam.positionCount = 2;
                beam.SetPosition(0, start);
                beam.SetPosition(1, GoggleModeEnabled && GoggleHUD.IsPointerOverHUD ? GoggleHUD.PointerWorldPosition
                    : RayScreen != null ? RayScreen.WorldPoint(rayScreenUV) : HasTarget ? targetPose.position : ray.GetPoint(1.5f));
                beam.startColor = beam.endColor = HasTarget || RayScreen != null || (GoggleModeEnabled && GoggleHUD.HoveredButton != null) ? validRayColor : noSurfaceRayColor;
            }
            footprint.enabled = PlaceModeEnabled && HasTarget;
            if (!footprint.enabled) return;
            Vector2 h = DefaultScreenSize * 0.5f;
            footprint.positionCount = 4;
            float z = -ScreenSurface.Thickness - 0.001f;
            footprint.SetPosition(0, targetPose.position + targetPose.rotation * new Vector3(-h.x, -h.y, z));
            footprint.SetPosition(1, targetPose.position + targetPose.rotation * new Vector3(h.x, -h.y, z));
            footprint.SetPosition(2, targetPose.position + targetPose.rotation * new Vector3(h.x, h.y, z));
            footprint.SetPosition(3, targetPose.position + targetPose.rotation * new Vector3(-h.x, h.y, z));
        }

        void RefreshScreenInput()
        {
            if (refreshingScreenInput) return;
            refreshingScreenInput = true;
            try
            {
                var previousInput = InputScreen;
                RayScreen = InputScreen = null;
                float closestRay = maximumPlacementDistance;
                float closestTouch = float.PositiveInfinity;
                Vector2 touchUV = default;
                if (ARSession.state == ARSessionState.SessionTracking)
                {
                    var ray = ARCamera.ViewportPointToRay(Hand.ViewportPoint);
                    foreach (var screen in placed)
                    {
                        if (screen == null || !screen.isActiveAndEnabled) continue;
                        if (!(GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) && Hand.PointerEnabled && Hand.IsTracked && screen.TryRaycast(ray, closestRay, out var distance, out var uv))
                        { closestRay = distance; RayScreen = screen; rayScreenUV = uv; }
                        float tolerance = previousInput == screen && screen.InputKind == ScreenInputKind.Touch ? touchToleranceMetres * 1.5f : touchToleranceMetres;
                        bool inFront = Vector3.Dot(ARCamera.transform.position - screen.transform.position, screen.FrontNormal) > ScreenSurface.Thickness;
                        if (inFront && Hand.HasTipDepth && screen.TryTouch(Hand.TipWorldPosition, tolerance, out var separation, out var contact) && separation < closestTouch)
                        { closestTouch = separation; InputScreen = screen; touchUV = contact; }
                    }
                }
                bool touching = InputScreen != null;
                if (!touching) InputScreen = RayScreen;
                // Event handlers may issue undo/clear commands, so do not enumerate a mutable list here.
                for (int i = placed.Count - 1; i >= 0; --i)
                {
                    if (i >= placed.Count) continue;
                    var screen = placed[i];
                    if (screen != null) screen.SetInput(screen == InputScreen ? touching ? ScreenInputKind.Touch : ScreenInputKind.Ray : ScreenInputKind.None, touching ? touchUV : rayScreenUV);
                }
                // A prefab event may have disabled its screen while input was being dispatched.
                if (RayScreen != null && !RayScreen.isActiveAndEnabled) RayScreen = null;
                if (InputScreen != null && (!InputScreen.isActiveAndEnabled || InputScreen.InputKind == ScreenInputKind.None)) InputScreen = null;
            }
            finally { refreshingScreenInput = false; }
        }

        void ExitAdjustMode()
        {
            if (AdjustedScreen != null) AdjustedScreen.IsAdjusting = false;
            AdjustedScreen = null;
        }

        void ClearScreenInput()
        {
            for (int i = placed.Count - 1; i >= 0; --i)
                if (i < placed.Count && placed[i] != null) placed[i].SetInput(ScreenInputKind.None, Vector2.zero);
            RayScreen = InputScreen = null;
        }

        public void SendLocal(string command) => Execute(new VisualizerCommand { command = command }, _ => { });

        bool SetGoggleMode(bool enabled)
        {
            if (GoggleHUD == null) return false;
            if (enabled == GoggleModeEnabled && (enabled || !Hand.PointerLockedOn)) return true;
            if (enabled) pointerBeforeGoggles = Hand.PointerEnabled;
            if (!GoggleHUD.SetMode(enabled)) return false;
            retainedPlane = null;
            Hand.SetPointerLock(enabled);
            if (!enabled) Hand.SetPointerEnabled(pointerBeforeGoggles);
            if (isActiveAndEnabled) ApplyOcclusionSettings();
            else occlusionSettingsDirty = true;
            hud.RefreshStatus();
            return true;
        }

        public async void Execute(VisualizerCommand command, System.Action<CommandReply> complete)
        {
            bool ok = true;
            string message;
            try
            {
                GoggleHUD?.RefreshPointer();
                RefreshScreenInput();
                RefreshTarget();
                switch (command.command)
                {
                    case "goggle.enter":
                        ok = SetGoggleMode(true);
                        message = ok ? "Goggle mode / point at a HUD button and send ui.click" : "Floating HUD is unavailable"; break;
                    case "goggle.exit":
                        ok = SetGoggleMode(false);
                        message = ok ? "Normal mode / screen HUD restored" : "HUD is unavailable"; break;
                    case "ui.click":
                        if (GoggleHUD == null) { ok = false; message = "HUD is unavailable"; break; }
                        hud.RefreshStatus();
                        ok = GoggleHUD.TryClick(out message); break;
                    case "pointer.on": Hand.SetPointerEnabled(true); message = "Hand pointer enabled"; break;
                    case "pointer.off":
                        if (GoggleModeEnabled) { ok = false; message = "The ray pointer stays on in goggle mode"; break; }
                        Hand.SetPointerEnabled(false); message = "Pointer off / aim with centre crosshair"; break;
                    case "place.enter": SetPlaceMode(true); message = "Place mode / move slowly to scan a flat surface"; break;
                    case "place.exit": SetPlaceMode(false); message = "View mode / surface highlights hidden"; break;
                    case "cube.place": case "screen.place":
                        if (!PlaceModeEnabled) { ok = false; message = "Enter place mode first (place.enter)"; break; }
                        RefreshTarget();
                        if (IsPlacing) { ok = false; message = "Placement already in progress"; break; }
                        if (!HasTarget) { ok = false; message = "Aim at a detected flat surface"; break; }
                        if (ScreenCount >= maximumScreens) { ok = false; message = "Screen limit reached; undo or clear screens"; break; }
                        if (screenPrefab == null) { ok = false; message = "Assign the Screen prefab in the application Inspector"; break; }
                        IsPlacing = true;
                        int version = placementGeneration;
                        bool horizontalSupport = targetOnHorizontal;
                        var result = await anchors.TryAddAnchorAsync(targetPose);
                        if (this == null || version != placementGeneration || !isActiveAndEnabled)
                        {
                            if (result.status.IsSuccess() && result.value != null) Destroy(result.value.gameObject);
                            ok = false; message = "Placement cancelled";
                        }
                        else if (!result.status.IsSuccess()) { ok = false; message = "Could not create AR anchor; try again"; }
                        else
                        {
                            var screen = Instantiate(screenPrefab, result.value.transform);
                            screen.transform.localPosition = Vector3.zero;
                            screen.transform.localRotation = Quaternion.identity;
                            screen.transform.localScale = Vector3.one;
                            screen.Initialize(nextScreenId++, horizontalSupport);
                            screen.name = "Screen " + screen.Id;
                            placed.Add(screen);
                            message = "Screen placed / " + (screen.Width * 100).ToString("0.#") + " x " + (screen.Height * 100).ToString("0.#") + " cm";
                        }
                        IsPlacing = false;
                        break;
                    case "cube.undo": case "screen.undo":
                        if (IsPlacing) { ++placementGeneration; message = "Pending placement cancelled"; break; }
                        if (ScreenCount == 0) { ok = false; message = "No screens to undo"; break; }
                        RemoveLast(); message = "Last screen removed"; break;
                    case "cubes.clear": case "screens.clear":
                        ++placementGeneration;
                        while (ScreenCount > 0) RemoveLast();
                        message = "All screens cleared"; break;
                    case "adjust.enter":
                        if (AdjustModeEnabled) { message = "Already adjusting screen " + AdjustedScreen.Id; break; }
                        if (IsPlacing) { ok = false; message = "Wait for placement to complete"; break; }
                        if (RayScreen == null) { ok = false; message = "Point the knuckle ray at a screen first"; break; }
                        SetPlaceMode(false);
                        AdjustedScreen = RayScreen;
                        AdjustedScreen.IsAdjusting = true;
                        message = "Adjusting screen " + AdjustedScreen.Id; break;
                    case "adjust.exit": ExitAdjustMode(); message = "Adjust mode closed"; break;
                    case "adjust.grow": case "adjust.shrink":
                        if (!AdjustModeEnabled) { ok = false; message = "Enter adjust mode first (adjust.enter)"; break; }
                        ok = AdjustedScreen.Resize(command.command == "adjust.grow" ? 1 : -1);
                        message = ok ? "Screen size / " + (AdjustedScreen.Width * 100).ToString("0.#") + " x " + (AdjustedScreen.Height * 100).ToString("0.#") + " cm" : "Screen size limit reached";
                        break;
                    case "adjust.rotate.cw": case "adjust.rotate.ccw":
                        if (!AdjustModeEnabled) { ok = false; message = "Enter adjust mode first (adjust.enter)"; break; }
                        ok = AdjustedScreen.Rotate(command.command == "adjust.rotate.cw" ? -rotationStepDegrees : rotationStepDegrees);
                        message = ok ? "Screen rotated" : "Only screens on horizontal surfaces can rotate"; break;
                    case "status": message = (AdjustModeEnabled ? "Adjust mode; " : PlaceModeEnabled ? "Place mode; " : "View mode; ") + TrackingStatus + "; " + Hand.Status + "; " + Communication.Status; break;
                    default: ok = false; message = "Unknown command"; break;
                }
            }
            catch (Exception e) { IsPlacing = false; ok = false; message = "Command failed: " + e.Message; Debug.LogException(e); }
            GoggleHUD?.RefreshPointer();
            RefreshScreenInput();
            Feedback = message;
            complete?.Invoke(new CommandReply { ok = ok, message = message, pointerEnabled = Hand.PointerEnabled,
                placeModeEnabled = PlaceModeEnabled, cubeCount = ScreenCount, screenCount = ScreenCount,
                goggleModeEnabled = GoggleModeEnabled, hudVisible = hud != null && hud.IsHUDVisible,
                pointedHUDButton = GoggleModeEnabled ? GoggleHUD.TargetName : "",
                adjustModeEnabled = AdjustModeEnabled, pointedScreenId = RayScreen != null ? RayScreen.Id : 0,
                interaction = ScreenInteractionState.From(InputScreen), adjustedScreen = ScreenState.From(AdjustedScreen) });
        }

        void RemoveLast()
        {
            var screen = placed[placed.Count - 1];
            placed.RemoveAt(placed.Count - 1);
            if (screen == null) return;
            if (AdjustedScreen == screen) ExitAdjustMode();
            if (InputScreen == screen) InputScreen = null;
            if (RayScreen == screen) RayScreen = null;
            screen.SetInput(ScreenInputKind.None, Vector2.zero);
            var anchor = screen.GetComponentInParent<ARAnchor>();
            if (anchor == null) { Destroy(screen.gameObject); return; }
            if (!anchors.TryRemoveAnchor(anchor)) Destroy(anchor.gameObject);
        }

        void OnApplicationPause(bool paused) { if (paused) { ++placementGeneration; retainedPlane = null; GoggleHUD?.ClearPointer(); ClearScreenInput(); } }
        void OnDisable() { ++placementGeneration; retainedPlane = null; if (Hand != null) SetGoggleMode(false); ClearScreenInput(); }
        void OnDestroy()
        {
            if (Communication != null) Communication.CommandReceived -= Execute;
            foreach (var action in poseActions) action.Dispose();
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
