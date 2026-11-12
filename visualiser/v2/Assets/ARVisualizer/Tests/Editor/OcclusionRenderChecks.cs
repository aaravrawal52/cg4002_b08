using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    internal static class OcclusionRenderChecks
    {
        // XR Simulation supplies environment depth, so this checks the complete background -> cube
        // depth-buffer path. Human segmentation and its edge quality still require a physical iPhone.
        public static IEnumerator ScreensRespectRealWorldDepth(ARVisualizerApp app, MeshRenderer placedCube)
        {
            var camera = app.ARCamera;
            var background = camera.GetComponent<ARCameraBackground>();
            var occlusion = camera.GetComponent<AROcclusionManager>();
            Assert.IsNotNull(occlusion.subsystem);
            Assert.IsTrue(occlusion.subsystem.running);
            float deadline = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < deadline)
            {
                // An unattended Editor has no Game view render loop to invoke this lifecycle hook.
                // Use the manager's real frame path so the background gets its textures and keywords.
                if (Application.isBatchMode) occlusion.SendMessage("OnBeforeRender", SendMessageOptions.RequireReceiver);
                if (occlusion.TryGetEnvironmentDepthTexture(out var depth) && depth != null &&
                    background.material != null && background.material.IsKeywordEnabled("SIMULATION_OCCLUSION_ENABLED")) break;
                yield return null;
            }
            Assert.IsTrue(occlusion.TryGetEnvironmentDepthTexture(out var depthTexture) && depthTexture != null,
                "The simulation must deliver real-world depth to the AR camera");
            Assert.IsTrue(background.material.IsKeywordEnabled("SIMULATION_OCCLUSION_ENABLED"),
                "The camera background must consume the occlusion manager's frames");

            var canvas = app.GetComponentInChildren<Canvas>();
            bool canvasWasEnabled = canvas.enabled;
            bool cubeWasEnabled = placedCube.enabled;
            bool wasPlaceMode = app.PlaceModeEnabled;
            var settings = new SerializedObject(app);
            int originalPreference = settings.FindProperty("occlusionPreference").intValue;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(320, 180, 24);
            var sample = new Texture2D(8, 8, TextureFormat.RGB24, false);
            var material = new Material(Resources.Load<Shader>("VisualizerScreen"));
            material.SetColor("_BaseColor", Color.magenta);
            var probe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            probe.name = "Depth test screen";
            probe.GetComponent<Renderer>().sharedMaterial = material;
            probe.transform.SetParent(camera.transform, false);
            var menuProbe = new GameObject("Depth test menu", typeof(RectTransform), typeof(Canvas), typeof(Image));
            var menuCanvas = menuProbe.GetComponent<Canvas>(); menuCanvas.renderMode = RenderMode.WorldSpace; menuCanvas.worldCamera = camera;
            var menuRect = (RectTransform)menuProbe.transform; menuRect.sizeDelta = Vector2.one;
            menuRect.SetParent(camera.transform, false);
            var menuGraphic = menuProbe.GetComponent<Image>(); menuGraphic.color = Color.magenta;
            menuGraphic.material = app.ScreenApps.transform.Find("Photo library").GetComponent<Image>().material;
            Assert.AreEqual("ARVisualizer/Occluded Screen UI", menuGraphic.material.shader.name);
            menuProbe.SetActive(false);
            try
            {
                target.Create();
                camera.targetTexture = target;
                canvas.enabled = false;
                placedCube.enabled = false;
                app.SendLocal("place.exit");
                probe.SetActive(false);
                yield return null;
                Color empty = SampleCentre(camera, target, sample);

                probe.SetActive(true);
                PositionProbe(probe, 15);
                Color behindSurface = SampleCentre(camera, target, sample);
                Assert.Less(Difference(empty, behindSurface), 0.08f,
                    "A cube behind the scanned environment should be hidden by real-world depth");

                PositionProbe(probe, 0.3f);
                Color inFront = SampleCentre(camera, target, sample);
                Assert.Greater(Difference(empty, inFront), 0.25f,
                    "A cube closer than the scanned environment must remain visible");

                probe.SetActive(false); menuProbe.SetActive(true);
                PositionProbe(menuProbe, 15); Canvas.ForceUpdateCanvases();
                Assert.Less(Difference(empty, SampleCentre(camera, target, sample)), 0.08f,
                    "The library material must be occluded by nearer real-world depth");
                PositionProbe(menuProbe, 0.3f); Canvas.ForceUpdateCanvases();
                Assert.Greater(Difference(empty, SampleCentre(camera, target, sample)), 0.25f,
                    "The library must remain visible in front of real-world depth");
                menuProbe.SetActive(false); probe.SetActive(true);

                // Exercise the same setting exposed in the Inspector; depth must really switch off.
                settings.FindProperty("occlusionPreference").intValue = (int)OcclusionPreferenceMode.NoOcclusion;
                settings.ApplyModifiedPropertiesWithoutUndo();
                yield return null;
                yield return null;
                Assert.IsFalse(occlusion.enabled);
                PositionProbe(probe, 15);
                Color withoutDepth = SampleCentre(camera, target, sample);
                Assert.Greater(Difference(empty, withoutDepth), 0.25f,
                    "The same distant cube should draw over the camera image when occlusion is disabled");
                probe.SetActive(false); menuProbe.SetActive(true); PositionProbe(menuProbe, 15); Canvas.ForceUpdateCanvases();
                Assert.Greater(Difference(empty, SampleCentre(camera, target, sample)), 0.25f,
                    "The distant library becomes visible when real-world occlusion is disabled");
            }
            finally
            {
                // Destroy is deferred; hide the temporary probe before subsequent screenshots.
                probe.SetActive(false);
                menuProbe.SetActive(false); Object.Destroy(menuProbe);
                settings.Update();
                settings.FindProperty("occlusionPreference").intValue = originalPreference;
                settings.ApplyModifiedPropertiesWithoutUndo();
                canvas.enabled = canvasWasEnabled;
                placedCube.enabled = cubeWasEnabled;
                if (wasPlaceMode) app.SendLocal("place.enter");
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.Destroy(probe);
                Object.Destroy(material);
                Object.Destroy(sample);
                target.Release();
                Object.Destroy(target);
            }
        }

        static void PositionProbe(GameObject probe, float depth)
        {
            probe.transform.localPosition = Vector3.forward * depth;
            probe.transform.localScale = new Vector3(depth * 0.16f, depth * 0.09f, 1);
        }

        static Color SampleCentre(Camera camera, RenderTexture target, Texture2D sample)
        {
            camera.Render();
            RenderTexture.active = target;
            sample.ReadPixels(new Rect(target.width / 2 - 4, target.height / 2 - 4, 8, 8), 0, 0);
            sample.Apply();
            var pixels = sample.GetPixels();
            Color mean = Color.clear;
            foreach (var pixel in pixels) mean += pixel;
            return mean / pixels.Length;
        }

        static float Difference(Color a, Color b) => new Vector3(a.r - b.r, a.g - b.g, a.b - b.b).magnitude;
    }
}
