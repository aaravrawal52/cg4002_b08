using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ARVisualizer.Tests
{
    internal static class PhoneClickChecks
    {
        static CommandReply Send(ARVisualizerApp app, string command)
        {
            CommandReply reply = null; app.Execute(new VisualizerCommand { command = command }, result => reply = result);
            Assert.IsNotNull(reply); return reply;
        }
        static void AimScreen(ARVisualizerApp app, ScreenSurface screen, Vector2 uv)
        {
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(screen.WorldPoint(uv)), null);
            Send(app, "status");
        }
        internal static IEnumerator Touch(Touchscreen device, int id, Vector2 position, TouchPhase phase)
        {
            InputSystem.QueueStateEvent(device, new TouchState { touchId = id, position = position, phase = phase });
            // EditMode test coroutines can tick faster than the player loop. Wait for real frames
            // so the input update, EventSystem and LateUpdate all run before inspecting delivery.
            int frame = Time.frameCount; float deadline = Time.realtimeSinceStartup + 3;
            while (Time.frameCount < frame + 3 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.GreaterOrEqual(Time.frameCount, frame + 3, "The player loop must process the queued touch");
        }
        static IEnumerator Tap(Touchscreen device, int id, Vector2 position)
        {
            yield return Touch(device, id, position, TouchPhase.Began);
            yield return Touch(device, id, position, TouchPhase.Ended);
        }

        public static void AssertIconVisible(DisplayModeButton button, Camera camera, Texture2D screenshot)
        {
            var icon = button.GetComponentInChildren<ModeSwitchIcon>();
            var corners = new Vector3[4]; icon.rectTransform.GetWorldCorners(corners);
            var min = camera.WorldToScreenPoint(corners[0]); var max = camera.WorldToScreenPoint(corners[2]);
            int whitePixels = 0;
            for (int y = Mathf.Max(0, Mathf.CeilToInt(min.y)); y < Mathf.Min(screenshot.height, max.y); ++y)
                for (int x = Mathf.Max(0, Mathf.CeilToInt(min.x)); x < Mathf.Min(screenshot.width, max.x); ++x)
                {
                    var color = screenshot.GetPixel(x, y);
                    if (color.r > 0.8f && color.g > 0.8f && color.b > 0.8f) ++whitePixels;
                }
            Assert.Greater(whitePixels, 40, "The mode button must render a visible white icon");
        }
        public static IEnumerator Exercise(ARVisualizerApp app, VisualizerHUD hud, ScreenSurface screen)
        {
            var toggle = app.ModeButton;
            Assert.IsNotNull(toggle);
            var button = toggle.GetComponent<Button>();
            var rect = (RectTransform)toggle.transform;
            var icon = toggle.GetComponentInChildren<ModeSwitchIcon>();
            var phoneCanvas = toggle.GetComponentInParent<Canvas>();
            Assert.IsFalse(toggle.transform.IsChildOf(hud.transform));
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, phoneCanvas.renderMode);
            Assert.AreEqual(rect.rect.width, rect.rect.height, "Mode switch must be square");
            Assert.AreEqual(new Vector2(0.5f, 1), rect.anchorMin);
            Assert.AreEqual(rect.anchorMin, rect.anchorMax);
            Assert.AreEqual("ToggleMode", button.onClick.GetPersistentMethodName(0));
            app.SendLocal("pointer.off");
            hud.SetHUDVisible(false);
            Assert.IsTrue(toggle.isActiveAndEnabled, "Mode switch must remain available when the HUD is hidden");
            Canvas.ForceUpdateCanvases();
            var normalPosition = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            button.onClick.Invoke(); yield return null;
            Assert.IsTrue(app.GoggleModeEnabled && app.Hand.PointerEnabled);
            Assert.IsTrue(icon.ShowsPhone);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, phoneCanvas.renderMode);
            Assert.Greater(phoneCanvas.sortingOrder, app.GoggleHUD.Stereo.OutputCanvas.sortingOrder);
            Assert.IsTrue(phoneCanvas.GetComponent<GraphicRaycaster>().enabled);
            Canvas.ForceUpdateCanvases();
            Assert.That(Vector2.Distance(normalPosition, RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))), Is.LessThan(0.01f));
            button.onClick.Invoke();
            yield return null;
            Assert.IsFalse(app.GoggleModeEnabled || app.Hand.PointerEnabled);
            Assert.IsFalse(icon.ShowsPhone);
            hud.SetHUDVisible(true); app.SendLocal("pointer.on");
            yield return null;

            var originalSettings = InputSystem.settings;
            var originalFocus = originalSettings.editorInputBehaviorInPlayMode;
            var originalBackground = originalSettings.backgroundBehavior;
            originalSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            originalSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var device = InputSystem.AddDevice<Touchscreen>();
            int left = 0, right = 0; Vector2 last = default;
            void Clicked(ScreenSurface target, PointerEventData.InputButton which, Vector2 uv)
            { if (which == PointerEventData.InputButton.Left) ++left; else ++right; last = uv; }
            screen.PointerClicked += Clicked;
            try
            {
                Vector2 leftArea = new Vector2(Screen.width * 0.04f, Screen.height * 0.5f);
                Vector2 rightArea = new Vector2(Screen.width * 0.96f, Screen.height * 0.5f);
                Assert.IsFalse(app.PhoneClicks.IsOverGUI(leftArea)); Assert.IsFalse(app.PhoneClicks.IsOverGUI(rightArea));
                // Blank screens now expand the app options over the lower face automatically.
                // Exercise screen clicks on the uncovered top edge, outside those GUI controls.
                var uv = new Vector2(0.35f, 0.95f);
                AimScreen(app, screen, uv);
                Assert.AreSame(screen, app.RayScreen);
                yield return Touch(device, 1, leftArea, TouchPhase.Began);
                Assert.AreEqual(1, left, $"Touch enabled={device.enabled}, phase={device.primaryTouch.phase.ReadValue()}, tracked={app.Hand.IsTracked}, GUI={app.PhoneClicks.IsOverGUI(leftArea)}, feedback={app.Feedback}, touches={UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count}"); Assert.AreEqual(0, right);
                Assert.That(Vector2.Distance(uv, last), Is.LessThan(0.001f), "Click coordinates come from the hand ray, not the phone touch");
                yield return null; yield return null;
                yield return Touch(device, 1, rightArea, TouchPhase.Moved);
                yield return Touch(device, 1, rightArea, TouchPhase.Ended);
                Assert.AreEqual(1, left); Assert.AreEqual(0, right, "Dragging across the split must not create another click");
                yield return Tap(device, 2, rightArea);
                Assert.AreEqual(1, right);
                var middle = new Vector2(Screen.width * 0.5f, 0);
                for (int y = 2; y <= 7; ++y)
                {
                    middle.y = Screen.height * y / 10f;
                    if (!app.PhoneClicks.IsOverGUI(middle)) break;
                }
                Assert.IsFalse(app.PhoneClicks.IsOverGUI(middle), "Test the centre split outside all GUI");
                yield return Tap(device, 3, middle);
                Assert.AreEqual(2, right, "The exact centre belongs to the right half");

                var panel = (RectTransform)hud.transform.Find("Safe Area/Placement Panel");
                var guiPoint = RectTransformUtility.WorldToScreenPoint(null, panel.TransformPoint(panel.rect.center));
                Assert.IsTrue(app.PhoneClicks.IsOverGUI(guiPoint), "Informational panels also count as GUI");
                yield return Tap(device, 4, guiPoint);
                Assert.AreEqual(1, left); Assert.AreEqual(2, right);

                app.Hand.SetEditorSample(new Vector2(-1, -1), null);
                app.SendLocal("pointer.off");
                yield return Tap(device, 5, leftArea); Assert.AreEqual(1, left, "A disabled pointer cannot reuse the previous target");
                AimScreen(app, screen, uv); app.SendLocal("pointer.off");
                yield return Tap(device, 6, rightArea); Assert.AreEqual(2, right);
                app.SendLocal("pointer.on"); AimScreen(app, screen, uv);
                app.SendLocal("goggle.enter");
                yield return Tap(device, 7, leftArea); Assert.AreEqual(1, left, "Phone split clicks are disabled in goggle mode");
                app.SendLocal("goggle.exit"); AimScreen(app, screen, uv);
                app.ScreenApps.OpenPhotos();
                float deadline = Time.realtimeSinceStartup + 5;
                while (app.ScreenApps.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(app.ScreenApps.IsModal);
                yield return Tap(device, 8, rightArea);
                Assert.IsFalse(app.ScreenApps.IsModal, "A right click goes back from the AR gallery");
                Assert.AreEqual(2, right, "A modal right click must not reach the screen behind it");

                // Touching the mode button itself must execute the GUI action once, without a ray click.
                Canvas.ForceUpdateCanvases();
                var modePoint = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                yield return Tap(device, 9, modePoint);
                Assert.IsTrue(app.GoggleModeEnabled);
                Assert.AreEqual(1, left); Assert.AreEqual(2, right);
                app.Hand.SetEditorSample(new Vector2(-1, -1), null);
                yield return Tap(device, 10, modePoint);
                Assert.IsFalse(app.GoggleModeEnabled, "The fixed phone overlay must accept touch in goggles even without hand tracking");
                Assert.AreEqual(1, left); Assert.AreEqual(2, right);
            }
            finally
            {
                screen.PointerClicked -= Clicked;
                InputSystem.RemoveDevice(device);
                originalSettings.editorInputBehaviorInPlayMode = originalFocus;
                originalSettings.backgroundBehavior = originalBackground;
                app.ScreenApps.Close(); app.Hand.ClearEditorSample(); app.SendLocal("pointer.off");
            }
        }
    }
}
