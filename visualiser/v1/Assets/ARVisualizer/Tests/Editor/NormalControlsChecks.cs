using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    internal static class NormalControlsChecks
    {
        public static IEnumerator Exercise(ARVisualizerApp app, VisualizerHUD hud, ScreenSurface screen)
        {
            var controls = hud.GetComponent<NormalScreenControls>();
            Assert.IsNotNull(controls);
            var row = hud.transform.Find("Safe Area/Normal Actions");
            Assert.IsNotNull(row);
            void Click(string path) => row.Find(path).GetComponent<Button>().onClick.Invoke();
            var originalPosition = screen.transform.localPosition;
            var originalRotation = screen.transform.localRotation;
            int left = 0, right = 0;
            Vector2 clickedUV = default;
            void Clicked(ScreenSurface target, PointerEventData.InputButton button, Vector2 uv)
            { if (button == PointerEventData.InputButton.Left) ++left; else ++right; clickedUV = uv; }
            screen.PointerClicked += Clicked;
            try
            {
                // Put an uncovered portion of the face under the camera's centre ray.
                var camera = app.ARCamera.transform;
                screen.transform.SetPositionAndRotation(camera.position + camera.forward * 0.75f - camera.up * screen.Height * 0.45f, camera.rotation);
                app.ScreenApps.Close(); app.SendLocal("pointer.on");
                app.Hand.SetEditorSample(new Vector2(-1, -1), null);
                app.SendLocal("status");
                Assert.IsTrue(app.PointerReady && app.UsesCentrePointer);
                Assert.AreEqual(Vector2.one * 0.5f, app.PointerViewportPoint);
                Assert.AreSame(screen, app.RayScreen);
                Click("Left Click Button"); Click("Right Click Button");
                Assert.AreEqual(1, left); Assert.AreEqual(1, right);
                Assert.That(Vector2.Distance(new Vector2(0.5f, 0.95f), clickedUV), Is.LessThan(0.002f));
                Canvas.ForceUpdateCanvases();
                var leftRect = (RectTransform)row.Find("Left Click Button");
                Assert.IsTrue(app.PhoneClicks.IsOverGUI(RectTransformUtility.WorldToScreenPoint(null, leftRect.TransformPoint(leftRect.rect.center))));

                Click("Screen Settings Button");
                Assert.IsTrue(controls.IsOpen && app.AdjustModeEnabled);
                Assert.AreSame(screen, app.AdjustedScreen);
                int steps = screen.SizeSteps;
                Click("Screen Settings Panel/Larger Button"); Assert.AreEqual(steps + 1, screen.SizeSteps);
                Click("Screen Settings Panel/Smaller Button"); Assert.AreEqual(steps, screen.SizeSteps);
                var rotation = screen.transform.rotation;
                if (screen.CanRotate)
                {
                    Click("Screen Settings Panel/Rotate Right Button");
                    Assert.That(Quaternion.Angle(rotation, screen.transform.rotation), Is.EqualTo(5).Within(0.01f));
                    Click("Screen Settings Panel/Rotate Left Button");
                }
                yield return Capture(app, hud);
                Click("Screen Settings Panel/Choose App Button");
                Assert.IsFalse(controls.IsOpen || app.AdjustModeEnabled);
                Assert.IsTrue(app.ScreenApps.IsChooserOpen);
                Assert.AreSame(screen, app.ScreenApps.Target);

                // Put the app option at the centre too: no hand sample is needed to launch it.
                Canvas.ForceUpdateCanvases();
                var option = (RectTransform)app.ScreenApps.transform.Find("Screen launcher/App options/Photos & videos");
                var optionCentre = option.TransformPoint(option.rect.center);
                float depth = app.ARCamera.WorldToViewportPoint(optionCentre).z;
                screen.transform.position += app.ARCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth)) - optionCentre;
                app.SendLocal("status");
                yield return Capture(app, hud, "ARVisualizer-centre-chooser.png");
                app.SendLocal("status");
                Assert.AreEqual("Photos & videos", app.ScreenApps.TargetName);
                Click("Left Click Button");
                Assert.IsTrue(app.ScreenApps.IsModal, "The centre ray and phone click button can open a screen app");
                app.ScreenApps.Close();

                app.SendLocal("goggle.enter");
                Assert.IsFalse(row.gameObject.activeSelf, "Phone controls never appear inside the goggle HUD");
                Assert.IsFalse(app.PointerReady, "Goggles still require a tracked hand");
                app.SendLocal("goggle.exit");
                Assert.IsTrue(row.gameObject.activeSelf && app.UsesCentrePointer);
                hud.SetHUDVisible(false); Assert.IsFalse(row.gameObject.activeSelf);
                hud.SetHUDVisible(true); Assert.IsTrue(row.gameObject.activeSelf);
            }
            finally
            {
                controls.Close(); app.ScreenApps.Close();
                screen.PointerClicked -= Clicked;
                screen.transform.localPosition = originalPosition; screen.transform.localRotation = originalRotation;
                app.Hand.ClearEditorSample(); app.SendLocal("pointer.off");
            }
        }

        public static IEnumerator Capture(ARVisualizerApp app, VisualizerHUD hud, string filename = "ARVisualizer-normal-settings.png")
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var camera = app.ARCamera; var canvas = hud.GetComponent<Canvas>();
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldDistance = canvas.planeDistance;
            var target = new RenderTexture(1280, 720, 24); target.Create();
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 0.2f;
                Canvas.ForceUpdateCanvases(); yield return null;
                camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes(Path.Combine("Logs", filename), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
                target.Release(); Object.Destroy(target); Object.Destroy(image);
            }
        }

        public static IEnumerator DeleteSelectedScreen(ARVisualizerApp app, VisualizerHUD hud, ScreenSurface first)
        {
            app.SendLocal("pointer.on"); app.SendLocal("place.enter");
            for (int y = 1; y <= 9 && !app.HasTarget; ++y)
                for (int x = 1; x <= 9 && !app.HasTarget; ++x)
                { app.Hand.SetEditorSample(new Vector2(x / 10f, y / 10f), null); app.SendLocal("status"); }
            Assert.IsTrue(app.HasTarget);
            app.SendLocal("screen.place");
            float deadline = Time.realtimeSinceStartup + 5;
            while (app.IsPlacing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(2, app.ScreenCount);
            var newest = app.Screens[1];
            yield return null; yield return null;
            app.ScreenApps.Close();
            var camera = app.ARCamera.transform;
            first.transform.SetPositionAndRotation(camera.position + camera.forward * 0.65f, camera.rotation);
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(first.WorldPoint(new Vector2(0.5f, 0.95f))), null);
            app.SendLocal("status");
            var row = hud.transform.Find("Safe Area/Normal Actions");
            row.Find("Screen Settings Button").GetComponent<Button>().onClick.Invoke();
            Assert.AreSame(first, app.AdjustedScreen);
            // Moving the ray cannot redirect Delete to a different (or newer) screen.
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(newest.WorldPoint(Vector2.one * 0.5f)), null);
            app.SendLocal("status");
            row.Find("Screen Settings Panel/Delete Screen Button").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(1, app.ScreenCount);
            Assert.AreSame(newest, app.Screens[0]);
            Assert.IsFalse(first.gameObject.activeSelf);
            Assert.IsFalse(app.AdjustModeEnabled || hud.GetComponent<NormalScreenControls>().IsOpen);
            Assert.IsFalse(app.ScreenApps.Target == first && (app.ScreenApps.IsModal || app.ScreenApps.IsChooserOpen));
            app.SendLocal("screen.undo"); // The existing Wi-Fi/goggle undo command remains supported.
            Assert.AreEqual(0, app.ScreenCount);
            app.Hand.ClearEditorSample();
        }
    }
}
