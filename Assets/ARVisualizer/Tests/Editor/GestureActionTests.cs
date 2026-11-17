using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Simulation;

namespace ARVisualizer.Tests
{
    public sealed class GestureActionTests
    {
        [TestCase("CRUMPLE_AND_TOSS", "gesture.back")]
        [TestCase("GRAB_AND_RELEASE", "gesture.primary")]
        [TestCase("PINCH_AND_PULL", "edit.resize")]
        [TestCase("PINCH_AND_DRAG", "move.enter")]
        [TestCase("PINCH_AND_ROTATE", "edit.rotate")]
        [TestCase("POINT_AND_ROTATE", "screen.orientation")]
        [TestCase("WIGGLE_FINGERS", "screen.options")]
        [TestCase("CLICK", "gesture.select")]
        [TestCase("ZOOM_IN", "settings.grow")]
        [TestCase("ZOOM_OUT", "settings.shrink")]
        [TestCase("SWIPE_UP", "gesture.up")]
        [TestCase("SWIPE_LEFT", "gesture.left")]
        [TestCase("SWIPE_RIGHT", "gesture.right")]
        [TestCase("SWIPT_LEFT", "gesture.left")]
        [TestCase("SWIPT_RIGHT", "gesture.right")]
        [TestCase("SWIPE_DOWN", "gesture.down")]
        [TestCase("WRIGGLE_FINGERS", "screen.options")]
        public void CatalogueRoutesBothHands(string gesture, string expected)
        {
            foreach (string hand in new[] { "left", "right" })
            {
                Assert.IsTrue(CommandProtocol.TryParseGesture(gesture.ToLowerInvariant(), hand, out var command, out var error), error);
                Assert.AreEqual(expected, command.command);
                Assert.AreEqual(hand, command.hand);
                Assert.IsNotEmpty(command.id);
            }
            Assert.IsTrue(CommandProtocol.TryParseGesture(gesture, null, out var legacy, out _));
            Assert.AreEqual("right", legacy.hand);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("UNKNOWN")]
        public void RejectsMissingOrUnknownGestures(string gesture) =>
            Assert.IsFalse(CommandProtocol.TryParseGesture(gesture, null, out _, out _));

        static void Gesture(GestureWebSocketReceiver receiver, string name, string hand = "right") =>
            receiver.ReceiveGesturePacket(JsonUtility.ToJson(new GestureMessage { type = "GESTURE", gesture = name, hand = hand }));

        static void Aim(ARVisualizerApp app, ScreenSurface screen, TrackedHand hand = TrackedHand.Right)
        {
            using var scope = app.UseHand(hand);
            app.Hand.SetPointerEnabled(true);
            // Distance-sized screens and portrait media can extend beyond the camera view.
            // Aim at a visible uncovered part of the face, never at an off-screen corner.
            foreach (var uv in new[] { new Vector2(0.99f, 0.65f), new Vector2(0.01f, 0.65f), new Vector2(0.5f, 0.95f), Vector2.one * 0.5f })
            {
                var viewport = app.ARCamera.WorldToViewportPoint(screen.WorldPoint(uv));
                if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) continue;
                app.Hand.SetEditorSample(viewport, null);
                app.SendLocal("status");
                if (app.PointedScreen == screen) return;
            }
            Assert.AreEqual(screen, app.PointedScreen, "No visible uncovered screen target; " + app.Feedback);
        }

        static IEnumerator WaitForMedia(ScreenAppMenu menu)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (menu.IsAdvancingMedia && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(menu.IsAdvancingMedia, "Next media did not finish");
            Assert.IsFalse(menu.IsBusy);
        }

        static IEnumerator AimBlankSurface(ARVisualizerApp app)
        {
            app.Hand.SetEditorSample(new Vector2(-1, -1), null);
            var camera = Object.FindAnyObjectByType<SimulationCameraPoseProvider>();
            Assert.IsNotNull(camera);
            var start = camera.transform.localPosition;
            var rotation = camera.transform.localRotation;
            float began = Time.realtimeSinceStartup;
            do
            {
                float t = Time.realtimeSinceStartup - began;
                camera.transform.localPosition = start + rotation * new Vector3(Mathf.Sin(t * 3) * 0.2f, 0, 0);
                camera.transform.localRotation = rotation * Quaternion.Euler(35 + Mathf.Sin(t * 2) * 8, Mathf.Sin(t * 3) * 16, 0);
                yield return null;
                app.SendLocal("status");
            } while (!app.HasTarget && Time.realtimeSinceStartup - began < 20);
            Assert.IsTrue(app.HasTarget, "No simulated blank surface found");
        }

        static IEnumerator WaitForPlacement(ARVisualizerApp app)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (app.IsPlacing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(app.IsPlacing);
            Assert.IsFalse(app.PlaceModeEnabled);
            Assert.IsTrue(app.ScreenApps.IsChooserOpen);
            app.ScreenApps.Close();
        }

        [UnityTest]
        public IEnumerator GesturesOperateOnTargetsAndRespectModesAndMediaLifetime()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            // Exercise the real packet dispatcher without contacting the configured Mac.
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            // These checks exercise free mode; the start-up tutorial choice has its own tests.
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            // Create callbacks after the Play Mode domain reload so their captured locals survive.
            yield return ExerciseGestureActions();
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static IEnumerator ExerciseGestureActions()
        {
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            app.Tutorial.EnterFreeMode(); app.Tutorial.CompleteTransition();
            var receiverUnderTest = Object.FindAnyObjectByType<GestureWebSocketReceiver>();
            var hud = app.GetComponentInChildren<VisualizerHUD>();
            var controls = hud.GetComponent<NormalScreenControls>();
            app.SendLocal("pointer.on");

            // A context action with no ray must never create a screen or enter place mode.
            app.SendLocal("pointer.off");
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Assert.AreEqual(0, app.ScreenCount);
            Assert.IsFalse(app.PlaceModeEnabled);
            app.SendLocal("pointer.on");
            yield return AimBlankSurface(app);
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Assert.AreEqual(0, app.ScreenCount, "Crumple never creates a screen");
            Gesture(receiverUnderTest, "CLICK");
            Assert.AreEqual(0, app.ScreenCount, "Clicking empty space never creates a screen");
            Assert.IsFalse(app.IsPlacing);
            Vector2 expectedInitialSize = app.PlacementScreenSize;
            Gesture(receiverUnderTest, "GRAB_AND_RELEASE");
            yield return WaitForPlacement(app);
            Assert.AreEqual(1, app.ScreenCount);
            var first = app.Screens[0];
            Assert.That(first.Width, Is.EqualTo(expectedInitialSize.x).Within(0.00001f));
            Assert.That(first.Height, Is.EqualTo(expectedInitialSize.y).Within(0.00001f));
            // Move it clear of the blank target so a second context gesture creates another.
            first.transform.position += Vector3.up * 10;
            yield return AimBlankSurface(app);
            Gesture(receiverUnderTest, "GRAB_AND_RELEASE");
            yield return WaitForPlacement(app);
            Assert.AreEqual(2, app.ScreenCount);
            var newest = app.Screens[1];
            newest.transform.position += Vector3.up * 10;
            var camera = app.ARCamera.transform;
            first.transform.SetPositionAndRotation(camera.position + camera.forward - camera.up * 0.18f, camera.rotation);
            // Rotation capability is determined at placement; test both supported directions here.
            first.Initialize(first.Id, true);
            Aim(app, first);

            int size = first.SizeSteps;
            Gesture(receiverUnderTest, "ZOOM_IN");
            Assert.AreSame(first, app.AdjustedScreen);
            Assert.AreEqual(size + 1, first.SizeSteps);
            Assert.IsTrue(controls.IsOpen);
            app.Hand.SetEditorSample(new Vector2(0.01f, 0.99f), null);
            Gesture(receiverUnderTest, "ZOOM_OUT");
            Assert.AreEqual(size, first.SizeSteps, "Settings keep their selected screen when the pointer moves away");
            var rotation = first.transform.rotation;
            Gesture(receiverUnderTest, "SWIPE_LEFT");
            Assert.That(Quaternion.Angle(rotation, first.transform.rotation), Is.EqualTo(5).Within(0.01f));
            Gesture(receiverUnderTest, "SWIPE_RIGHT");
            Assert.That(Quaternion.Angle(rotation, first.transform.rotation), Is.LessThan(0.01f));
            Gesture(receiverUnderTest, "SWIPE_DOWN");
            Assert.IsFalse(app.ScreenApps.IsAdvancingMedia);
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Assert.IsFalse(app.AdjustModeEnabled);
            Assert.IsFalse(controls.IsOpen);
            Assert.AreEqual(2, app.ScreenCount, "Exiting settings must not delete anything");
            Gesture(receiverUnderTest, "SWIPE_LEFT");
            Assert.That(Quaternion.Angle(rotation, first.transform.rotation), Is.LessThan(0.01f));
            Aim(app, first);
            Gesture(receiverUnderTest, "ZOOM_OUT");
            Assert.AreEqual(size - 1, first.SizeSteps, "Zoom out also enters settings automatically");
            Gesture(receiverUnderTest, "ZOOM_IN");
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Aim(app, first);
            Gesture(receiverUnderTest, "PINCH_AND_PULL");
            Assert.AreSame(first, app.AdjustedScreen);
            Assert.IsTrue(app.Hand.PointerEnabled);
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");

            int clicks = 0;
            first.PointerClicked += (_, button, uv) =>
            {
                Assert.AreEqual(UnityEngine.EventSystems.PointerEventData.InputButton.Left, button);
                ++clicks;
            };
            Aim(app, first);
            // A blank screen may already have opened its chooser after the preceding edit.
            if (app.ScreenApps.IsChooserOpen) Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Gesture(receiverUnderTest, "WRIGGLE_FINGERS");
            Assert.IsTrue(app.ScreenApps.IsSettingsOpen, "Wriggle is an alias for the screen-settings gesture");
            Assert.AreEqual(0, clicks, "Opening screen settings must not click through to the screen");
            Gesture(receiverUnderTest, "WIGGLE_FINGERS");
            Assert.IsFalse(app.ScreenApps.IsSettingsOpen);
            app.SendLocal("ui.click");
            Assert.AreEqual(1, clicks, "The explicit ray-click command is unchanged");

            // No popup is needed to advance a black screen, then a displayed item; wrap at the end.
            var menu = app.ScreenApps;
            var library = menu.GetComponentInChildren<PhotoLibrary>();
            var media = first.GetComponent<ScreenMedia>();
            library.EditorSampleCount = 2;
            // Explicit media.next still supports opening the library's first item on a blank screen.
            Aim(app, first); app.SendLocal("media.next"); yield return WaitForMedia(menu);
            for (int i = 0; i < 3; ++i)
            {
                Aim(app, first);
                Gesture(receiverUnderTest, "SWIPE_RIGHT");
                Assert.IsTrue(menu.IsAdvancingMedia);
                Assert.IsFalse(menu.IsModal);
                Gesture(receiverUnderTest, "SWIPE_RIGHT"); // A burst must not skip an item.
                Assert.AreEqual(2, app.ScreenCount, "Loading consumes conflicting gestures");
                app.Hand.SetEditorSample(new Vector2(0.01f, 0.99f), null);
                yield return WaitForMedia(menu);
                Assert.AreEqual(((i + 1) % 2).ToString(), media.LibraryAssetId);
                Assert.IsFalse(media.IsBlank);
                Assert.IsTrue(newest.GetComponent<ScreenMedia>().IsBlank);
            }

            // The same next action must also prepare and play a real video.
            string videoPath = Path.Combine(library.CacheDirectory, "gesture-video.mp4");
            library.EditorResultOverride = op =>
            {
                if (op == "next") return new LibraryResult { ok = true, total = 1,
                    assets = new[] { new LibraryAsset { id = "video", kind = "video" } } };
                if (op != "export") return null;
                File.Copy("Assets/ARVisualizer/Tests/Editor/MediaTest.mp4", videoPath, true);
                return new LibraryResult { ok = true, path = videoPath };
            };
            Aim(app, first);
            Gesture(receiverUnderTest, "SWIPE_RIGHT");
            yield return WaitForMedia(menu);
            Assert.IsTrue(media.IsVideo && media.IsPlaying, media.Error);
            Assert.AreEqual("video", media.LibraryAssetId);
            Aim(app, first);
            Gesture(receiverUnderTest, "CLICK");
            Assert.IsFalse(media.IsPlaying, "Click pauses the pointed video");
            Gesture(receiverUnderTest, "CLICK");
            Assert.IsTrue(media.IsPlaying);
            library.EditorResultOverride = null;
            Aim(app, first);
            Gesture(receiverUnderTest, "SWIPE_RIGHT");
            yield return WaitForMedia(menu);
            Assert.AreEqual("0", media.LibraryAssetId, "An item outside the current library starts at the first accessible item");
            Assert.IsFalse(File.Exists(videoPath), "Replacing video releases its cached file");

            string current = media.LibraryAssetId;
            library.EditorResultOverride = op => op == "authorize" ? new LibraryResult { error = "Photos access denied" } : null;
            Aim(app, first);
            Gesture(receiverUnderTest, "SWIPE_RIGHT");
            yield return WaitForMedia(menu);
            Assert.AreEqual(current, media.LibraryAssetId);
            StringAssert.Contains("denied", app.Feedback);
            library.EditorResultOverride = op => op == "export" ? new LibraryResult { error = "Export failed" } : null;
            Aim(app, first);
            Gesture(receiverUnderTest, "SWIPE_RIGHT");
            yield return WaitForMedia(menu);
            Assert.AreEqual(current, media.LibraryAssetId);
            StringAssert.Contains("Export failed", app.Feedback);
            library.EditorResultOverride = op => op == "authorize" ? new LibraryResult { ok = true, limited = true } : null;
            library.EditorSampleCount = 0;
            Aim(app, first);
            Gesture(receiverUnderTest, "SWIPE_RIGHT");
            yield return WaitForMedia(menu);
            Assert.AreEqual(current, media.LibraryAssetId);
            StringAssert.Contains("No accessible", app.Feedback);
            library.EditorResultOverride = null;
            library.EditorSampleCount = 2;

            // The modal picker consumes input; free-mode gestures cannot delete its screen.
            Aim(app, first);
            menu.OpenPhotos();
            Gesture(receiverUnderTest, "ZOOM_IN");
            Assert.IsFalse(app.AdjustModeEnabled);
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Assert.IsFalse(menu.IsModal);
            Assert.AreEqual(2, app.ScreenCount);
            menu.Close();

            // Each hand retains its own selection and the other hand cannot destroy it.
            Aim(app, first, TrackedHand.Left);
            Gesture(receiverUnderTest, "PINCH_AND_PULL", "left");
            using (app.UseHand(TrackedHand.Left)) Assert.AreSame(first, app.AdjustedScreen);
            Aim(app, first);
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Assert.AreEqual(2, app.ScreenCount);
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS", "left");
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null);
            app.MenuFor(TrackedHand.Left).Close();
            Aim(app, first);
            Gesture(receiverUnderTest, "CRUMPLE_AND_TOSS");
            Assert.AreEqual(1, app.ScreenCount);
            Assert.AreSame(newest, app.Screens[0], "Delete must target the pointed screen, not the newest one");

            newest.transform.SetPositionAndRotation(camera.position + camera.forward - camera.up * 0.18f, camera.rotation);
            Aim(app, newest);
            app.SendLocal("media.next");
            Assert.IsTrue(menu.IsAdvancingMedia);
            Assert.IsTrue(app.DeleteScreen(newest));
            yield return null;
            Assert.IsFalse(menu.IsAdvancingMedia);
            Assert.AreEqual(0, app.ScreenCount, "A late library result must not resurrect a deleted screen");
        }
    }
}
