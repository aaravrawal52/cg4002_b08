using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer
{
    public enum TrackedHand { Unknown = 0, Left = -1, Right = 1 }

    /// <summary>Index MCP for ray aiming; fingertip with measured LiDAR depth for touch.</summary>
    public sealed class HandPointerSource : MonoBehaviour
    {
        [Header("iPhone hand detection")]
        [SerializeField, Range(5, 30)] float samplesPerSecond = 15;
        [Tooltip("Minimum confidence for the index knuckle and fingertip.")]
        [SerializeField, Range(0.1f, 1)] float minimumConfidence = 0.35f;
        [Tooltip("Older samples cannot aim or touch a screen.")]
        [SerializeField, Range(0.05f, 0.3f)] float maximumSampleAge = 0.2f;
        [Header("Ray offset")]
        [Tooltip("Metres to the left of a right hand, or right of a left hand, in the phone's view.")]
        [SerializeField, Min(0)] float lateralOffsetMetres = 0.03f;
        [Tooltip("Estimated knuckle distance when LiDAR depth is unavailable. Used only for ray positioning.")]
        [SerializeField, Min(0.1f)] float fallbackRootDepthMetres = 0.5f;
        [Header("Editor touch simulation")]
        [SerializeField] TrackedHand editorHand = TrackedHand.Right;
        [Tooltip("Hold the left mouse button to simulate a fingertip at this camera depth.")]
        [SerializeField, Min(0.05f)] float editorTouchDepthMetres = 0.5f;
        public ARSession Session { get; set; }
        public Transform TrackingOrigin { get; set; }
        public Camera ARCamera { get; set; }
        // In goggles the camera targets a 4:3 texture rather than the phone's wide display.
        public Vector2Int ViewportSize => ARCamera != null && ARCamera.targetTexture != null
            ? new Vector2Int(ARCamera.targetTexture.width, ARCamera.targetTexture.height)
            : new Vector2Int(Screen.width, Screen.height);
        public bool PointerEnabled { get; private set; } = true;
        public bool PointerLockedOn { get; private set; }
        public bool IsTracked { get; private set; }
        public bool IsTipTracked { get; private set; }
        public bool HasTipDepth { get; private set; }
        public Vector2 RootViewportPoint { get; private set; } = Vector2.one * 0.5f;
        public TrackedHand Handedness { get; private set; }
        public bool HasRootDepth { get; private set; }
        public Vector3 RootWorldPosition { get; private set; }
        public float LateralOffsetMetres => lateralOffsetMetres;
        public Vector3 RayOrigin
        {
            get
            {
                if (ARCamera == null) return RootWorldPosition;
                var root = HasRootDepth ? RootWorldPosition : ARCamera.ViewportToWorldPoint(
                    new Vector3(RootViewportPoint.x, RootViewportPoint.y, fallbackRootDepthMetres));
                float side = Handedness == TrackedHand.Right ? -1 : Handedness == TrackedHand.Left ? 1 : 0;
                return root + ARCamera.transform.right * (side * lateralOffsetMetres);
            }
        }
        // Every ray hit, placement target and HUD marker uses the same offset viewport point.
        public Vector2 ViewportPoint => ARCamera != null ? (Vector2)ARCamera.WorldToViewportPoint(RayOrigin) : RootViewportPoint;
        public Vector2 TipViewportPoint { get; private set; }
        public Vector3 TipWorldPosition { get; private set; }
        public string Status { get; private set; } = "Show your index knuckle";
        float nextSample;
        int previousOrientation, previousWidth, previousHeight;

        [StructLayout(LayoutKind.Sequential)]
        struct NativeSample
        {
            public float rootX, rootY, rootConfidence, tipX, tipY, tipConfidence;
            public float tipSessionX, tipSessionY, tipSessionZ;
            public int hasDepth;
            public double age;
            public float rootSessionX, rootSessionY, rootSessionZ;
            public int hasRootDepth, handedness;
        }
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int AVHand_Submit(IntPtr session, int orientation, int width, int height);
        [DllImport("__Internal")] static extern int AVHand_Read(out NativeSample sample);
        [DllImport("__Internal")] static extern void AVHand_Reset();
#endif
#if UNITY_EDITOR
        bool useEditorSample;
        // Explicit simulation hook for tests or external Editor input. Excluded from device builds.
        public void SetEditorSample(Vector2 root, Vector3? fingertip, TrackedHand handedness = TrackedHand.Unknown, Vector3? rootWorld = null)
        {
            useEditorSample = true;
            IsTracked = InViewport(root);
            RootViewportPoint = root;
            Handedness = handedness;
            HasRootDepth = rootWorld.HasValue;
            RootWorldPosition = rootWorld ?? Vector3.zero;
            IsTipTracked = HasTipDepth = fingertip.HasValue;
            TipWorldPosition = fingertip ?? Vector3.zero;
            if (ARCamera != null && fingertip.HasValue) TipViewportPoint = ARCamera.WorldToViewportPoint(fingertip.Value);
        }
        public void ClearEditorSample() { useEditorSample = false; ResetTracking(); }
#endif
        public void SetPointerEnabled(bool value)
        {
            if (PointerLockedOn && !value) return;
            PointerEnabled = value; ResetTracking();
        }
        public void SetPointerLock(bool locked)
        {
            PointerLockedOn = locked;
            if (locked && !PointerEnabled) SetPointerEnabled(true);
        }
        void ResetTracking()
        {
            IsTracked = IsTipTracked = HasTipDepth = false;
            HasRootDepth = false;
            Handedness = TrackedHand.Unknown;
            nextSample = 0;
#if UNITY_IOS && !UNITY_EDITOR
            AVHand_Reset();
#endif
        }
        void OnDisable() => ResetTracking();
        void OnApplicationPause(bool paused) => ResetTracking();
        static bool InViewport(Vector2 p) => p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1;

        void Update()
        {
            // Turning off the ray leaves fingertip tracking running for direct touch.
#if UNITY_EDITOR
            if (useEditorSample) return;
            IsTracked = Mouse.current != null;
            if (IsTracked)
            {
                var p = Mouse.current.position.ReadValue();
                RootViewportPoint = new Vector2(p.x / Screen.width, p.y / Screen.height);
                IsTracked = InViewport(RootViewportPoint);
            }
            Handedness = IsTracked ? editorHand : TrackedHand.Unknown;
            HasRootDepth = false;
            TipViewportPoint = RootViewportPoint;
            IsTipTracked = IsTracked;
            HasTipDepth = IsTracked && ARCamera != null && Mouse.current.leftButton.isPressed;
            if (HasTipDepth) TipWorldPosition = ARCamera.ViewportToWorldPoint(new Vector3(RootViewportPoint.x, RootViewportPoint.y, editorTouchDepthMetres));
            Status = "Editor / " + Handedness + " hand";
#elif UNITY_IOS
            if (ARSession.state != ARSessionState.SessionTracking || Session == null || Session.subsystem == null)
            { ResetTracking(); Status = "Waiting for AR camera"; return; }
            int orientation = Screen.orientation == ScreenOrientation.LandscapeRight ? 4 : 3;
            var viewportSize = ViewportSize;
            if (previousOrientation != orientation || previousWidth != viewportSize.x || previousHeight != viewportSize.y)
            { previousOrientation = orientation; previousWidth = viewportSize.x; previousHeight = viewportSize.y; ResetTracking(); }
            if (Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + 1f / samplesPerSecond;
                if (AVHand_Submit(Session.subsystem.nativePtr, orientation, viewportSize.x, viewportSize.y) < 0)
                { ResetTracking(); Status = "Hand tracking unavailable"; return; }
            }
            bool fresh = AVHand_Read(out var sample) == 1 && sample.age >= 0 && sample.age < maximumSampleAge;
            var root = new Vector2(sample.rootX, sample.rootY);
            var tip = new Vector2(sample.tipX, sample.tipY);
            bool rootValid = fresh && sample.rootConfidence >= minimumConfidence && InViewport(root);
            var handedness = rootValid && sample.handedness == 1 ? TrackedHand.Right
                : rootValid && sample.handedness == -1 ? TrackedHand.Left : TrackedHand.Unknown;
            if (rootValid) RootViewportPoint = IsTracked && handedness == Handedness
                ? Vector2.Lerp(RootViewportPoint, root, 1 - Mathf.Exp(-22 * Time.unscaledDeltaTime)) : root;
            IsTracked = rootValid;
            Handedness = handedness;
            HasRootDepth = rootValid && sample.hasRootDepth != 0;
            if (HasRootDepth)
            {
                var rootSession = new Vector3(sample.rootSessionX, sample.rootSessionY, sample.rootSessionZ);
                RootWorldPosition = TrackingOrigin != null ? TrackingOrigin.TransformPoint(rootSession) : rootSession;
                if (ARCamera != null && ARCamera.WorldToViewportPoint(RootWorldPosition).z <= ARCamera.nearClipPlane)
                    HasRootDepth = false;
            }
            IsTipTracked = fresh && sample.tipConfidence >= minimumConfidence && InViewport(tip);
            TipViewportPoint = tip;
            HasTipDepth = rootValid && IsTipTracked && sample.hasDepth != 0;
            if (HasTipDepth)
            {
                var sessionPoint = new Vector3(sample.tipSessionX, sample.tipSessionY, sample.tipSessionZ);
                TipWorldPosition = TrackingOrigin != null ? TrackingOrigin.TransformPoint(sessionPoint) : sessionPoint;
            }
            Status = !rootValid ? "Show your index knuckle" : Handedness + " hand / " +
                (HasTipDepth ? "touch depth" : HasRootDepth ? "knuckle depth" : "estimated ray depth");
#else
            IsTracked = IsTipTracked = HasTipDepth = false;
            Status = "Hand tracking requires iPhone";
#endif
        }
    }
}
