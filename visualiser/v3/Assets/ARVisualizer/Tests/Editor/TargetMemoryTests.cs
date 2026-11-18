using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer.Tests
{
    public sealed class TargetMemoryTests
    {
        static CommandReply Gesture(ARVisualizerApp app, string name, string hand = "right")
        {
            Assert.IsTrue(CommandProtocol.TryParseGesture(name, hand, out var command, out _));
            CommandReply reply = null; app.Execute(command, result => reply = result); return reply;
        }
        static void Aim(ARVisualizerApp app, Vector3 point)
        {
            app.RightHand.SetEditorSample(app.ARCamera.WorldToViewportPoint(point), null);
            app.SendLocal("right.status");
        }
        [UnityTest]
        public IEnumerator FallbackExpiresAndDeletionRequiresLiveConfirmation()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>()) receiver.enabled = false;
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>()) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float until = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            var settings = new SerializedObject(app);
            settings.FindProperty("gestureCooldownSeconds").floatValue = 0; settings.ApplyModifiedPropertiesWithoutUndo();
            app.Tutorial.EnterFreeMode(); app.Tutorial.CompleteTransition(); app.SendLocal("right.pointer.on");
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(991, true); ((IList<ScreenSurface>)app.Screens).Add(screen);
            screen.transform.SetPositionAndRotation(app.ARCamera.transform.position + app.ARCamera.transform.forward * .6f, app.ARCamera.transform.rotation);
            screen.GetComponent<ScreenAppContent>().Open("timer", app);
            var face = screen.WorldPoint(new Vector2(.9f, .8f));
            var away = app.ARCamera.ViewportToWorldPoint(new Vector3(.85f, .65f, 1));
            Aim(app, face); Assert.AreSame(screen, app.RayScreen);
            Aim(app, away); Assert.IsNull(app.RayScreen);
            Assert.IsTrue(Gesture(app, "wiggle_fingers").ok, "A recent screen remains available after gesture movement");
            Assert.IsTrue(app.ScreenApps.IsSettingsOpen); Assert.AreSame(screen, app.ScreenApps.Target);
            app.ScreenApps.Close();
            Aim(app, face); Aim(app, away);
            app.LeftHand.SetEditorSample(new Vector2(.85f, .65f), null);
            Assert.IsFalse(Gesture(app, "wiggle_fingers", "left").ok, "The left hand cannot borrow right-hand memory");
            Assert.IsTrue(Gesture(app, "wiggle_fingers").ok);
            app.ScreenApps.Close();
            Aim(app, face); app.SendLocal("app.choose");
            int menuFrame = Time.frameCount;
            while (Time.frameCount <= menuFrame + 1) yield return null;
            Canvas.ForceUpdateCanvases();
            var timer = (RectTransform)app.ScreenApps.transform.Find("Screen launcher/App options/Timer");
            Aim(app, timer.TransformPoint(timer.rect.center));
            Assert.IsNotNull(app.ScreenApps.HoveredButton);
            Aim(app, away);
            Assert.IsTrue(Gesture(app, "click").ok, "A remembered app button can be clicked after moving away");
            Assert.IsFalse(app.ScreenApps.IsChooserOpen);
            Aim(app, face); app.SendLocal("app.choose");
            menuFrame = Time.frameCount;
            while (Time.frameCount <= menuFrame + 1) yield return null;
            Aim(app, timer.TransformPoint(timer.rect.center)); Aim(app, away);
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.IsFalse(Gesture(app, "click").ok, "An expired menu target must not select the first app by default");
            app.ScreenApps.Close();
            Aim(app, face); Aim(app, away);
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.IsFalse(Gesture(app, "wiggle_fingers").ok, "Memory expires after one second");
            Aim(app, face); Aim(app, away);
            Assert.IsFalse(Gesture(app, "crumple_and_toss").ok, "Deletion never uses memory");
            Assert.IsFalse(screen.DeletionPending);
            Aim(app, face);
            Assert.IsTrue(Gesture(app, "crumple_and_toss").ok);
            Assert.IsTrue(screen.DeletionPending); Assert.AreEqual(1, app.ScreenCount);
            Aim(app, away);
            Assert.IsFalse(Gesture(app, "grab_and_release").ok);
            Assert.AreEqual(1, app.ScreenCount);
            Aim(app, face);
            Assert.IsTrue(Gesture(app, "grab_and_release").ok);
            Assert.AreEqual(0, app.ScreenCount);
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }
    }
}
