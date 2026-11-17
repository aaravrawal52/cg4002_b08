using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    public sealed class HUDStyleTests
    {
        const string HUDPath = "Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab";
        static readonly string[] Prefabs =
        {
            HUDPath, "Assets/ARVisualizer/Prefabs/PhoneControls.prefab",
            "Assets/ARVisualizer/Resources/ScreenAppMenu.prefab", "Assets/ARVisualizer/Resources/ScreenAppView.prefab"
        };

        static Sprite Squircle() => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/ARVisualizer/UI/Squircle.png");

        [Test]
        public void SquircleSpriteIsNineSliced()
        {
            var sprite = Squircle();
            Assert.IsNotNull(sprite, "Run Tools > AR Visualizer > Apply iOS HUD style");
            Assert.AreEqual(new Vector4(48, 48, 48, 48), sprite.border);
            var texture = sprite.texture;
            var pixels = AssetDatabase.GetAssetPath(texture);
            var readable = new Texture2D(2, 2);
            Assert.IsTrue(readable.LoadImage(File.ReadAllBytes(pixels)));
            Assert.AreEqual(0, readable.GetPixel(0, 0).a, 0.01f, "Corners are transparent");
            Assert.AreEqual(1, readable.GetPixel(64, 64).a, 0.01f, "The centre is solid");
            Assert.AreEqual(1, readable.GetPixel(0, 64).a, 0.01f, "Straight edges reach the slice border");
            // A superellipse corner keeps more of the corner than a circle of the same size.
            Assert.Greater(readable.GetPixel(10, 10).a, 0.5f);
            Object.DestroyImmediate(readable);
        }

        [Test]
        public void EveryHUDPrefabUsesTheSystemTypeface()
        {
            foreach (var path in Prefabs)
            {
                var typeface = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<HUDTypeface>();
                Assert.IsNotNull(typeface, path);
                Assert.GreaterOrEqual(typeface.StyleVersion, 1, path);
            }
        }

        [Test]
        public void PhoneHUDTextIsReadableAndPanelsAreSquircles()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HUDPath);
            foreach (var text in hud.GetComponentsInChildren<Text>(true))
            {
                Assert.GreaterOrEqual(text.fontSize, 13, text.name + " / " + text.text);
                if (text.resizeTextForBestFit) Assert.GreaterOrEqual(text.resizeTextMinSize, 11, text.name);
            }
            var sprite = Squircle();
            foreach (var image in hud.GetComponentsInChildren<Image>(true))
            {
                var rect = image.rectTransform;
                bool overlay = !Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x) && !Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y);
                if (overlay || image.GetComponentsInParent<Transform>(true).Any(t => t.name == "Aim Position")) continue;
                Assert.AreEqual(sprite, image.sprite, image.name);
                Assert.AreEqual(Image.Type.Sliced, image.type, image.name);
            }
            var bottom = (RectTransform)hud.transform.Find("Safe Area/Controls/Pointer Button");
            Assert.Less(bottom.sizeDelta.x * bottom.sizeDelta.y, 136 * 44, "Buttons are more compact than before");
            Assert.That(bottom.sizeDelta.y, Is.InRange(40, 48), "But still easy to tap");
        }

        [TestCase("POINTER  ON", "Pointer On")]
        [TestCase("CRUMPLE AND TOSS", "Crumple and Toss")]
        [TestCase("HUD", "HUD")]
        [TestCase("WI-FI", "Wi-Fi")]
        public void ButtonCaptionsUseTitleCase(string caption, string expected) =>
            Assert.AreEqual(expected, HUDTypeface.TitleCase(caption));

        [Test]
        public void TypefaceReplacesOnlyTheBuiltInFontAndRendersAPreview()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HUDPath));
            var cameraObject = new GameObject("HUD style preview", typeof(Camera));
            var target = new RenderTexture(1280, 720, 24); target.Create();
            var capture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var custom = root.transform.Find("Safe Area/Top Bar/Brand").GetComponent<Text>();
                var customFont = Font.CreateDynamicFontFromOSFont("Courier New", 16);
                custom.font = customFont;
                var chosen = root.GetComponent<HUDTypeface>().Apply();
                Assert.AreSame(customFont, custom.font, "A deliberately chosen font is kept");
                if (chosen != null)
                    foreach (var text in root.GetComponentsInChildren<Text>(true))
                        if (text != custom) Assert.AreSame(chosen, text.font, text.name);
                custom.font = chosen != null ? chosen : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                root.GetComponent<VisualizerHUD>().Preview(VisualizerHUD.PreviewState.ReadyToPlace);
                root.transform.Find("Safe Area/Normal Actions/Screen Settings Panel").gameObject.SetActive(true);
                var camera = cameraObject.GetComponent<Camera>(); camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.32f, 0.35f, 0.38f);
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); capture.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/ARVisualizer-ios-HUD.png", capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(capture);
                target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
