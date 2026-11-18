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
        [Tooltip("Minimum confidence for acquiring a hand and for fingertip touch.")]
        [SerializeField, Range(0.1f, 1)] float minimumConfidence = 0.35f;
        [Tooltip("Older samples cannot aim or touch a screen.")]
        [SerializeField, Range(0.05f, 0.3f)] float maximumSampleAge = 0.2f;
        [Header("Hand identity stability")]
        [Tooltip("A new hand needs this many consecutive camera detections with the same left/right label.")]
        [SerializeField, Range(2, 8)] int identityAcquisitionFrames = 3;
        [Tooltip("Remember a hand identity through brief detection gaps. Missing poses cannot click or touch.")]
        [SerializeField, Range(0.15f, 0.8f)] float identityMemorySeconds = 0.45f;
        [Tooltip("Maximum motion between detections, in viewport units. Ambiguous overlapping tracks pause rather than change hands.")]
        [SerializeField, Range(0.05f, 0.35f)] float identityMatchDistance = 0.18f;
        [Tooltip("An already identified hand can keep aiming at this confidence. Fingertip touches still require Minimum Confidence.")]
        [SerializeField, Range(0.1f, 1)] float continuingConfidence = 0.25f;
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
        public float LateralOffsetMetres => lateralOffsetMetres;
        internal float FallbackRootDepthMetres => fallbackRootDepthMetres;
        HandPointer left, right;
        public HandPointer Left => left ??= new HandPointer(this, TrackedHand.Left);
        public HandPointer Right => right ??= new HandPointer(this, TrackedHand.Right);
        public HandPointer GetHand(TrackedHand side) => side == TrackedHand.Left ? Left : Right;
        readonly HandIdentityTracker identities = new HandIdentityTracker();
        NativeSample firstObservation, secondObservation;
        double observationFrame, observationReadTime;
        double lastObservationTime = double.NegativeInfinity;
        public int DetectedHandCount { get; private set; }
        // Avoid a centre-pointer flash during identity acquisition or a short dropout.
        public bool SuppressCentreFallback => Time.realtimeSinceStartupAsDouble - lastObservationTime < identityMemorySeconds;
        public bool IsRecognizingHands => DetectedHandCount > 0 && !Left.IsTracked && !Right.IsTracked;
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
        [DllImport("__Internal")] static extern double AVHand_ReadObservations(out NativeSample first, out NativeSample second);
        [DllImport("__Internal")] static extern void AVHand_Reset();
#endif
        void ResetTracking()
        {
            Left.ResetTracking();
            Right.ResetTracking();
            identities.Reset();
            firstObservation = secondObservation = default;
            observationFrame = 0;
            DetectedHandCount = 0;
            lastObservationTime = double.NegativeInfinity;
            nextSample = 0;
#if UNITY_IOS && !UNITY_EDITOR
            AVHand_Reset();
#endif
        }
        void OnDisable() => ResetTracking();
        void OnApplicationPause(bool paused) => ResetTracking();
        internal static bool InViewport(Vector2 p) => float.IsFinite(p.x) && float.IsFinite(p.y)
            && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1;
        void SetStatus(string text) { Left.Status = Right.Status = text; }

        void Update()
        {
            // Turning off the ray leaves fingertip tracking running for direct touch.
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
                if (AVHand_Submit(Session.subsystem.nativePtr, orientation, viewportSize.x, viewportSize.y) < 0)
                { ResetTracking(); SetStatus("Hand tracking unavailable"); return; }
            }
            double frame = AVHand_ReadObservations(out var first, out var second);
            ApplyObservations(frame, first, second, Time.realtimeSinceStartupAsDouble);
#else
            ResetTracking();
            SetStatus("Hand tracking requires iPhone");
#endif
        }

        void ConfigureIdentityTracking()
        {
            identities.AcquisitionFrames = identityAcquisitionFrames;
            identities.MemorySeconds = identityMemorySeconds;
            identities.MaximumMovement = identityMatchDistance;
            identities.AcquisitionConfidence = minimumConfidence;
            identities.ContinueConfidence = continuingConfidence;
        }

        HandIdentityTracker.Observation IdentityObservation(NativeSample sample)
        {
            bool fresh = sample.age >= 0 && sample.age < maximumSampleAge;
            return new HandIdentityTracker.Observation(new Vector2(sample.rootX, sample.rootY),
                sample.handedness == -1 ? TrackedHand.Left : sample.handedness == 1 ? TrackedHand.Right : TrackedHand.Unknown,
                fresh ? sample.rootConfidence : 0,
                new Vector3(sample.rootSessionX, sample.rootSessionY, sample.rootSessionZ), fresh && sample.hasRootDepth != 0);
        }

        void ApplyObservations(double frame, NativeSample first, NativeSample second, double now)
        {
            firstObservation = first; secondObservation = second;
            observationFrame = frame; observationReadTime = now;
            ConfigureIdentityTracking();
            var firstIdentity = IdentityObservation(first);
            var secondIdentity = IdentityObservation(second);
            bool FirstValid(HandIdentityTracker.Observation value) => value.Confidence >= Mathf.Min(minimumConfidence, continuingConfidence)
                && InViewport(value.Position);
            DetectedHandCount = (FirstValid(firstIdentity) ? 1 : 0) + (FirstValid(secondIdentity) ? 1 : 0);
            double age = Math.Max(first.age, second.age);
            double capturedAt = double.IsFinite(age) ? now - age : double.NegativeInfinity;
            if (DetectedHandCount > 0) lastObservationTime = capturedAt;
            identities.Resolve(frame, capturedAt, now, firstIdentity, secondIdentity, out int leftIndex, out int rightIndex);
            ApplyIdentifiedSample(Left, leftIndex);
            ApplyIdentifiedSample(Right, rightIndex);
        }

        void ApplyIdentifiedSample(HandPointer hand, int index)
        {
            var sample = index == 0 ? firstObservation : index == 1 ? secondObservation : default;
            int reported = sample.handedness;
            if (index >= 0) sample.handedness = (int)hand.Side;
            ApplySample(hand, sample);
            hand.ReportedHand = reported == -1 ? TrackedHand.Left : reported == 1 ? TrackedHand.Right : TrackedHand.Unknown;
            hand.IdentityStabilized = hand.IsTracked && hand.ReportedHand != hand.Side;
            if (hand.IdentityStabilized) hand.Status += " / identity stabilized";
            if (!hand.IsTracked && IsRecognizingHands) hand.Status = "Identifying hands / hold steady briefly";
        }

        /// <summary>Explicitly identify the only visible hand when Vision's initial label is wrong.</summary>
        public bool TryCalibrate(TrackedHand side, out string message)
        {
            message = "Show only your " + side.ToString().ToLowerInvariant() + " hand clearly, then calibrate again";
            double now = Time.realtimeSinceStartupAsDouble;
            var first = firstObservation; var second = secondObservation;
            first.age += now - observationReadTime; second.age += now - observationReadTime;
            var a = IdentityObservation(first); var b = IdentityObservation(second);
            bool Visible(HandIdentityTracker.Observation value) => value.Confidence >= Mathf.Min(minimumConfidence, continuingConfidence)
                && InViewport(value.Position);
            bool firstVisible = Visible(a), secondVisible = Visible(b);
            if (firstVisible == secondVisible || (side != TrackedHand.Left && side != TrackedHand.Right)) return false;
            int index = firstVisible ? 0 : 1;
            var observation = firstVisible ? a : b;
            if (observation.Confidence < minimumConfidence) return false;
            double capturedAt = now - (firstVisible ? first.age : second.age);
            ConfigureIdentityTracking();
            identities.Calibrate(side, index, observationFrame, capturedAt, observation);
            Left.ResetTracking(); Right.ResetTracking();
            firstObservation = first; secondObservation = second; observationReadTime = now;
            ApplyIdentifiedSample(Left, side == TrackedHand.Left ? index : -1);
            ApplyIdentifiedSample(Right, side == TrackedHand.Right ? index : -1);
            message = "Visible hand identified as " + side + "; identity is retained while it stays tracked";
            return true;
        }

        void ApplySample(HandPointer hand, NativeSample sample)
        {
            bool fresh = sample.age >= 0 && sample.age < maximumSampleAge && sample.handedness == (int)hand.Side;
            var root = new Vector2(sample.rootX, sample.rootY);
            var tip = new Vector2(sample.tipX, sample.tipY);
            float rootThreshold = hand.IsTracked ? Mathf.Min(minimumConfidence, continuingConfidence) : minimumConfidence;
            bool rootValid = fresh && sample.rootConfidence >= rootThreshold && InViewport(root);
            hand.RootConfidence = sample.rootConfidence;
            hand.SampleAgeSeconds = double.IsFinite(sample.age) ? (float)sample.age : -1;
            var handedness = rootValid && sample.handedness == 1 ? TrackedHand.Right
                : rootValid && sample.handedness == -1 ? TrackedHand.Left : TrackedHand.Unknown;
            if (rootValid) hand.RootViewportPoint = hand.IsTracked && handedness == hand.Handedness
                ? Vector2.Lerp(hand.RootViewportPoint, root, 1 - Mathf.Exp(-22 * Time.unscaledDeltaTime)) : root;
            hand.IsTracked = rootValid;
            hand.Handedness = handedness;
            hand.HasRootDepth = rootValid && sample.hasRootDepth != 0;
            if (hand.HasRootDepth)
            {
                var rootSession = new Vector3(sample.rootSessionX, sample.rootSessionY, sample.rootSessionZ);
                hand.RootWorldPosition = TrackingOrigin != null ? TrackingOrigin.TransformPoint(rootSession) : rootSession;
                if (ARCamera != null && ARCamera.WorldToViewportPoint(hand.RootWorldPosition).z <= ARCamera.nearClipPlane)
                    hand.HasRootDepth = false;
            }
            hand.IsTipTracked = fresh && sample.tipConfidence >= minimumConfidence && InViewport(tip);
            hand.TipViewportPoint = tip;
            hand.HasTipDepth = rootValid && hand.IsTipTracked && sample.hasDepth != 0;
            if (hand.HasTipDepth)
            {
                var sessionPoint = new Vector3(sample.tipSessionX, sample.tipSessionY, sample.tipSessionZ);
                hand.TipWorldPosition = TrackingOrigin != null ? TrackingOrigin.TransformPoint(sessionPoint) : sessionPoint;
            }
            hand.Status = !rootValid ? "Show your index knuckle" : hand.Handedness + " hand / " +
                (hand.HasTipDepth ? "touch depth" : hand.HasRootDepth ? "knuckle depth" : "estimated ray depth");
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
