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
            if (name == "screen.options" && command.sourceGesture == "WIGGLE_FINGERS")
            {
                var keyboardBrowser = PointerReady && CanInteractWithScreenApp(RayScreen, CurrentHand)
                    ? RayScreen.GetComponent<ScreenBrowser>() : null;
                if (keyboardBrowser != null && keyboardBrowser.IsKeyboardOpen)
                {
                    var key = keyboardBrowser.View.ButtonAt(rayScreenUV);
                    return key != null && key.transform.IsChildOf(keyboardBrowser.View.keyboard.transform)
                        ? "browser.click" : "gesture.unavailable";
                }
            }
            if (!name.StartsWith("gesture.")) return name;
            if (GuideBlocksPointer) return "gesture.blocked";
            bool menu = ScreenApps != null && (ScreenApps.IsModal || ScreenApps.IsChooserOpen || ScreenApps.IsSettingsOpen);
            bool menuSelection = menu && (ScreenApps.HoveredButton != null || ScreenApps.NavigationFocus != null
                || (ScreenApps.IsModal && ScreenApps.LibraryGrid.FocusedIndex >= 0));
            var browser = PointerReady && CanInteractWithScreenApp(RayScreen, CurrentHand) ? RayScreen.GetComponent<ScreenBrowser>() : null;
            if (name == "gesture.select")
            {
                // A non-modal app dropdown must not steal clicks from the floating HUD.
                if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD && (ScreenApps == null || !ScreenApps.BlocksPointer)) return "ui.click";
                // An explicit swipe selection wins over a shaky ray. Otherwise use the aimed control.
                if (menu) return menuSelection ? "menu.select" : "gesture.unavailable";
                if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) return "ui.click";
                if (ScreenApps != null && ScreenApps.IsPointerOverUI) return "ui.click";
                if (MoveModeEnabled || AdjustModeEnabled || PlaceModeEnabled || IsPlacing) return "gesture.unavailable";
                if (browser != null && browser.IsOpen) return "browser.click";
                if (content != null && content.IsOpen) return "app.primary";
                if (PointedScreen != null)
                    return PointedScreen.GetComponent<ScreenMedia>().IsBlank ? "app.choose" : "media.primary";
                return "gesture.unavailable";
            }
            if (name == "gesture.apps")
            {
                if (MoveModeEnabled || AdjustModeEnabled || PlaceModeEnabled || IsPlacing
                    || (ScreenApps != null && (ScreenApps.IsModal || ScreenApps.IsAdvancingMedia))) return "gesture.unavailable";
                if (ScreenApps != null && ScreenApps.IsChooserOpen) return "menu.back";
                if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) return "gesture.unavailable";
                return "app.choose";
            }
            if (name == "gesture.primary")
            {
                if (Context.PendingDelete != null) return "screen.delete.confirm";
                if (MoveModeEnabled) return "move.confirm";
                if (AdjustModeEnabled) return "edit.confirm";
                if (menu) return menuSelection ? "menu.select" : "gesture.unavailable";
                if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD) return "gesture.unavailable";
                if (PlaceModeEnabled) return "screen.place";
                if (PointedScreen != null) return "gesture.unavailable";
                return "screen.create";
            }
            if (name == "gesture.back")
            {
                if (MoveModeEnabled) return "move.cancel";
                if (AdjustModeEnabled) return "edit.cancel";
                if (!PlaceModeEnabled && !IsPlacing && !ScreenApps.IsModal && PointedScreen != null) return "screen.delete.arm";
                if (menu || (ScreenApps != null && ScreenApps.IsAdvancingMedia)) return "menu.back";
                if (PlaceModeEnabled || IsPlacing) return "place.exit";
                return "screen.delete.arm";
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
            if (browser != null && browser.IsOpen)
                return CommandProtocol.CommandForAppGesture(browser.IsPdf ? "pdf" : "browser",
                    command.sourceGesture ?? name.Replace("gesture.", "SWIPE_").ToUpperInvariant()) ?? "gesture.unavailable";
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
            AdjustedScreen.EditMode = mode;
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

        // Browser and PDF screens turn between landscape and portrait. Other apps keep their own shapes.
        bool TrySetOrientation(string command, out string message)
        {
            message = "Point at a web browser or PDF screen outside an edit or the file list";
            if (!PointerReady || GuideBlocksPointer || IsPlacing || MoveModeEnabled || AdjustModeEnabled || PlaceModeEnabled
                || (ScreenApps != null && (ScreenApps.IsModal || ScreenApps.IsAdvancingMedia))
                || (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD)) return false;
            var screen = PointedScreen;
            if (screen == null || IsScreenBusyForOtherHand(screen)) return false;
            var browser = screen.GetComponent<ScreenBrowser>();
            if (browser == null || !browser.IsOpen)
            {
                message = "Vertical mode is available in the web browser and PDF viewer";
                return false;
            }
            bool vertical = command == "screen.vertical" || (command == "screen.orientation" && !browser.IsVertical);
            // Menus attached to the old outline would no longer line up with the screen.
            ScreenApps?.Close();
            return browser.SetVertical(vertical, out message);
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
