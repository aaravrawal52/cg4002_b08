using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer
{
    public sealed partial class ARVisualizerApp
    {
        bool TryEnterMoveMode(out string message)
        {
            if (MoveModeEnabled)
            {
                message = "Already moving screen " + MovingScreen.Id;
                return true;
            }
            if (IsPlacing)
            {
                message = "Wait for placement to complete";
                return false;
            }
            var screen = AdjustedScreen != null ? AdjustedScreen : PointedScreen;
            if (screen == null || !screen.isActiveAndEnabled || !placed.Contains(screen))
            {
                message = "Point at a screen or select it in Screen Settings first";
                return false;
            }

            if (IsScreenBusyForOtherHand(screen))
            {
                message = "This screen is in use by the other hand";
                return false;
            }
            SetPlaceMode(true);
            MovingScreen = screen;
            retainedPlane = null;
            if (!Hand.PointerEnabled) Hand.SetPointerEnabled(true);
            ClearScreenInput();
            message = "Moving screen " + screen.Id + " / aim at a surface, then confirm or cancel";
            return true;
        }

        async Awaitable<(bool ok, string message)> MoveScreenAsync()
        {
            if (!MoveModeEnabled) return (false, "Select a screen to move first (move.enter)");
            if (IsPlacing) return (false, "Placement already in progress");
            RefreshTarget();
            if (!HasTarget) return (false, "Aim at a detected flat surface");

            var owner = Context;
            var screen = MovingScreen;
            int version = placementGeneration;
            bool horizontalSupport = targetOnHorizontal;
            IsPlacing = true;
            try
            {
                // The old anchor and media stay live until the new anchor is ready.
                var result = await anchors.TryAddAnchorAsync(targetPose);
                using var scope = UseHand(owner.Side);
                bool cancelled = this == null || !isActiveAndEnabled || version != placementGeneration
                    || screen == null || !screen.isActiveAndEnabled || MovingScreen != screen || !placed.Contains(screen);
                if (cancelled)
                {
                    if (result.status.IsSuccess()) ReleaseAnchor(result.value);
                    return (false, "Move cancelled / original screen position kept");
                }
                if (!result.status.IsSuccess()) return (false, "Could not create AR anchor; try moving again");

                var oldAnchor = screen.GetComponentInParent<ARAnchor>();
                screen.transform.SetParent(result.value.transform, false);
                screen.transform.localPosition = Vector3.zero;
                screen.transform.localRotation = Quaternion.identity;
                screen.transform.localScale = Vector3.one;
                screen.SetHorizontalSupport(horizontalSupport);
                ReleaseAnchor(oldAnchor);
                SetPlaceMode(false);
                return (true, "Screen " + screen.Id + " moved");
            }
            finally
            {
                owner.IsPlacing = false;
            }
        }
    }
}
