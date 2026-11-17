using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ARVisualizer
{
    /// <summary>Four tag25h9 AprilTags. IDs identify both the hand and the point being tracked.</summary>
    [Serializable]
    public sealed class GloveMarkerSettings
    {
        [Header("Printed marker IDs (tag25h9)")]
        [SerializeField, Range(0, 34)] int leftRootId = 0;
        [SerializeField, Range(0, 34)] int leftTipId = 1;
        [SerializeField, Range(0, 34)] int rightRootId = 2;
        [SerializeField, Range(0, 34)] int rightTipId = 3;
        [Header("Detection quality")]
        [Tooltip("Minimum black/white decode margin. Increase to reject weak detections; decrease only after testing your lighting.")]
        [SerializeField, Range(1, 100)] float minimumMargin = 30;
        [Tooltip("Minimum length of every detected marker edge, in camera pixels.")]
        [SerializeField, Range(4, 80)] float minimumEdgePixels = 14;
        [Tooltip("1 uses the full camera resolution for finding squares. Larger values save work but can miss small fingertip markers.")]
        [SerializeField, Range(1, 4)] float quadDecimate = 1;
        [Header("Fingertip contact calibration")]
        [Tooltip("Measured width of the printed fingertip marker's BLACK square, excluding white border, in metres. Nominal: 0.014 m. If printing shrinks it to 12.6 mm, enter 0.0126. Measure your print instead of relying on the print dialog's scale.")]
        [SerializeField, Range(0.005f, 0.1f)] float tipWidthMetres = 0.014f;
        [Tooltip("Offset from the left tip marker's centre to the actual contact point, in metres. X: printed right; Y: printed TOP; Z: out of the printed face. Negative Z goes into the glove. Zero uses the marker centre.")]
        [SerializeField] Vector3 leftContactOffset;
        [Tooltip("Same marker-local axes as Left Contact Offset. Calibrate against your glove/finger-pad position; start at zero.")]
        [SerializeField] Vector3 rightContactOffset;

        public int RootId(TrackedHand side) => side == TrackedHand.Left ? leftRootId : rightRootId;
        public int TipId(TrackedHand side) => side == TrackedHand.Left ? leftTipId : rightTipId;

        public bool TryValidate(out string error)
        {
            bool IdValid(int id) => id >= 0 && id <= 34;
            if (!IdValid(leftRootId) || !IdValid(leftTipId) || !IdValid(rightRootId) || !IdValid(rightTipId))
            { error = "Marker IDs must be between 0 and 34 (tag25h9)"; return false; }
            if (leftRootId == leftTipId || leftRootId == rightRootId || leftRootId == rightTipId
                || leftTipId == rightRootId || leftTipId == rightTipId || rightRootId == rightTipId)
            { error = "Use a different ID for each root and fingertip marker"; return false; }
            bool OffsetValid(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z)
                && Mathf.Abs(value.x) <= 0.05f && Mathf.Abs(value.y) <= 0.05f && Mathf.Abs(value.z) <= 0.05f;
            if (!float.IsFinite(minimumMargin) || minimumMargin < 1 || minimumMargin > 255
                || !float.IsFinite(minimumEdgePixels) || minimumEdgePixels < 4
                || !float.IsFinite(quadDecimate) || quadDecimate < 1 || quadDecimate > 4
                || !float.IsFinite(tipWidthMetres) || tipWidthMetres < 0.005f || tipWidthMetres > 0.1f
                || !OffsetValid(leftContactOffset) || !OffsetValid(rightContactOffset))
            { error = "Check marker quality, fingertip size and contact offsets (up to 5 cm per axis)"; return false; }
            error = null;
            return true;
        }

        internal NativeMarkerConfig NativeConfig => new NativeMarkerConfig
        {
            leftRootId = leftRootId, leftTipId = leftTipId, rightRootId = rightRootId, rightTipId = rightTipId,
            minimumMargin = minimumMargin, minimumEdgePixels = minimumEdgePixels, quadDecimate = quadDecimate,
            tipWidthMetres = tipWidthMetres, leftContactOffset = leftContactOffset, rightContactOffset = rightContactOffset
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMarkerConfig
    {
        public int leftRootId, leftTipId, rightRootId, rightTipId;
        public float minimumMargin, minimumEdgePixels, quadDecimate, tipWidthMetres;
        public Vector3 leftContactOffset, rightContactOffset;
    }
}
