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
            // These baseline tests isolate the background's tolerance from mounted-content tolerance.
            screenMaterial.SetFloat("_SurfaceDepthTolerance", 0);
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

        [TestCase("VisualizerScreen")]
        [TestCase("ScreenMedia")]
        [TestCase("OccludedScreenUI")]
        public void MountedContentSurvivesApproachingItsWallButNotNearerObstacles(string shaderName)
        {
            var shader = Resources.Load<Shader>(shaderName);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
            Object.DestroyImmediate(screenMaterial);
            screenMaterial = new Material(shader);
            Assert.That(screenMaterial.GetFloat("_SurfaceDepthTolerance"), Is.EqualTo(0.02f).Within(0.00001f));
            var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, Color.magenta); texture.Apply();
            var mesh = Object.Instantiate(screen.GetComponent<MeshFilter>().sharedMesh);
            var colours = new Color[mesh.vertexCount]; System.Array.Fill(colours, Color.white); mesh.colors = colours;
            screen.GetComponent<MeshFilter>().sharedMesh = mesh;
            screen.GetComponent<Renderer>().sharedMaterial = screenMaterial;
            if (screenMaterial.HasProperty("_BaseColor")) screenMaterial.SetColor("_BaseColor", Color.magenta);
            if (screenMaterial.HasProperty("_MainTex")) screenMaterial.SetTexture("_MainTex", texture);
            screen.transform.position = Vector3.forward * 2;
            screen.transform.localScale = new Vector3(8, 8, 1);
            // Overscan the synthetic background so shifted eye projections cannot expose
            // an uncovered border and mistake it for a failure of depth occlusion.
            background.transform.localScale *= 2;
            try
            {
                // The mounting plane is 12 mm behind the measured wall. Move the camera,
                // keeping both the wall-mounted screen and its physical dimensions fixed.
                foreach (float distance in new[] { 2f, 0.75f, 0.3f })
                foreach (float eyeOffset in new[] { 0f, -0.032f, 0.032f })
                {
                    camera.transform.position = new Vector3(eyeOffset, 0, 2 - distance);
                    var projection = Matrix4x4.Perspective(60, 2, 0.1f, 20);
                    projection.m02 = eyeOffset; // Exercise the asymmetric projections used by the eye views.
                    camera.projectionMatrix = projection;
                    FillDepth(distance - 0.012f, distance - 0.012f);
                    screenMaterial.SetFloat("_SurfaceDepthTolerance", 0);
                    Assert.Less(VisiblePixels(0, 0.005f), 10, "Reproduce the wall hiding its own mounted screen");
                    screenMaterial.SetFloat("_SurfaceDepthTolerance", 0.02f);
                    Assert.Greater(VisiblePixels(0, 0.005f), Width * Height - 10, shaderName + " must remain visible when approached");
                    FillDepth(distance - 0.012f, distance + 0.006f);
                    Assert.Greater(VisiblePixels(0, 0.005f), Width * Height - 10, "Small rough-wall variations must not cut holes in the screen");
                    FillDepth(distance - 0.08f, distance - 0.08f);
                    Assert.Less(VisiblePixels(0, 0.005f), 10, "An obstacle 8 cm in front must still occlude it");
                }
                // Render-only correction must not change projected size or location.
                camera.transform.position = Vector3.zero; camera.ResetProjectionMatrix();
                screen.transform.localScale = new Vector3(0.64f, 0.36f, 1);
                FillDepth(0, 0);
                screenMaterial.SetFloat("_SurfaceDepthTolerance", 0); int ordinary = VisiblePixels(0, 0);
                screenMaterial.SetFloat("_SurfaceDepthTolerance", 0.02f);
                Assert.AreEqual(ordinary, VisiblePixels(0, 0));
                Assert.Greater(ordinary, 50);
            }
            finally { Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh); }
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
