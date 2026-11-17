using System.Collections;
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
    public sealed class GestureDebugHUDTests
    {
        const string PrefabPath = "Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab";
        [Test]
        public void SavedPanelHasAllGestureBindingsAndCompactButtons()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var panel = prefab.transform.Find("Safe Area/Gesture Controls/Gesture Panel");
            Assert.IsNotNull(panel);
            Assert.IsFalse(panel.gameObject.activeSelf);
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            foreach (var gesture in catalogue.gestures)
            {
                var button = panel.Find(gesture.gesture).GetComponent<Button>();
                Assert.AreEqual("TriggerGesture", button.onClick.GetPersistentMethodName(0));
                Assert.AreEqual(gesture.gesture, new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("m_Arguments.m_StringArgument").stringValue);
            }
            var left = (RectTransform)prefab.transform.Find("Safe Area/Normal Actions/Left Click Button");
            Assert.That(left.rect.height, Is.InRange(40, 48));
            Assert.Less(left.rect.width, 150);
        }

        [Test]
        public void OpenPanelsFitBesideEachOtherAndRemainEditable()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var cameraObject = new GameObject("Debug HUD preview", typeof(Camera));
            var target = new RenderTexture(1280, 720, 24); target.Create();
            var capture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var hud = root.GetComponent<VisualizerHUD>();
                var panel = (RectTransform)root.transform.Find("Safe Area/Gesture Controls/Gesture Panel");
                var settings = (RectTransform)root.transform.Find("Safe Area/Normal Actions/Screen Settings Panel");
                panel.gameObject.SetActive(true); settings.gameObject.SetActive(true);
                var camera = cameraObject.GetComponent<Camera>(); camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.16f, 0.19f, 0.22f);
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                Rect Bounds(RectTransform rect)
                {
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    return Rect.MinMaxRect(camera.WorldToScreenPoint(corners[0]).x, camera.WorldToScreenPoint(corners[0]).y,
                        camera.WorldToScreenPoint(corners[2]).x, camera.WorldToScreenPoint(corners[2]).y);
                }
                var gestureBounds = Bounds(panel);
                Assert.IsFalse(gestureBounds.Overlaps(Bounds(settings)));
                Assert.IsFalse(gestureBounds.Overlaps(Bounds((RectTransform)root.transform.Find("Safe Area/Top Bar"))));
                Assert.IsFalse(gestureBounds.Overlaps(Bounds((RectTransform)root.transform.Find("Safe Area/Placement Panel"))));
                Assert.IsFalse(gestureBounds.Overlaps(Bounds((RectTransform)root.transform.Find("Safe Area/Gesture History"))));
                Assert.GreaterOrEqual(gestureBounds.xMin, 0); Assert.LessOrEqual(gestureBounds.xMax, 1280);
                Assert.GreaterOrEqual(gestureBounds.yMin, 0); Assert.LessOrEqual(gestureBounds.yMax, 720);
                RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); capture.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/ARVisualizer-compact-debug-HUD.png", capture.EncodeToPNG());
                var position = panel.anchoredPosition;
                hud.Preview(VisualizerHUD.PreviewState.ReadyToPlace);
                Assert.AreEqual(position, panel.anchoredPosition);
                hud.SetHUDVisible(false);
                Assert.IsFalse(panel.gameObject.activeInHierarchy);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(capture);
                target.Release(); Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator DebugButtonsUseAppGestureContextAndPreserveScreenSettings()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            yield return new EnterPlayMode();
            yield return Exercise();
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static IEnumerator Exercise()
        {
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            var hud = app.GetComponentInChildren<VisualizerHUD>(); var debug = hud.GetComponent<GestureDebugHUD>();
            Assert.IsNotNull(debug, "The saved scene must inherit the new prefab controls");
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            Assert.IsFalse(app.ARCamera.GetComponent<AROcclusionManager>().environmentDepthTemporalSmoothingRequested);
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(901, true);
            ((System.Collections.Generic.IList<ScreenSurface>)app.Screens).Add(screen);
            screen.transform.SetPositionAndRotation(app.ARCamera.transform.position + app.ARCamera.transform.forward * 0.5f, app.ARCamera.transform.rotation);
            var content = screen.GetComponent<ScreenAppContent>(); Assert.IsTrue(content.Open("timer", app));
            var digit = (RectTransform)content.View.timeDigits[0].transform;
            app.LeftHand.SetPointerEnabled(true);
            app.LeftHand.SetEditorSample(app.ARCamera.WorldToViewportPoint(digit.TransformPoint(digit.rect.center)), null);
            app.SendLocal("left.status");
            var receiver = Object.FindAnyObjectByType<GestureWebSocketReceiver>(); int receipts = receiver.RecentGestures.Count;
            debug.TogglePanel(); debug.SelectLeftHand();
            var panel = hud.transform.Find("Safe Area/Gesture Controls/Gesture Panel");
            panel.Find("SWIPE_UP").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(660, content.Timer.PresetSeconds);
            Assert.AreEqual(TrackedHand.Left, app.ControlHand);
            Assert.AreEqual(receipts, receiver.RecentGestures.Count, "Local debug actions are not Wi-Fi receipts");
            panel.Find("PINCH_AND_PULL").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(app.AdjustModeEnabled && debug.IsOpen);
            Assert.AreEqual(ScreenEditMode.Resize, app.EditMode);
            debug.TogglePanel(); Assert.IsFalse(debug.IsOpen); Assert.IsTrue(app.AdjustModeEnabled);
            debug.TogglePanel(); Assert.IsTrue(app.AdjustModeEnabled);
            panel.Find("SWIPE_RIGHT").GetComponent<Button>().onClick.Invoke();
            panel.Find("CRUMPLE_AND_TOSS").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(app.AdjustModeEnabled); Assert.AreEqual(1, app.ScreenCount);
            hud.SetHUDVisible(false); Assert.IsFalse(debug.IsOpen);
            hud.SetHUDVisible(true); Assert.IsFalse(debug.IsOpen);
            debug.TogglePanel(); app.SendLocal("goggle.enter");
            Assert.IsFalse(debug.IsOpen); Assert.IsFalse(hud.transform.Find("Safe Area/Gesture Controls").gameObject.activeInHierarchy);
            app.SendLocal("goggle.exit"); Assert.IsFalse(debug.IsOpen);
            app.SendLocal("screens.clear");
            yield return null;
        }
    }
}
