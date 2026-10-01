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
        [Tooltip("Canvas units for the floating 4:3 HUD. Normal-mode geometry is restored on exit.")]
        [SerializeField] Vector2 layoutSize = new Vector2(1280, 960);
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
        readonly List<GraphicRaycaster> pausedTouchRaycasters = new List<GraphicRaycaster>();
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

        readonly struct RectState
        {
            readonly Vector2 anchorMin, anchorMax, pivot, size;
            readonly Vector3 position, scale;
            readonly Quaternion rotation;

            public RectState(RectTransform value)
            {
                anchorMin = value.anchorMin;
                anchorMax = value.anchorMax;
                pivot = value.pivot;
                size = value.sizeDelta;
                position = value.anchoredPosition3D;
                scale = value.localScale;
                rotation = value.localRotation;
            }

            public void Restore(RectTransform value)
            {
                value.anchorMin = anchorMin;
                value.anchorMax = anchorMax;
                value.pivot = pivot;
                value.sizeDelta = size;
                value.anchoredPosition3D = position;
                value.localScale = scale;
                value.localRotation = rotation;
            }
        }

        public void Initialize(ARVisualizerApp application)
        {
            app = application;
            canvas = GetComponent<Canvas>();
            scaler = GetComponent<CanvasScaler>();
            raycaster = GetComponent<GraphicRaycaster>();
            hud = GetComponent<VisualizerHUD>();
            rect = (RectTransform)transform;
            Stereo = GetComponent<StereoGoggles>();
            if (Stereo == null) Stereo = gameObject.AddComponent<StereoGoggles>();
            Stereo.Initialize(app.ARCamera);
            if (hud != null) hud.VisibilityChanged += OnVisibilityChanged;
        }

        public bool SetMode(bool enabled)
        {
            if (enabled == IsActive) return true;
            if (enabled) return EnterMode();

            ExitMode();
            return true;
        }

        bool EnterMode()
        {
            if (!isActiveAndEnabled || app == null || app.ARCamera == null || EventSystem.current == null) return false;
            var shader = Resources.Load<Shader>("FloatingHUD");
            if (shader == null || !Stereo.SetMode(true)) return false;
            if (floatingMaterial == null) floatingMaterial = new Material(shader);

            SaveNormalLayout();
            PausePhoneInput();
            ApplyFloatingMaterials();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = app.ARCamera;
            canvas.scaleFactor = 1;
            IsActive = true;
            ApplyPose();
            Canvas.ForceUpdateCanvases();
            return true;
        }

        void ExitMode()
        {
            ClearPointer();
            IsActive = false;
            if (Stereo != null) Stereo.SetMode(false);
            RestoreNormalMaterials();
            RestoreNormalLayout();
            ResumePhoneInput();
            Canvas.ForceUpdateCanvases();
        }

        void SaveNormalLayout()
        {
            normalRect = new RectState(rect);
            normalMode = canvas.renderMode;
            normalCamera = canvas.worldCamera;
            normalScale = canvas.scaleFactor;
            normalPlaneDistance = canvas.planeDistance;
            normalScalerEnabled = scaler != null && scaler.enabled;
            if (scaler != null) scaler.enabled = false;

            safeFitter = GetComponentInChildren<SafeAreaFitter>(true);
            if (safeFitter == null) return;
            var safeTransform = (RectTransform)safeFitter.transform;
            safeRect = new RectState(safeTransform);
            normalFitterEnabled = safeFitter.enabled;
            safeFitter.enabled = false;
            safeTransform.anchorMin = Vector2.zero;
            safeTransform.anchorMax = Vector2.one;
        }

        void RestoreNormalLayout()
        {
            canvas.renderMode = normalMode;
            canvas.worldCamera = normalCamera;
            canvas.scaleFactor = normalScale;
            canvas.planeDistance = normalPlaneDistance;
            normalRect.Restore(rect);
            if (safeFitter != null)
            {
                safeRect.Restore((RectTransform)safeFitter.transform);
                safeFitter.enabled = normalFitterEnabled;
            }
            if (scaler != null) scaler.enabled = normalScalerEnabled;
        }

        void PausePhoneInput()
        {
            eventSystem = EventSystem.current;
            normalSelection = eventSystem.currentSelectedGameObject;
            eventSystem.SetSelectedGameObject(null);

            // Keep native touch for the fixed mode switch. Disabled world-space raycasters
            // still support the explicit Raycast calls used by the hand pointer.
            foreach (var candidate in app.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                if (!candidate.enabled || candidate.GetComponentInChildren<DisplayModeButton>(true) != null) continue;
                pausedTouchRaycasters.Add(candidate);
                candidate.enabled = false;
            }
        }

        void ResumePhoneInput()
        {
            foreach (var candidate in pausedTouchRaycasters)
                if (candidate != null) candidate.enabled = true;
            pausedTouchRaycasters.Clear();

            if (eventSystem == null) return;
            bool canRestoreSelection = normalSelection != null && normalSelection.activeInHierarchy;
            eventSystem.SetSelectedGameObject(canRestoreSelection ? normalSelection : null);
        }

        void ApplyFloatingMaterials()
        {
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.material != Graphic.defaultGraphicMaterial) continue;
                normalMaterials.Add(graphic, graphic.material);
                graphic.material = floatingMaterial;
            }
        }

        void RestoreNormalMaterials()
        {
            foreach (var entry in normalMaterials)
                if (entry.Key != null) entry.Key.material = entry.Value;
            normalMaterials.Clear();
        }

        void ApplyPose()
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(Mathf.Max(1, layoutSize.x), Mathf.Max(1, layoutSize.y));
            float distance = Mathf.Max(app.ARCamera.nearClipPlane + 0.1f, distanceMetres);
            float panelWidth = Mathf.Max(0.2f, widthMetres);
            if (fitWithinEyeViews)
            {
                float fittingWidth = Stereo.PanelWidthThatFits(distance, rect.sizeDelta.x / rect.sizeDelta.y, verticalOffsetMetres);
                panelWidth = Mathf.Min(panelWidth, fittingWidth);
            }
            float scale = panelWidth / rect.sizeDelta.x;
            var parentScale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
            rect.localScale = new Vector3(scale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.x)),
                scale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.y)), scale / Mathf.Max(0.00001f, Mathf.Abs(parentScale.z)));
            var cameraTransform = app.ARCamera.transform;
            var offset = new Vector3(0, verticalOffsetMetres, distance);
            rect.SetPositionAndRotation(Stereo.EyeCentrePosition + cameraTransform.rotation * offset, cameraTransform.rotation);
        }

        // Follow the latest camera pose without reparenting during Unity's hierarchy teardown.
        void FollowCameraBeforeRender()
        {
            if (IsActive && app != null && app.ARCamera != null) ApplyPose();
        }

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
            if (!CanUseHandPointer())
            {
                ClearPointer();
                return;
            }
            ApplyPose();
            if (EventSystem.current == null)
            {
                ClearPointer();
                return;
            }
            if (pointer == null)
                pointer = new PointerEventData(EventSystem.current) { pointerId = -101, button = PointerEventData.InputButton.Left };
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

        bool CanUseHandPointer()
        {
            if (!IsActive || app == null || !app.isActiveAndEnabled) return false;
            if (app.ScreenApps != null && app.ScreenApps.BlocksPointer) return false;
            return app.Hand.PointerEnabled && app.Hand.IsTracked && ARSession.state == ARSessionState.SessionTracking;
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
            if (!IsActive)
            {
                message = "Enter goggle mode first (goggle.enter)";
                return false;
            }
            if (IsDispatchingClick)
            {
                message = "HUD click already in progress";
                return false;
            }
            RefreshPointer();
            var button = HoveredButton;
            if (button == null)
            {
                message = "Point the hand ray at an enabled HUD button";
                return false;
            }
            string name = button.name;
            IsDispatchingClick = true;
            try
            {
                pointer.pressPosition = pointer.position;
                pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                if (button != null) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                if (!IsActive || button == null || !button.isActiveAndEnabled || !button.IsInteractable())
                {
                    message = "HUD button became unavailable";
                    return false;
                }
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

        void OnApplicationPause(bool paused)
        {
            if (paused) ClearPointer();
        }

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
