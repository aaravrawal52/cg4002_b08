using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer.Tests
{
    public sealed class BrowserTests
    {
        [TestCase("example.com/docs", "https://example.com/docs")]
        [TestCase("https://example.com/a?q=1", "https://example.com/a?q=1")]
        [TestCase("german vocabulary", "https://www.google.com/search?q=german%20vocabulary")]
        [TestCase("javascript:alert(1)", "https://www.google.com/search?q=javascript%3Aalert%281%29")]
        [TestCase(" ", null)]
        public void AddressesAndSearches(string input, string expected) => Assert.AreEqual(expected, ScreenBrowser.AddressToUrl(input));

        [Test]
        public void SavedViewMapsPageAndKeyboardWithoutClickingThrough()
        {
            var view = Object.Instantiate(Resources.Load<ScreenBrowserView>("ScreenBrowserView"));
            try
            {
                Assert.IsTrue(view.TryPagePoint(new Vector2(0.25f, 0.66f), out var page));
                Assert.That(page.x, Is.EqualTo(0.25f).Within(0.001));
                Assert.That(page.y, Is.EqualTo(0.25f).Within(0.001), "Vuplex uses a top-left origin within the page, below the toolbar");
                Assert.IsFalse(view.TryPagePoint(new Vector2(0.5f, 0.96f), out _));
                Assert.AreEqual("Address / search", view.ButtonAt(new Vector2(0.5f, 0.96f)).name);
                view.keyboard.SetActive(true);
                Assert.IsTrue(view.TryPagePoint(new Vector2(0.5f, 0.2f), out _), "The keyboard pops out below the page instead of covering it");
                var keys = view.KeyboardRegion;
                Assert.That(keys.width, Is.EqualTo(view.keyboardWidthScale).Within(0.001f), "1.5 times the screen width");
                Assert.That(keys.center.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.Less(keys.yMax, 0, "Below the face");
                var q = view.keyboard.transform.Find("q");
                Assert.AreSame(q.GetComponent<Button>(), view.ButtonAt(FaceUV(view, q)));
                Assert.AreEqual(view.keyCaptionSize, q.GetComponentInChildren<Text>().fontSize);
                view.SetShift(true); Assert.AreEqual("Q", q.GetComponentInChildren<Text>().text);
                view.SetShift(false); Assert.AreEqual("q", q.GetComponentInChildren<Text>().text);

                // Portrait: the page fills the taller face, the address field narrows, Keys stays at the right.
                var keysButton = (RectTransform)view.transform.Find("Toolbar/Keys");
                view.SetVertical(true);
                Assert.AreEqual(new Vector2(800, 1280), view.Rect.sizeDelta);
                Assert.AreEqual(new Vector2(800, 1280 - 96), view.page.rectTransform.rect.size);
                var field = (RectTransform)view.addressButton.transform;
                Assert.Greater(field.rect.width, 150);
                Assert.Less(Corner(view, field, 1).x, Corner(view, keysButton, 0).x, "Address and Keys do not overlap");
                Assert.LessOrEqual(Corner(view, keysButton, 1).x, view.Rect.rect.xMax + 0.01f);
                Assert.That(view.KeyboardRegion.width, Is.EqualTo(view.keyboardWidthScale).Within(0.001f));
                Assert.AreSame(q.GetComponent<Button>(), view.ButtonAt(FaceUV(view, q)));
                view.SetVertical(false);
                Assert.AreEqual(new Vector2(1280, 704), view.page.rectTransform.rect.size);
                Assert.AreEqual(756, field.rect.width, 0.01f, "Landscape keeps the authored layout");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab").GetComponent<ScreenBrowser>());
                var registry = new SerializedObject(Resources.Load<ScreenAppMenu>("ScreenAppMenu")).FindProperty("apps");
                bool found = false;
                for (int i = 0; i < registry.arraySize; i++) found |= registry.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == "browser";
                Assert.IsTrue(found, "Browser is registered in the saved app chooser");
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        static Vector2 FaceUV(ScreenBrowserView view, Transform target)
        {
            var rect = (RectTransform)target;
            Vector2 local = view.Rect.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return (local - view.Rect.rect.min) / view.Rect.rect.size;
        }
        static Vector2 Corner(ScreenBrowserView view, RectTransform rect, int corner) =>
            view.Rect.InverseTransformPoint(rect.TransformPoint(corner == 0 ? rect.rect.min : rect.rect.max));

        static CommandReply Command(ARVisualizerApp app, string name)
        {
            CommandReply result = null;
            app.Execute(new VisualizerCommand { command = name, hand = "right" }, reply => result = reply);
            Assert.IsNotNull(result); return result;
        }

        static CommandReply Gesture(ARVisualizerApp app, string gesture, string hand = "right")
        {
            Assert.IsTrue(CommandProtocol.TryParseGesture(gesture, hand, out var command, out var error), error);
            CommandReply result = null; app.Execute(command, reply => result = reply);
            Assert.IsNotNull(result); return result;
        }
        static void Aim(ARVisualizerApp app, ScreenSurface screen, Vector2 uv, TrackedHand hand = TrackedHand.Right)
        {
            app.Hands.GetHand(hand).SetPointerEnabled(true);
            app.Hands.GetHand(hand).SetEditorSample(app.ARCamera.WorldToViewportPoint(screen.WorldPoint(uv)), null);
            app.SendLocal(hand == TrackedHand.Left ? "left.status" : "right.status");
        }

        [UnityTest]
        public IEnumerator BrowserGesturesKeyboardEditsAndCleanup()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            var appSettings = new SerializedObject(app);
            appSettings.FindProperty("gestureCooldownSeconds").floatValue = 0;
            appSettings.ApplyModifiedPropertiesWithoutUndo(); // Most of this integration check sends commands in bursts.
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            app.Tutorial.EnterFreeMode(); app.Tutorial.CompleteTransition();
            Assert.AreEqual(TutorialHUD.Mode.Free, app.Tutorial.CurrentMode);
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(970, true); ((IList<ScreenSurface>)app.Screens).Add(screen);
            screen.transform.SetPositionAndRotation(app.ARCamera.transform.position + app.ARCamera.transform.forward * 0.6f, app.ARCamera.transform.rotation);
            var browser = screen.GetComponent<ScreenBrowser>();
            Aim(app, screen, new Vector2(0.5f, 0.65f));
            Assert.IsTrue(app.ScreenApps.IsChooserOpen);
            for (int frame = 0; frame < 3; ++frame) { yield return null; Canvas.ForceUpdateCanvases(); }
            var choice = app.ScreenApps.transform.Find("Screen launcher/App options/Browser").GetComponent<Button>();
            var choiceRect = (RectTransform)choice.transform;
            var choicePoint = app.ARCamera.WorldToViewportPoint(choiceRect.TransformPoint(choiceRect.rect.center));
            app.RightHand.SetEditorSample(choicePoint, null);
            app.SendLocal("right.status");
            Assert.AreEqual("Browser", app.ScreenApps.TargetName, "Chooser viewport=" + choicePoint + " graphic depth=" + choice.targetGraphic.depth + " pointer ready=" + app.PointerReady);
            Assert.IsTrue(Gesture(app, "click").ok);
            Assert.IsTrue(browser.IsOpen);
            deadline = Time.realtimeSinceStartup + 8;
            while (!browser.IsReady && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(browser.IsReady, browser.View.status.text);
            Assert.AreEqual("browser", screen.GetComponent<ScreenMedia>().AppId);
            var launcherCanvas = app.ScreenApps.transform.Find("Screen launcher").GetComponent<Canvas>();
            Assert.Greater(launcherCanvas.sortingOrder, browser.View.GetComponent<Canvas>().sortingOrder,
                "Screen menus render after browser/PDF canvases");
            var web = browser.WebView;
            var point = new Vector2(0.25f, 0.66f);
            Aim(app, screen, point);
            LogAssert.Expect(LogType.Log, new Regex(@"\[MockWebView\] Click\(\(0\.2500, 0\.2500\)"));
            Assert.IsTrue(Gesture(app, "click").ok);
            LogAssert.Expect(LogType.Log, new Regex(@"\[MockWebView\] Scroll\("));
            Assert.IsTrue(Gesture(app, "swipe_up").ok);
            Assert.IsTrue(Gesture(app, "swipe_down").ok);
            Aim(app, screen, point, TrackedHand.Left);
            Assert.IsTrue(Gesture(app, "click", "left").ok);
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null); app.MenuFor(TrackedHand.Left).Close();
            Aim(app, screen, point);
            var tip = screen.WorldPoint(point);
            var rootPoint = app.ARCamera.WorldToViewportPoint(tip);
            LogAssert.Expect(LogType.Log, new Regex(@"\[MockWebView\] Click\("));
            app.RightHand.SetEditorSample(rootPoint, tip); app.SendLocal("right.status");
            Assert.AreEqual(ScreenInputKind.Touch, screen.GetHandInput(TrackedHand.Right).Kind);
            app.SendLocal("right.status");
            Assert.IsTrue(Gesture(app, "click").ok, "A command during held contact is consumed without a second click");
            app.RightHand.SetEditorSample(rootPoint, tip + screen.FrontNormal * 0.1f); app.SendLocal("right.status");
            Aim(app, screen, new Vector2(0.5f, 0.96f));
            Assert.IsTrue(Gesture(app, "click").ok); Assert.IsTrue(browser.View.keyboard.activeSelf);
            deadline = Time.realtimeSinceStartup + 5;
            while (screen.InputExtension.yMax >= 0 && Time.realtimeSinceStartup < deadline) yield return null;
            // The pop-up keyboard hangs below the face; the ray reaches its keys through the screen.
            Assert.Less(screen.InputExtension.yMax, 0);
            Assert.Greater(browser.KeyboardDropMetres, 0);
            Aim(app, screen, FaceUV(browser.View, browser.View.keyboard.transform.Find("e")));
            Assert.AreEqual(screen, app.RayScreen, "A ray on the keyboard targets its screen");
            appSettings.Update();
            appSettings.FindProperty("gestureCooldownSeconds").floatValue = 0.5f;
            appSettings.ApplyModifiedPropertiesWithoutUndo();
            app.RightHand.SetEditorSample(new Vector2(.85f, .65f), null);
            app.SendLocal("right.status");
            Assert.IsNull(app.RayScreen, "The gesture moves the pointer off the keyboard");
            Assert.IsTrue(Gesture(app, "wiggle_fingers").ok);
            StringAssert.StartsWith("e", browser.View.address.text);
            Assert.IsFalse(app.ScreenApps.IsSettingsOpen);
            Assert.IsFalse(app.ScreenApps.TryToggleSettings(screen, out _));
            Assert.IsFalse(Gesture(app, "wiggle_fingers").ok, "An immediate repeat is ignored");
            Assert.IsFalse(Gesture(app, "click", "left").ok, "Cooldown is shared by both hands");
            yield return new WaitForSecondsRealtime(0.55f);
            Assert.IsTrue(Gesture(app, "wiggle_fingers").ok);
            StringAssert.StartsWith("ee", browser.View.address.text);
            browser.RunAction("key:Backspace");
            yield return new WaitForSecondsRealtime(0.55f);
            appSettings.Update(); appSettings.FindProperty("gestureCooldownSeconds").floatValue = 0;
            appSettings.ApplyModifiedPropertiesWithoutUndo();
            browser.RunAction("key:Backspace");
            browser.RunAction("key:e"); browser.RunAction("key:x"); browser.RunAction("key:Backspace");
            browser.RunAction("key:."); browser.RunAction("key:c"); browser.RunAction("key:o"); browser.RunAction("key:m");
            browser.RunAction("key:Enter");
            Assert.IsFalse(browser.View.keyboard.activeSelf);
            Assert.AreEqual("https://e.com", web.Url.TrimEnd('/'));
            deadline = Time.realtimeSinceStartup + 5;
            while (screen.InputExtension.width != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(0, screen.InputExtension.width, "A hidden keyboard takes no input area");

            // Point-and-rotate turns the browser upright about its centre and lays the page out again.
            int landscapeSteps = screen.SizeSteps; float longSide = screen.Width; var centre = screen.transform.position;
            Aim(app, screen, point);
            Assert.IsTrue(Gesture(app, "point_and_rotate").ok);
            Assert.IsTrue(browser.IsVertical); Assert.Less(screen.Width, screen.Height);
            Assert.That(screen.Height, Is.EqualTo(longSide).Within(ScreenSurface.WidthStep * 1.7f), "The long side keeps its length");
            Assert.AreEqual(centre, screen.transform.position);
            Assert.That(screen.Width / screen.Height, Is.EqualTo(800f / 1280).Within(0.001f));
            Assert.Less(web.Size.x, web.Size.y, "The web page is laid out again in portrait");
            Assert.AreEqual(web.Size, new Vector2Int(browser.View.page.texture.width, browser.View.page.texture.height));
            Assert.AreSame(web, browser.WebView, "Turning keeps the page and its history");
            Assert.IsTrue(Gesture(app, "click").ok, "The portrait page still takes clicks");
            Assert.IsTrue(Command(app, "screen.vertical").ok, "Already vertical is not an error");
            Aim(app, screen, point);
            Assert.IsTrue(Gesture(app, "point_and_rotate").ok);
            Assert.IsFalse(browser.IsVertical);
            Assert.AreEqual(landscapeSteps, screen.SizeSteps, "Turning back restores the exact size");
            Assert.Greater(web.Size.x, web.Size.y);
            Aim(app, screen, point);
            Assert.IsTrue(Gesture(app, "pinch_and_pull").ok);
            Assert.IsFalse(Gesture(app, "click").ok);
            int width = web.Size.x;
            Gesture(app, "swipe_up"); Gesture(app, "grab_and_release");
            Assert.AreEqual(width, web.Size.x, "Physical resizing keeps the browser resolution and session");
            Assert.AreSame(web, browser.WebView);
            app.SendLocal("goggle.enter");
            yield return null;
            Assert.IsTrue(browser.IsReady); Assert.IsTrue(app.GoggleHUD.Stereo.IsActive);
            app.RightHand.SetEditorSample(new Vector2(-1, -1), null);
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null);
            app.SendLocal("right.status");
            Assert.IsTrue(app.PointerReady && app.UsesCentrePointer, "Goggles keep a centre ray with no hands");
            Assert.AreEqual(Vector2.one * 0.5f, app.PointerViewportPoint);
            Assert.IsTrue(app.LeftHand.PointerLockedOn && app.RightHand.PointerLockedOn);
            app.RightHand.SetPointerEnabled(false);
            Assert.IsTrue(app.PointerReady, "The goggle pointer cannot be switched off");
            int tipFrame = Time.frameCount;
            while (Time.frameCount <= tipFrame + 1) yield return null;
            var centreTip = app.transform.Find("Hand ray").GetComponent<LineRenderer>();
            Assert.IsTrue(centreTip.enabled, "The centre ray keeps a visible tip");
            var cross = app.transform.Find("Right ray tip horizontal").GetComponent<LineRenderer>();
            Assert.IsTrue(cross.enabled);
            Assert.That(Vector3.Distance((cross.GetPosition(0) + cross.GetPosition(1)) * 0.5f,
                centreTip.GetPosition(1)), Is.LessThan(0.0001f), "The cross is centred on the ray endpoint");
            Assert.Less(Vector3.Distance(centreTip.GetPosition(0), centreTip.GetPosition(1)), centreTip.startWidth * 0.02f,
                "The centre ray has no long body across the view");
            var pointerHud = app.GetComponentInChildren<VisualizerHUD>();
            bool hudWasVisible = pointerHud.IsHUDVisible;
            pointerHud.SetHUDVisible(false);
            tipFrame = Time.frameCount;
            while (Time.frameCount <= tipFrame + 1) yield return null;
            Assert.IsTrue(centreTip.enabled, "Hiding the HUD keeps the centre tip visible");
            pointerHud.SetHUDVisible(hudWasVisible);
            using (app.UseHand(TrackedHand.Left)) Assert.IsFalse(app.UsesCentrePointer, "Only one centre ray is drawn");
            Aim(app, screen, point);
            Assert.IsFalse(app.UsesCentrePointer);
            tipFrame = Time.frameCount;
            while (Time.frameCount <= tipFrame + 1) yield return null;
            Assert.Greater(Vector3.Distance(centreTip.GetPosition(0), centreTip.GetPosition(1)), centreTip.startWidth * 2,
                "A tracked hand still uses the full ray");
            app.SendLocal("goggle.exit");
            yield return null;
            Assert.AreEqual("ARVisualizer/Occluded Screen UI", browser.View.page.material.shader.name);
            Assert.IsInstanceOf<RenderTexture>(browser.View.page.texture);
            Capture(browser.View);
            // PDFs share the authored explorer and browser surface, with local-file URLs and hand ownership.
            string pdfPath = Path.Combine(PdfLibrary.FolderPath, "000 AR test #100% ä " + System.Guid.NewGuid().ToString("N") + ".pdf");
            PdfLibraryTests.WriteSample(pdfPath);
            int pdfSteps = 0;
            try
            {
                Aim(app, screen, point);
                Assert.IsTrue(app.ScreenApps.TryOpenChooser(screen, out _));
                yield return null; Canvas.ForceUpdateCanvases();
                var pdfChoice = app.ScreenApps.transform.Find("Screen launcher/App options/PDF viewer").GetComponent<Button>();
                pdfChoice.onClick.Invoke();
                Assert.IsTrue(app.ScreenApps.IsModal);
                Assert.AreEqual(1, app.ScreenApps.LibraryGrid.Total);
                yield return null;
                CaptureExplorer(app.ScreenApps.ActiveCanvas);
                Assert.IsTrue(app.ScreenApps.LibraryGrid.SelectFocused(out _));
                yield return null;
                Assert.IsTrue(web.IsDisposed, "PDF launch releases the previous web session");
                Assert.IsTrue(browser.IsReady); Assert.IsTrue(browser.IsPdf);
                Assert.AreEqual("pdf", screen.GetComponent<ScreenMedia>().AppId);
                Assert.AreEqual(Path.GetFullPath(pdfPath), new System.Uri(browser.WebView.Url).LocalPath);
                Assert.AreEqual(Path.GetFileName(pdfPath), browser.View.address.text);
                Assert.IsFalse(browser.View.addressButton.interactable);
                Assert.AreEqual("Files", browser.View.homeButton.GetComponentInChildren<Text>().text);
                var pdfWeb = browser.WebView;
                Aim(app, screen, point); Assert.IsTrue(Gesture(app, "swipe_up").ok);
                Aim(app, screen, point, TrackedHand.Left); Assert.IsTrue(Gesture(app, "swipe_down", "left").ok);
                var homeRect = (RectTransform)browser.View.homeButton.transform;
                var local = (Vector2)browser.View.Rect.InverseTransformPoint(homeRect.TransformPoint(homeRect.rect.center));
                var uv = new Vector2((local.x - browser.View.Rect.rect.xMin) / browser.View.Rect.rect.width,
                    (local.y - browser.View.Rect.rect.yMin) / browser.View.Rect.rect.height);
                Aim(app, screen, uv, TrackedHand.Left);
                Assert.IsTrue(Gesture(app, "click", "left").ok);
                Assert.IsTrue(app.MenuFor(TrackedHand.Left).IsModal, "Files opens for the hand that clicked it");
                app.MenuFor(TrackedHand.Left).Close();
                app.LeftHand.SetEditorSample(new Vector2(-1, -1), null);
                app.ScreenApps.TryBrowsePdfs(screen);
                File.Delete(pdfPath); app.ScreenApps.OpenPdfs();
                Assert.AreEqual(0, app.ScreenApps.LibraryGrid.Total);
                StringAssert.Contains("Files > On My iPhone", app.ScreenApps.Status);
                Assert.AreSame(pdfWeb, browser.WebView, "Refreshing the picker does not replace the open document");
                Assert.IsFalse(browser.OpenPdf(app, pdfPath));
                Assert.AreSame(pdfWeb, browser.WebView, "Selecting a deleted file preserves the current session");
                app.ScreenApps.OpenPhotos();
                deadline = Time.realtimeSinceStartup + 8;
                while (app.ScreenApps.LibraryGrid.Total == 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.Greater(app.ScreenApps.LibraryGrid.Total, 0, "The shared grid can switch back to the photo provider");
                app.ScreenApps.Close();
                app.SendLocal("goggle.enter"); yield return null;
                Assert.IsTrue(browser.IsReady); Assert.IsTrue(app.GoggleHUD.Stereo.IsActive);
                app.SendLocal("goggle.exit"); yield return null;
                pdfSteps = screen.SizeSteps;
                Aim(app, screen, point);
                Assert.IsTrue(Gesture(app, "point_and_rotate").ok);
                Assert.IsTrue(browser.IsVertical); Assert.Less(pdfWeb.Size.x, pdfWeb.Size.y, "PDFs reflow upright too");
                browser.Open(app); yield return null;
                Assert.IsTrue(pdfWeb.IsDisposed);
                Assert.IsTrue(browser.IsVertical, "Another page on the same screen stays vertical");
                Assert.Less(browser.WebView.Size.x, browser.WebView.Size.y);
                Assert.IsTrue(browser.View.addressButton.interactable);
                Assert.AreEqual("Home", browser.View.homeButton.GetComponentInChildren<Text>().text);
                web = browser.WebView;
            }
            finally { if (File.Exists(pdfPath)) File.Delete(pdfPath); }
            Assert.IsTrue(screen.GetComponent<ScreenAppContent>().Open("timer", app));
            Assert.IsTrue(web.IsDisposed, "Switching apps disposes the native browser");
            Assert.IsFalse(browser.IsVertical, "Other apps start from the landscape size");
            Assert.AreEqual(pdfSteps, screen.SizeSteps, "And get back the width the screen had before it turned");
            Assert.That(screen.Width, Is.GreaterThan(screen.Height));
            Aim(app, screen, new Vector2(0.5f, 0.5f));
            Assert.IsFalse(Gesture(app, "point_and_rotate").ok, "Only the browser and PDF viewer turn vertical");
            Assert.IsFalse(browser.IsOpen); Assert.IsNull(browser.WebView);
            browser.Open(app);
            // Close immediately, including when initialization hasn't completed yet.
            screen.GetComponent<ScreenMedia>().Clear();
            yield return null;
            Assert.IsFalse(browser.IsOpen); Assert.IsNull(browser.WebView);
            browser.Open(app); yield return null;
            web = browser.WebView; Assert.IsNotNull(web);
            app.DeleteScreen(screen); yield return null;
            Assert.IsTrue(web.IsDisposed, "Deleting a screen releases its browser");
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static void Capture(ScreenBrowserView source)
        {
            var root = Object.Instantiate(source);
            var cameraObject = new GameObject("Browser preview", typeof(Camera));
            var target = new RenderTexture(1280, 800, 24); target.Create();
            var capture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var camera = cameraObject.GetComponent<Camera>(); camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                // Isolate the layout preview from the simulation room's foreground geometry.
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                camera.cullingMask = 1 << 31;
                root.transform.localScale = Vector3.one;
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                root.keyboard.SetActive(true);
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); capture.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/ARVisualizer-browser.png", capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; Object.Destroy(root.gameObject); Object.Destroy(cameraObject); Object.Destroy(capture);
                target.Release(); Object.Destroy(target);
            }
        }

        static void CaptureExplorer(Canvas source)
        {
            var root = Object.Instantiate(source);
            var rect = (RectTransform)root.transform;
            int width = Mathf.RoundToInt(rect.rect.width), height = Mathf.RoundToInt(rect.rect.height);
            var cameraObject = new GameObject("PDF explorer preview", typeof(Camera));
            var target = new RenderTexture(width, height, 24); target.Create();
            var capture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                var camera = cameraObject.GetComponent<Camera>(); camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.02f, 0.03f, 0.04f);
                camera.cullingMask = 1 << 31;
                root.transform.localScale = Vector3.one;
                root.renderMode = RenderMode.ScreenSpaceCamera; root.worldCamera = camera; root.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, width, height), 0, 0); capture.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/ARVisualizer-pdf-explorer.png", capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; Object.Destroy(root.gameObject); Object.Destroy(cameraObject); Object.Destroy(capture);
                target.Release(); Object.Destroy(target);
            }
        }
    }
}
