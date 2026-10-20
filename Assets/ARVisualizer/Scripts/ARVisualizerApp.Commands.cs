using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ARVisualizer
{
    // These methods belong to the same Inspector component as ARVisualizerApp.cs.
    // Keep protocol routing here and frame-by-frame interaction in the main file.
    public sealed partial class ARVisualizerApp
    {
        public void SendLocal(string command) => Execute(new VisualizerCommand
        {
            command = command,
            hand = command.StartsWith("left.", StringComparison.Ordinal) || command.StartsWith("right.", StringComparison.Ordinal)
                ? null : CurrentHand == TrackedHand.Left ? "left" : "right"
        }, _ => { });

        public async void Execute(VisualizerCommand command, Action<CommandReply> complete)
        {
            if (!CommandProtocol.TryNormalize(command, out var error))
            {
                complete?.Invoke(new CommandReply { id = command?.id, message = error });
                return;
            }
            var side = command.hand == "left" ? TrackedHand.Left : TrackedHand.Right;
            if (command.command != "status" && scopedHand == null) ControlHand = side;
            bool ok = true;
            string message = "";
            Awaitable<(bool ok, string message)> pending = null;
            try
            {
                using (UseHand(side))
                {
                    RefreshInteractionTargets();
                    switch (command.command)
                    {
                        case "goggle.enter":
                            ok = SetGoggleMode(true);
                            message = ok ? "Goggle mode / point at a HUD button and send ui.click" : "Floating HUD is unavailable";
                            break;
                        case "goggle.exit":
                            ok = SetGoggleMode(false);
                            message = ok ? "Normal mode / screen HUD restored" : "HUD is unavailable";
                            break;
                        case "ui.click":
                            ok = ClickPointer(PointerEventData.InputButton.Left, out message);
                            break;
                        case "ui.rightclick":
                            ok = ClickPointer(PointerEventData.InputButton.Right, out message);
                            break;
                        case "ui.press":
                            ok = ScreenApps.MenuInput.BeginRayPress(out message);
                            break;
                        case "ui.release":
                            ok = ScreenApps.MenuInput.EndRayPress(out message);
                            break;
                        case "app.choose":
                            message = "Screen app menu is unavailable";
                            ok = ScreenApps != null && ScreenApps.TryOpenChooser(RayScreen, out message);
                            break;
                        case "hand.calibrate":
                            ok = Hands.TryCalibrate(CurrentHand, out message);
                            break;
                        case "pointer.on":
                            Hand.SetPointerEnabled(true);
                            message = "Hand pointer enabled";
                            break;
                        case "pointer.off":
                            ok = TryDisablePointer(out message);
                            break;
                        case "place.enter":
                            if (MoveModeEnabled) SetPlaceMode(false);
                            SetPlaceMode(true);
                            message = "Place mode / move slowly to scan a flat surface";
                            break;
                        case "place.exit":
                            SetPlaceMode(false);
                            message = "View mode / surface highlights hidden";
                            break;
                        case "cube.place":
                        case "screen.place":
                            pending = MoveModeEnabled ? MoveScreenAsync() : PlaceScreenAsync();
                            break;
                        case "move.enter":
                            ok = TryEnterMoveMode(out message);
                            break;
                        case "move.confirm":
                            pending = MoveScreenAsync();
                            break;
                        case "move.cancel":
                            if (MoveModeEnabled) SetPlaceMode(false);
                            message = "Move cancelled / original screen position kept";
                            break;
                        case "cube.undo":
                        case "screen.undo":
                            ok = TryUndoScreen(out message);
                            break;
                        case "cubes.clear":
                        case "screens.clear":
                            ForEachHand(() => { SetPlaceMode(false); ++placementGeneration; });
                            while (ScreenCount > 0) RemoveLast();
                            message = "All screens cleared";
                            break;
                        case "adjust.enter":
                            ok = TryEnterAdjustMode(out message);
                            break;
                        case "adjust.exit":
                            ExitAdjustMode();
                            message = "Adjust mode closed";
                            break;
                        case "adjust.grow":
                            ok = TryResizeScreen(1, out message);
                            break;
                        case "adjust.shrink":
                            ok = TryResizeScreen(-1, out message);
                            break;
                        case "adjust.rotate.cw":
                            ok = TryRotateScreen(-rotationStepDegrees, out message);
                            break;
                        case "adjust.rotate.ccw":
                            ok = TryRotateScreen(rotationStepDegrees, out message);
                            break;
                        case "status":
                            string mode = MoveModeEnabled ? "Move mode" : AdjustModeEnabled ? "Adjust mode" : PlaceModeEnabled ? "Place mode" : "View mode";
                            message = mode + "; " + TrackingStatus + "; " + Hand.Status + "; " + Communication.Status;
                            break;
                        default:
                            ok = false;
                            message = "Unknown command";
                            break;
                    }
                }
                // The hand scope ends before yielding: another hand can issue commands
                // while ARKit creates this anchor. The operation captures its own context.
                if (pending != null) (ok, message) = await pending;
            }
            catch (Exception exception)
            {
                ContextFor(side).IsPlacing = false;
                ok = false;
                message = "Command failed: " + exception.Message;
                Debug.LogException(exception);
            }

            if (this == null || !isActiveAndEnabled)
            {
                complete?.Invoke(new CommandReply { id = command.id, hand = command.hand, message = "Application stopped; command cancelled" });
                return;
            }

            // Refresh the reply after the command's effects, without reopening dismissed menus.
            using (UseHand(side))
            {
                GoggleHUD?.RefreshPointer();
                ScreenApps?.RefreshPointer();
                RefreshScreenInput();
                Feedback = message;
                hud.RefreshStatus();
                var reply = CreateReply(ok, message);
                reply.id = command.id;
                complete?.Invoke(reply);
            }
        }

        bool TryDisablePointer(out string message)
        {
            if (GoggleModeEnabled)
            {
                message = "The ray pointer stays on in goggle mode";
                return false;
            }
            Hand.SetPointerEnabled(false);
            message = "Pointer off / aim with centre crosshair";
            return true;
        }

        bool ClickPointer(PointerEventData.InputButton button, out string message)
        {
            if (!PointerReady)
            {
                message = "Enable the pointer and wait for tracking to click";
                return false;
            }
            if (ScreenApps != null && ScreenApps.BlocksPointer)
                return button == PointerEventData.InputButton.Left
                    ? ScreenApps.TryClick(out message)
                    : ScreenApps.TryRightClick(out message);
            if (GoggleModeEnabled && GoggleHUD.IsPointerOverHUD)
            {
                if (button == PointerEventData.InputButton.Left)
                {
                    hud.RefreshStatus();
                    return GoggleHUD.TryClick(out message);
                }
                message = "This HUD control has no right-click action";
                return false;
            }
            var screen = RayScreen;
            if (screen == null)
            {
                message = "Point the ray at a screen or menu control";
                return false;
            }
            int id = screen.Id;
            bool clicked = screen.Click(CurrentHand, button, rayScreenUV);
            message = clicked ? button + " click on screen " + id : "Screen is unavailable for clicks";
            return clicked;
        }

        async Awaitable<(bool ok, string message)> PlaceScreenAsync()
        {
            if (!PlaceModeEnabled) return (false, "Enter place mode first (place.enter)");
            RefreshTarget();
            if (IsPlacing) return (false, "Placement already in progress");
            if (!HasTarget) return (false, "Aim at a detected flat surface");
            if (ScreenCount + (OtherContext.IsPlacing && OtherContext.MovingScreen == null ? 1 : 0) >= maximumScreens) return (false, "Screen limit reached; undo or clear screens");
            if (screenPrefab == null) return (false, "Assign the Screen prefab in the application Inspector");

            var owner = Context;
            IsPlacing = true;
            int version = placementGeneration;
            bool horizontalSupport = targetOnHorizontal;
            try
            {
                var result = await anchors.TryAddAnchorAsync(targetPose);
                using var scope = UseHand(owner.Side);
                if (this == null || version != placementGeneration || !isActiveAndEnabled)
                {
                    if (result.status.IsSuccess() && result.value != null) Destroy(result.value.gameObject);
                    return (false, "Placement cancelled");
                }
                if (!result.status.IsSuccess()) return (false, "Could not create AR anchor; try again");

                var screen = Instantiate(screenPrefab, result.value.transform);
                screen.transform.localPosition = Vector3.zero;
                screen.transform.localRotation = Quaternion.identity;
                screen.transform.localScale = Vector3.one;
                screen.Initialize(nextScreenId++, horizontalSupport);
                screen.name = "Screen " + screen.Id;
                placed.Add(screen);

                SetPlaceMode(false);
                if (!Hand.PointerEnabled) Hand.SetPointerEnabled(true);
                ScreenApps?.ShowPinnedChooser(screen);
                return (true, "Screen placed / " + FormatScreenSize(screen));
            }
            finally
            {
                owner.IsPlacing = false;
            }
        }

        bool TryUndoScreen(out string message)
        {
            if (MoveModeEnabled)
            {
                SetPlaceMode(false);
                message = "Move cancelled / original screen position kept";
                return true;
            }
            if (IsPlacing)
            {
                ++placementGeneration;
                message = "Pending placement cancelled";
                return true;
            }
            if (ScreenCount == 0)
            {
                message = "No screens to undo";
                return false;
            }
            RemoveLast();
            message = "Last screen removed";
            return true;
        }

        bool TryEnterAdjustMode(out string message)
        {
            if (MoveModeEnabled)
            {
                message = "Confirm or cancel the screen move first";
                return false;
            }
            if (AdjustModeEnabled)
            {
                message = "Already adjusting screen " + AdjustedScreen.Id;
                return true;
            }
            if (IsPlacing)
            {
                message = "Wait for placement to complete";
                return false;
            }
            if (PointedScreen == null)
            {
                message = "Point the ray at a screen first";
                return false;
            }
            if (IsScreenBusyForOtherHand(PointedScreen))
            {
                message = "This screen is in use by the other hand";
                return false;
            }
            SetPlaceMode(false);
            AdjustedScreen = PointedScreen;
            AdjustedScreen.IsAdjusting = true;
            message = "Adjusting screen " + AdjustedScreen.Id;
            return true;
        }

        bool TryResizeScreen(int steps, out string message)
        {
            if (!AdjustModeEnabled)
            {
                message = "Enter adjust mode first (adjust.enter)";
                return false;
            }
            bool resized = AdjustedScreen.Resize(steps);
            message = resized ? "Screen size / " + FormatScreenSize(AdjustedScreen) : "Screen size limit reached";
            return resized;
        }

        bool TryRotateScreen(float degrees, out string message)
        {
            if (!AdjustModeEnabled)
            {
                message = "Enter adjust mode first (adjust.enter)";
                return false;
            }
            bool rotated = AdjustedScreen.Rotate(degrees);
            message = rotated ? "Screen rotated" : "Only screens on horizontal surfaces can rotate";
            return rotated;
        }

        static string FormatScreenSize(ScreenSurface screen) =>
            (screen.Width * 100).ToString("0.#") + " x " + (screen.Height * 100).ToString("0.#") + " cm";

        CommandReply CreateReply(bool ok, string message) => new CommandReply
        {
            ok = ok,
            message = message,
            hand = CurrentHand == TrackedHand.Left ? "left" : "right",
            leftHand = HandState(leftContext),
            rightHand = HandState(rightContext),
            pointerEnabled = Hand.PointerEnabled,
            placeModeEnabled = PlaceModeEnabled,
            cubeCount = ScreenCount,
            screenCount = ScreenCount,
            goggleModeEnabled = GoggleModeEnabled,
            hudVisible = hud != null && hud.IsHUDVisible,
            pointedHUDButton = GoggleModeEnabled ? GoggleHUD.TargetName : "",
            pointedAppButton = ScreenApps != null ? ScreenApps.TargetName : "",
            appMenuScreenId = ScreenApps != null && ScreenApps.Target != null ? ScreenApps.Target.Id : 0,
            appChooserOpen = ScreenApps != null && ScreenApps.IsChooserOpen,
            photoLibraryOpen = ScreenApps != null && ScreenApps.IsModal,
            adjustModeEnabled = AdjustModeEnabled,
            moveModeEnabled = MoveModeEnabled,
            movingScreenId = MovingScreen != null ? MovingScreen.Id : 0,
            pointedScreenId = RayScreen != null ? RayScreen.Id : 0,
            interaction = InteractionState(Context),
            adjustedScreen = ScreenState.From(AdjustedScreen)
        };
        HandCommandState HandState(HandContext state) => new HandCommandState
        {
            tracked = Hands.GetHand(state.Side).IsTracked,
            reportedHand = Hands.GetHand(state.Side).ReportedHand.ToString().ToLowerInvariant(),
            identityStabilized = Hands.GetHand(state.Side).IdentityStabilized,
            rootConfidence = Hands.GetHand(state.Side).RootConfidence,
            sampleAgeSeconds = Hands.GetHand(state.Side).SampleAgeSeconds,
            pointerEnabled = Hands.GetHand(state.Side).PointerEnabled,
            hasTipDepth = Hands.GetHand(state.Side).HasTipDepth,
            placeModeEnabled = state.PlaceMode,
            moveModeEnabled = state.MovingScreen != null,
            pointedScreenId = state.RayScreen != null ? state.RayScreen.Id : 0,
            adjustedScreenId = state.AdjustedScreen != null ? state.AdjustedScreen.Id : 0,
            movingScreenId = state.MovingScreen != null ? state.MovingScreen.Id : 0,
            interaction = InteractionState(state)
        };

        static ScreenInteractionState InteractionState(HandContext state) => state.InputScreen == null
            ? new ScreenInteractionState { kind = "none" }
            : new ScreenInteractionState { screenId = state.InputScreen.Id, kind = state.InputKind.ToString().ToLowerInvariant(),
                u = state.InputUV.x, v = state.InputUV.y,
                xMetres = state.InputUV.x * state.InputScreen.Width, yMetres = state.InputUV.y * state.InputScreen.Height };
    }
}
