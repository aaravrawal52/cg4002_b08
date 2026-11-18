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
            app.RightHand.SetEditorSample(new Vector2(0.01f, 0.99f), null);
            Assert.IsTrue(Send(app, "swipe_up").ok);
            Assert.AreEqual(originalSize + 1, screen.SizeSteps, "The selected screen stays locked when the ray leaves");
            Aim(app, screen.WorldPoint(edge), TrackedHand.Left);
            Assert.IsFalse(Send(app, "pinch_and_drag", TrackedHand.Left).ok, "The other hand cannot take an edit");
            Assert.IsFalse(Send(app, "crumple_and_toss", TrackedHand.Left).ok);
            Assert.IsTrue(Send(app, "pinch_and_rotate").ok);
            Assert.IsTrue(Send(app, "swipt_left").ok);
            Assert.That(Quaternion.Angle(originalRotation, screen.transform.localRotation), Is.EqualTo(5).Within(0.01));
            Assert.IsTrue(Send(app, "crumple_and_toss").ok);
            Assert.AreEqual(originalSize, screen.SizeSteps);
            Assert.That(Quaternion.Angle(originalRotation, screen.transform.localRotation), Is.LessThan(0.01));
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null); app.MenuFor(TrackedHand.Left).Close();

            Aim(app, screen.WorldPoint(edge));
            Send(app, "pinch_and_pull"); Send(app, "swipe_up"); Send(app, "grab_and_release");
            Assert.IsFalse(app.AdjustModeEnabled); Assert.AreEqual(originalSize + 1, screen.SizeSteps);
            Aim(app, screen.WorldPoint(edge));
            Send(app, "pinch_and_drag"); Assert.IsTrue(app.MoveModeEnabled);
            var position = screen.transform.position;
            Send(app, "crumple_and_toss"); Assert.IsFalse(app.MoveModeEnabled);
            Assert.AreEqual(position, screen.transform.position);
            screen.Initialize(screen.Id, false);
            Aim(app, screen.WorldPoint(edge)); Assert.IsFalse(Send(app, "pinch_and_rotate").ok);
            Assert.IsFalse(app.AdjustModeEnabled); screen.Initialize(screen.Id, true);

            Aim(app, Centre(content.View.timeDigits[1]));
            Assert.IsTrue(Send(app, "swipt_right").ok); // Move from minutes to tens of seconds.
            Assert.AreEqual(2, content.SelectedDigit(TrackedHand.Right));
            Aim(app, screen.WorldPoint(edge)); Send(app, "swipe_up");
            Assert.AreEqual(70, content.Timer.PresetSeconds);
            Assert.IsTrue(Send(app, "grab_and_release").ok); Assert.IsTrue(content.Timer.IsRunning);
            Send(app, "grab_and_release"); Assert.IsFalse(content.Timer.IsRunning);
            Aim(app, Centre(content.View.timeDigits[0]), TrackedHand.Left);
            Send(app, "swipt_left", TrackedHand.Left);
            Assert.AreEqual(3, content.SelectedDigit(TrackedHand.Left));
            Assert.AreEqual(2, content.SelectedDigit(TrackedHand.Right));
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null); app.MenuFor(TrackedHand.Left).Close();

            Aim(app, screen.WorldPoint(edge));
            Assert.IsTrue(Send(app, "point_and_rotate").ok); Assert.IsTrue(app.ScreenApps.IsSettingsOpen);
            Send(app, "swipe_right"); Send(app, "swipe_right");
            Assert.AreEqual("Move", app.ScreenApps.NavigationFocus.name);
            Send(app, "grab_and_release"); Assert.IsTrue(app.MoveModeEnabled);
            Send(app, "crumple_and_toss");
            Aim(app, screen.WorldPoint(edge)); Send(app, "point_and_rotate");
            Send(app, "point_and_rotate"); Assert.IsFalse(app.ScreenApps.IsSettingsOpen);
            Aim(app, screen.WorldPoint(edge)); Send(app, "point_and_rotate");
            Send(app, "swipe_right"); Send(app, "grab_and_release");
            Assert.IsTrue(app.ScreenApps.IsChooserOpen);
            Send(app, "swipe_right"); Send(app, "swipe_right"); Send(app, "swipe_right");
            Send(app, "grab_and_release"); Assert.AreEqual("flashcards", content.AppId);
            Aim(app, screen.WorldPoint(edge)); Send(app, "grab_and_release"); Assert.IsTrue(content.AnswerVisible);
            Send(app, "swipt_right"); Assert.AreEqual(1, content.CardIndex); Assert.IsFalse(content.AnswerVisible);

            var media = screen.GetComponent<ScreenMedia>(); media.ShowPhoto(new Texture2D(16, 9));
            var library = app.ScreenApps.GetComponentInChildren<PhotoLibrary>(); library.EditorSampleCount = 30;
            Aim(app, screen.WorldPoint(edge)); Send(app, "grab_and_release"); Assert.IsTrue(app.ScreenApps.IsModal);
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
            Assert.IsTrue(Send(app, "grab_and_release").ok);
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
            app.RightHand.SetEditorSample(new Vector2(0.01f, 0.01f), null); app.SendLocal("right.status");
            yield return new WaitForSecondsRealtime(1.4f); Assert.IsFalse(app.GestureGuide.IsOpen);
            hud.SetHUDVisible(true); Assert.IsFalse(strip.gameObject.activeInHierarchy);

            hud.SetHUDVisible(false); app.SendLocal("goggle.enter"); yield return null; Canvas.ForceUpdateCanvases();
            Aim(app, Centre(strip), TrackedHand.Left);
            using (app.UseHand(TrackedHand.Left))
            { Assert.IsTrue(app.GestureGuide.IsOpen); Assert.IsTrue(app.GuideBlocksPointer); }
            Assert.IsTrue(app.GoggleModeEnabled); Assert.IsTrue(guidePanel.gameObject.activeInHierarchy);
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
