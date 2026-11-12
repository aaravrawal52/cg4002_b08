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
    public sealed class BuiltInAppTests
    {
        [Test]
        public void CountdownPausesResumesAndFinishesWithoutFrameDrift()
        {
            var timer = new CountdownTimer();
            timer.Toggle(10);
            Assert.AreEqual(60, timer.Seconds(10.1));
            timer.Toggle(11.25);
            Assert.AreEqual(59, timer.Seconds(100));
            timer.Toggle(100);
            Assert.AreEqual(1, timer.Seconds(158));
            Assert.AreEqual(0, timer.Seconds(158.75));
            Assert.IsTrue(timer.Finished);
            Assert.IsFalse(timer.IsRunning);
            timer.Toggle(200);
            Assert.IsFalse(timer.IsRunning, "Zero needs a reset or a new duration");
            timer.Reset();
            Assert.AreEqual(60, timer.Seconds(250));
            Assert.IsFalse(timer.Finished);
        }

        [Test]
        public void EditingPausesClampsAndUpdatesTheResetDuration()
        {
            var timer = new CountdownTimer();
            timer.Toggle(0);
            timer.Adjust(10, 5.5);
            Assert.AreEqual(65, timer.Seconds(100));
            Assert.IsFalse(timer.IsRunning);
            timer.Toggle(100); timer.Toggle(110); timer.Reset();
            Assert.AreEqual(65, timer.Seconds(120));
            timer.Adjust(-1000, 120);
            Assert.AreEqual(0, timer.Seconds(120));
            timer.Adjust(10000, 120);
            Assert.AreEqual(5999, timer.Seconds(120));
        }

        [Test]
        public void GestureContextPreservesExplicitCommandsAndHandIdentity()
        {
            Assert.IsTrue(CommandProtocol.TryParseGesture("swipe_up", "left", out var gesture, out _));
            Assert.AreEqual("gesture.up", gesture.command);
            Assert.AreEqual("left", gesture.hand);
            Assert.AreEqual("app.increase", CommandProtocol.CommandForAppGesture("timer", gesture.sourceGesture));
            Assert.AreEqual("app.reveal", CommandProtocol.CommandForAppGesture("flashcards", gesture.sourceGesture));
            Assert.IsNull(CommandProtocol.CommandForAppGesture("photos", gesture.sourceGesture));
            Assert.IsTrue(CommandProtocol.TryParse("left.settings.enter", out var explicitCommand, out _));
            Assert.IsNull(explicitCommand.sourceGesture);
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            foreach (var entry in catalogue.appGestures)
                Assert.IsTrue(CommandProtocol.TryParse("right." + entry.command, out _, out var error), error);
        }

        static CommandReply Send(ARVisualizerApp app, string name, TrackedHand hand = TrackedHand.Right, bool gesture = false)
        {
            var command = new VisualizerCommand { command = name, hand = hand == TrackedHand.Left ? "left" : "right" };
            if (gesture) Assert.IsTrue(CommandProtocol.TryParseGesture(name, command.hand, out command, out _));
            CommandReply reply = null;
            app.Execute(command, result => reply = result);
            Assert.IsNotNull(reply);
            return reply;
        }

        static void Aim(ARVisualizerApp app, Vector3 point, TrackedHand hand = TrackedHand.Right, Vector3? tip = null)
        {
            app.Hands.GetHand(hand).SetEditorSample(app.ARCamera.WorldToViewportPoint(point), tip);
            Send(app, "status", hand);
        }
        static Vector3 Centre(Component component)
        {
            var rect = (RectTransform)component.transform;
            return rect.TransformPoint(rect.rect.center);
        }
        static void Draw(ARVisualizerApp app, string capture = null)
        {
            Canvas.ForceUpdateCanvases();
            var camera = app.ARCamera; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24); target.Create();
            try
            {
                camera.targetTexture = target; camera.Render();
                if (capture == null) return;
                RenderTexture.active = target;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/" + capture + ".png", image.EncodeToPNG());
                Object.Destroy(image);
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; target.Release(); Object.Destroy(target); }
        }

        static void Choose(ARVisualizerApp app, ScreenSurface screen, string title)
        {
            // The larger chooser covers the face centre; the outer border is still reachable.
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.9f)));
            Assert.IsTrue(Send(app, "app.choose").ok);
            Draw(app);
            var option = app.ScreenApps.transform.Find("Screen launcher/App options/" + title).GetComponent<Button>();
            Aim(app, Centre(option));
            Assert.AreEqual(title, app.ScreenApps.TargetName);
            Assert.IsTrue(Send(app, "ui.click").ok);
        }

        [UnityTest]
        public IEnumerator TimerAndFlashcardsWorkThroughTheScreenInputPath()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            yield return new EnterPlayMode();
            yield return ExerciseApps();
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static IEnumerator ExerciseApps()
        {
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(app != null && ARSession.state == ARSessionState.SessionTracking);
            // Register real screen prefabs at a deterministic viewing pose; placement itself has smoke coverage.
            var prefab = AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab");
            var screen = Object.Instantiate(prefab); screen.Initialize(801, true);
            ((IList<ScreenSurface>)app.Screens).Add(screen);
            var camera = app.ARCamera.transform;
            screen.transform.SetPositionAndRotation(camera.position + camera.forward * 0.45f, camera.rotation);
            Send(app, "pointer.on");
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.9f)));
            Assert.IsTrue(app.ScreenApps.IsChooserOpen, "Empty screen opens the chooser automatically");
            Draw(app, "ARVisualizer-app-chooser");
            var optionRect = (RectTransform)app.ScreenApps.transform.Find("Screen launcher/App options/Timer");
            Assert.Greater(optionRect.rect.width * optionRect.lossyScale.x, screen.Width * 0.85f);
            Choose(app, screen, "Timer");
            var content = screen.GetComponent<ScreenAppContent>();
            Assert.AreEqual("timer", content.AppId);
            Assert.IsTrue(content.IsOpen);
            int[] steps = { 600, 60, 10, 1 };
            for (int i = 0; i < 4; i++)
            {
                Aim(app, Centre(content.View.timeDigits[i]));
                int before = content.Timer.PresetSeconds;
                Assert.IsTrue(Send(app, "SWIPE_UP", gesture: true).ok);
                Assert.AreEqual(before + steps[i], content.Timer.PresetSeconds);
                Assert.IsFalse(app.AdjustModeEnabled);
                Assert.IsTrue(Send(app, "SWIPE_DOWN", gesture: true).ok);
                Assert.AreEqual(before, content.Timer.PresetSeconds);
            }
            Draw(app, "ARVisualizer-timer");
            Aim(app, Centre(content.View.timerToggle));
            Assert.IsTrue(Send(app, "WRIGGLE_FINGERS", gesture: true).ok);
            Assert.IsTrue(content.Timer.IsRunning);
            app.RightHand.SetEditorSample(new Vector2(0.9f, 0.9f), null);
            Send(app, "status");
            Assert.AreNotEqual(screen, app.RayScreen);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.Less(content.Timer.Seconds(Time.realtimeSinceStartupAsDouble), 60);
            Aim(app, Centre(content.View.timeDigits[3]));
            Send(app, "app.reset");
            Assert.IsFalse(content.Timer.IsRunning);
            Assert.AreEqual(60, content.Timer.Seconds(Time.realtimeSinceStartupAsDouble));

            // Explicit settings commands keep their meaning; gesture rotation remains available there.
            Assert.IsTrue(Send(app, "settings.enter").ok);
            int preset = content.Timer.PresetSeconds;
            Send(app, "SWIPE_UP", gesture: true);
            Assert.AreEqual(preset, content.Timer.PresetSeconds);
            Assert.IsTrue(Send(app, "SWIPE_RIGHT", gesture: true).ok);
            Send(app, "adjust.exit");
            screen.transform.rotation = camera.rotation;

            // Touch activates once per contact, even across missing depth samples.
            var touch = Centre(content.View.timeDigits[3]);
            Aim(app, touch, tip: touch); yield return null; yield return null;
            Assert.AreEqual(preset + 1, content.Timer.PresetSeconds);
            Aim(app, touch); yield return null;
            Aim(app, touch, tip: touch); yield return null; yield return null;
            Assert.AreEqual(preset + 1, content.Timer.PresetSeconds);
            Aim(app, touch, tip: touch + screen.FrontNormal * 0.06f); yield return null;
            Aim(app, touch, tip: touch); yield return null; yield return null;
            Assert.AreEqual(preset + 2, content.Timer.PresetSeconds);
            Aim(app, touch, tip: touch + screen.FrontNormal * 0.06f); yield return null;
            Aim(app, touch);

            Choose(app, screen, "German flashcards");
            Assert.AreEqual("flashcards", content.AppId);
            Assert.AreEqual(32, content.CardCount);
            Assert.AreEqual("der Apfel", content.View.cardWord.text);
            Aim(app, Centre(content.View.cardBody));
            Send(app, "SWIPE_UP", gesture: true);
            Assert.AreEqual("apple", content.View.cardAnswer.text);
            Draw(app, "ARVisualizer-flashcards");
            Send(app, "SWIPE_RIGHT", gesture: true);
            Assert.AreEqual(1, content.CardIndex); Assert.IsFalse(content.AnswerVisible);
            Send(app, "SWIPE_LEFT", gesture: true);
            Assert.AreEqual(0, content.CardIndex);
            Send(app, "WRIGGLE_FINGERS", gesture: true); Assert.IsTrue(content.AnswerVisible);
            Send(app, "SWIPE_DOWN", gesture: true); Assert.IsFalse(content.AnswerVisible);
            Send(app, "app.previous"); Assert.AreEqual(31, content.CardIndex);
            Send(app, "app.next"); Assert.AreEqual(0, content.CardIndex);

            Assert.IsTrue(Send(app, "goggle.enter").ok);
            Aim(app, Centre(content.View.cardBody));
            Send(app, "SWIPE_UP", gesture: true); Assert.IsTrue(content.AnswerVisible);
            Send(app, "goggle.exit");

            var second = Object.Instantiate(prefab); second.Initialize(802, true);
            ((IList<ScreenSurface>)app.Screens).Add(second);
            second.transform.SetPositionAndRotation(screen.transform.position + camera.right * 0.19f, camera.rotation);
            var other = second.GetComponent<ScreenAppContent>();
            Assert.IsTrue(other.Open("timer", app));
            Send(app, "pointer.on", TrackedHand.Left);
            Aim(app, Centre(other.View.timeDigits[0]), TrackedHand.Left);
            Assert.IsTrue(Send(app, "SWIPE_UP", TrackedHand.Left, true).ok);
            Assert.AreEqual(660, other.Timer.PresetSeconds);
            Assert.AreEqual("flashcards", content.AppId); Assert.AreEqual(0, content.CardIndex);
            Aim(app, Centre(content.View.cardBody));
            Send(app, "app.next"); Assert.AreEqual(1, content.CardIndex);
            Assert.AreEqual(660, other.Timer.PresetSeconds);

            // Replacing and clearing an app release its visible content and re-enable blank-screen selection.
            var media = screen.GetComponent<ScreenMedia>();
            Assert.IsTrue(media.ShowPhoto(new Texture2D(8, 4)));
            Assert.IsFalse(content.IsOpen);
            Assert.AreEqual("photos", media.AppId);
            media.Clear(); app.ScreenApps.Close();
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.9f)));
            Assert.IsTrue(app.ScreenApps.IsChooserOpen);
            Send(app, "screens.clear");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnlargedChooserKeepsDualHandInteractionsIndependent()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            yield return new EnterPlayMode();
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            yield return DualHandInteractionChecks.Exercise(app, includePlacement: false);
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }
    }
}
