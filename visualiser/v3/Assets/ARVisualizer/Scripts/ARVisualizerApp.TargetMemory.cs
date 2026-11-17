using System;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    public sealed partial class ARVisualizerApp
    {
        void RememberGestureTarget()
        {
            if (Context.GestureViewport.HasValue || !PointerReady || !Hand.IsTracked || GuideBlocksPointer) return;
            var button = ScreenApps != null ? ScreenApps.HoveredButton : null;
            var screen = button != null ? ScreenApps.Target : RayScreen;
            if (screen == null) return;
            var browser = screen.GetComponent<ScreenBrowser>();
            if (button == null && browser != null && browser.IsOpen) button = browser.View.ButtonAt(rayScreenUV);
            Context.RememberedScreen = screen;
            Context.RememberedButton = button;
            Context.RememberedUV = rayScreenUV;
            Context.RememberedApp = screen.GetComponent<ScreenMedia>()?.AppId;
            Context.RememberedAt = Time.unscaledTime;
        }

        GestureMemoryScope UseRememberedGestureTarget(VisualizerCommand command)
        {
            var scope = new GestureMemoryScope(Context);
            if (string.IsNullOrEmpty(command.sourceGesture)) return scope;
            bool deletion = command.sourceGesture == "CRUMPLE_AND_TOSS";
            bool confirm = command.sourceGesture == "GRAB_AND_RELEASE" && Context.PendingDelete != null;
            if (!deletion && !confirm) ClearPendingDeletion();
            // Never use memory for deletion, or override a live screen/UI target.
            if (deletion || confirm || MoveModeEnabled || AdjustModeEnabled || PlaceModeEnabled
                || RayScreen != null || GuideBlocksPointer || ScreenApps.IsPointerOverUI
                || (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) || !Hand.PointerEnabled
                || UnityEngine.XR.ARFoundation.ARSession.state != UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking)
                return scope;
            var screen = Context.RememberedScreen;
            var button = Context.RememberedButton;
            if (screen == null || !screen.isActiveAndEnabled || screen.IsAdjusting || screen.DeletionPending
                || Time.unscaledTime - Context.RememberedAt > 1
                || screen.GetComponent<ScreenMedia>()?.AppId != Context.RememberedApp) return scope;
            // Unity's destroyed-object comparison also invalidates replaced keys/menu buttons.
            if (!ReferenceEquals(button, null) && (button == null || !button.isActiveAndEnabled || !button.IsInteractable())) return scope;
            Vector3 world = screen.WorldPoint(Context.RememberedUV);
            if (button != null)
            {
                var rect = (RectTransform)button.transform;
                world = rect.TransformPoint(rect.rect.center);
            }
            var viewport = ARCamera.WorldToViewportPoint(world);
            if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) return scope;
            Context.GestureViewport = viewport;
            RefreshInteractionTargets();
            // Re-raycast: a newly blocking menu/screen must not receive the remembered action.
            bool matches = button != null
                ? ScreenApps.HoveredButton == button || (RayScreen == screen && screen.GetComponent<ScreenBrowser>() is ScreenBrowser browser
                    && browser.IsOpen && browser.View.ButtonAt(rayScreenUV) == button)
                : RayScreen == screen;
            if (!matches) { Context.GestureViewport = null; RefreshInteractionTargets(); }
            return scope;
        }

        readonly struct GestureMemoryScope : IDisposable
        {
            readonly HandContext context;
            readonly Vector2? previous;
            public GestureMemoryScope(HandContext context) { this.context = context; previous = context.GestureViewport; }
            public void Dispose() { context.GestureViewport = previous; }
        }

        void ClearPendingDeletion()
        {
            var screen = Context.PendingDelete;
            Context.PendingDelete = null;
            if (screen != null && OtherContext.PendingDelete != screen) screen.DeletionPending = false;
        }

        bool ArmDeletion(out string message)
        {
            message = "Point at a screen to mark it for deletion";
            var screen = RayScreen != null ? RayScreen : ScreenApps.IsPointerOverUI ? ScreenApps.Target : null;
            if (!PointerReady || GuideBlocksPointer || screen == null || MoveModeEnabled || AdjustModeEnabled
                || PlaceModeEnabled || IsPlacing || IsScreenBusyForOtherHand(screen)
                || (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD)) return false;
            ClearPendingDeletion();
            Context.PendingDelete = screen;
            screen.DeletionPending = true;
            ScreenApps.Close();
            message = "Screen " + screen.Id + " marked red / point at it and grab to delete";
            return true;
        }

        bool ConfirmDeletion(out string message)
        {
            message = "Point at the red screen and grab to confirm deletion";
            var screen = Context.PendingDelete;
            if (!PointerReady || GuideBlocksPointer || screen == null || !screen.DeletionPending || RayScreen != screen
                || MoveModeEnabled || AdjustModeEnabled || PlaceModeEnabled || IsScreenBusyForOtherHand(screen)) return false;
            int id = screen.Id;
            ClearPendingDeletion();
            bool deleted = DeleteScreen(screen);
            if (deleted) message = "Screen " + id + " deleted";
            return deleted;
        }
    }
}
