using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer
{
    public enum TrackedHand { Unknown = 0, Left = -1, Right = 1 }

    /// <summary>Glove markers for ray aiming; separate tip markers with measured LiDAR depth for touch.
    /// The component name/GUID stays compatible with existing scenes and prefabs.</summary>
    [AddComponentMenu("AR Visualizer/Glove Marker Tracking")]
    public sealed class HandPointerSource : MonoBehaviour
    {
        [Header("iPhone glove markers")]
        [SerializeField] GloveMarkerSettings markers = new GloveMarkerSettings();
        [SerializeField, Range(5, 30)] float samplesPerSecond = 15;
        [Header("Tracking stability")]
        [Tooltip("Hold each ray's last valid aim through brief missed detections. Seconds since the last successful camera capture; 0 disables the hold. Touch always requires a fresh detection.")]
        [SerializeField, Range(0, 0.8f)] float trackingGraceSeconds = 0.5f;
        [Tooltip("Older camera samples cannot update aim or touch. The last valid aim may remain during Tracking Grace Seconds.")]
        [SerializeField, Range(0.05f, 0.3f)] float maximumSampleAge = 0.3f;
        [Tooltip("Minimum delay before normal-mode centre fallback after the last root observation. Tracking Grace Seconds can keep a ray visible longer.")]
        [SerializeField, Range(0.15f, 0.8f)] float markerMemorySeconds = 0.5f;
        public GloveMarkerSettings Markers => markers;
        [Header("Ray offset")]
        [Tooltip("Metres to the left of a right hand, or right of a left hand, in the phone's view.")]
        [SerializeField, Min(0)] float lateralOffsetMetres = 0.03f;
        [Tooltip("Estimated root-marker distance when LiDAR depth is unavailable. Used only for ray positioning.")]
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
        public float LateralOffsetMetres => lateralOffsetMetres;
        internal float FallbackRootDepthMetres => fallbackRootDepthMetres;
        HandPointer left, right;
        public HandPointer Left => left ??= new HandPointer(this, TrackedHand.Left);
        public HandPointer Right => right ??= new HandPointer(this, TrackedHand.Right);
        public HandPointer GetHand(TrackedHand side) => side == TrackedHand.Left ? Left : Right;
        double lastObservationTime = double.NegativeInfinity;
        public int DetectedHandCount { get; private set; }
        public bool SuppressCentreFallback => Left.IsTracked || Right.IsTracked
            || Time.realtimeSinceStartupAsDouble - lastObservationTime < markerMemorySeconds;
        public bool IsRecognizingHands => false; // IDs identify markers immediately; no chirality acquisition step.
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
        [DllImport("__Internal")] static extern int AVMarker_Submit(IntPtr session, int orientation, int width, int height, ref NativeMarkerConfig config);
        [DllImport("__Internal")] static extern double AVMarker_ReadObservations(out NativeSample first, out NativeSample second);
        [DllImport("__Internal")] static extern void AVMarker_Reset();
#endif
        void ClearObservations()
        {
            Left.ResetTracking();
            Right.ResetTracking();
            DetectedHandCount = 0;
            lastObservationTime = double.NegativeInfinity;
        }
        void ResetTracking()
        {
            ClearObservations();
            nextSample = 0;
#if UNITY_IOS && !UNITY_EDITOR
            AVMarker_Reset();
#endif
        }
        void OnDisable() => ResetTracking();
        void OnApplicationPause(bool paused) => ResetTracking();
        internal static bool InViewport(Vector2 p) => float.IsFinite(p.x) && float.IsFinite(p.y)
            && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1;
        static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        void SetStatus(string text) { Left.Status = Right.Status = text; }

        void Update()
        {
            // Turning off the ray leaves marker tracking running for direct touch.
            string markerError = null;
            if (markers == null || !markers.TryValidate(out markerError))
            {
                ResetTracking();
                SetStatus(markers == null ? "Assign glove marker settings" : markerError);
                return;
            }
#if UNITY_EDITOR
            UpdateEditorHand(Left);
            UpdateEditorHand(Right);
#elif UNITY_IOS
            if (ARSession.state != ARSessionState.SessionTracking || Session == null || Session.subsystem == null)
            { ResetTracking(); SetStatus("Waiting for AR camera"); return; }
            int orientation = Screen.orientation == ScreenOrientation.LandscapeRight ? 4 : 3;
            var viewportSize = ViewportSize;
            if (previousOrientation != orientation || previousWidth != viewportSize.x || previousHeight != viewportSize.y)
            { previousOrientation = orientation; previousWidth = viewportSize.x; previousHeight = viewportSize.y; ResetTracking(); }
            if (Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + 1f / samplesPerSecond;
                var config = markers.NativeConfig;
                if (AVMarker_Submit(Session.subsystem.nativePtr, orientation, viewportSize.x, viewportSize.y, ref config) < 0)
                { ResetTracking(); SetStatus("Marker tracking unavailable"); return; }
            }
            double frame = AVMarker_ReadObservations(out var first, out var second);
            ApplyObservations(frame, first, second, Time.realtimeSinceStartupAsDouble);
#else
            ResetTracking();
            SetStatus("Marker tracking requires iPhone");
#endif
        }

        void ApplyObservations(double frame, NativeSample first, NativeSample second, double now)
        {
            // A cleared native buffer means reset/reconfiguration, not a missed detection.
            // Do not reset the native worker here: it may be preparing the first frame.
            if (!double.IsFinite(frame) || frame <= 0) { ClearObservations(); return; }
            // Marker IDs are authoritative. Do not reassign a left marker to a right-hand track
            // when the hands cross or one disappears. Duplicate IDs are rejected in the detector.
            NativeSample For(TrackedHand side) => first.handedness == (int)side ? first
                : second.handedness == (int)side ? second : default;
            if (first.handedness != 0 && first.handedness == second.handedness) first = second = default;
            ApplySample(Left, For(TrackedHand.Left), frame, now);
            ApplySample(Right, For(TrackedHand.Right), frame, now);
            DetectedHandCount = (Left.IsTracked ? 1 : 0) + (Right.IsTracked ? 1 : 0);
        }

        /// <summary>Compatibility command: marker IDs fix identity; calibration never swaps them.</summary>
        public bool TryCalibrate(TrackedHand side, out string message)
        {
            bool tracked = side != TrackedHand.Unknown && GetHand(side).IsTracked;
            message = tracked ? side + " marker ID " + markers.RootId(side) + " determines this hand; no identity calibration needed"
                : "Show the " + side.ToString().ToLowerInvariant() + " root marker; check its ID in Glove Marker Tracking";
            return tracked;
        }

        bool CanHoldRay(HandPointer hand, double now)
        {
            double elapsed = now - hand.LastRootObservationTime;
            if (!hand.IsTracked || elapsed < 0 || elapsed >= trackingGraceSeconds) return false;
            if (!hand.HasRootDepth || ARCamera == null) return true;
            var viewport = ARCamera.WorldToViewportPoint(hand.RootWorldPosition);
            return viewport.z > ARCamera.nearClipPlane && InViewport(viewport);
        }

        void ApplySample(HandPointer hand, NativeSample sample, double frame, double now)
        {
            double age = sample.age;
            if (frame == hand.LastRootFrame && age >= 0)
                age = Math.Max(age, now - hand.LastRootObservationTime);
            bool fresh = age >= 0 && age < maximumSampleAge && frame >= hand.LastRootFrame
                && sample.handedness == (int)hand.Side;
            var root = new Vector2(sample.rootX, sample.rootY);
            var tip = new Vector2(sample.tipX, sample.tipY);
            bool rootValid = fresh && float.IsFinite(sample.rootConfidence) && sample.rootConfidence > 0 && InViewport(root);
            hand.RootConfidence = rootValid ? sample.rootConfidence : 0;
            hand.SampleAgeSeconds = double.IsFinite(age) ? (float)age : -1;
            if (rootValid) hand.RootViewportPoint = hand.IsTracked
                ? Vector2.Lerp(hand.RootViewportPoint, root, 1 - Mathf.Exp(-22 * Time.unscaledDeltaTime)) : root;
            hand.IsRayHeld = !rootValid && CanHoldRay(hand, now);
            hand.IsTracked = rootValid || hand.IsRayHeld;
            hand.Handedness = hand.IsTracked ? hand.Side : TrackedHand.Unknown;
            hand.ReportedHand = rootValid ? hand.Side : TrackedHand.Unknown;
            hand.IdentityStabilized = false;
            if (rootValid && frame > hand.LastRootFrame)
            {
                hand.LastRootFrame = frame;
                hand.LastRootObservationTime = now - age;
                lastObservationTime = Math.Max(lastObservationTime, hand.LastRootObservationTime);
            }
            if (!hand.IsRayHeld) hand.HasRootDepth = rootValid && sample.hasRootDepth != 0;
            if (rootValid && hand.HasRootDepth)
            {
                var rootSession = new Vector3(sample.rootSessionX, sample.rootSessionY, sample.rootSessionZ);
                hand.RootWorldPosition = TrackingOrigin != null ? TrackingOrigin.TransformPoint(rootSession) : rootSession;
                if (!Finite(hand.RootWorldPosition) || (ARCamera != null && ARCamera.WorldToViewportPoint(hand.RootWorldPosition).z <= ARCamera.nearClipPlane))
                    hand.HasRootDepth = false;
            }
            // Only the ray is debounced. Never synthesize a contact from a held root or tip.
            hand.IsTipTracked = rootValid && float.IsFinite(sample.tipConfidence) && sample.tipConfidence > 0 && InViewport(tip);
            hand.TipViewportPoint = tip;
            hand.HasTipDepth = hand.HasRootDepth && hand.IsTipTracked && sample.hasDepth != 0;
            if (hand.HasTipDepth)
            {
                var sessionPoint = new Vector3(sample.tipSessionX, sample.tipSessionY, sample.tipSessionZ);
                hand.TipWorldPosition = TrackingOrigin != null ? TrackingOrigin.TransformPoint(sessionPoint) : sessionPoint;
                if (!Finite(hand.TipWorldPosition) || (ARCamera != null && ARCamera.WorldToViewportPoint(hand.TipWorldPosition).z <= ARCamera.nearClipPlane))
                    hand.HasTipDepth = false;
            }
            hand.Status = hand.IsRayHeld ? hand.Side + " marker / holding ray briefly"
                : !rootValid ? "Show " + hand.Side.ToString().ToLowerInvariant() + " root marker (ID " + markers.RootId(hand.Side) + ")"
                : hand.Side + " marker / " + (hand.HasTipDepth ? "touch depth" : hand.HasRootDepth ? "root depth" : "estimated ray depth");
        }
#if UNITY_EDITOR
        void UpdateEditorHand(HandPointer hand)
        {
            if (hand.UsesEditorSample) return;
            hand.ResetTracking();
            // The mouse simulates the selected hand; explicit samples can simulate both.
            if (hand.Side != editorHand || Mouse.current == null) return;
            var p = Mouse.current.position.ReadValue();
            hand.RootViewportPoint = new Vector2(p.x / Screen.width, p.y / Screen.height);
            hand.IsTracked = InViewport(hand.RootViewportPoint);
            hand.Handedness = hand.IsTracked ? editorHand : TrackedHand.Unknown;
            hand.TipViewportPoint = hand.RootViewportPoint;
            hand.IsTipTracked = hand.IsTracked;
            hand.HasTipDepth = hand.IsTracked && ARCamera != null && Mouse.current.leftButton.isPressed;
            if (hand.HasTipDepth) hand.TipWorldPosition = ARCamera.ViewportToWorldPoint(
                new Vector3(hand.RootViewportPoint.x, hand.RootViewportPoint.y, editorTouchDepthMetres));
            hand.Status = "Editor / " + hand.Handedness + " hand";
        }
#endif
    }
}
