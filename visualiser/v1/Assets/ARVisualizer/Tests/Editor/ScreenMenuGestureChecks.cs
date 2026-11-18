using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ARVisualizer.Tests
{
    internal static class ScreenMenuGestureChecks
    {
        const string Tiles = "Photo library/Media viewport/Content/";
        static Vector3 Centre(Transform target) { var rect = (RectTransform)target; return rect.TransformPoint(rect.rect.center); }
        static CommandReply Send(ARVisualizerApp app, string command)
        {
            CommandReply reply = null; app.Execute(new VisualizerCommand { command = command }, value => reply = value); return reply;
        }
        static void Aim(ARVisualizerApp app, Vector3 point, Vector3? fingertip = null)
        { app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(point), fingertip); Send(app, "status"); }
        static void Draw(ARVisualizerApp app)
        {
            Canvas.ForceUpdateCanvases();
            if (app.GoggleModeEnabled) { app.GoggleHUD.Stereo.LeftEye.Render(); return; }
            var previous = app.ARCamera.targetTexture;
            var target = new RenderTexture(1280, 720, 24); target.Create();
            try { app.ARCamera.targetTexture = target; app.ARCamera.Render(); }
            finally { app.ARCamera.targetTexture = previous; target.Release(); Object.Destroy(target); }
        }
        static IEnumerator Ready(ScreenAppMenu menu)
        {
            float until = Time.realtimeSinceStartup + 8;
            while (menu.IsBusy && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(menu.IsBusy, menu.Status);
        }
        static IEnumerator ScrollTo(MediaLibraryGrid grid, float value)
        {
            grid.Scroll.verticalNormalizedPosition = value;
            grid.Scroll.onValueChanged.Invoke(grid.Scroll.normalizedPosition);
            yield return null;
        }
        static IEnumerator PhoneSwipes(ARVisualizerApp app, ScreenMedia media)
        {
            var settings = InputSystem.settings;
            var focus = settings.editorInputBehaviorInPlayMode; var background = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var device = InputSystem.AddDevice<Touchscreen>();
            var menu = app.ScreenApps; var grid = menu.LibraryGrid;
            try
            {
                yield return ScrollTo(grid, 1); Draw(app);
                Aim(app, Centre(menu.transform.Find(Tiles + "Media 4")));
                var emptyLeft = new Vector2(Screen.width * 0.04f, Screen.height * 0.5f);
                Assert.IsFalse(app.PhoneClicks.IsOverGUI(emptyLeft));
                yield return PhoneClickChecks.Touch(device, 31, emptyLeft, TouchPhase.Began);
                Assert.IsTrue(menu.MenuInput.IsRayPressed, "An empty left-side phone press holds the gallery ray");
                var end = emptyLeft + Vector2.up * Screen.height * 0.2f;
                yield return PhoneClickChecks.Touch(device, 31, end, TouchPhase.Moved);
                yield return PhoneClickChecks.Touch(device, 31, end, TouchPhase.Ended);
                Assert.IsFalse(menu.MenuInput.IsPressed);
                Assert.Greater(grid.Scroll.content.anchoredPosition.y, 100);
                Assert.IsTrue(menu.IsModal && media.IsBlank);

                // Native phone touches on the gallery itself still use Unity's ScrollRect.
                yield return ScrollTo(grid, 1); Draw(app);
                Vector2 start = app.ARCamera.WorldToScreenPoint(Centre(menu.transform.Find(Tiles + "Media 4")));
                Assert.IsTrue(app.PhoneClicks.IsOverGUI(start));
                end = start + Vector2.up * Screen.height * 0.12f;
                yield return PhoneClickChecks.Touch(device, 32, start, TouchPhase.Began);
                // ScrollRect starts its local drag origin after crossing the phone's drag threshold.
                yield return PhoneClickChecks.Touch(device, 32, start + Vector2.up * 12, TouchPhase.Moved);
                yield return PhoneClickChecks.Touch(device, 32, end, TouchPhase.Moved);
                yield return PhoneClickChecks.Touch(device, 32, end, TouchPhase.Ended);
                grid.Scroll.StopMovement();
                Assert.Greater(grid.Scroll.content.anchoredPosition.y, 50);
                Assert.IsTrue(menu.IsModal && media.IsBlank, "A native gallery swipe cannot also select a photo");
            }
            finally
            {
                if (device.added) InputSystem.RemoveDevice(device);
                settings.editorInputBehaviorInPlayMode = focus; settings.backgroundBehavior = background;
            }
        }
        public static IEnumerator Exercise(ARVisualizerApp app, ScreenSurface screen)
        {
            var menu = app.ScreenApps; var grid = menu.LibraryGrid; var media = screen.GetComponent<ScreenMedia>();
            Assert.IsNotNull(grid.Scroll.viewport.GetComponent<RectMask2D>());
            Assert.IsNull(menu.transform.Find("Photo library/Next page"));
            Assert.IsNull(menu.transform.Find("Photo library/Previous page"));
            Assert.AreEqual(8, grid.Total);
            var viewport = grid.Scroll.viewport;
            var first = menu.transform.Find(Tiles + "Media 4");
            foreach (bool goggles in new[] { false, true })
            {
                if (goggles) app.SendLocal("goggle.enter");
                yield return ScrollTo(grid, 1); Draw(app);
                Vector3 start = Centre(first);
                Aim(app, start);
                Assert.IsTrue(Send(app, "ui.press").ok);
                Assert.IsTrue(Send(app, "ui.press").ok, "Repeated press does not reset a drag");
                Aim(app, start + viewport.TransformVector(Vector3.up * 160));
                Assert.Greater(grid.Scroll.content.anchoredPosition.y, 140);
                Assert.IsTrue(Send(app, "ui.release").ok);
                Assert.IsTrue(menu.IsModal && media.IsBlank, "Scrolling must not select the tile under the press or release");
                Draw(app);
                start = viewport.TransformPoint(new Vector3(0, 80)); Aim(app, start);
                Assert.IsTrue(Send(app, "ui.press").ok);
                Aim(app, start - viewport.TransformVector(Vector3.up * 150));
                Assert.Less(grid.Scroll.content.anchoredPosition.y, 30);
                Send(app, "ui.release");
                if (goggles)
                {
                    Aim(app, Centre(first)); Send(app, "ui.press");
                    app.Hand.SetEditorSample(new Vector2(-1, -1), null);
                    menu.MenuInput.EndRayPress(out _);
                    Assert.IsFalse(menu.MenuInput.IsPressed, "Tracking loss cancels a held press");
                    Send(app, "ui.release"); Assert.IsTrue(menu.IsModal && media.IsBlank);
                    app.SendLocal("goggle.exit");
                }
            }
            yield return ScrollTo(grid, 1); Draw(app);
            var hold = app.GetComponentInChildren<VisualizerHUD>().GetComponentInChildren<PointerHoldButton>();
            var eventData = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            Aim(app, Centre(first)); hold.OnPointerDown(eventData);
            Assert.IsTrue(menu.MenuInput.IsRayPressed);
            Aim(app, Centre(first) + viewport.TransformVector(Vector3.up * 155));
            hold.OnPointerUp(eventData); hold.Click();
            Assert.IsTrue(menu.IsModal && media.IsBlank, "HUD release must not add a second click after dragging");

            // A phone swipe can scroll the centre ray even when the hand is absent.
            yield return ScrollTo(grid, 1); Draw(app); Aim(app, Centre(first));
            Assert.IsTrue(menu.MenuInput.BeginRayPress(out _));
            menu.MenuInput.DragFromPhone(Vector2.up * Screen.height * 0.2f);
            menu.MenuInput.EndRayPress(out _);
            Assert.Greater(grid.Scroll.content.anchoredPosition.y, 100);
            Assert.IsTrue(media.IsBlank);

            yield return PhoneSwipes(app, media);

            // Thumbnails outside the clipped viewport cannot be clicked.
            yield return ScrollTo(grid, 0); Draw(app);
            Aim(app, Centre(menu.transform.Find(Tiles + "Media 1")));
            Assert.AreNotEqual("Media 1", menu.TargetName);
            yield return ScrollTo(grid, 1); Draw(app);

            // Direct fingertip drag and lift: no ray click command, no accidental selection.
            app.SendLocal("pointer.off");
            var startTip = Centre(first); var normal = -viewport.forward;
            Aim(app, startTip, startTip + normal * 0.006f);
            Assert.IsTrue(menu.MenuInput.IsPressed);
            var endTip = startTip + viewport.TransformVector(Vector3.up * 150);
            Aim(app, endTip, endTip + normal * 0.006f);
            Aim(app, endTip, endTip + normal * 0.05f);
            Assert.Greater(grid.Scroll.content.anchoredPosition.y, 120);
            Assert.IsTrue(menu.IsModal && media.IsBlank);
            yield return ScrollTo(grid, 1); Draw(app);
            var selected = Centre(menu.transform.Find(Tiles + "Media 2"));
            Aim(app, selected, selected + normal * 0.006f);
            Assert.IsNull(app.InputScreen, "Menu contact must not reach the screen behind it");
            Aim(app, selected, selected + normal * 0.05f);
            yield return Ready(menu);
            Assert.IsFalse(media.IsBlank || menu.IsModal, "A fingertip tap selects media with the ray off");
            media.Clear();

            menu.ShowPinnedChooser(screen); Draw(app);
            var launcher = (RectTransform)menu.transform.Find("Screen launcher");
            Assert.LessOrEqual(launcher.rect.width * launcher.lossyScale.x, screen.Width);
            Assert.That(Vector3.Dot(launcher.position - screen.WorldPoint(new Vector2(0.5f, 0)), screen.transform.up), Is.EqualTo(0.003f).Within(0.0001f));
            var choose = Centre(launcher.Find("Choose app")); normal = -launcher.forward;
            Aim(app, choose, choose + normal * 0.006f);
            Assert.IsFalse(menu.IsChooserOpen, "Touching Choose app collapses it once");
            for (int i = 0; i < 4; ++i) Send(app, "status");
            Assert.IsFalse(menu.IsChooserOpen, "Held fingertip contact never repeatedly toggles a button");
            Aim(app, choose, choose + normal * 0.05f);
            Aim(app, choose, choose + normal * 0.006f);
            Assert.IsTrue(menu.IsChooserOpen);
            Aim(app, choose, choose + normal * 0.05f); Draw(app);
            var option = Centre(launcher.Find("App options/Photos & videos"));
            Aim(app, option, option + normal * 0.006f);
            Assert.IsTrue(menu.IsModal, "A fingertip opens the photo app without a ray click");
            yield return Ready(menu); Draw(app);
            var close = Centre(menu.transform.Find("Photo library/Close library"));
            Aim(app, close, close - menu.ActiveCanvas.transform.forward * 0.006f);
            Assert.IsFalse(menu.IsModal, "Fingertip contact activates non-HUD buttons");
            app.Hand.SetEditorSample(new Vector2(0.5f, 0.5f), null); app.SendLocal("pointer.on");

            // Scrolling a large library reuses the same twelve UI objects and caps decoded images.
            var library = menu.GetComponentInChildren<PhotoLibrary>(); library.EditorSampleCount = 600;
            menu.OpenPhotos(); yield return Ready(menu);
            foreach (float position in new[] { 0.9f, 0.7f, 0.5f, 0.3f, 0.1f, 0f, 1f })
            {
                yield return ScrollTo(grid, position); yield return Ready(menu);
                Assert.AreEqual(600, grid.Total);
                Assert.LessOrEqual(grid.CachedThumbnailCount, 48);
                Assert.AreEqual(12, grid.Scroll.content.childCount);
            }
            menu.Close(); Assert.AreEqual(0, grid.CachedThumbnailCount);
            library.EditorSampleCount = 8; menu.OpenPhotos(); yield return Ready(menu);
        }
    }
}
