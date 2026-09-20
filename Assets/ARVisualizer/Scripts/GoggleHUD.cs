using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer
{
    /// <summary>Temporarily presents the authored HUD as a camera-following panel with hand-ray input.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    [AddComponentMenu("AR Visualizer/Goggle HUD")]
    public sealed class GoggleHUD : MonoBehaviour
    {
        [Header("Floating panel")]
        [SerializeField, Range(0.3f, 2)] float distanceMetres = 0.8f;
        [SerializeField, Range(0.2f, 1.5f)] float widthMetres = 0.9f;
        [SerializeField, Range(-0.5f, 0.5f)] float verticalOffsetMetres;
        [Tooltip("Reduce the panel width when necessary to keep the full HUD inside both eye views.")]
        [SerializeField] bool fitWithinEyeViews = true;
        [Tooltip("Canvas units used by the existing HUD layout.")]
        [SerializeField] Vector2 layoutSize = new Vector2(1280, 720);
        public bool IsActive { get; private set; }
        public bool IsPointerOverHUD { get; private set; }
        public Button HoveredButton { get; private set; }
        public Vector3 PointerWorldPosition { get; private set; }
        public bool IsDispatchingClick { get; private set; }
        public float DistanceMetres => distanceMetres;
        public StereoGoggles Stereo { get; private set; }
        public string TargetName => HoveredButton != null ? HoveredButton.name : "";

        ARVisualizerApp app;
        Canvas canvas;
        CanvasScaler scaler;
        GraphicRaycaster raycaster;
        VisualizerHUD hud;
        RectTransform rect;
        PointerEventData pointer;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        readonly List<BaseInputModule> pausedModules = new List<BaseInputModule>();
        readonly Dictionary<Graphic, Material> normalMaterials = new Dictionary<Graphic, Material>();
        Material floatingMaterial;
        RectState normalRect;
        RectState safeRect;
        SafeAreaFitter safeFitter;
        RenderMode normalMode;
        Camera normalCamera;
        float normalScale, normalPlaneDistance;
        bool normalScalerEnabled, normalFitterEnabled;
        GameObject normalSelection;
        EventSystem eventSystem;

        struct RectState
        {
            Vector2 min, max, pivot, size;
            Vector3 position, scale;
            Quaternion rotation;
            public RectState(RectTransform value)
            {
                min = value.anchorMin; max = value.anchorMax; pivot = value.pivot; size = value.sizeDelta;
                position = value.anchoredPosition3D; scale = value.localScale; rotation = value.localRotation;
            }
            public void Restore(RectTransform value)
            {
                value.anchorMin = min; value.anchorMax = max; value.pivot = pivot; value.sizeDelta = size;
                value.anchoredPosition3D = position; value.localScale = scale; value.localRotation = rotation;
            }
        }

        public void Initialize(ARVisualizerApp application)
        {
            app = application;
            canvas = GetComponent<Canvas>(); scaler = GetComponent<CanvasScaler>();
            raycaster = GetComponent<GraphicRaycaster>(); hud = GetComponent<VisualizerHUD>();
            rect = (RectTransform)transform;
            Stereo = GetComponent<StereoGoggles>();
            if (Stereo == null) Stereo = gameObject.AddComponent<StereoGoggles>();
            Stereo.Initialize(app.ARCamera);
            if (hud != null) hud.VisibilityChanged += OnVisibilityChanged;
        }

        public bool SetMode(bool enabled)
        {
            if (enabled == IsActive) return true;
            if (enabled)
            {
                if (!isActiveAndEnabled || app == null || app.ARCamera == null || EventSystem.current == null) return false;
                var shader = Resources.Load<Shader>("FloatingHUD");
                if (shader == null) return false;
                if (!Stereo.SetMode(true)) return false;
                if (floatingMaterial == null) floatingMaterial = new Material(shader);
                normalRect = new RectState(rect);
                normalMode = canvas.renderMode; normalCamera = canvas.worldCamera;
                normalScale = canvas.scaleFactor; normalPlaneDistance = canvas.planeDistance;
                normalScalerEnabled = scaler != null && scaler.enabled;
                if (scaler != null) scaler.enabled = false;
                safeFitter = GetComponentInChildren<SafeAreaFitter>(true);
                if (safeFitter != null)
                {
                    safeRect = new RectState((RectTransform)safeFitter.transform);
                    normalFitterEnabled = safeFitter.enabled;
                    safeFitter.enabled = false;
                    ((RectTransform)safeFitter.transform).anchorMin = Vector2.zero;
                    ((RectTransform)safeFitter.transform).anchorMax = Vector2.one;
                }
                eventSystem = EventSystem.current;
                normalSelection = eventSystem.currentSelectedGameObject;
                foreach (var module in eventSystem.GetComponents<BaseInputModule>())
                    if (module.enabled) { pausedModules.Add(module); module.enabled = false; }
                foreach (var graphic in GetComponentsInChildren<Graphic>(true))
                    if (graphic.material == Graphic.defaultGraphicMaterial)
                    { normalMaterials.Add(graphic, graphic.material); graphic.material = floatingMaterial; }
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = app.ARCamera;
                canvas.scaleFactor = 1;
                IsActive = true;
                ApplyPose();
                Canvas.ForceUpdateCanvases();
            }
            else
            {
                ClearPointer();
                IsActive = false;
                if (Stereo != null) Stereo.SetMode(false);
                foreach (var entry in normalMaterials) if (entry.Key != null) entry.Key.material = entry.Value;
                normalMaterials.Clear();
                canvas.renderMode = normalMode; canvas.worldCamera = normalCamera;
                canvas.scaleFactor = normalScale; canvas.planeDistance = normalPlaneDistance;
                normalRect.Restore(rect);
                if (safeFitter != null)
                {
                    safeRect.Restore((RectTransform)safeFitter.transform);
                    safeFitter.enabled = normalFitterEnabled;
                }
                if (scaler != null) scaler.enabled = normalScalerEnabled;
                foreach (var module in pausedModules) if (module != null) module.enabled = true;
                pausedModules.Clear();
                if (eventSystem != null) eventSystem.SetSelectedGameObject(normalSelection != null && normalSelection.activeInHierarchy ? normalSelection : null);
                Canvas.ForceUpdateCanvases();
            }
            return true;
        }

        void ApplyPose()
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(Mathf.Max(1, layoutSize.x), Mathf.Max(1, layoutSize.y));
            float distance = Mathf.Max(app.ARCamera.nearClipPlane + 0.1f, distanceMetres);
            float panelWidth = Mathf.Max(0.2f, widthMetres);
            if (fitWithinEyeViews) panelWidth = Mathf.Min(panelWidth, Stereo.PanelWidthThatFits(distance, rect.sizeDelta.x / rect.sizeDelta.y, verticalOffsetMetres));
            float scale = panelWidth / rect.sizeDelta.x;
            var parentScale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
            rect.localScale = new Vector3(scale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.x)),
                scale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.y)), scale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.z)));
            var cameraTransform = app.ARCamera.transform;
            var offset = new Vector3(0, verticalOffsetMetres, distance);
            rect.SetPositionAndRotation(Stereo.EyeCentrePosition + cameraTransform.rotation * offset, cameraTransform.rotation);
        }

        // Follow the latest camera pose without reparenting during Unity's hierarchy teardown.
        void FollowCameraBeforeRender() { if (IsActive && app != null && app.ARCamera != null) ApplyPose(); }

        public bool ViewportToPanel(Vector2 viewport, out Vector2 panelPoint)
        {
            panelPoint = viewport;
            if (!IsActive) return true;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, app.ARCamera.ViewportToScreenPoint(viewport), app.ARCamera, out var local)) return false;
            panelPoint = new Vector2((local.x - rect.rect.xMin) / rect.rect.width, (local.y - rect.rect.yMin) / rect.rect.height);
            return panelPoint.x >= 0 && panelPoint.x <= 1 && panelPoint.y >= 0 && panelPoint.y <= 1;
        }

        public void RefreshPointer()
        {
            if (IsDispatchingClick) return;
            if (!IsActive || app == null || !app.isActiveAndEnabled || !app.Hand.PointerEnabled || !app.Hand.IsTracked || ARSession.state != ARSessionState.SessionTracking)
            { ClearPointer(); return; }
            ApplyPose();
            if (EventSystem.current == null) { ClearPointer(); return; }
            if (pointer == null) pointer = new PointerEventData(EventSystem.current) { pointerId = -101, button = PointerEventData.InputButton.Left };
            pointer.position = app.ARCamera.ViewportToScreenPoint(app.Hand.ViewportPoint);
            hits.Clear();
            raycaster.Raycast(pointer, hits);
            IsPointerOverHUD = hits.Count > 0;
            Button next = null;
            if (IsPointerOverHUD)
            {
                var hit = hits[0];
                PointerWorldPosition = hit.worldPosition;
                pointer.pointerCurrentRaycast = hit;
                var button = hit.gameObject.GetComponentInParent<Button>();
                if (button != null && button.transform.IsChildOf(transform) && button.isActiveAndEnabled && button.IsInteractable()) next = button;
            }
            SetHovered(next);
        }

        void SetHovered(Button next)
        {
            if (HoveredButton == next) return;
            var previous = HoveredButton;
            HoveredButton = next;
            if (previous != null && pointer != null) ExecuteEvents.Execute(previous.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            if (HoveredButton != null && pointer != null) ExecuteEvents.Execute(HoveredButton.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        }

        public void ClearPointer()
        {
            SetHovered(null);
            IsPointerOverHUD = false;
        }

        public bool TryClick(out string message)
        {
            if (!IsActive) { message = "Enter goggle mode first (goggle.enter)"; return false; }
            if (IsDispatchingClick) { message = "HUD click already in progress"; return false; }
            RefreshPointer();
            var button = HoveredButton;
            if (button == null) { message = "Point the hand ray at an enabled HUD button"; return false; }
            string name = button.name;
            IsDispatchingClick = true;
            try
            {
                pointer.pressPosition = pointer.position;
                pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                if (button != null) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                if (!IsActive || button == null || !button.isActiveAndEnabled || !button.IsInteractable())
                { message = "HUD button became unavailable"; return false; }
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                message = "Clicked " + name;
                return true;
            }
            finally
            {
                IsDispatchingClick = false;
                if (IsActive && eventSystem != null) eventSystem.SetSelectedGameObject(null);
                RefreshPointer();
            }
        }

        void OnVisibilityChanged(bool visible) => ClearPointer();
        void OnApplicationPause(bool paused) { if (paused) ClearPointer(); }
        void OnEnable() => Application.onBeforeRender += FollowCameraBeforeRender;
        void OnDisable()
        {
            Application.onBeforeRender -= FollowCameraBeforeRender;
            if (IsActive) SetMode(false);
        }
        void OnDestroy()
        {
            if (hud != null) hud.VisibilityChanged -= OnVisibilityChanged;
            if (floatingMaterial != null) Destroy(floatingMaterial);
        }
    }
}
