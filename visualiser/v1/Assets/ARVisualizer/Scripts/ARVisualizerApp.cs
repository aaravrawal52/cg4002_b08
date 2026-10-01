using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Serialization;

namespace ARVisualizer
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HandPointerSource), typeof(CommunicationManager))]
    [AddComponentMenu("AR Visualizer/Application")]
    public sealed partial class ARVisualizerApp : MonoBehaviour
    {
        [Header("Screen placement")]
        [Tooltip("Editable flat screen prefab. Size Steps set its width; loaded media determines its aspect ratio (16:9 when black).")]
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
        [SerializeField, Range(1, 90)] float rotationStepDegrees = 5;
        public HandPointerSource Hand { get; private set; }
        public CommunicationManager Communication { get; private set; }
        public Camera ARCamera { get; private set; }
        public GoggleHUD GoggleHUD { get; private set; }
        public ScreenAppMenu ScreenApps { get; private set; }
        public PhonePointerClicks PhoneClicks { get; private set; }
        public DisplayModeButton ModeButton { get; private set; }
        public bool GoggleModeEnabled => GoggleHUD != null && GoggleHUD.IsActive;
        public bool UsesCentrePointer => !GoggleModeEnabled && Hand != null && !Hand.IsTracked;
        public bool PointerReady => Hand != null && Hand.PointerEnabled && (Hand.IsTracked || UsesCentrePointer)
            && ARSession.state == ARSessionState.SessionTracking;
        public Vector2 PointerViewportPoint => Hand != null && Hand.IsTracked ? Hand.ViewportPoint : Vector2.one * 0.5f;
        public bool HasTarget { get; private set; }
        public bool IsPlacing { get; private set; }
        public bool PlaceModeEnabled { get; private set; }
        public int ScreenCount => placed.Count;
        public int CubeCount => ScreenCount; // Compatibility with earlier controllers.
        public IReadOnlyList<ScreenSurface> Screens => placed;
        public ScreenSurface RayScreen { get; private set; }
        public ScreenSurface PointedScreen
        {
            get
            {
                if (RayScreen != null) return RayScreen;
                if (ScreenApps != null && !ScreenApps.IsModal && ScreenApps.IsPointerOverUI) return ScreenApps.Target;
                return null;
            }
        }
        public ScreenSurface InputScreen { get; private set; }
        public ScreenSurface AdjustedScreen { get; private set; }
        public bool AdjustModeEnabled => AdjustedScreen != null;
        public Vector2 DefaultScreenSize => screenPrefab != null ? new Vector2(screenPrefab.Width, screenPrefab.Height) : new Vector2(0.16f, 0.09f);
        public int SurfaceCount { get; private set; }
        public string Feedback { get; private set; } = "Enter place mode to highlight surfaces and add screens";
        public string TrackingStatus => ARSession.state == ARSessionState.SessionTracking
            ? "AR tracking" : ARSession.state + " / " + ARSession.notTrackingReason;

        ARSceneRig rig;
        ARPlaneManager planes;
        ARRaycastManager raycasts;
        ARAnchorManager anchors;
        AROcclusionManager occlusion;
        bool occlusionSettingsDirty;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly List<ScreenSurface> placed = new List<ScreenSurface>();
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
            ConfigurePhoneDisplay();
            InitializeARRig();
            InitializeHandPointer();
            InitializeCommunication();
            InitializeHUD();
            InitializeScreenApps();
            InitializePhoneControls();
        }

        static void ConfigurePhoneDisplay()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
        }

        void InitializeARRig()
        {
            rig = new ARSceneRig(transform, surfaceVisualizationPrefab);
            ARCamera = rig.Camera;
            planes = rig.Planes;
            raycasts = rig.Raycasts;
            anchors = rig.Anchors;
            occlusion = rig.Occlusion;
            ApplyOcclusionSettings();
            rig.Activate();
        }

        void InitializeHandPointer()
        {
            Hand = GetComponent<HandPointerSource>();
            Hand.Session = rig.Session;
            Hand.TrackingOrigin = rig.CameraOffset;
            Hand.ARCamera = ARCamera;
            Hand.SetPointerEnabled(pointerInitiallyEnabled);
            lineMaterial = new Material(Resources.Load<Shader>("VisualizerLine"));
            beam = CreateLine("Hand ray", rayWidth, validRayColor, false, transform);
            footprint = CreateLine("Screen footprint", footprintWidth, validRayColor, true, transform);
        }

        void InitializeCommunication()
        {
            Communication = GetComponent<CommunicationManager>();
            if (Communication == null) Communication = gameObject.AddComponent<CommunicationManager>();
            Communication.CommandReceived += Execute;
        }

        void InitializeHUD()
        {
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

        void InitializeScreenApps()
        {
            var menuPrefab = Resources.Load<ScreenAppMenu>("ScreenAppMenu");
            if (menuPrefab == null) return;
            ScreenApps = Instantiate(menuPrefab, transform);
            ScreenApps.Initialize(this);
        }

        void InitializePhoneControls()
        {
            PhoneClicks = GetComponent<PhonePointerClicks>();
            if (PhoneClicks == null) PhoneClicks = gameObject.AddComponent<PhonePointerClicks>();
            PhoneClicks.Initialize(this, hud);
            ModeButton = GetComponentInChildren<DisplayModeButton>(true);
            if (ModeButton != null) ModeButton.Initialize(hud);
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
            RefreshInteractionTargets();
            DrawPointer();
        }

        void RefreshInteractionTargets()
        {
            // Menus consume the ray before screen faces are tested. Newly opened menus
            // then need a second pointer refresh before placement chooses a surface.
            ScreenApps?.RefreshPointer();
            GoggleHUD?.RefreshPointer();
            RefreshScreenInput();
            var menuScreen = RayScreen;
            if (menuScreen == null && InputScreen != null && InputScreen.InputKind == ScreenInputKind.Touch)
                menuScreen = InputScreen;
            ScreenApps?.ObserveRayScreen(menuScreen);
            ScreenApps?.RefreshPointer();
            RefreshTarget();
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
                bool tracked = IsTrackedPlane(plane);
                if (tracked) ++SurfaceCount;
                var renderer = plane.GetComponent<MeshRenderer>();
                // Keep ARPlane and its mesh updater active so tracking continues in view mode.
                // forceRenderingOff also takes precedence over ARPlaneMeshVisualizer's visibility updates.
                if (renderer != null) renderer.forceRenderingOff = !PlaceModeEnabled || !tracked;
            }
        }

        void SetPlaceMode(bool enabled)
        {
            if (enabled)
            {
                ExitAdjustMode();
                ScreenApps?.Close();
            }
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
            bool menuBlocksPlacement = ScreenApps != null && ScreenApps.BlocksPointer;
            bool trackingUnavailable = ARSession.state != ARSessionState.SessionTracking || (Hand.PointerEnabled && !PointerReady);
            if (menuBlocksPlacement || trackingUnavailable)
            {
                retainedPlane = null;
                return;
            }
            if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD)
            {
                RefreshRetainedTarget();
                return;
            }
            retainedPlane = null;
            if (RayScreen != null) return;
            Vector2 viewport = Hand.PointerEnabled ? PointerViewportPoint : new Vector2(0.5f, 0.5f);
            // Native screen-point raycasts assume the physical phone aspect. Goggles use
            // the calibrated 4:3 camera ray so surfaces agree with the displayed hand/video.
            bool hitSurface = GoggleModeEnabled
                ? raycasts.Raycast(ARCamera.ViewportPointToRay(viewport), hits, TrackableType.PlaneWithinPolygon)
                : raycasts.Raycast(new Vector2(viewport.x * Screen.width, viewport.y * Screen.height), hits, TrackableType.PlaneWithinPolygon);
            if (!hitSurface) return;
            foreach (var hit in hits)
            {
                var plane = planes.GetPlane(hit.trackableId);
                if (!IsTrackedPlane(plane) || hit.distance > maximumPlacementDistance) continue;
                SetPlacementTarget(plane, hit.pose.position);
                if (GoggleModeEnabled && PlaceModeEnabled)
                {
                    retainedPlane = plane;
                    retainedLocalPoint = plane.transform.InverseTransformPoint(hit.pose.position);
                }
                break;
            }
        }

        static bool IsTrackedPlane(ARPlane plane) =>
            plane != null && plane.trackingState == TrackingState.Tracking && plane.subsumedBy == null;

        void RefreshRetainedTarget()
        {
            // Keep the last world target while aiming at Place Screen on the floating HUD.
            if (!PlaceModeEnabled || !IsTrackedPlane(retainedPlane)) return;
            var position = retainedPlane.transform.TransformPoint(retainedLocalPoint);
            if (Vector3.Distance(position, ARCamera.transform.position) <= maximumPlacementDistance)
                SetPlacementTarget(retainedPlane, position);
        }

        void SetPlacementTarget(ARPlane plane, Vector3 position)
        {
            var normal = plane.transform.up;
            if (Vector3.Dot(normal, ARCamera.transform.position - position) < 0) normal = -normal;
            targetPose = ScreenSurface.PlacementPose(position, normal, ARCamera.transform.position, out targetOnHorizontal);
            HasTarget = true;
        }

        void DrawPointer()
        {
            beam.enabled = PointerReady;
            if (beam.enabled)
            {
                Vector2 point = PointerViewportPoint;
                var ray = ARCamera.ViewportPointToRay(point);
                // The ray tracks the index MCP (base knuckle); fingertip depth is reserved for touch.
                Vector3 start = Hand.IsTracked ? Hand.RayOrigin : ray.GetPoint(0.05f);
                beam.positionCount = 2;
                beam.SetPosition(0, start);
                beam.SetPosition(1, PointerEndPosition(ray));
                bool hasButton = (ScreenApps != null && ScreenApps.HoveredButton != null)
                    || (GoggleModeEnabled && GoggleHUD.HoveredButton != null);
                beam.startColor = beam.endColor = HasTarget || RayScreen != null || hasButton ? validRayColor : noSurfaceRayColor;
            }
            DrawPlacementFootprint();
        }

        Vector3 PointerEndPosition(Ray ray)
        {
            if (ScreenApps != null && ScreenApps.IsPointerOverUI) return ScreenApps.PointerWorldPosition;
            if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) return GoggleHUD.PointerWorldPosition;
            if (RayScreen != null) return RayScreen.WorldPoint(rayScreenUV);
            if (HasTarget) return targetPose.position;
            return ray.GetPoint(1.5f);
        }

        void DrawPlacementFootprint()
        {
            footprint.enabled = PlaceModeEnabled && HasTarget;
            if (!footprint.enabled) return;
            Vector2 halfSize = DefaultScreenSize * 0.5f;
            footprint.positionCount = 4;
            float z = -ScreenSurface.SurfaceOffset - 0.001f;
            footprint.SetPosition(0, targetPose.position + targetPose.rotation * new Vector3(-halfSize.x, -halfSize.y, z));
            footprint.SetPosition(1, targetPose.position + targetPose.rotation * new Vector3(halfSize.x, -halfSize.y, z));
            footprint.SetPosition(2, targetPose.position + targetPose.rotation * new Vector3(halfSize.x, halfSize.y, z));
            footprint.SetPosition(3, targetPose.position + targetPose.rotation * new Vector3(-halfSize.x, halfSize.y, z));
        }

        void RefreshScreenInput()
        {
            if (refreshingScreenInput) return;
            refreshingScreenInput = true;
            try
            {
                var previousInput = InputScreen;
                RayScreen = InputScreen = null;
                Vector2 touchUV = default;
                if (ARSession.state == ARSessionState.SessionTracking)
                    FindScreenInput(previousInput, out touchUV);

                bool touching = InputScreen != null;
                if (!touching) InputScreen = RayScreen;
                DispatchScreenInput(touching ? ScreenInputKind.Touch : ScreenInputKind.Ray, touching ? touchUV : rayScreenUV);

                // A prefab event may have disabled its screen while input was being dispatched.
                if (RayScreen != null && !RayScreen.isActiveAndEnabled) RayScreen = null;
                if (InputScreen != null && (!InputScreen.isActiveAndEnabled || InputScreen.InputKind == ScreenInputKind.None)) InputScreen = null;
            }
            finally { refreshingScreenInput = false; }
        }

        void FindScreenInput(ScreenSurface previousInput, out Vector2 touchUV)
        {
            touchUV = default;
            float closestRay = maximumPlacementDistance;
            float closestTouch = float.PositiveInfinity;
            var ray = ARCamera.ViewportPointToRay(PointerViewportPoint);
            bool rayAvailable = PointerReady && !(ScreenApps != null && ScreenApps.BlocksPointer)
                && !(GoggleModeEnabled && GoggleHUD.IsPointerOverHUD);
            bool touchAvailable = Hand.HasTipDepth && !(ScreenApps != null && ScreenApps.BlocksTouch);

            foreach (var screen in placed)
            {
                if (screen == null || !screen.isActiveAndEnabled) continue;
                if (rayAvailable && screen.TryRaycast(ray, closestRay, out var distance, out var uv))
                {
                    closestRay = distance;
                    RayScreen = screen;
                    rayScreenUV = uv;
                }

                if (!touchAvailable) continue;
                bool continuingTouch = previousInput == screen && screen.InputKind == ScreenInputKind.Touch;
                float tolerance = continuingTouch ? touchToleranceMetres * 1.5f : touchToleranceMetres;
                bool inFront = Vector3.Dot(ARCamera.transform.position - screen.transform.position, screen.FrontNormal) > ScreenSurface.SurfaceOffset;
                if (inFront && screen.TryTouch(Hand.TipWorldPosition, tolerance, out var separation, out var contact) && separation < closestTouch)
                {
                    closestTouch = separation;
                    InputScreen = screen;
                    touchUV = contact;
                }
            }
        }

        void DispatchScreenInput(ScreenInputKind kind, Vector2 uv)
        {
            // Events can delete screens or issue commands. Preserve the reentrancy guard
            // in RefreshScreenInput and tolerate a shrinking list during dispatch.
            for (int i = placed.Count - 1; i >= 0; --i)
            {
                if (i >= placed.Count) continue;
                var screen = placed[i];
                if (screen != null) screen.SetInput(screen == InputScreen ? kind : ScreenInputKind.None, uv);
            }
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

        bool SetGoggleMode(bool enabled)
        {
            if (GoggleHUD == null) return false;
            ScreenApps?.MenuInput.CancelPress();
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

        void RemoveLast()
        {
            var screen = placed[placed.Count - 1];
            placed.RemoveAt(placed.Count - 1);
            ReleaseScreen(screen);
        }

        /// <summary>Removes this specific placed screen, including its anchor and media.</summary>
        public bool DeleteScreen(ScreenSurface screen)
        {
            if (screen == null || !placed.Remove(screen)) return false;
            int id = screen.Id;
            ReleaseScreen(screen);
            Feedback = "Screen " + id + " deleted";
            hud.RefreshStatus();
            return true;
        }

        void ReleaseScreen(ScreenSurface screen)
        {
            if (screen == null) return;
            if (ScreenApps != null && ScreenApps.Target == screen) ScreenApps.Close();
            if (AdjustedScreen == screen) ExitAdjustMode();
            if (InputScreen == screen) InputScreen = null;
            if (RayScreen == screen) RayScreen = null;
            screen.SetInput(ScreenInputKind.None, Vector2.zero);
            screen.gameObject.SetActive(false);
            var anchor = screen.GetComponentInParent<ARAnchor>();
            if (anchor == null)
            {
                Destroy(screen.gameObject);
                return;
            }
            if (!anchors.TryRemoveAnchor(anchor)) Destroy(anchor.gameObject);
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            ++placementGeneration;
            retainedPlane = null;
            GoggleHUD?.ClearPointer();
            ClearScreenInput();
        }

        void OnDisable()
        {
            ++placementGeneration;
            retainedPlane = null;
            ScreenApps?.Close();
            if (Hand != null) SetGoggleMode(false);
            ClearScreenInput();
        }

        void OnDestroy()
        {
            if (Communication != null) Communication.CommandReceived -= Execute;
            rig?.Dispose();
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
