using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class StereoAspectTests
    {
        [TestCase(2796, 1290)] // iPhone 14 Pro Max, either landscape orientation.
        [TestCase(1920, 1080)]
        [TestCase(1024, 768)]
        [TestCase(3200, 1000)]
        public void EyeImagesFitTheirDisplayHalvesWithoutStretching(int width, int height)
        {
            var viewport = StereoGoggles.EyeViewport((float)width / height);
            Assert.That(viewport.width * width * 0.5f / (viewport.height * height), Is.EqualTo(4f / 3).Within(0.0001f));
            Assert.AreEqual(Vector2.one * 0.5f, viewport.center);
            Assert.That(viewport.xMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(viewport.yMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(viewport.xMax, Is.LessThanOrEqualTo(1));
            Assert.That(viewport.yMax, Is.LessThanOrEqualTo(1));
        }

        internal static void CheckCaptureAndEdges(ARVisualizerApp app, VisualizerHUD hud)
        {
            var rig = app.GoggleHUD.Stereo;
            rig.PrepareFrame();
            foreach (var texture in new[] { rig.SourceColour, rig.SourceDepth, rig.LeftEye.targetTexture, rig.RightEye.targetTexture })
                Assert.That((float)texture.width / texture.height, Is.EqualTo(4f / 3).Within(0.0001f));
            Assert.AreEqual(rig.CaptureSize, app.Hand.ViewportSize, "Vision coordinates must use the same uncropped viewport as video");
            var rect = (RectTransform)hud.transform;
            Assert.That(rect.rect.width / rect.rect.height, Is.EqualTo(4f / 3).Within(0.0001f));
            Assert.AreEqual(app.ARCamera.projectionMatrix, rig.LeftEye.projectionMatrix, "Default eye FOV must preserve the camera frame's full vertical extent");
            Assert.AreEqual(app.ARCamera.projectionMatrix, rig.RightEye.projectionMatrix);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;

            // A thin band at each edge of a 4:3 camera image would disappear under a 16:9 crop.
            // Exercise the actual stereo reprojection with no depth or scene geometry.
            var pattern = new Texture2D(128, 96, TextureFormat.RGBA32, false);
            int leftMask = rig.LeftEye.cullingMask, rightMask = rig.RightEye.cullingMask;
            var active = RenderTexture.active;
            try
            {
                for (int y = 0; y < pattern.height; ++y)
                    for (int x = 0; x < pattern.width; ++x)
                        pattern.SetPixel(x, y, y < 8 ? Color.red : y >= 88 ? Color.green : Color.blue);
                pattern.Apply();
                Graphics.Blit(pattern, rig.SourceColour);
                RenderTexture.active = rig.SourceDepth; GL.Clear(false, true, Color.clear);
                rig.LeftEye.cullingMask = rig.RightEye.cullingMask = 0;
                rig.LeftEye.Render(); rig.RightEye.Render();
                foreach (var eye in new[] { rig.LeftEye, rig.RightEye })
                {
                    var pixels = new Texture2D(eye.targetTexture.width, eye.targetTexture.height, TextureFormat.RGB24, false);
                    try
                    {
                        RenderTexture.active = eye.targetTexture;
                        pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
                        var bottom = pixels.GetPixel(pixels.width / 2, Mathf.RoundToInt(pixels.height * 0.03f));
                        var top = pixels.GetPixel(pixels.width / 2, Mathf.RoundToInt(pixels.height * 0.97f));
                        Assert.Greater(bottom.r, 0.8f, "The bottom of the camera frame must survive stereo rendering");
                        Assert.Less(bottom.g + bottom.b, 0.1f);
                        Assert.Greater(top.g, 0.8f, "The top of the camera frame must survive stereo rendering");
                        Assert.Less(top.r + top.b, 0.1f);
                    }
                    finally { Object.Destroy(pixels); }
                }
            }
            finally
            {
                RenderTexture.active = active;
                rig.LeftEye.cullingMask = leftMask; rig.RightEye.cullingMask = rightMask;
                Object.Destroy(pattern);
                app.ARCamera.Render(); // Restore real camera colour and depth for the remaining checks.
            }
        }
    }
}
