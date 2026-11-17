using UnityEngine;

namespace ARVisualizer
{
    public enum ScreenEditMode { None, Resize, Rotate }

    public sealed partial class ARVisualizerApp
    {
        public ScreenEditMode EditMode => Context.EditMode;
        public GestureGuideHUD GestureGuide { get; private set; }
        public bool GuideBlocksPointer => GestureGuide != null && GestureGuide.BlocksPointer(CurrentHand);

        // Resolve intent before executing a command. Explicit app/adjust commands retain their meaning.
        string ResolveGestureCommand(VisualizerCommand command, ScreenAppContent content)
        {
            string name = command.command;
            if (!string.IsNullOrEmpty(command.sourceGesture) && GuideBlocksPointer) return "gesture.blocked";
            if (!name.StartsWith("gesture.")) return name;
            if (GuideBlocksPointer) return "gesture.blocked";
            bool menu = ScreenApps != null && (ScreenApps.IsModal || ScreenApps.IsChooserOpen || ScreenApps.IsSettingsOpen);
            if (name == "gesture.primary")
            {
                if (MoveModeEnabled) return "move.confirm";
                if (AdjustModeEnabled) return "edit.confirm";
                if (menu) return "menu.select";
                if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) return "ui.click";
                if (PlaceModeEnabled) return "screen.place";
                if (content != null && content.IsOpen) return "app.primary";
                if (PointedScreen != null) return PointedScreen.GetComponent<ScreenMedia>().IsBlank ? "app.choose" : "media.primary";
                return "screen.create";
            }
            if (name == "gesture.back")
            {
                if (MoveModeEnabled) return "move.cancel";
                if (AdjustModeEnabled) return "edit.cancel";
                if (menu || (ScreenApps != null && ScreenApps.IsAdvancingMedia)) return "menu.back";
                if (PlaceModeEnabled || IsPlacing) return "place.exit";
                return "screen.delete";
            }
            if (MoveModeEnabled || PlaceModeEnabled || IsPlacing) return "gesture.unavailable";
            if (AdjustModeEnabled)
            {
                bool resize = EditMode == ScreenEditMode.Resize || EditMode == ScreenEditMode.None;
                bool rotate = EditMode == ScreenEditMode.Rotate || EditMode == ScreenEditMode.None;
                if (resize && name == "gesture.up") return "adjust.grow";
                if (resize && name == "gesture.down") return "adjust.shrink";
                if (rotate && name == "gesture.left") return "adjust.rotate.ccw";
                if (rotate && name == "gesture.right") return "adjust.rotate.cw";
                return "gesture.unavailable";
            }
            if (menu) return name.Replace("gesture.", "menu.");
            if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) return "gesture.unavailable";
            if (content != null && content.IsOpen)
                return CommandProtocol.CommandForAppGesture(content.AppId,
                    command.sourceGesture ?? name.Replace("gesture.", "SWIPE_").ToUpperInvariant()) ?? "gesture.unavailable";
            var media = PointedScreen != null ? PointedScreen.GetComponent<ScreenMedia>() : null;
            if (media != null && media.AppId == "photos")
            {
                if (name == "gesture.left") return "media.previous";
                if (name == "gesture.right") return "media.next";
                if (name == "gesture.up") return "media.browse";
            }
            return "gesture.unavailable";
        }

        bool TryBeginGestureEdit(ScreenEditMode mode, out string message)
        {
            message = "Close the library or finish moving first";
            if (MoveModeEnabled || IsPlacing || (ScreenApps != null && (ScreenApps.IsModal || ScreenApps.IsAdvancingMedia))) return false;
            var screen = AdjustedScreen != null ? AdjustedScreen : PointedScreen;
            if (mode == ScreenEditMode.Rotate && screen != null && !screen.CanRotate)
            { message = "Only screens on horizontal surfaces can rotate"; return false; }
            if (!AdjustModeEnabled && (!PointerReady || GuideBlocksPointer))
            { message = "Point at a screen first"; return false; }
            if (!TryEnterAdjustMode(out message)) return false;
            Context.EditMode = mode;
            ScreenApps?.Close();
            // Keep the same snapshot when switching between resize and rotate.
            message = mode.ToString().ToUpperInvariant() + " / swipe to adjust; grab to confirm; crumple to cancel";
            return true;
        }

        bool FinishScreenEdit(bool cancel, out string message)
        {
            message = "No screen edit is active";
            if (!AdjustModeEnabled) return false;
            if (cancel)
            {
                AdjustedScreen.Resize(Context.OriginalSizeSteps - AdjustedScreen.SizeSteps);
                AdjustedScreen.transform.localRotation = Context.OriginalRotation;
            }
            ExitAdjustMode();
            message = cancel ? "Edit cancelled / original size and rotation restored" : "Screen edit confirmed";
            return true;
        }

        bool TryDeletePointedScreen(out string message)
        {
            message = "Point at an available screen to delete it";
            if (!PointerReady || GuideBlocksPointer || IsPlacing || MoveModeEnabled || AdjustModeEnabled
                || (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD)) return false;
            var screen = PointedScreen;
            if (screen == null || IsScreenBusyForOtherHand(screen)) return false;
            int id = screen.Id;
            if (!DeleteScreen(screen)) return false;
            message = "Screen " + id + " deleted";
            return true;
        }

        async Awaitable<(bool ok, string message)> CreateScreenFromGestureAsync()
        {
            if (!PointerReady || GuideBlocksPointer || PointedScreen != null || MoveModeEnabled || AdjustModeEnabled
                || (ScreenApps != null && ScreenApps.BlocksPointer) || (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD))
                return (false, "Point at a blank flat surface to create a screen");
            var ray = ARCamera.ViewportPointToRay(PointerViewportPoint);
            foreach (var screen in placed)
                if (screen != null && screen.TryRaycast(ray, maximumPlacementDistance, out _, out _))
                    return (false, "This surface already has a screen");
            return await PlaceScreenAsync(requirePlaceMode: false);
        }

        bool TryMediaAction(string action, out string message)
        {
            message = "Point at a photo or video in free mode";
            if (!PointerReady || AdjustModeEnabled || PlaceModeEnabled || IsPlacing || GuideBlocksPointer) return false;
            var screen = PointedScreen;
            var media = screen != null ? screen.GetComponent<ScreenMedia>() : null;
            if (media == null || media.AppId != "photos" || IsScreenBusyForOtherHand(screen)) return false;
            if (action == "media.primary" && media.IsVideo)
            { media.TogglePlayback(); message = media.IsPlaying ? "Video playing" : "Video paused"; return true; }
            return ScreenApps.TryBrowseMedia(screen, out message);
        }
    }
}
