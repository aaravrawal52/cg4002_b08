using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    internal static class GoggleInteractionChecks
    {
        static CommandReply Send(ARVisualizerApp app, string command)
        {
            CommandReply reply = null;
            app.Execute(new VisualizerCommand { command = command }, value => reply = value);
            Assert.IsNotNull(reply);
            return reply;
        }

        static void Aim(ARVisualizerApp app, RectTransform target)
        {
            Canvas.ForceUpdateCanvases();
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(target.TransformPoint(target.rect.center)), null);
        }

        public static IEnumerator Exercise(ARVisualizerApp app, VisualizerHUD hud, ScreenSurface screen)
        {
            var canvas = hud.GetComponent<Canvas>();
            var scaler = hud.GetComponent<CanvasScaler>();
            var safe = hud.GetComponentInChildren<SafeAreaFitter>(true);
            var originalParent = hud.transform.parent;
            var originalMode = canvas.renderMode;
            var originalScale = hud.transform.localScale;
            var originalPanel = (RectTransform)hud.transform.Find("Safe Area/Placement Panel");
            var originalPanelSize = originalPanel.sizeDelta;
            var originalPanelPosition = originalPanel.anchoredPosition;
            var graphic = originalPanel.GetComponent<Graphic>();
            var originalMaterial = graphic.material;
            var normalCameraTarget = app.ARCamera.targetTexture;
            int normalCameraMask = app.ARCamera.cullingMask;
            var inputModule = EventSystem.current.currentInputModule;
            var pointerButton = hud.transform.Find("Safe Area/Controls/Pointer Button").GetComponent<Button>();
            var modeButton = hud.transform.Find("Safe Area/Controls/Mode Button").GetComponent<Button>();
            var placeButton = hud.transform.Find("Safe Area/Controls/Place Screen Button").GetComponent<Button>();
            var toggle = hud.GetComponentInChildren<HUDVisibilityToggle>().GetComponent<Button>();
            app.SendLocal("pointer.off");
            Assert.IsFalse(Send(app, "ui.click").ok, "Normal mode must not accept remote HUD clicks");
            Assert.IsTrue(Send(app, "goggle.enter").ok);
            Assert.IsTrue(Send(app, "goggle.enter").ok, "Repeated enter must preserve the normal-mode pointer state");
            Assert.IsTrue(app.GoggleModeEnabled && app.Hand.PointerEnabled && app.Hand.PointerLockedOn);
            Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode);
            var stereo = app.GoggleHUD.Stereo;
            Assert.IsTrue(stereo.IsActive);
            Assert.IsNotNull(stereo.LeftEye.targetTexture);
            Assert.AreNotSame(stereo.LeftEye.targetTexture, stereo.RightEye.targetTexture);
            Assert.AreEqual(0, app.ARCamera.cullingMask, "The source camera must capture video without duplicating virtual objects");
            Assert.That(Vector3.Distance(stereo.LeftEye.transform.position, stereo.RightEye.transform.position),
                Is.EqualTo(stereo.InterpupillaryDistance).Within(0.0001f));
            var nearPoint = stereo.EyeCentrePosition + app.ARCamera.transform.forward * 0.4f;
            var farPoint = stereo.EyeCentrePosition + app.ARCamera.transform.forward * 2;
            float nearDisparity = stereo.LeftEye.WorldToViewportPoint(nearPoint).x - stereo.RightEye.WorldToViewportPoint(nearPoint).x;
            float farDisparity = stereo.LeftEye.WorldToViewportPoint(farPoint).x - stereo.RightEye.WorldToViewportPoint(farPoint).x;
            Assert.Greater(nearDisparity, farDisparity * 4, "Near geometry must have greater binocular disparity");
            Assert.AreSame(originalParent, hud.transform.parent);
            Assert.That(Vector3.Distance(hud.transform.position, app.ARCamera.transform.position),
                Is.EqualTo(app.GoggleHUD.DistanceMetres).Within(0.001f));
            Assert.That(Quaternion.Angle(hud.transform.rotation, app.ARCamera.transform.rotation), Is.LessThan(0.001f));
            Assert.IsFalse(scaler.enabled || safe.enabled || inputModule.enabled);
            Assert.IsFalse(pointerButton.interactable);
            Assert.AreEqual("ARVisualizer/Floating HUD", graphic.material.shader.name);
            Assert.IsFalse(Send(app, "pointer.off").ok);
            app.Hand.SetPointerEnabled(false);
            Assert.IsTrue(app.Hand.PointerEnabled, "Pointer lock also covers direct script calls");

            // Existing placed screens are still pointable through the empty centre of the panel.
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(screen.WorldPoint(Vector2.one * 0.5f)), null);
            Send(app, "status");
            Assert.IsFalse(app.GoggleHUD.IsPointerOverHUD);
            Assert.AreSame(screen, app.RayScreen);

            Aim(app, (RectTransform)modeButton.transform);
            var status = Send(app, "status");
            Assert.AreEqual(modeButton.name, status.pointedHUDButton);
            Assert.IsTrue(app.GoggleHUD.IsPointerOverHUD);
            Assert.IsNull(app.RayScreen, "UI input takes priority over screen ray input");
            bool wasPlacing = app.PlaceModeEnabled;
            Assert.IsTrue(Send(app, "ui.click").ok);
            Assert.AreEqual(!wasPlacing, app.PlaceModeEnabled, "Command must invoke the authored button action");

            // Aim at a disabled control; it blocks ray input but cannot be clicked.
            Aim(app, (RectTransform)pointerButton.transform);
            status = Send(app, "status");
            Assert.IsTrue(app.GoggleHUD.IsPointerOverHUD);
            Assert.AreEqual("", status.pointedHUDButton);
            Assert.IsFalse(Send(app, "ui.click").ok);
            app.Hand.SetEditorSample(new Vector2(-1, -1), null);
            Assert.IsFalse(Send(app, "ui.click").ok, "Tracking loss must clear the old button");

            // The always-visible toggle must remain reachable, including after a real UDP click.
            Aim(app, (RectTransform)toggle.transform);
            Assert.AreEqual(toggle.name, Send(app, "status").pointedHUDButton);
            using (var client = new UdpClient())
            {
                client.Connect(IPAddress.Loopback, int.Parse(app.Communication.Endpoint.Split(':')[1]));
                var packet = Encoding.UTF8.GetBytes("{\"id\":\"goggle-hide-once\",\"command\":\"ui.click\"}");
                for (int attempt = 0; attempt < 2; ++attempt)
                {
                    client.Send(packet, packet.Length);
                    float deadline = Time.realtimeSinceStartup + 3;
                    while (client.Available == 0 && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.Greater(client.Available, 0, "No UDP click reply");
                    IPEndPoint endpoint = null;
                    var reply = JsonUtility.FromJson<CommandReply>(Encoding.UTF8.GetString(client.Receive(ref endpoint)));
                    Assert.IsTrue(reply.ok && reply.goggleModeEnabled);
                    Assert.IsFalse(hud.IsHUDVisible, "Retrying the same packet must not show the HUD again");
                }
            }
            Assert.IsTrue(toggle.isActiveAndEnabled);
            Aim(app, (RectTransform)toggle.transform);
            Assert.IsTrue(Send(app, "ui.click").ok);
            Assert.IsTrue(hud.IsHUDVisible);
            yield return StereoRenderingChecks.Exercise(app, hud);
            yield return CaptureFloatingHUD(app, hud);

            // Retain the world target while the hand moves to the floating Place Screen button.
            app.SendLocal("place.enter");
            bool found = false;
            for (int y = 2; y <= 8 && !found; ++y)
                for (int x = 2; x <= 8 && !found; ++x)
                {
                    app.Hand.SetEditorSample(new Vector2(x / 10f, y / 10f), null);
                    Send(app, "status");
                    found = app.HasTarget && !app.GoggleHUD.IsPointerOverHUD;
                }
            Assert.IsTrue(found, "Simulation must offer a surface outside the floating controls");
            int count = app.ScreenCount;
            Aim(app, (RectTransform)placeButton.transform);
            Send(app, "status");
            Assert.IsTrue(app.HasTarget, "Moving onto the Place button must keep the last valid surface");
            Assert.IsTrue(Send(app, "ui.click").ok);
            float until = Time.realtimeSinceStartup + 5;
            while (app.IsPlacing && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(count + 1, app.ScreenCount);
            // XR Simulation must report the newly added anchor before test cleanup removes it.
            yield return null;
            yield return null;
            app.SendLocal("screen.undo");

            Aim(app, (RectTransform)modeButton.transform);
            Send(app, "status");
            Assert.IsTrue(Send(app, "goggle.exit").ok);
            Assert.IsTrue(Send(app, "goggle.exit").ok);
            Assert.IsFalse(app.GoggleModeEnabled || app.Hand.PointerLockedOn || app.Hand.PointerEnabled);
            Assert.IsNull(app.GoggleHUD.HoveredButton);
            Assert.AreEqual(originalMode, canvas.renderMode);
            Assert.AreEqual(originalParent, hud.transform.parent);
            Assert.AreEqual(originalScale, hud.transform.localScale);
            Assert.AreEqual(originalPanelSize, originalPanel.sizeDelta);
            Assert.AreEqual(originalPanelPosition, originalPanel.anchoredPosition);
            Assert.AreEqual(originalMaterial, graphic.material);
            Assert.IsFalse(stereo.IsActive);
            Assert.AreEqual(normalCameraTarget, app.ARCamera.targetTexture);
            Assert.AreEqual(normalCameraMask, app.ARCamera.cullingMask);
            Assert.IsTrue(scaler.enabled && safe.enabled && inputModule.enabled && pointerButton.interactable);
            Assert.IsFalse(Send(app, "ui.click").ok);
            app.Hand.ClearEditorSample();
            app.SendLocal("pointer.off");
            app.SendLocal("place.enter");
        }

        static IEnumerator CaptureFloatingHUD(ARVisualizerApp app, VisualizerHUD hud)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var texture = new RenderTexture(1280, 720, 24);
            var stereo = app.GoggleHUD.Stereo;
            var camera = stereo.DisplayCamera;
            var outputCanvas = stereo.OutputCanvas;
            var previousTarget = camera.targetTexture;
            int previousMask = camera.cullingMask;
            var previousActive = RenderTexture.active;
            Texture2D screenshot = null;
            try
            {
                texture.Create();
                camera.targetTexture = texture;
                camera.cullingMask = ~0; // Screen-space-camera screenshot needs to include the output canvas.
                outputCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                outputCanvas.worldCamera = camera;
                outputCanvas.planeDistance = 0.5f;
                Canvas.ForceUpdateCanvases();
                yield return null;
                hud.RefreshStatus();
                app.ARCamera.Render();
                stereo.LeftEye.Render();
                stereo.RightEye.Render();
                camera.Render();
                RenderTexture.active = texture;
                screenshot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                screenshot.Apply();
                int coloured = 0;
                foreach (var pixel in screenshot.GetPixels32()) if (pixel.r > 30 || pixel.g > 30 || pixel.b > 30) ++coloured;
                Assert.Greater(coloured, 1280 * 720 / 10, "The lens compositor must display both eye images");
                System.IO.Directory.CreateDirectory("Logs");
                System.IO.File.WriteAllBytes("Logs/ARVisualizer-goggle-HUD.png", screenshot.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                camera.cullingMask = previousMask;
                outputCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                if (screenshot != null) Object.Destroy(screenshot);
                texture.Release(); Object.Destroy(texture);
            }
        }
    }
}
