using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer.Tests
{
    internal static class ScreenMoveChecks
    {
        static CommandReply Send(ARVisualizerApp app, string command)
        {
            CommandReply reply = null;
            app.Execute(new VisualizerCommand { command = command }, value => reply = value);
            Assert.IsNotNull(reply, command + " should finish synchronously");
            return reply;
        }

        static void AimScreen(ARVisualizerApp app, ScreenSurface screen)
        {
            app.ScreenApps.Close();
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(screen.WorldPoint(new Vector2(0.5f, 0.95f))), null);
            Send(app, "status");
            Assert.AreSame(screen, app.PointedScreen);
        }

        static void AimSurface(ARVisualizerApp app)
        {
            // Move far enough from the old pose to distinguish relocation from a no-op.
            for (int y = 1; y <= 9; ++y)
            {
                for (int x = 1; x <= 9; ++x)
                {
                    app.Hand.SetEditorSample(new Vector2(x / 10f, y / 10f), null);
                    Send(app, "status");
                    if (app.HasTarget) return;
                }
            }
            Assert.Fail("No destination surface found for moving");
        }

        static IEnumerator FinishAnchor(ARVisualizerApp app)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (app.IsPlacing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(app.IsPlacing, app.Feedback);
            Assert.IsFalse(app.MoveModeEnabled, app.Feedback);
        }

        static IEnumerator Frames()
        {
            int frame = Time.frameCount;
            float deadline = Time.realtimeSinceStartup + 3;
            while (Time.frameCount < frame + 2 && Time.realtimeSinceStartup < deadline) yield return null;
        }

        public static IEnumerator Exercise(ARVisualizerApp app, VisualizerHUD hud, ScreenSurface screen)
        {
            app.SendLocal("place.exit");
            app.SendLocal("pointer.on");
            var controls = hud.GetComponent<NormalScreenControls>();
            var row = hud.transform.Find("Safe Area/Normal Actions");
            var settings = row.Find("Screen Settings Button").GetComponent<Button>();
            var move = row.Find("Screen Settings Panel/Move Screen Button").GetComponent<Button>();
            var confirm = hud.transform.Find("Safe Area/Controls/Mode Button").GetComponent<Button>();
            Assert.AreEqual("MoveScreen", move.onClick.GetPersistentMethodName(0));
            Assert.IsFalse(Send(app, "move.confirm").ok);

            var media = screen.GetComponent<ScreenMedia>();
            var photo = new Texture2D(48, 64, TextureFormat.RGB24, false);
            Assert.IsTrue(media.ShowPhoto(photo));
            screen.Resize(2);
            int id = screen.Id;
            int steps = screen.SizeSteps;
            float width = screen.Width;
            float height = screen.Height;
            var face = screen.transform.Find("Body").GetComponent<Renderer>();

            // Select using the authored settings buttons, then move the ray away before Move.
            AimScreen(app, screen);
            settings.onClick.Invoke();
            Assert.IsTrue(controls.IsOpen);
            app.Hand.SetEditorSample(new Vector2(0.02f, 0.02f), null);
            move.onClick.Invoke();
            Assert.IsTrue(app.Hand.IsTracked, "Starting a move must preserve an already tracked hand ray");
            Assert.AreSame(screen, app.MovingScreen);
            Assert.IsFalse(controls.IsOpen || app.AdjustModeEnabled);
            Assert.AreEqual("MOVE HERE", confirm.GetComponentInChildren<Text>().text);
            Assert.AreEqual("CANCEL MOVE", settings.GetComponentInChildren<Text>().text);
            Assert.IsTrue(Send(app, "move.enter").ok, "Repeated enter preserves the selected screen");
            Assert.IsFalse(Send(app, "app.choose").ok);
            Assert.IsFalse(Send(app, "adjust.enter").ok);
            var status = Send(app, "status");
            Assert.IsTrue(status.moveModeEnabled && status.placeModeEnabled);
            Assert.AreEqual(id, status.movingScreenId);

            var oldPosition = screen.transform.position;
            var oldRotation = screen.transform.rotation;
            var oldAnchor = screen.GetComponentInParent<ARAnchor>();
            bool wasHorizontal = screen.CanRotate;
            AimSurface(app);
            yield return Frames();
            var outline = app.transform.Find("Screen footprint").GetComponent<LineRenderer>();
            Assert.IsTrue(outline.enabled);
            Assert.That(Vector3.Distance(outline.GetPosition(0), outline.GetPosition(1)), Is.EqualTo(width).Within(0.001f));
            Assert.That(Vector3.Distance(outline.GetPosition(1), outline.GetPosition(2)), Is.EqualTo(height).Within(0.001f));
            Assert.AreEqual(oldPosition, screen.transform.position, "The old screen stays put until confirmation");

            // Moving at the screen limit must not attempt to create another screen.
            var fields = new SerializedObject(app);
            int limit = fields.FindProperty("maximumScreens").intValue;
            fields.FindProperty("maximumScreens").intValue = app.ScreenCount;
            fields.ApplyModifiedPropertiesWithoutUndo();
            confirm.onClick.Invoke();
            yield return FinishAnchor(app);
            fields.Update(); fields.FindProperty("maximumScreens").intValue = limit; fields.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(1, app.ScreenCount);
            Assert.AreSame(screen, app.Screens[0]);
            Assert.AreEqual(id, screen.Id);
            Assert.AreEqual(steps, screen.SizeSteps);
            Assert.AreEqual(height, screen.Height);
            Assert.AreSame(photo, face.sharedMaterial.GetTexture("_MainTex"));
            Assert.AreNotSame(oldAnchor, screen.GetComponentInParent<ARAnchor>());
            Assert.Greater(Vector3.Distance(oldPosition, screen.transform.position), 0.01f);
            if (wasHorizontal && screen.CanRotate)
                Assert.That(Vector3.Angle(Vector3.ProjectOnPlane(oldRotation * Vector3.up, screen.FrontNormal), screen.transform.up), Is.LessThan(1));
            yield return Frames();
            Assert.IsTrue(oldAnchor == null, "The old anchor must be released after relocation");

            AimScreen(app, screen);
            Assert.IsTrue(Send(app, "move.enter").ok);
            oldPosition = screen.transform.position; oldRotation = screen.transform.rotation;
            oldAnchor = screen.GetComponentInParent<ARAnchor>();
            AimSurface(app);
            settings.onClick.Invoke();
            Assert.IsFalse(app.MoveModeEnabled || app.PlaceModeEnabled);
            Assert.AreEqual(oldPosition, screen.transform.position);
            Assert.AreEqual(oldRotation, screen.transform.rotation);
            Assert.AreSame(oldAnchor, screen.GetComponentInParent<ARAnchor>());
            Assert.IsFalse(Send(app, "move.confirm").ok);

            // Test stereo confirmation through the ray-clicked HUD while video keeps playing.
            string path = Path.Combine(app.ScreenApps.GetComponentInChildren<PhotoLibrary>().CacheDirectory, "move-video.mp4");
            File.Copy("Assets/ARVisualizer/Tests/Editor/MediaPortraitTest.mp4", path, true);
            Assert.IsTrue(media.ShowVideo(path));
            float deadline = Time.realtimeSinceStartup + 8;
            while (media.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(media.IsPlaying, media.Error);
            var player = screen.GetComponent<VideoPlayer>();
            var texture = player.targetTexture;
            app.SendLocal("goggle.enter");
            // Hide the HUD briefly while selecting a screen that might be behind it.
            hud.SetHUDVisible(false);
            AimScreen(app, screen);
            Assert.IsTrue(Send(app, "move.enter").ok);
            hud.SetHUDVisible(true);
            app.Hand.SetEditorSample(new Vector2(-1, -1), null);
            Assert.IsFalse(Send(app, "move.confirm").ok, "Hand loss must not confirm a stale destination");
            Assert.AreEqual(oldPosition, screen.transform.position);
            AimSurface(app);
            var stereoConfirm = hud.transform.Find("Safe Area/Controls/Place Screen Button").GetComponent<Button>();
            Assert.AreEqual("MOVE HERE", stereoConfirm.GetComponentInChildren<Text>().text);
            Canvas.ForceUpdateCanvases(); app.GoggleHUD.Stereo.LeftEye.Render();
            var rect = (RectTransform)stereoConfirm.transform;
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(rect.TransformPoint(rect.rect.center)), null);
            Assert.IsTrue(Send(app, "ui.click").ok);
            yield return FinishAnchor(app);
            Assert.AreSame(texture, player.targetTexture);
            Assert.IsTrue(media.IsPlaying && File.Exists(path));
            Assert.AreEqual(id, screen.Id);
            Assert.AreEqual(1, app.ScreenCount);

            hud.SetHUDVisible(false);
            AimScreen(app, screen);
            Assert.IsTrue(Send(app, "move.enter").ok);
            oldPosition = screen.transform.position;
            app.SendLocal("goggle.exit");
            Assert.IsFalse(app.MoveModeEnabled || app.PlaceModeEnabled, "Changing viewing modes cancels the move");
            Assert.AreEqual(oldPosition, screen.transform.position);
            hud.SetHUDVisible(true);
            media.Clear(); screen.Resize(-2);
            app.ScreenApps.Close(); app.Hand.ClearEditorSample();
        }
    }
}
