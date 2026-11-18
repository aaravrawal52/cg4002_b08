using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class OcclusionEdgeTests
    {
        GameObject cameraObject, background, screen;
        Camera camera;
        Material backgroundMaterial, screenMaterial;
        Texture2D depth, luma, chroma, readback;
        RenderTexture target;
        const int Width = 128, Height = 64;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(ShaderUtil.ShaderHasError(Resources.Load<Shader>("ARKitTightOcclusion")));
            cameraObject = new GameObject("Depth edge camera", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 20; camera.fieldOfView = 60; camera.aspect = 2;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            target = new RenderTexture(Width, Height, 24); target.Create(); camera.targetTexture = target;
            readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            backgroundMaterial = new Material(Resources.Load<Shader>("ARKitTightOcclusion"));
            backgroundMaterial.EnableKeyword("ARKIT_ENVIRONMENT_DEPTH_ENABLED");
            backgroundMaterial.SetMatrix("_UnityDisplayTransform", Matrix4x4.identity);
            backgroundMaterial.SetFloat("_UnityCameraForwardScale", 1);
            luma = new Texture2D(1, 1, TextureFormat.RGBA32, false, true); luma.SetPixel(0, 0, Color.gray); luma.Apply();
            chroma = new Texture2D(1, 1, TextureFormat.RGBA32, false, true); chroma.SetPixel(0, 0, Color.gray); chroma.Apply();
            backgroundMaterial.SetTexture("_textureY", luma); backgroundMaterial.SetTexture("_textureCbCr", chroma);
            depth = new Texture2D(32, 16, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            backgroundMaterial.SetTexture("_EnvironmentDepth", depth);
            background = GameObject.CreatePrimitive(PrimitiveType.Quad); background.layer = 31;
            background.transform.position = Vector3.forward * 5;
            float height = 10 * Mathf.Tan(30 * Mathf.Deg2Rad);
            background.transform.localScale = new Vector3(height * 2, height, 1);
            background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
            screenMaterial = new Material(Resources.Load<Shader>("VisualizerScreen")); screenMaterial.SetColor("_BaseColor", Color.magenta);
            screen = GameObject.CreatePrimitive(PrimitiveType.Quad); screen.layer = 31;
            screen.transform.position = Vector3.forward;
            screen.transform.localScale = new Vector3(4, 4, 1);
            screen.GetComponent<Renderer>().sharedMaterial = screenMaterial;
        }

        void FillDepth(float left, float right)
        {
            var pixels = new float[32 * 16];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++) pixels[y * 32 + x] = x < 16 ? left : right;
            depth.SetPixelData(pixels, 0); depth.Apply();
        }
        int VisiblePixels(float inset, float bias)
        {
            backgroundMaterial.SetFloat("_EdgeInsetPixels", inset);
            backgroundMaterial.SetFloat("_DepthBiasMetres", bias);
            camera.Render();
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); readback.Apply();
                int visible = 0;
                foreach (var pixel in readback.GetPixels32()) if (pixel.r > 200 && pixel.b > 200 && pixel.g < 70) visible++;
                return visible;
            }
            finally { RenderTexture.active = previous; }
        }

        [Test]
        public void InsetShrinksTheObstacleEdgeWithoutRemovingItsInterior()
        {
            FillDepth(0.5f, 2);
            int ordinary = VisiblePixels(0, 0);
            int inset = VisiblePixels(1, 0);
            Assert.That(ordinary, Is.InRange(Width * Height / 2 - 150, Width * Height / 2 + 150));
            Assert.That(inset - ordinary, Is.InRange(150, 350), "One low-resolution depth pixel retracts the foreground edge");
            FillDepth(0.5f, 0.5f);
            Assert.Less(VisiblePixels(2, 0), 10, "The interior of a nearer obstacle still hides the screen");
        }

        [Test]
        public void MetricToleranceAndMissingDepthDoNotCreateFalseObstacles()
        {
            FillDepth(0.995f, 0.995f);
            Assert.Less(VisiblePixels(0, 0), 10);
            Assert.Greater(VisiblePixels(0, 0.01f), Width * Height - 10, "A one-centimetre tolerance is applied in metres");
            FillDepth(0, 0);
            Assert.Greater(VisiblePixels(1, 0.005f), Width * Height - 10, "Unknown depth must not hide content");
            FillDepth(0.5f, 0.5f);
            backgroundMaterial.DisableKeyword("ARKIT_ENVIRONMENT_DEPTH_ENABLED");
            Assert.Greater(VisiblePixels(1, 0.005f), Width * Height - 10, "No-occlusion mode must leave screens visible");
        }

        [Test]
        public void HumanFallbackStillRequiresBothStencilAndDepth()
        {
            FillDepth(0.5f, 2);
            backgroundMaterial.DisableKeyword("ARKIT_ENVIRONMENT_DEPTH_ENABLED");
            backgroundMaterial.EnableKeyword("ARKIT_HUMAN_SEGMENTATION_ENABLED");
            backgroundMaterial.SetTexture("_HumanDepth", depth);
            backgroundMaterial.SetTexture("_HumanStencil", Texture2D.whiteTexture);
            Assert.That(VisiblePixels(0, 0), Is.InRange(Width * Height / 2 - 150, Width * Height / 2 + 150));
            backgroundMaterial.SetTexture("_HumanStencil", Texture2D.blackTexture);
            Assert.Greater(VisiblePixels(1, 0.005f), Width * Height - 10);
        }

        [TearDown]
        public void TearDown()
        {
            if (camera != null) camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(background); Object.DestroyImmediate(screen);
            Object.DestroyImmediate(backgroundMaterial); Object.DestroyImmediate(screenMaterial);
            Object.DestroyImmediate(depth); Object.DestroyImmediate(luma); Object.DestroyImmediate(chroma); Object.DestroyImmediate(readback);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
        }
    }
}
