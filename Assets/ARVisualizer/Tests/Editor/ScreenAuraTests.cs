using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace ARVisualizer.Tests
{
    public sealed class ScreenAuraTests
    {
        [UnityTest]
        public IEnumerator GlowFollowsTimerLooksAndPortraitLandscapeMedia()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(990, true);
            var content = screen.GetComponent<ScreenAppContent>();
            var media = screen.GetComponent<ScreenMedia>();
            var halo = screen.transform.Find("Edit Aura").GetComponent<MeshRenderer>();
            var cameraObject = new GameObject("Aura preview", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -1);
            camera.orthographic = true; camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.04f, 0.04f, 0.04f);
            var target = new RenderTexture(800, 600, 24); target.Create(); camera.targetTexture = target;
            var capture = new Texture2D(800, 600, TextureFormat.RGB24, false);
            try
            {
                Assert.IsTrue(content.Open("timer", null));
                for (int style = 0; style < content.TimerStyleCount + 2; ++style)
                {
                    if (style < content.TimerStyleCount) Assert.IsTrue(content.SetTimerStyle(style));
                    else
                    {
                        bool portrait = style == content.TimerStyleCount;
                        var photo = new Texture2D(portrait ? 32 : 64, portrait ? 64 : 32);
                        var pixels = new Color[photo.width * photo.height];
                        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.1f, 0.17f, 0.2f);
                        photo.SetPixels(pixels); photo.Apply();
                        Assert.IsTrue(media.ShowPhoto(photo));
                    }
                    foreach (var mode in new[] { ScreenEditMode.Resize, ScreenEditMode.Rotate })
                    {
                        SetMode(screen, mode);
                        yield return RenderFrame();
                        Assert.IsTrue(halo.enabled, $"Aura follows {screen.EditMode}; component active={screen.GetComponent<ScreenEditAura>().isActiveAndEnabled}");
                        camera.orthographicSize = Mathf.Max(screen.Height, screen.Width / camera.aspect) * 0.7f;
                        Capture(camera, target, capture);
                        float width = Mathf.Min(screen.Width, screen.Height) * 0.1f;
                        var edge = Pixel(camera, capture, new Vector3(screen.Width / 2 + width * 0.2f, 0, 0));
                        Assert.Greater(mode == ScreenEditMode.Rotate ? edge.r - edge.b : edge.b - edge.r, 0.08f,
                            $"{mode} hue is visible beyond the actual face edge (style {style})");
                        if (media.CornerRadius >= 0.3f)
                        {
                            var corner = Pixel(camera, capture, new Vector3(screen.Width / 2, screen.Height / 2, 0));
                            Assert.Less(Mathf.Max(corner.r, corner.g, corner.b) - Mathf.Min(corner.r, corner.g, corner.b), 0.03f,
                                "Round/capsule faces must not retain a rectangular halo in their empty corners");
                        }
                        Directory.CreateDirectory("Logs");
                        File.WriteAllBytes($"Logs/ARVisualizer-aura-{style}-{mode}.png", capture.EncodeToPNG());
                    }
                }
                screen.Resize(4); yield return RenderFrame();
                camera.orthographicSize = Mathf.Max(screen.Height, screen.Width / camera.aspect) * 0.7f;
                Capture(camera, target, capture);
                var resized = Pixel(camera, capture, new Vector3(screen.Width * 0.5f + screen.Height * 0.02f, 0, 0));
                Assert.Greater(resized.r - resized.b, 0.08f, "Aura follows a live resize");
                SetMode(screen, ScreenEditMode.None); yield return RenderFrame();
                Assert.IsFalse(halo.enabled);
            }
            finally
            {
                Object.Destroy(screen.gameObject); Object.Destroy(cameraObject); Object.Destroy(capture);
                target.Release(); Object.Destroy(target);
            }
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static void SetMode(ScreenSurface screen, ScreenEditMode mode) => typeof(ScreenSurface).GetProperty(nameof(ScreenSurface.EditMode)).SetValue(screen, mode);

        static IEnumerator RenderFrame()
        {
            // EditMode's test iterator may resume more than once before a Play Mode LateUpdate.
            int frame = Time.frameCount;
            float deadline = Time.realtimeSinceStartup + 5;
            while (Time.frameCount <= frame + 1 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.Greater(Time.frameCount, frame + 1);
        }

        static Color Pixel(Camera camera, Texture2D image, Vector3 point)
        {
            var uv = camera.WorldToViewportPoint(point);
            return image.GetPixel(Mathf.RoundToInt(uv.x * (image.width - 1)), Mathf.RoundToInt(uv.y * (image.height - 1)));
        }

        static void Capture(Camera camera, RenderTexture target, Texture2D image)
        {
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
            RenderTexture.active = previous;
        }
    }
}
