using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer.Tests
{
    public sealed class GoggleMenuTests
    {
        [UnityTest]
        public IEnumerator MenusFitScreensAndDwellOncePerVisit()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>()) receiver.enabled = false;
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>()) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            yield return Exercise(); // Captured test locals are created after the domain reload.
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static ScreenSurface Screen(ARVisualizerApp app, int id, bool horizontal = true)
        {
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(id, horizontal);
            ((IList<ScreenSurface>)app.Screens).Add(screen);
            screen.transform.SetPositionAndRotation(app.ARCamera.transform.position + app.ARCamera.transform.forward * 0.6f,
                app.ARCamera.transform.rotation);
            return screen;
        }

        static Vector3 Centre(Component button)
        {
            var rect = (RectTransform)button.transform;
            return rect.TransformPoint(rect.rect.center);
        }

        static void Aim(ARVisualizerApp app, Vector3 point, TrackedHand hand = TrackedHand.Right)
        {
            app.Hands.GetHand(hand).SetEditorSample(app.ARCamera.WorldToViewportPoint(point), null);
            app.SendLocal(hand == TrackedHand.Left ? "left.status" : "right.status");
        }

        static IEnumerator Hold(ARVisualizerApp app, Button button, float seconds, TrackedHand hand = TrackedHand.Right)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                Aim(app, Centre(button), hand);
                yield return null;
            }
        }

        static Button Button(ScreenAppMenu menu, string path) => menu.transform.Find("Screen launcher/" + path).GetComponent<Button>();
        static void SameWidth(ScreenSurface screen, Transform panel)
        {
            var rect = (RectTransform)panel;
            Assert.That(rect.TransformVector(Vector3.right * rect.rect.width).magnitude,
                Is.EqualTo(screen.transform.TransformVector(Vector3.right * screen.Width).magnitude).Within(0.00001f),
                panel.name + " matches the screen's physical width");
        }

        static IEnumerator Exercise()
        {
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            app.Tutorial.EnterFreeMode(); app.Tutorial.CompleteTransition();
            var screen = Screen(app, 980);
            var menu = app.MenuFor(TrackedHand.Right);
            app.SendLocal("right.pointer.on");
            var options = (RectTransform)menu.transform.Find("Screen launcher/App options");
            var settings = (RectTransform)menu.transform.Find("Screen launcher/Screen Options");
            // Include edited prefab geometry, small/large screens and non-standard media shapes.
            options.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 620);
            settings.localScale = Vector3.one * 0.8f;
            screen.transform.localScale = new Vector3(1.25f, 0.8f, 1);
            foreach (bool goggles in new[] { false, true })
            {
                app.SendLocal(goggles ? "goggle.enter" : "goggle.exit");
                foreach (float distance in new[] { 0.45f, 2f })
                foreach (int steps in new[] { 1, 10, 100 })
                foreach (float aspect in new[] { 0.6f, 16f / 9, 3f })
                {
                    screen.Resize(steps - screen.SizeSteps); screen.SetMediaAspectRatio(aspect);
                    screen.transform.position = app.ARCamera.transform.position + app.ARCamera.transform.forward * distance;
                    Aim(app, screen.transform.position);
                    menu.ShowPinnedChooser(screen); Canvas.ForceUpdateCanvases(); menu.RefreshPointer();
                    SameWidth(screen, options);
                    menu.Close();
                    Assert.IsTrue(menu.TryToggleSettings(screen, out var message), message);
                    menu.RefreshPointer(); SameWidth(screen, settings);
                    menu.Close();
                }
            }
            options.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 560);
            settings.localScale = Vector3.one;
            screen.transform.localScale = Vector3.one;
            screen.Resize(10 - screen.SizeSteps); screen.SetMediaAspectRatio(16f / 9);
            screen.transform.position = app.ARCamera.transform.position + app.ARCamera.transform.forward * 0.6f;

            app.SendLocal("goggle.exit");
            menu.ShowPinnedChooser(screen); Canvas.ForceUpdateCanvases();
            var timer = Button(menu, "App options/Timer");
            yield return Hold(app, timer, 3.2f);
            Assert.IsTrue(screen.GetComponent<ScreenMedia>().IsBlank, "Phone mode never dwell-clicks");
            Assert.AreEqual(0, menu.ButtonDwell);
            app.SendLocal("goggle.enter");
            yield return Hold(app, timer, 1.1f);
            Assert.Greater(menu.ButtonDwell, 0.5f);
            var progress = (RectTransform)timer.transform.Find("Hold Track/Hold Progress");
            Assert.That(progress.anchorMax.x, Is.InRange(0.1f, 0.8f));
            Assert.IsFalse(progress.GetComponent<Image>().raycastTarget, "The progress bar cannot steal the ray");
            Aim(app, app.ARCamera.transform.position + app.ARCamera.transform.up * 2 + app.ARCamera.transform.forward);
            menu.RefreshDwell();
            Assert.AreEqual(0, menu.ButtonDwell, "Leaving the button resets the hold");
            yield return Hold(app, timer, 2.1f);
            Assert.IsTrue(screen.GetComponent<ScreenMedia>().IsBlank, "The interrupted hold cannot carry over");
            yield return Hold(app, timer, 1.1f);
            Assert.AreEqual("timer", screen.GetComponent<ScreenMedia>().AppId, "Three seconds launches the real app");

            screen.GetComponent<ScreenMedia>().Clear(); menu.ShowPinnedChooser(screen);
            var dropdown = Button(menu, "Choose app");
            int presses = 0;
            dropdown.onClick.AddListener(() => presses++);
            yield return Hold(app, dropdown, 6.4f);
            Assert.AreEqual(1, presses, "Continuous pointing activates only once");
            Assert.IsFalse(menu.IsChooserOpen);
            Aim(app, screen.WorldPoint(Vector2.one)); menu.RefreshDwell();
            yield return Hold(app, dropdown, 3.2f);
            Assert.AreEqual(2, presses, "Leaving and returning rearms the button");
            Assert.IsTrue(menu.IsChooserOpen);
            Assert.IsTrue(menu.TryClick(out _));
            yield return Hold(app, dropdown, 3.2f);
            Assert.AreEqual(3, presses, "A gesture click consumes the visit too");

            // The centre fallback cannot accidentally activate a button after hand tracking is lost.
            screen.transform.position += app.ARCamera.transform.position + app.ARCamera.transform.forward * 0.6f - Centre(dropdown);
            menu.RefreshPointer();
            app.RightHand.SetEditorSample(new Vector2(-1, -1), null);
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null);
            yield return new WaitForSecondsRealtime(3.2f);
            menu.RefreshPointer();
            Assert.AreSame(dropdown, menu.HoveredButton);
            Assert.AreEqual(0, menu.ButtonDwell);
            Assert.AreEqual(3, presses);
            menu.Close();

            // Settings buttons use the owning hand and the same activation path as ordinary clicks.
            var leftScreen = Screen(app, 981, false);
            var leftMenu = app.MenuFor(TrackedHand.Left);
            Aim(app, leftScreen.transform.position, TrackedHand.Left);
            Assert.IsTrue(leftMenu.TryToggleSettings(leftScreen, out _));
            var rotate = Button(leftMenu, "Screen Options/Rotate");
            Assert.IsFalse(rotate.IsInteractable());
            yield return Hold(app, rotate, 0.3f, TrackedHand.Left);
            Assert.AreEqual(0, leftMenu.ButtonDwell, "Disabled buttons never start a hold");
            var choose = Button(leftMenu, "Screen Options/Choose app");
            yield return Hold(app, choose, 3.2f, TrackedHand.Left);
            Assert.IsTrue(leftMenu.IsChooserOpen && !leftMenu.IsSettingsOpen);
            Assert.AreSame(leftScreen, leftMenu.Target);
            Assert.AreSame(screen, menu.Target, "A left-hand hold does not retarget the right-hand menu");
            leftMenu.Close(); app.SendLocal("goggle.exit");
        }
    }
}
