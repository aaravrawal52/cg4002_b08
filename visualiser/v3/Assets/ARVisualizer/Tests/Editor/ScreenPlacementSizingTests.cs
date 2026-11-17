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
    public sealed class ScreenPlacementSizingTests
    {
        [Test]
        public void PlacementSizeFollowsDistanceAndHonoursPrefabAndInspectorLimits()
        {
            var root = new GameObject("Screen size settings"); root.SetActive(false);
            try
            {
                var app = root.AddComponent<ARVisualizerApp>();
                Assert.AreEqual(5, app.InitialSizeStepsAtDistance(0.25f));
                Assert.AreEqual(10, app.InitialSizeStepsAtDistance(0.5f));
                Assert.AreEqual(20, app.InitialSizeStepsAtDistance(1));
                Assert.AreEqual(40, app.InitialSizeStepsAtDistance(2));
                Assert.AreEqual(5, app.InitialSizeStepsAtDistance(0.01f));
                Assert.AreEqual(100, app.InitialSizeStepsAtDistance(100));
                Assert.AreEqual(10, app.InitialSizeStepsAtDistance(float.NaN));
                var fields = new SerializedObject(app);
                fields.FindProperty("screenSizeReferenceDistance").floatValue = 1;
                fields.FindProperty("minimumInitialSizeSteps").intValue = 8;
                fields.FindProperty("maximumInitialSizeSteps").intValue = 25;
                fields.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(20, app.InitialSizeStepsAtDistance(2));
                Assert.AreEqual(8, app.InitialSizeStepsAtDistance(0.2f));
                Assert.AreEqual(25, app.InitialSizeStepsAtDistance(10));
                var prefab = AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab");
                var customPrefab = Object.Instantiate(prefab, root.transform);
                customPrefab.Resize(5);
                fields.FindProperty("screenPrefab").objectReferenceValue = customPrefab;
                fields.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(15, app.InitialSizeStepsAtDistance(1), "Use the authored reference size");
                fields.FindProperty("sizeNewScreensByDistance").boolValue = false;
                fields.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(15, app.InitialSizeStepsAtDistance(2));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator SettingsScaleWithTheScreenInPhoneAndGoggleMode()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            // These checks exercise free mode; the start-up tutorial choice has its own tests.
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            yield return Exercise();
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static IEnumerator Exercise()
        {
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            app.GetComponentInChildren<VisualizerHUD>().SetHUDVisible(false);
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(975, false);
            ((IList<ScreenSurface>)app.Screens).Add(screen);
            var menu = app.ScreenApps;
            var fields = new SerializedObject(menu);
            var settings = (RectTransform)fields.FindProperty("screenOptions").objectReferenceValue;
            var authoredSize = settings.sizeDelta;
            var authoredPosition = settings.anchoredPosition;
            var camera = app.ARCamera.transform;
            var origin = camera.position;
            var rotation = camera.rotation;
            foreach (bool goggles in new[] { false, true })
            {
                app.SendLocal(goggles ? "goggle.enter" : "goggle.exit");
                foreach (int steps in new[] { 5, 10, 40, 100 })
                {
                    screen.Resize(steps - screen.SizeSteps);
                    screen.transform.SetPositionAndRotation(origin + rotation * Vector3.forward * 2, rotation);
                    app.RightHand.SetPointerEnabled(true);
                    app.RightHand.SetEditorSample(app.ARCamera.WorldToViewportPoint(screen.WorldPoint(new Vector2(0.99f, 0.9f))), null);
                    app.SendLocal("right.status");
                    Assert.IsTrue(menu.TryToggleSettings(screen, out var message), message);
                    Canvas.ForceUpdateCanvases();
                    Assert.That(settings.rect.width * settings.lossyScale.x, Is.EqualTo(screen.Width).Within(0.00001f));
                    Assert.AreEqual(authoredSize, settings.sizeDelta); Assert.AreEqual(authoredPosition, settings.anchoredPosition);
                    // Changing viewing distance never resizes an existing screen or its settings.
                    camera.position += rotation * Vector3.forward * 0.4f;
                    menu.RefreshPointer();
                    Assert.AreEqual(steps, screen.SizeSteps);
                    Assert.That(settings.rect.width * settings.lossyScale.x, Is.EqualTo(screen.Width).Within(0.00001f));
                    camera.position = origin;
                    menu.Close();
                }
            }
            app.SendLocal("goggle.exit"); app.SendLocal("screens.clear");
            yield return null;
        }
    }
}
