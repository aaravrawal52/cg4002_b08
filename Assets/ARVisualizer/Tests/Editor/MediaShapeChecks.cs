using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Video;

namespace ARVisualizer.Tests
{
    internal static class MediaShapeChecks
    {
        public static IEnumerator Exercise(ARVisualizerApp app, ScreenSurface screen)
        {
            app.ScreenApps.Close();
            var media = screen.GetComponent<ScreenMedia>();
            float width = screen.Width;
            var position = screen.transform.position; var rotation = screen.transform.rotation;
            foreach (var size in new[] { new Vector2Int(48, 64), new Vector2Int(48, 48), new Vector2Int(96, 40) })
            {
                var photo = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                var pixels = new Color[size.x * size.y];
                for (int i = 0; i < pixels.Length; ++i) pixels[i] = Color.cyan;
                photo.SetPixels(pixels); photo.Apply();
                Assert.IsTrue(media.ShowPhoto(photo));
                Assert.That(screen.Width, Is.EqualTo(width).Within(0.00001f));
                Assert.That(screen.Height, Is.EqualTo(width * size.y / size.x).Within(0.00001f));
                Assert.AreEqual(position, screen.transform.position); Assert.AreEqual(rotation, screen.transform.rotation);
                Assert.IsTrue(screen.Resize(1));
                Assert.That(screen.Width / screen.Height, Is.EqualTo((float)size.x / size.y).Within(0.00001f));
                screen.Resize(-1);
                if (size.x < size.y) AssertPhotoFillsFace(screen);
            }

            var library = app.ScreenApps.GetComponentInChildren<PhotoLibrary>();
            string path = Path.Combine(library.CacheDirectory, "portrait-shape-test.mp4");
            File.Copy("Assets/ARVisualizer/Tests/Editor/MediaPortraitTest.mp4", path, true);
            Assert.IsTrue(media.ShowVideo(path));
            float deadline = Time.realtimeSinceStartup + 8;
            while (media.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(media.IsLoading, media.Error);
            Assert.IsTrue(media.IsVideo && media.IsPlaying, media.Error);
            // 90 x 160 pixels with 4:3 pixel aspect: displayed as an upright 3:4 rectangle.
            Assert.That(screen.Width / screen.Height, Is.EqualTo(0.75f).Within(0.001f));
            Assert.AreEqual(VideoAspectRatio.Stretch, screen.GetComponent<VideoPlayer>().aspectRatio);
            media.Clear();
            Assert.That(screen.Height, Is.EqualTo(width * 9 / 16).Within(0.00001f));
            Assert.AreEqual(width, screen.Width);
            Assert.IsTrue(media.IsBlank);
            Assert.IsFalse(File.Exists(path));
        }

        static void AssertPhotoFillsFace(ScreenSurface screen)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var body = screen.transform.Find("Body").gameObject;
            int layer = body.layer;
            var obj = new GameObject("Media shape test camera", typeof(Camera));
            var camera = obj.GetComponent<Camera>(); camera.enabled = false;
            var target = new RenderTexture(96, 128, 24); target.Create();
            var sample = new Texture2D(96, 128, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            try
            {
                body.layer = 31; camera.cullingMask = 1 << 31;
                camera.transform.SetPositionAndRotation(screen.transform.position + screen.FrontNormal * 0.5f, screen.transform.rotation);
                camera.orthographic = true; camera.orthographicSize = screen.Height * 0.5f;
                camera.nearClipPlane = 0.01f; camera.farClipPlane = 1;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.targetTexture = target; camera.aspect = screen.AspectRatio;
                camera.Render(); RenderTexture.active = target;
                sample.ReadPixels(new Rect(0, 0, 96, 128), 0, 0); sample.Apply();
                foreach (var uv in new[] { new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.08f), new Vector2(0.08f, 0.92f), new Vector2(0.92f, 0.92f) })
                {
                    var color = sample.GetPixelBilinear(uv.x, uv.y);
                    Assert.Greater(color.g, 0.8f, "Photo must fill the face right to its corners, without fixed-ratio borders");
                    Assert.Greater(color.b, 0.8f);
                }
            }
            finally
            {
                body.layer = layer; camera.targetTexture = null; RenderTexture.active = active;
                Object.Destroy(obj); Object.Destroy(sample); target.Release(); Object.Destroy(target);
            }
        }
    }
}
