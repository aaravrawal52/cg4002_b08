using UnityEngine;

namespace ARVisualizer
{
    /// <summary>One hand's tracking and pointer switch. The source samples both hands together.</summary>
    public sealed class HandPointer
    {
        readonly HandPointerSource source;
        public TrackedHand Side { get; }
        public bool PointerEnabled { get; private set; } = true;
        public bool PointerLockedOn { get; private set; }
        public bool IsTracked { get; internal set; }
        public bool IsTipTracked { get; internal set; }
        public bool HasTipDepth { get; internal set; }
        public bool HasRootDepth { get; internal set; }
        public TrackedHand Handedness { get; internal set; }
        public TrackedHand ReportedHand { get; internal set; }
        public bool IdentityStabilized { get; internal set; }
        public float RootConfidence { get; internal set; }
        public float SampleAgeSeconds { get; internal set; }
        public Vector2 RootViewportPoint { get; internal set; } = Vector2.one * 0.5f;
        public Vector2 TipViewportPoint { get; internal set; }
        public Vector3 RootWorldPosition { get; internal set; }
        public Vector3 TipWorldPosition { get; internal set; }
        public string Status { get; internal set; } = "Show your index knuckle";
        public Vector2Int ViewportSize => source.ViewportSize;
        public float LateralOffsetMetres => source.LateralOffsetMetres;
        public Vector3 RayOrigin
        {
            get
            {
                var camera = source.ARCamera;
                if (camera == null) return RootWorldPosition;
                var root = HasRootDepth ? RootWorldPosition : camera.ViewportToWorldPoint(
                    new Vector3(RootViewportPoint.x, RootViewportPoint.y, source.FallbackRootDepthMetres));
                float side = Handedness == TrackedHand.Right ? -1 : Handedness == TrackedHand.Left ? 1 : 0;
                return root + camera.transform.right * (side * LateralOffsetMetres);
            }
        }
        public Vector2 ViewportPoint => source.ARCamera != null
            ? (Vector2)source.ARCamera.WorldToViewportPoint(RayOrigin) : RootViewportPoint;

        internal HandPointer(HandPointerSource source, TrackedHand side) { this.source = source; Side = side; }

        public void SetPointerEnabled(bool value)
        {
            if (PointerLockedOn && !value) return;
            PointerEnabled = value;
            ResetTracking();
        }

        public void SetPointerLock(bool locked)
        {
            PointerLockedOn = locked;
            if (locked && !PointerEnabled) SetPointerEnabled(true);
        }

        internal void ResetTracking()
        {
            IsTracked = IsTipTracked = HasTipDepth = HasRootDepth = false;
            Handedness = ReportedHand = TrackedHand.Unknown;
            IdentityStabilized = false;
            RootConfidence = 0;
            SampleAgeSeconds = -1;
        }

#if UNITY_EDITOR
        internal bool UsesEditorSample { get; private set; }
        public void SetEditorSample(Vector2 root, Vector3? fingertip, TrackedHand handedness = TrackedHand.Unknown, Vector3? rootWorld = null)
        {
            UsesEditorSample = true;
            IsTracked = HandPointerSource.InViewport(root);
            RootViewportPoint = root;
            Handedness = handedness;
            HasRootDepth = rootWorld.HasValue;
            RootWorldPosition = rootWorld ?? Vector3.zero;
            IsTipTracked = HasTipDepth = fingertip.HasValue;
            TipWorldPosition = fingertip ?? Vector3.zero;
            if (source.ARCamera != null && fingertip.HasValue) TipViewportPoint = source.ARCamera.WorldToViewportPoint(fingertip.Value);
        }
        public void ClearEditorSample() { UsesEditorSample = false; ResetTracking(); }
#endif
    }
}
