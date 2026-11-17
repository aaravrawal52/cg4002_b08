using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Vuplex.WebView;
using Object = UnityEngine.Object;

namespace ARVisualizer.Tests
{
    public sealed class BrowserVideoTests
    {
        sealed class VideoSource : IWithFallbackVideo
        {
            public event EventHandler<Vuplex.WebView.EventArgs<Rect>> VideoRectChanged;
            public Texture2D VideoTexture { get; set; }
            public int Subscribers => VideoRectChanged?.GetInvocationList().Length ?? 0;
            public void SetFallbackVideoEnabled(bool enabled) { }
            public void Move(Rect rect) => VideoRectChanged?.Invoke(this, new Vuplex.WebView.EventArgs<Rect>(rect));
        }

        [UnityTest]
        public IEnumerator CompositorShowsVideoAndPreservesControlsAcrossRectAndTextureChanges()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            var browser = screen.GetComponent<ScreenBrowser>();
            var material = new Material(Resources.Load<Material>("AppleWebMaterial"));
            var page = Solid(Color.black, 64);
            var green = Solid(Color.green, 8);
            var blue = Solid(Color.blue, 8);
            var output = new RenderTexture(64, 64, 0); output.Create();
            var capture = new Texture2D(64, 64, TextureFormat.RGB24, false);
            var video = new VideoSource { VideoTexture = green };
            try
            {
                // A white HTML control overlays the video in the otherwise black video rectangle.
                for (int y = 30; y < 36; y++) for (int x = 30; x < 36; x++) page.SetPixel(x, y, Color.white);
                page.Apply();
                typeof(ScreenBrowser).GetField("webMaterial", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(browser, material);
                Call(browser, "BindFallbackVideo", video);
                Assert.AreEqual(1, video.Subscribers);
                video.Move(new Rect(.25f, .125f, .5f, .5f));
                Render(page, output, material, capture);
                Assert.Greater(capture.GetPixel(24, 44).g, .8f, "The iOS video texture appears inside its page rectangle");
                Assert.Less(capture.GetPixel(5, 32).maxColorComponent, .05f, "Video does not cover the rest of the page");
                Assert.Greater(capture.GetPixel(32, 32).r, .8f, "HTML playback controls remain on top");
                video.Move(new Rect(0, 0, .25f, .25f));
                video.VideoTexture = blue;
                Call(browser, "RefreshVideoTexture");
                Render(page, output, material, capture);
                Assert.Greater(capture.GetPixel(8, 56).b, .8f, "Rect/texture changes follow scrolling, resizing and playback");
                Assert.Less(capture.GetPixel(24, 44).maxColorComponent, .05f);
                video.Move(Rect.zero);
                Render(page, output, material, capture);
                Assert.Less(capture.GetPixel(8, 56).maxColorComponent, .05f, "No stale video remains when its rectangle disappears");
                Call(browser, "CloseSession");
                Assert.AreEqual(0, video.Subscribers, "Switching apps releases the video event subscription");
            }
            finally
            {
                Object.Destroy(screen.gameObject); Object.Destroy(material); Object.Destroy(page);
                Object.Destroy(green); Object.Destroy(blue); Object.Destroy(capture);
                output.Release(); Object.Destroy(output);
            }
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static void Call(ScreenBrowser browser, string method, params object[] args) =>
            typeof(ScreenBrowser).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(browser, args);

        static Texture2D Solid(Color color, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        static void Render(Texture page, RenderTexture output, Material material, Texture2D capture)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = output; GL.Clear(false, true, Color.white);
            Graphics.Blit(page, output, material);
            RenderTexture.active = output; capture.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); capture.Apply();
            RenderTexture.active = previous;
        }
    }
}
