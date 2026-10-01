using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    internal static class ScreenAppChecks
    {
        static CommandReply Send(ARVisualizerApp app, string command)
        {
            CommandReply reply = null; app.Execute(new VisualizerCommand { command = command }, value => reply = value);
            Assert.IsNotNull(reply); return reply;
        }
        static void Aim(ARVisualizerApp app, Transform transform)
        {
            Canvas.ForceUpdateCanvases();
            // An unattended Editor has no Game view render loop. Newly shown canvases need a draw
            // before GraphicRaycaster can use their graphic depth, just as on the phone.
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                if (app.GoggleModeEnabled) app.GoggleHUD.Stereo.LeftEye.Render();
                else
                {
                    var previous = app.ARCamera.targetTexture;
                    var render = new RenderTexture(1280, 720, 24); render.Create();
                    try { app.ARCamera.targetTexture = render; app.ARCamera.Render(); }
                    finally { app.ARCamera.targetTexture = previous; render.Release(); Object.Destroy(render); }
                }
            }
            var rect = (RectTransform)transform;
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(rect.TransformPoint(rect.rect.center)), null);
            Send(app, "status");
        }
        static void Click(ARVisualizerApp app, string path)
        {
            var target = app.ScreenApps.transform.Find(path); Assert.IsNotNull(target, path);
            Aim(app, target);
            Assert.AreEqual(target.name, app.ScreenApps.TargetName);
            Assert.IsTrue(Send(app, "ui.click").ok, path);
        }
        static CommandReply AimScreen(ARVisualizerApp app, ScreenSurface screen)
        {
            // The top edge stays outside the screen-attached launcher, even when expanded.
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(screen.WorldPoint(new Vector2(0.5f, 0.95f))), null);
            return Send(app, "status");
        }
        static IEnumerator WaitForLibrary(ScreenAppMenu menu)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (menu.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(menu.IsBusy, menu.Status);
        }
        public static IEnumerator Exercise(ARVisualizerApp app, ScreenSurface screen)
        {
            Assert.IsNotNull(app.ScreenApps, "App menu prefab must be included");
            var menu = app.ScreenApps;
            var media = screen.GetComponent<ScreenMedia>(); Assert.IsNotNull(media);
            var originalPosition = screen.transform.localPosition;
            var originalRotation = screen.transform.localRotation;
            // View both the screen and its larger, world-anchored gallery in the test camera.
            var camera = app.ARCamera.transform;
            screen.transform.SetPositionAndRotation(camera.position + camera.forward - camera.up * 0.24f, camera.rotation);
            Assert.IsTrue(media.IsBlank);
            menu.Close();
            app.SendLocal("pointer.on");
            var reply = AimScreen(app, screen);
            Assert.AreEqual(screen, menu.Target);
            Assert.IsTrue(menu.IsChooserOpen && reply.appChooserOpen, "Pointing at a blank screen expands its apps without a click");
            Assert.AreEqual(screen.Id, reply.appMenuScreenId);
            menu.Close();
            Assert.IsTrue(Send(app, "goggle.enter").ok);
            Assert.IsTrue(AimScreen(app, screen).appChooserOpen, "Blank screens also open automatically in goggles");
            Assert.IsTrue(Send(app, "goggle.exit").ok);
            AimScreen(app, screen);
            Aim(app, menu.transform.Find("Screen launcher/App options/Photos & videos"));
            Assert.IsTrue(Send(app, "ui.rightclick").ok);
            Assert.IsFalse(AimScreen(app, screen).appChooserOpen, "Dismissal must not reopen the menu every frame");
            Assert.IsTrue(Send(app, "app.choose").ok);
            Assert.IsTrue(Send(app, "app.choose").appChooserOpen, "Choose is idempotent, not a toggle");
            Click(app, "Screen launcher/App options/Photos & videos");
            Assert.IsTrue(menu.IsModal);
            AssertWorldAttached(app, screen);
            Assert.IsFalse(Send(app, "app.choose").ok, "A modal library must not leak commands to the screen behind it");
            yield return WaitForLibrary(menu);
            Assert.IsTrue(menu.transform.Find("Photo library/Media viewport/Content/Media 1").gameObject.activeSelf);
            Assert.IsTrue(menu.transform.Find("Photo library/Media viewport/Content/Media 6").gameObject.activeSelf);
            Assert.IsNull(app.RayScreen, "The modal gallery consumes the ray");
            yield return ScreenMenuGestureChecks.Exercise(app, screen);
            Aim(app, menu.transform.Find("Photo library/Media viewport/Content/Media 1"));
            app.Hand.SetEditorSample(new Vector2(-1, -1), null);
            Send(app, "status");
            Assert.IsTrue(app.UsesCentrePointer);
            Assert.AreNotEqual("Media 1", menu.TargetName, "Hand loss must replace the old hover with the centre target");
            Assert.IsTrue(media.IsBlank);

            // Closing immediately after a selection must discard its async result.
            Click(app, "Photo library/Media viewport/Content/Media 1"); menu.Close();
            yield return null; yield return null;
            Assert.IsTrue(media.IsBlank);
            menu.OpenPhotos(); yield return WaitForLibrary(menu);
            yield return Capture(app, menu);
            Click(app, "Photo library/Media viewport/Content/Media 2"); yield return WaitForLibrary(menu);
            Assert.IsFalse(menu.IsModal || media.IsBlank);
            Assert.AreEqual("photos", media.AppId);
            var face = screen.transform.Find("Body");
            Assert.IsTrue(face.gameObject.activeSelf);
            var material = face.GetComponent<Renderer>().sharedMaterial;
            Assert.IsNotNull(material.GetTexture("_MainTex"));
            Assert.That(screen.Width / screen.Height, Is.EqualTo(1.5f).Within(0.001f));
            screen.Resize(1); yield return null;
            Assert.That(face.localScale.x, Is.EqualTo(screen.Width).Within(0.0001f));
            Assert.That(face.localPosition.z, Is.EqualTo(-ScreenSurface.SurfaceOffset));
            screen.Resize(-1);

            // A running app keeps its content until a replacement is selected.
            var photo = material.GetTexture("_MainTex");
            Assert.IsFalse(AimScreen(app, screen).appChooserOpen, "Populated screens retain their ordinary playback controls");
            var controls = menu.transform.Find("Screen launcher/Playback controls").gameObject;
            Assert.IsTrue(controls.activeInHierarchy);
            Aim(app, menu.transform.Find("Screen launcher/Playback controls/Choose media"));
            Assert.IsNull(app.RayScreen, "The playback control consumes the ray over its screen");
            Assert.IsTrue(Send(app, "app.choose").ok, "The currently pointed launcher also identifies its screen");
            Assert.IsTrue(menu.IsChooserOpen);
            Assert.IsFalse(controls.activeInHierarchy, "Playback controls must not overlap the app chooser");
            Assert.AreSame(photo, material.GetTexture("_MainTex"));
            Aim(app, menu.transform.Find("Screen launcher/Choose app"));
            Assert.IsTrue(Send(app, "ui.rightclick").ok);
            Assert.IsFalse(AimScreen(app, screen).appChooserOpen);
            Assert.IsTrue(controls.activeInHierarchy);
            Assert.IsTrue(Send(app, "adjust.enter").ok);
            Assert.IsFalse(Send(app, "app.choose").ok, "An adjusting screen cannot open its chooser");
            Assert.IsFalse(menu.IsChooserOpen);
            Assert.IsTrue(Send(app, "adjust.exit").ok);
            AimScreen(app, screen);
            app.Hand.SetEditorSample(new Vector2(0.01f, 0.99f), null);
            Assert.IsFalse(Send(app, "app.choose").ok, "The grace period must not let commands reuse a previous target");
            app.Hand.SetEditorSample(new Vector2(-1, -1), null);
            app.SendLocal("goggle.enter");
            Assert.IsFalse(Send(app, "app.choose").ok, "Tracking loss must invalidate the chooser target");
            app.SendLocal("goggle.exit");
            app.SendLocal("pointer.off");
            Assert.IsFalse(Send(app, "app.choose").ok);
            app.SendLocal("pointer.on"); AimScreen(app, screen);
            Assert.IsTrue(Send(app, "app.choose").ok);
            Click(app, "Screen launcher/App options/Photos & videos");
            Assert.IsTrue(menu.IsModal, "Launching an app must work on a populated screen too");
            yield return WaitForLibrary(menu);
            Assert.IsTrue(Send(app, "ui.rightclick").ok);
            Assert.AreSame(photo, material.GetTexture("_MainTex"), "Cancelling the picker preserves the previous photo");

            // Permission denial and empty/limited access are visible and retryable.
            var library = menu.GetComponentInChildren<PhotoLibrary>();
            library.EditorResultOverride = op => op == "authorize" ? new LibraryResult { error = "Permission denied (test)" } : null;
            menu.OpenPhotos(); yield return WaitForLibrary(menu);
            Assert.AreEqual("Permission denied (test)", menu.Status);
            Assert.IsFalse(menu.transform.Find("Photo library/Media viewport/Content/Media 1").gameObject.activeSelf);
            library.EditorResultOverride = op => op == "authorize" ? new LibraryResult { ok = true, limited = true }
                : op == "page" ? new LibraryResult { ok = true, total = 0, assets = new LibraryAsset[0] } : null;
            Click(app, "Photo library/Refresh library"); yield return WaitForLibrary(menu);
            StringAssert.Contains("No accessible", menu.Status);
            library.EditorResultOverride = null;
            Click(app, "Photo library/Refresh library"); yield return WaitForLibrary(menu);
            Assert.IsTrue(Send(app, "goggle.enter").ok);
            AssertWorldAttached(app, screen);
            Aim(app, menu.transform.Find("Photo library/Close library"));
            Assert.IsNull(app.GoggleHUD.HoveredButton, "Gallery clicks must not also hit the HUD behind it");
            Assert.IsTrue(Send(app, "ui.click").ok);
            Assert.IsFalse(menu.IsModal);
            AimScreen(app, screen);
            Assert.IsTrue(Send(app, "app.choose").ok, "The same command opens the chooser in goggles");
            Click(app, "Screen launcher/App options/Photos & videos");
            Assert.IsTrue(menu.IsModal);
            yield return WaitForLibrary(menu);
            Assert.IsTrue(Send(app, "ui.rightclick").ok);
            Assert.AreSame(photo, material.GetTexture("_MainTex"));
            app.SendLocal("goggle.exit");

            // A real H.264 file exercises prepare, playback, pause, loop configuration and owned-file cleanup.
            string videoPath = Path.Combine(library.CacheDirectory, "test-video.mp4");
            library.EditorResultOverride = op =>
            {
                if (op == "page") return new LibraryResult { ok = true, total = 1,
                    assets = new[] { new LibraryAsset { id = "video", kind = "video", caption = "Test video" } } };
                if (op != "export") return null;
                File.Copy("Assets/ARVisualizer/Tests/Editor/MediaTest.mp4", videoPath, true);
                return new LibraryResult { ok = true, path = videoPath };
            };
            menu.OpenPhotos(); yield return WaitForLibrary(menu);
            Click(app, "Photo library/Media viewport/Content/Media 1"); yield return WaitForLibrary(menu);
            Assert.IsTrue(media.IsVideo && media.IsPlaying, media.Error);
            Assert.That(screen.Width / screen.Height, Is.EqualTo(16f / 9).Within(0.001f));
            var player = screen.GetComponent<UnityEngine.Video.VideoPlayer>();
            float videoDeadline = Time.realtimeSinceStartup + 5;
            while (player.frame <= 0 && Time.realtimeSinceStartup < videoDeadline) yield return null;
            Assert.Greater(player.frame, 0, "The decoder must advance beyond the first frame");
            media.TogglePlayback(); Assert.IsFalse(media.IsPlaying);
            media.TogglePlayback(); Assert.IsTrue(media.IsPlaying);
            library.EditorResultOverride = null;

            // Disabling/removing a screen closes its picker and invalidates pending work.
            AimScreen(app, screen);
            Assert.IsTrue(Send(app, "app.choose").ok);
            Assert.IsTrue(media.IsPlaying, "Opening the app chooser must not interrupt video playback");
            Click(app, "Screen launcher/App options/Photos & videos");
            Assert.IsTrue(menu.IsModal && media.IsPlaying);
            screen.gameObject.SetActive(false); menu.RefreshPointer();
            Assert.IsFalse(menu.IsModal);
            screen.gameObject.SetActive(true);
            media.Clear(); Assert.IsTrue(media.IsBlank); Assert.IsTrue(face.gameObject.activeSelf);
            Assert.AreSame(Texture2D.blackTexture, material.GetTexture("_MainTex"), "The same quad returns to black");
            Assert.IsTrue(AimScreen(app, screen).appChooserOpen, "A cleared screen returns to automatic app selection");
            Assert.IsFalse(File.Exists(videoPath), "Closing the app must release its temporary movie");
            yield return MediaShapeChecks.Exercise(app, screen);
            menu.Close(); app.Hand.ClearEditorSample(); app.SendLocal("pointer.off");
            screen.transform.localPosition = originalPosition; screen.transform.localRotation = originalRotation;
        }
        static void AssertWorldAttached(ARVisualizerApp app, ScreenSurface screen)
        {
            var menu = app.ScreenApps;
            var rect = (RectTransform)menu.transform.Find("Photo library");
            menu.RefreshPointer();
            var bottom = rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin));
            var offset = bottom - screen.WorldPoint(new Vector2(0.5f, 1));
            Assert.That(Vector3.Dot(offset, screen.transform.up), Is.EqualTo(0.015f).Within(0.0001f));
            Assert.That(Vector3.Dot(offset, screen.FrontNormal), Is.EqualTo(0.03f).Within(0.0001f));
            Assert.Less(Quaternion.Angle(rect.rotation, screen.transform.rotation), 0.01f);
            foreach (var graphic in rect.GetComponentsInChildren<Graphic>(true))
                Assert.AreEqual("ARVisualizer/Occluded Screen UI", graphic.material.shader.name);
            var camera = app.ARCamera.transform;
            var cameraPosition = camera.position;
            Vector3 Bottom() => rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin));
            Vector3 Viewer() => app.GoggleModeEnabled ? app.GoggleHUD.Stereo.EyeCentrePosition : camera.position;
            float Angle() => Vector3.Angle(rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.center.y)) - Viewer(),
                rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.center.y)) - Viewer());
            var menuBottom = Bottom();
            float width = rect.rect.width * rect.lossyScale.x;
            try
            {
                Assert.That(Angle(), Is.EqualTo(38).Within(0.03f));
                camera.position += camera.right * 0.05f - camera.forward * 0.7f;
                menu.RefreshPointer();
                Assert.Less(Vector3.Distance(menuBottom, Bottom()), 0.0001f, "The library's bottom edge stays attached while it scales");
                Assert.Greater(rect.rect.width * rect.lossyScale.x, width * 1.4f);
                Assert.That(Angle(), Is.EqualTo(38).Within(0.03f), "Apparent width stays stable in normal and goggle mode");
            }
            finally { camera.position = cameraPosition; menu.RefreshPointer(); }
            var screenPosition = screen.transform.position;
            try
            {
                screen.transform.position += screen.transform.right * 0.04f;
                menu.RefreshPointer();
                Assert.That(Vector3.Distance(menuBottom, Bottom()), Is.EqualTo(0.04f).Within(0.0001f), "The library follows its screen's anchor");
                screen.Resize(1); menu.RefreshPointer();
                bottom = rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin));
                Assert.That(Vector3.Dot(bottom - screen.WorldPoint(new Vector2(0.5f, 1)), screen.transform.up), Is.EqualTo(0.015f).Within(0.0001f));
                screen.Resize(-1);
            }
            finally { screen.transform.position = screenPosition; menu.RefreshPointer(); }
        }
        static IEnumerator Capture(ARVisualizerApp app, ScreenAppMenu menu)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var camera = app.ARCamera; var previous = camera.targetTexture; var active = RenderTexture.active;
            var texture = new RenderTexture(1280, 720, 24); texture.Create();
            var screenshot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture; Canvas.ForceUpdateCanvases(); yield return null;
                camera.Render(); RenderTexture.active = texture;
                screenshot.ReadPixels(new Rect(0,0,1280,720), 0, 0); screenshot.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/ARVisualizer-photo-library.png", screenshot.EncodeToPNG());
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; Object.Destroy(screenshot); texture.Release(); Object.Destroy(texture); }
        }
    }
}
