using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Simulation;

namespace ARVisualizer.Tests
{
    public sealed class RuntimeSmokeTests
    {
        static CommandReply lastReply;

        [UnityTest]
        public IEnumerator BootsLandscapeHudAndPlacesInteractiveScreenInSimulation()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            // These are ordinary Inspector edits made before Play, including prefab overrides.
            var authoredHUD = Object.FindAnyObjectByType<VisualizerHUD>();
            Assert.IsNotNull(authoredHUD, "HUD must exist in the saved scene before Play");
            var panel = (RectTransform)authoredHUD.transform.Find("Safe Area/Placement Panel");
            panel.anchoredPosition = new Vector2(274, 110);
            panel.sizeDelta = new Vector2(480, 148);
            PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
            var brand = authoredHUD.transform.Find("Safe Area/Top Bar/Brand").GetComponent<Text>();
            brand.text = "CUSTOM FIELD / AR";
            brand.fontSize = 27;
            PrefabUtility.RecordPrefabInstancePropertyModifications(brand);
            var marker = (RectTransform)authoredHUD.transform.Find("Aim Position/Aim Marker");
            marker.sizeDelta = new Vector2(36, 36);
            PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            yield return new EnterPlayMode();
            yield return null;
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            Assert.IsNotNull(app);
            Assert.AreEqual(1, app.GetComponentsInChildren<ARSession>().Length);
            Assert.IsNotNull(app.GetComponentInChildren<ARCameraBackground>());
            var occlusion = app.ARCamera.GetComponent<AROcclusionManager>();
            Assert.IsNotNull(occlusion, "Depth occlusion must be on the AR camera, alongside its background");
            Assert.IsTrue(occlusion.enabled);
            Assert.IsNotNull(app.GetComponentInChildren<ARPlaneManager>());
            Assert.IsNotNull(app.GetComponentInChildren<Canvas>());
            Assert.AreEqual(1, app.GetComponentsInChildren<VisualizerHUD>(true).Length);
            Assert.AreEqual(1, app.GetComponents<HandPointerSource>().Length);
            Assert.AreEqual(1, app.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>().Length);
            var hud = app.GetComponentInChildren<VisualizerHUD>();
            Assert.IsTrue(hud.HasRequiredReferences);
            var runtimePanel = (RectTransform)hud.transform.Find("Safe Area/Placement Panel");
            Assert.AreEqual(new Vector2(274, 110), runtimePanel.anchoredPosition);
            Assert.AreEqual(new Vector2(480, 148), runtimePanel.sizeDelta);
            var runtimeBrand = hud.transform.Find("Safe Area/Top Bar/Brand").GetComponent<Text>();
            Assert.AreEqual("CUSTOM FIELD / AR", runtimeBrand.text);
            Assert.AreEqual(27, runtimeBrand.fontSize);
            Assert.AreEqual(new Vector2(36, 36), ((RectTransform)hud.transform.Find("Aim Position/Aim Marker")).sizeDelta);
            Assert.IsFalse(Screen.autorotateToPortrait);
            Assert.IsFalse(Screen.autorotateToPortraitUpsideDown);
            Assert.IsTrue(Screen.autorotateToLandscapeLeft && Screen.autorotateToLandscapeRight);
            Assert.That(Vector2.Distance(new Vector2(0.16f, 0.09f), app.DefaultScreenSize), Is.LessThan(0.00001f));
            Assert.IsFalse(app.PlaceModeEnabled, "Start in view mode");
            lastReply = null;
            app.Execute(new VisualizerCommand { command = "screen.place" }, r => lastReply = r);
            Assert.IsFalse(lastReply.ok);
            StringAssert.Contains("place mode", lastReply.message);
            app.Execute(new VisualizerCommand { command = "place.enter" }, r => lastReply = r);
            Assert.IsTrue(lastReply.ok && lastReply.placeModeEnabled);
            Assert.IsTrue(app.PlaceModeEnabled);
            Click(hud, "Mode Button");
            Assert.IsFalse(app.PlaceModeEnabled, "Saved button action exits place mode");
            Click(hud, "Mode Button");
            Assert.IsTrue(app.PlaceModeEnabled, "Saved button action enters place mode");
            app.SendLocal("place.enter"); // Enter/exit commands are idempotent.
            Click(hud, "Pointer Button");
            Assert.IsFalse(app.Hand.PointerEnabled);
            Click(hud, "Pointer Button");
            Assert.IsFalse(app.Hand.IsTracked, "Toggling clears stale hand data");
            lastReply = null;
            app.Execute(new VisualizerCommand { command = "screen.place" }, r => lastReply = r);
            Assert.IsNotNull(lastReply);
            Assert.IsFalse(lastReply.ok);
            Assert.AreEqual(0, app.CubeCount);
            app.SendLocal("pointer.off");
            // The simulator only scans after its input camera moves, just as a user scans with a phone.
            var simulatedCamera = Object.FindAnyObjectByType<SimulationCameraPoseProvider>();
            Assert.IsNotNull(simulatedCamera);
            var startPosition = simulatedCamera.transform.localPosition;
            var startRotation = simulatedCamera.transform.localRotation;
            float scanStarted = Time.realtimeSinceStartup;
            while (!app.HasTarget && Time.realtimeSinceStartup - scanStarted < 20)
            {
                float t = Time.realtimeSinceStartup - scanStarted;
                simulatedCamera.transform.localPosition = startPosition + startRotation * new Vector3(Mathf.Sin(t * 3) * 0.2f, 0, 0);
                simulatedCamera.transform.localRotation = startRotation * Quaternion.Euler(35 + Mathf.Sin(t * 2) * 8, Mathf.Sin(t * 3) * 16, 0);
                yield return null;
            }
            Assert.IsTrue(app.HasTarget, "XR Simulation did not discover a surface under the crosshair. Surfaces: " + app.SurfaceCount);
            var planeManager = app.GetComponentInChildren<ARPlaneManager>();
            Assert.AreEqual("ARFeatheredOcclusionPlane", planeManager.planePrefab.name);
            // A plane can be raycastable one frame before its visualization has built its mesh.
            float meshDeadline = Time.realtimeSinceStartup + 3;
            while (!TrackedPlaneMeshesReady(planeManager) && Time.realtimeSinceStartup < meshDeadline) yield return null;
            AssertSurfaceVisibility(planeManager, true);
            hud.RefreshStatus();
            Assert.IsTrue(hud.transform.Find("Safe Area/Controls/Place Screen Button").GetComponent<Button>().interactable);
            Click(hud, "Place Screen Button");
            float anchorDeadline = Time.realtimeSinceStartup + 5;
            while (app.IsPlacing && Time.realtimeSinceStartup < anchorDeadline) yield return null;
            Assert.IsFalse(app.IsPlacing, "Anchor creation did not complete");
            Assert.AreEqual(1, app.CubeCount);
            var anchor = app.GetComponentInChildren<ARAnchor>();
            Assert.IsNotNull(anchor);
            var cube = anchor.GetComponentInChildren<MeshRenderer>();
            Assert.That(Vector3.Distance(new Vector3(0.16f, 0.09f, 0.005f), cube.transform.localScale), Is.LessThan(0.00001f));
            Assert.AreEqual(Vector3.back * 0.0025f, cube.transform.localPosition);
            var screen = anchor.GetComponentInChildren<ScreenSurface>();
            Assert.IsNotNull(screen);
            var hudToggle = hud.GetComponentInChildren<HUDVisibilityToggle>().GetComponent<Button>();
            hudToggle.onClick.Invoke();
            Assert.IsFalse(hud.IsHUDVisible);
            Assert.IsTrue(hudToggle.isActiveAndEnabled && app.Hand.isActiveAndEnabled && app.Communication.isActiveAndEnabled);
            ScreenInteractionChecks.Exercise(app, screen);
            Assert.IsFalse(hud.IsHUDVisible, "Hand input and Wi-Fi commands must work while the HUD stays hidden");
            hudToggle.onClick.Invoke();
            Assert.IsTrue(hud.IsHUDVisible);
            yield return GoggleInteractionChecks.Exercise(app, hud, screen);
            for (int i = 0; i < 3; ++i) yield return null;
            hud.RefreshStatus();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                yield return OcclusionRenderChecks.ScreensRespectRealWorldDepth(app, cube);
                yield return null;
                hud.RefreshStatus();
                var canvas = app.GetComponentInChildren<Canvas>();
                var rt = new RenderTexture(1280, 720, 24);
                rt.Create();
                app.ARCamera.targetTexture = rt;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = app.ARCamera;
                canvas.planeDistance = 0.2f;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                canvas.scaleFactor = 1;
                Canvas.ForceUpdateCanvases();
                yield return null;
                app.ARCamera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var screenshot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                screenshot.Apply();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/ARVisualizer-screen-HUD.png", screenshot.EncodeToPNG());
                hudToggle.onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                yield return null;
                app.ARCamera.Render();
                RenderTexture.active = rt;
                screenshot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                screenshot.Apply();
                File.WriteAllBytes("Logs/ARVisualizer-hidden-HUD.png", screenshot.EncodeToPNG());
                hudToggle.onClick.Invoke();
                RenderTexture.active = previous;
                app.ARCamera.targetTexture = null;
                Object.Destroy(screenshot);
                Object.Destroy(rt);
            }
            app.Execute(new VisualizerCommand { command = "place.exit" }, r => lastReply = r);
            Assert.IsTrue(lastReply.ok && !lastReply.placeModeEnabled);
            Assert.IsFalse(app.PlaceModeEnabled);
            Assert.IsTrue(planeManager.enabled, "Tracking remains active outside place mode");
            AssertSurfaceVisibility(planeManager, false);
            Assert.IsFalse(cube.forceRenderingOff, "Existing cube stays visible");
            Assert.AreEqual(1, app.CubeCount);
            Assert.IsFalse(app.transform.Find("Screen footprint").GetComponent<LineRenderer>().enabled);
            app.Execute(new VisualizerCommand { command = "screen.place" }, r => lastReply = r);
            Assert.IsFalse(lastReply.ok);
            Assert.AreEqual(1, app.CubeCount);
            app.SendLocal("place.exit");
            for (int i = 0; i < 3; ++i) yield return null;
            AssertSurfaceVisibility(planeManager, false);
            app.SendLocal("place.enter");
            AssertSurfaceVisibility(planeManager, true);
            Assert.AreEqual(1, app.CubeCount);
            Click(hud, "Undo Button");
            Assert.AreEqual(0, app.CubeCount);
            app.SendLocal("screens.clear");
            Assert.AreEqual(0, app.CubeCount);
            // Stopping with the floating HUD active must also clean up safely.
            app.SendLocal("goggle.enter");
            Assert.IsTrue(app.GoggleModeEnabled);
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static void Click(VisualizerHUD hud, string name) =>
            hud.transform.Find("Safe Area/Controls/" + name).GetComponent<Button>().onClick.Invoke();

        static bool TrackedPlaneMeshesReady(ARPlaneManager manager)
        {
            foreach (var plane in manager.trackables)
            {
                if (plane.trackingState != UnityEngine.XR.ARSubsystems.TrackingState.Tracking || plane.subsumedBy != null) continue;
                var mesh = plane.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) return false;
            }
            return true;
        }

        static void AssertSurfaceVisibility(ARPlaneManager manager, bool placeMode)
        {
            int visible = 0;
            foreach (var plane in manager.trackables)
            {
                var renderer = plane.GetComponent<MeshRenderer>();
                Assert.IsNotNull(renderer, "Sample plane must have a filled mesh renderer");
                bool tracked = plane.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Tracking && plane.subsumedBy == null;
                Assert.AreEqual(!(placeMode && tracked), renderer.forceRenderingOff);
                if (placeMode && tracked)
                {
                    var mesh = plane.GetComponent<MeshFilter>().sharedMesh;
                    Assert.IsNotNull(mesh, "The tracked sample plane must finish building its mesh");
                    Assert.Greater(mesh.vertexCount, 0);
                    Assert.IsTrue(renderer.sharedMaterial.HasProperty("_PlaneAlpha"));
                    ++visible;
                }
            }
            if (placeMode) Assert.Greater(visible, 0);
        }
    }
}
