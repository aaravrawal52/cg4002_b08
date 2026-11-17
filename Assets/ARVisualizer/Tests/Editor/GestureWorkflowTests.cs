using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer.Tests
{
    public sealed class GestureWorkflowTests
    {
        static CommandReply Send(ARVisualizerApp app, string gesture, TrackedHand hand = TrackedHand.Right)
        {
            Assert.IsTrue(CommandProtocol.TryParseGesture(gesture, hand == TrackedHand.Left ? "left" : "right", out var command, out var error), error);
            CommandReply reply = null;
            app.Execute(command, r => reply = r);
            Assert.IsNotNull(reply, gesture + " should be synchronous in this check");
            return reply;
        }

        static void Aim(ARVisualizerApp app, Vector3 point, TrackedHand hand = TrackedHand.Right)
        {
            var pointer = app.Hands.GetHand(hand);
            pointer.SetPointerEnabled(true);
            pointer.SetEditorSample(app.ARCamera.WorldToViewportPoint(point), null);
            app.SendLocal(hand == TrackedHand.Left ? "left.status" : "right.status");
        }
        static Vector3 Centre(Component component)
        { var rect = (RectTransform)component.transform; return rect.TransformPoint(rect.rect.center); }

        [UnityTest]
        public IEnumerator EditsMenusAppsAndGuideRespectBothHands()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            // These checks exercise free mode; the start-up tutorial choice has its own tests.
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            yield return Exercise();
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static IEnumerator Exercise()
        {
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            var hud = app.GetComponentInChildren<VisualizerHUD>();
            float until = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            app.Tutorial.EnterFreeMode(); app.Tutorial.CompleteTransition();
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(950, true);
            ((IList<ScreenSurface>)app.Screens).Add(screen);
            screen.transform.SetPositionAndRotation(app.ARCamera.transform.position + app.ARCamera.transform.forward * 0.6f, app.ARCamera.transform.rotation);
            var content = screen.GetComponent<ScreenAppContent>(); content.Open("timer", app);
            var edge = new Vector2(0.99f, 0.9f);
            Aim(app, screen.WorldPoint(edge));
            int originalSize = screen.SizeSteps; var originalRotation = screen.transform.localRotation;
            Assert.IsTrue(Send(app, "pinch_and_pull").ok);
            Assert.AreEqual(ScreenEditMode.Resize, app.EditMode);
            Assert.AreEqual(ScreenEditMode.Resize, screen.EditMode);
            Assert.IsFalse(Send(app, "click").ok, "Editing cannot accidentally activate the app");
            Assert.IsFalse(Send(app, "wiggle_fingers").ok, "Finish or cancel the edit before switching apps");
            app.RightHand.SetEditorSample(new Vector2(0.01f, 0.99f), null);
            Assert.IsTrue(Send(app, "swipe_up").ok);
            Assert.AreEqual(originalSize + 1, screen.SizeSteps, "The selected screen stays locked when the ray leaves");
            Aim(app, screen.WorldPoint(edge), TrackedHand.Left);
            Assert.IsFalse(Send(app, "pinch_and_drag", TrackedHand.Left).ok, "The other hand cannot take an edit");
            Assert.IsFalse(Send(app, "click", TrackedHand.Left).ok);
            Assert.IsFalse(Send(app, "crumple_and_toss", TrackedHand.Left).ok);
            Assert.IsTrue(Send(app, "pinch_and_rotate").ok);
            Assert.AreEqual(ScreenEditMode.Rotate, screen.EditMode, "Switching edits changes the screen's aura, even while the ray points away");
            Assert.IsTrue(Send(app, "swipt_left").ok);
            Assert.That(Quaternion.Angle(originalRotation, screen.transform.localRotation), Is.EqualTo(5).Within(0.01));
            Assert.IsTrue(Send(app, "crumple_and_toss").ok);
            Assert.AreEqual(originalSize, screen.SizeSteps);
            Assert.AreEqual(ScreenEditMode.None, screen.EditMode, "Cancel removes the aura");
            Assert.That(Quaternion.Angle(originalRotation, screen.transform.localRotation), Is.LessThan(0.01));
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null); app.MenuFor(TrackedHand.Left).Close();

            Aim(app, screen.WorldPoint(edge));
            Send(app, "pinch_and_pull"); Send(app, "swipe_up"); Send(app, "grab_and_release");
            Assert.IsFalse(app.AdjustModeEnabled); Assert.AreEqual(originalSize + 1, screen.SizeSteps);
            Assert.AreEqual(ScreenEditMode.None, screen.EditMode, "Confirm removes the aura");
            Aim(app, screen.WorldPoint(edge));
            Send(app, "pinch_and_drag"); Assert.IsTrue(app.MoveModeEnabled);
            var position = screen.transform.position;
            Send(app, "crumple_and_toss"); Assert.IsFalse(app.MoveModeEnabled);
            Assert.AreEqual(position, screen.transform.position);
            screen.Initialize(screen.Id, false);
            Aim(app, screen.WorldPoint(edge)); Assert.IsFalse(Send(app, "pinch_and_rotate").ok);
            Assert.IsFalse(app.AdjustModeEnabled); screen.Initialize(screen.Id, true);

            // Up / down change the digit the hand points towards; nothing is selected first.
            Aim(app, Centre(content.View.ActiveStyle.digits[2]));
            Assert.IsTrue(Send(app, "swipe_up").ok);
            Assert.AreEqual(70, content.Timer.PresetSeconds);
            // Left / right change the timer's look, and with it the shape of the screen.
            Assert.IsTrue(Send(app, "swipt_right").ok);
            Assert.AreEqual(1, content.TimerStyleIndex);
            Assert.AreEqual(70, content.Timer.PresetSeconds);
            Assert.That(screen.Width / screen.Height, Is.EqualTo(content.View.ActiveStyle.aspect).Within(0.001f));
            Aim(app, Centre(content.View.ActiveStyle.digits[3])); Send(app, "swipe_up");
            Assert.AreEqual(71, content.Timer.PresetSeconds);
            Assert.IsFalse(Send(app, "grab_and_release").ok, "Grab does not operate apps");
            Assert.IsFalse(content.Timer.IsRunning);
            Assert.IsTrue(Send(app, "click").ok); Assert.IsTrue(content.Timer.IsRunning);
            Send(app, "click"); Assert.IsFalse(content.Timer.IsRunning);
            Aim(app, Centre(content.View.ActiveStyle.digits[0]), TrackedHand.Left);
            Assert.IsTrue(Send(app, "swipt_left", TrackedHand.Left).ok);
            Assert.AreEqual(0, content.TimerStyleIndex, "Either hand changes the look of the shared timer");
            Assert.That(screen.Width / screen.Height, Is.EqualTo(ScreenSurface.DefaultAspectRatio).Within(0.001f));
            Assert.IsTrue(Send(app, "click", TrackedHand.Left).ok);
            Assert.IsTrue(content.Timer.IsRunning);
            Send(app, "click", TrackedHand.Left);
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null); app.MenuFor(TrackedHand.Left).Close();

            Aim(app, screen.WorldPoint(edge));
            Assert.IsFalse(Send(app, "point_and_rotate").ok, "Only browser and PDF screens turn vertical");
            Assert.IsTrue(Send(app, "wiggle_fingers").ok); Assert.IsTrue(app.ScreenApps.IsSettingsOpen);
            Assert.IsFalse(content.Timer.IsRunning, "Screen settings must not activate the app");
            Assert.IsTrue(Send(app, "wiggle_fingers").ok); Assert.IsFalse(app.ScreenApps.IsSettingsOpen);
            Aim(app, screen.WorldPoint(edge));
            Assert.IsTrue(Send(app, "wiggle_fingers").ok); Assert.IsTrue(app.ScreenApps.IsSettingsOpen);
            Send(app, "swipe_right"); Send(app, "swipe_right");
            Assert.AreEqual("Move", app.ScreenApps.NavigationFocus.name);
            app.RightHand.SetEditorSample(new Vector2(0.01f, 0.99f), null);
            Send(app, "click"); Assert.IsTrue(app.MoveModeEnabled, "Click accepts the swipe selection even when the ray leaves");
            Send(app, "crumple_and_toss");
            Aim(app, screen.WorldPoint(edge)); Send(app, "wiggle_fingers");
            Send(app, "wiggle_fingers"); Assert.IsFalse(app.ScreenApps.IsSettingsOpen);
            Aim(app, screen.WorldPoint(edge)); Send(app, "wiggle_fingers");
            Send(app, "swipe_right"); Send(app, "click");
            Assert.IsTrue(app.ScreenApps.IsChooserOpen);
            Send(app, "swipe_right"); Send(app, "swipe_right"); Send(app, "swipe_right");
            Send(app, "click"); Assert.AreEqual("flashcards", content.AppId);
            Aim(app, screen.WorldPoint(edge)); Send(app, "click"); Assert.IsTrue(content.AnswerVisible);
            Send(app, "swipt_right"); Assert.AreEqual(1, content.CardIndex); Assert.IsFalse(content.AnswerVisible);

            var media = screen.GetComponent<ScreenMedia>(); media.ShowPhoto(new Texture2D(16, 9));
            var library = app.ScreenApps.GetComponentInChildren<PhotoLibrary>(); library.EditorSampleCount = 30;
            Aim(app, screen.WorldPoint(edge)); Send(app, "click"); Assert.IsTrue(app.ScreenApps.IsModal);
            until = Time.realtimeSinceStartup + 10;
            while ((app.ScreenApps.LibraryGrid.Total == 0 || app.ScreenApps.IsBusy) && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(30, app.ScreenApps.LibraryGrid.Total);
            app.RightHand.SetEditorSample(new Vector2(0.99f, 0.99f), null);
            for (int i = 0; i < 5; i++) Send(app, "swipe_up");
            Assert.Greater(app.ScreenApps.LibraryGrid.Scroll.content.anchoredPosition.y, 0);
            int focused = app.ScreenApps.LibraryGrid.FocusedIndex;
            Send(app, "swipt_left"); Assert.AreEqual(focused - 1, app.ScreenApps.LibraryGrid.FocusedIndex);
            until = Time.realtimeSinceStartup + 8;
            while (app.ScreenApps.IsBusy && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(Send(app, "click").ok);
            until = Time.realtimeSinceStartup + 8;
            while (app.ScreenApps.IsModal && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual((focused - 1).ToString(), media.LibraryAssetId);
            Aim(app, screen.WorldPoint(edge)); Send(app, "swipe_left");
            until = Time.realtimeSinceStartup + 8;
            while (app.ScreenApps.IsAdvancingMedia && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual((focused - 2).ToString(), media.LibraryAssetId);

            hud.SetHUDVisible(false); Canvas.ForceUpdateCanvases();
            Assert.IsFalse(hud.transform.Find("Safe Area/Normal Actions").gameObject.activeInHierarchy);
            Assert.IsNotNull(app.GestureGuide);
            var strip = (RectTransform)hud.transform.Find("Safe Area/Gesture Guide/Guide Strip");
            var guidePanel = (RectTransform)hud.transform.Find("Safe Area/Gesture Guide/Guide Panel");
            // Overlay geometry uses physical screen coordinates; the pointer takes normalized camera UV.
            Vector2 stripPixel = RectTransformUtility.WorldToScreenPoint(null, strip.TransformPoint(strip.rect.center));
            app.RightHand.SetEditorSample(new Vector2(stripPixel.x / Screen.width, stripPixel.y / Screen.height), null);
            app.SendLocal("right.status"); Assert.IsTrue(app.GestureGuide.IsOpen); Assert.IsTrue(app.GuideBlocksPointer);
            Assert.IsFalse(Send(app, "crumple_and_toss").ok); Assert.AreEqual(1, app.ScreenCount);
            Assert.IsFalse(Send(app, "wiggle_fingers").ok);
            Assert.IsFalse(Send(app, "click").ok);
            app.RightHand.SetEditorSample(new Vector2(0.01f, 0.01f), null); app.SendLocal("right.status");
            yield return new WaitForSecondsRealtime(1.4f); Assert.IsFalse(app.GestureGuide.IsOpen);
            hud.SetHUDVisible(true); Assert.IsFalse(strip.gameObject.activeInHierarchy);

            hud.SetHUDVisible(false); app.SendLocal("goggle.enter"); yield return null; Canvas.ForceUpdateCanvases();
            Aim(app, Centre(strip), TrackedHand.Left);
            using (app.UseHand(TrackedHand.Left))
            { Assert.IsTrue(app.GestureGuide.IsOpen); Assert.IsTrue(app.GuideBlocksPointer); }
            Assert.IsTrue(app.GoggleModeEnabled); Assert.IsTrue(guidePanel.gameObject.activeInHierarchy);
            hud.SetHUDVisible(true); Canvas.ForceUpdateCanvases();
            // Keep the open dropdown away from the HUD button being tested.
            screen.transform.position = app.ARCamera.transform.position + app.ARCamera.transform.forward
                + app.ARCamera.transform.right * 0.8f;
            app.SendLocal("right.status"); app.ScreenApps.ShowPinnedChooser(screen);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var modeButton = hud.transform.Find("Safe Area/Controls/Mode Button").GetComponent<Button>();
            Aim(app, Centre(modeButton));
            Assert.IsTrue(app.ScreenApps.IsChooserOpen);
            Assert.IsFalse(app.ScreenApps.BlocksPointer, "The test ray must not overlap the screen dropdown");
            Assert.IsTrue(app.GoggleHUD.IsPointerOverHUD, "HUD target: " + app.GoggleHUD.TargetName
                + "; tracked: " + app.RightHand.IsTracked + "; active button: " + modeButton.isActiveAndEnabled
                + "; graphic depth: " + modeButton.targetGraphic.depth + "; viewport: " + app.RightHand.ViewportPoint);
            Assert.IsTrue(Send(app, "click").ok);
            Assert.IsTrue(app.PlaceModeEnabled, "The floating HUD receives the click instead of the open app dropdown");
            app.SendLocal("place.exit");
            app.SendLocal("goggle.exit"); app.SendLocal("screens.clear");
        }

        [Test]
        public void GuideFitsPhoneAndGoggleLayouts()
        {
            foreach (int height in new[] { 720, 960 })
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab"));
                var cameraObject = new GameObject("Guide preview", typeof(Camera));
                var texture = new RenderTexture(1280, height, 24); texture.Create();
                var capture = new Texture2D(1280, height, TextureFormat.RGB24, false);
                var active = RenderTexture.active;
                try
                {
                    root.GetComponent<VisualizerHUD>().SetHUDVisible(false);
                    // Runtime NormalScreenControls hides itself through its app-backed visibility handler.
                    root.transform.Find("Safe Area/Normal Actions").gameObject.SetActive(false);
                    root.transform.Find("Safe Area/Gesture Guide").gameObject.SetActive(true);
                    var panel = (RectTransform)root.transform.Find("Safe Area/Gesture Guide/Guide Panel"); panel.gameObject.SetActive(true);
                    var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
                    Assert.AreEqual(12, catalogue.gestures.Length);
                    foreach (var binding in catalogue.gestures)
                    {
                        var row = panel.Find(binding.gesture);
                        Assert.IsNotNull(row, binding.gesture);
                        Assert.AreEqual(binding.guide, row.Find("Action").GetComponent<Text>().text);
                    }
                    var camera = cameraObject.GetComponent<Camera>(); camera.targetTexture = texture;
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .19f, .22f);
                    var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                    Canvas.ForceUpdateCanvases(); camera.Render();
                    var corners = new Vector3[4]; panel.GetWorldCorners(corners);
                    foreach (var corner in corners)
                    { var point = camera.WorldToViewportPoint(corner); Assert.That(point.x, Is.InRange(0, 1)); Assert.That(point.y, Is.InRange(0, 1)); }
                    RenderTexture.active = texture; capture.ReadPixels(new Rect(0, 0, 1280, height), 0, 0); capture.Apply();
                    Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/ARVisualizer-gesture-guide-" + height + ".png", capture.EncodeToPNG());
                }
                finally
                { RenderTexture.active = active; Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(capture); texture.Release(); Object.DestroyImmediate(texture); }
            }
        }
    }
}
