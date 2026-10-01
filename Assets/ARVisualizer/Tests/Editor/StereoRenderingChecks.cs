using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    internal static class StereoRenderingChecks
    {
        public static IEnumerator Exercise(ARVisualizerApp app, VisualizerHUD hud)
        {
            yield return ExerciseProbe(app, hud, false);
            yield return ExerciseProbe(app, hud, true);
        }

        static IEnumerator ExerciseProbe(ARVisualizerApp app, VisualizerHUD hud, bool menu)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var rig = app.GoggleHUD.Stereo;
            var occlusion = app.ARCamera.GetComponent<AROcclusionManager>();
            var background = app.ARCamera.GetComponent<ARCameraBackground>();
            float deadline = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Application.isBatchMode) occlusion.SendMessage("OnBeforeRender", SendMessageOptions.RequireReceiver);
                if (background.material != null && background.material.IsKeywordEnabled("SIMULATION_OCCLUSION_ENABLED")) break;
                yield return null;
            }
            Assert.IsTrue(background.material.IsKeywordEnabled("SIMULATION_OCCLUSION_ENABLED"));
            var canvas = hud.GetComponent<Canvas>();
            var settings = new SerializedObject(app);
            int originalPreference = settings.FindProperty("occlusionPreference").intValue;
            var material = menu ? null : new Material(Resources.Load<Shader>("VisualizerScreen"));
            if (material != null) material.SetColor("_BaseColor", Color.magenta);
            var probe = menu ? new GameObject("Stereo menu probe", typeof(RectTransform), typeof(Canvas), typeof(Image))
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            probe.name = "Stereo depth probe";
            if (menu)
            {
                probe.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                probe.GetComponent<Canvas>().worldCamera = app.ARCamera;
                ((RectTransform)probe.transform).sizeDelta = Vector2.one;
                var graphic = probe.GetComponent<Image>(); graphic.color = Color.magenta;
                graphic.material = app.ScreenApps.transform.Find("Photo library").GetComponent<Image>().material;
            }
            else probe.GetComponent<Renderer>().sharedMaterial = material;
            try
            {
                canvas.enabled = false;
                probe.transform.SetPositionAndRotation(app.ARCamera.transform.TransformPoint(new Vector3(0.05f, 0.04f, 0.3f)), app.ARCamera.transform.rotation);
                probe.transform.localScale = new Vector3(0.05f, 0.04f, 0.005f);
                Canvas.ForceUpdateCanvases();
                rig.PrepareFrame();
                app.ARCamera.Render(); rig.LeftEye.Render(); rig.RightEye.Render();
                var left = FindProbe(rig.LeftEye.targetTexture);
                var right = FindProbe(rig.RightEye.targetTexture);
                Assert.Greater(left.z, 10, "Near geometry must render in the left eye");
                Assert.Greater(right.z, 10, "Near geometry must render in the right eye");
                Assert.Greater(left.x, right.x + 5, "The eye images must have real geometric disparity");
                var expectedLeft = rig.LeftEye.WorldToViewportPoint(probe.transform.position);
                Assert.That(left.x / rig.LeftEye.targetTexture.width, Is.EqualTo(expectedLeft.x).Within(0.02f));
                Assert.That(left.y / rig.LeftEye.targetTexture.height, Is.EqualTo(expectedLeft.y).Within(0.02f));
                probe.transform.position = app.ARCamera.transform.position + app.ARCamera.transform.forward * 15;
                probe.transform.localScale = new Vector3(3, 2, 0.005f);
                app.ARCamera.Render(); rig.LeftEye.Render(); rig.RightEye.Render();
                float hiddenLeft = FindProbe(rig.LeftEye.targetTexture).z;
                float hiddenRight = FindProbe(rig.RightEye.targetTexture).z;
                settings.FindProperty("occlusionPreference").intValue = (int)OcclusionPreferenceMode.NoOcclusion;
                settings.ApplyModifiedPropertiesWithoutUndo();
                yield return null;
                yield return null;
                app.ARCamera.Render(); rig.LeftEye.Render(); rig.RightEye.Render();
                float visibleLeft = FindProbe(rig.LeftEye.targetTexture).z;
                float visibleRight = FindProbe(rig.RightEye.targetTexture).z;
                Assert.Greater(visibleLeft, 100, "The far probe must be visible when depth is disabled");
                Assert.Greater(visibleRight, 100);
                // Reprojected single-camera depth can leave small disocclusions at depth boundaries.
                Assert.Less(hiddenLeft, visibleLeft * 0.02f, "Real-world depth must hide distant geometry in the left eye");
                Assert.Less(hiddenRight, visibleRight * 0.02f, "Real-world depth must hide distant geometry in the right eye");
            }
            finally
            {
                canvas.enabled = true;
                settings.Update();
                settings.FindProperty("occlusionPreference").intValue = originalPreference;
                settings.ApplyModifiedPropertiesWithoutUndo();
                probe.SetActive(false); Object.Destroy(probe); Object.Destroy(material);
            }
            yield return null;
        }

        static Vector3 FindProbe(RenderTexture target)
        {
            var active = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                var pixels = texture.GetPixels32();
                float x = 0, y = 0; int count = 0;
                for (int i = 0; i < pixels.Length; ++i)
                {
                    var c = pixels[i];
                    if (c.r > 200 && c.b > 200 && c.g < 70) { x += i % target.width; y += i / target.width; ++count; }
                }
                return new Vector3(count > 0 ? x / count : 0, count > 0 ? y / count : 0, count);
            }
            finally { RenderTexture.active = active; Object.Destroy(texture); }
        }
    }
}
