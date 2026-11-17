using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer.Tests
{
    public sealed class TutorialTests
    {
        [Test]
        public void GestureIsLearnedByOneHandThenAppliedOnce()
        {
            var session = new TutorialSession(new[] { "click", "SWIPE_UP" });
            Assert.AreEqual("CLICK", session.ExpectedGesture);
            Assert.AreEqual(TutorialVerdict.Wrong, session.Handle("swipe_up", TrackedHand.Right));
            Assert.AreEqual(TutorialVerdict.Correct, session.Handle("CLICK", TrackedHand.Right));
            Assert.AreEqual(TutorialVerdict.Correct, session.Handle("click", TrackedHand.Left));
            Assert.AreEqual(TutorialVerdict.Correct, session.Handle("click", TrackedHand.Left));
            Assert.AreEqual(1, session.Correct(TrackedHand.Right)); Assert.AreEqual(2, session.Correct(TrackedHand.Left));
            Assert.AreEqual(TutorialPhase.Practise, session.Phase, "Three in total is not enough; one hand needs all three");
            Assert.AreEqual(TutorialVerdict.Learned, session.Handle("click", TrackedHand.Left));
            Assert.AreEqual(TutorialPhase.Apply, session.Phase);
            Assert.AreEqual(TutorialVerdict.Wrong, session.Handle("swipe_up", TrackedHand.Left));
            Assert.AreEqual(TutorialVerdict.NextStep, session.Handle("click", TrackedHand.Right), "Either hand may continue");
            Assert.AreEqual(1, session.StepIndex); Assert.AreEqual(0, session.Correct(TrackedHand.Left));
            for (int i = 0; i < 2; i++) Assert.AreEqual(TutorialVerdict.Correct, session.Handle("SWIPE_UP", TrackedHand.Right));
            Assert.AreEqual(TutorialVerdict.Learned, session.Handle("SWIPE_UP", TrackedHand.Right));
            Assert.AreEqual(TutorialVerdict.Finished, session.Handle("SWIPE_UP", TrackedHand.Right));
            Assert.AreEqual(TutorialPhase.Complete, session.Phase);
            Assert.AreEqual(TutorialVerdict.Ignored, session.Handle("SWIPE_UP", TrackedHand.Right));
            Assert.AreEqual(TutorialVerdict.Exit, session.Handle("swipe_left", TrackedHand.Left));
        }

        [Test]
        public void SkippingAndCustomRepetitionCountsAreSupported()
        {
            var session = new TutorialSession(new[] { "CLICK", "SWIPE_UP" }, 1, "SWIPE_RIGHT");
            Assert.AreEqual(TutorialVerdict.Learned, session.Handle("CLICK", TrackedHand.Right));
            Assert.AreEqual(TutorialVerdict.NextStep, session.Skip());
            Assert.AreEqual(TutorialVerdict.Finished, session.Skip());
            Assert.AreEqual(TutorialVerdict.Ignored, session.Skip());
            Assert.AreEqual(TutorialVerdict.Ignored, session.Handle("SWIPE_LEFT", TrackedHand.Right));
            Assert.AreEqual(TutorialVerdict.Exit, session.Handle("SWIPE_RIGHT", TrackedHand.Right));
            Assert.AreEqual(TutorialPhase.Complete, new TutorialSession(new string[0]).Phase);
        }

        [Test]
        public void GuideProgressMovesThroughStepsSkipsAndRetreats()
        {
            var progress = new TutorialGuideProgress(new[] { 2, 0, 3 });
            Assert.AreEqual(3, progress.GuideCount);
            Assert.AreEqual(0, progress.GuideIndex); Assert.AreEqual(2, progress.StepCount);
            Assert.IsFalse(progress.CompleteStep()); Assert.AreEqual(1, progress.StepIndex);
            Assert.IsTrue(progress.CompleteStep(), "The last step of a guide begins the next guide");
            Assert.AreEqual(2, progress.GuideIndex, "A guide without steps is passed over");
            Assert.AreEqual(0, progress.StepIndex); Assert.AreEqual(3, progress.StepCount);
            progress.CompleteStep(); progress.CompleteStep();
            progress.Retreat(5); Assert.AreEqual(2, progress.StepIndex, "Only earlier steps can be returned to");
            progress.Retreat(0); Assert.AreEqual(0, progress.StepIndex);
            progress.SkipGuide();
            Assert.IsTrue(progress.Finished); Assert.AreEqual(0, progress.StepCount);
            Assert.IsFalse(progress.CompleteStep());
            Assert.IsTrue(new TutorialGuideProgress(new int[0]).Finished);
        }

        [Test]
        public void GuidesCoverTheFiveTasksWithAClipAndAKnownGoalForEveryStep()
        {
            var script = TutorialScript.Parse(Resources.Load<TextAsset>("tutorial").text);
            Assert.AreEqual(5, script.guides.Length);
            var goals = new HashSet<string>();
            foreach (var guide in script.guides)
            {
                Assert.IsNotEmpty(guide.title);
                Assert.IsFalse(guide.steps[guide.steps.Length - 1].skipIfMet, guide.title + " ends on something to do");
                foreach (var step in guide.steps)
                {
                    CollectionAssert.Contains(TutorialScript.Goals, step.goal);
                    goals.Add(step.goal);
                    Assert.IsNotEmpty(step.title); Assert.IsNotEmpty(step.text);
                    Assert.LessOrEqual(step.text.Length, 190, step.title + " fits the message panel");
                    Assert.IsTrue(File.Exists("Assets/ARVisualizer/Media/Tutorial/" + step.video + ".mp4"), step.video);
                }
            }
            foreach (string goal in new[] { "screen.placed", "screen.deleted", "screen.resized", "screen.rotated", "settings.open", "screen.vertical", "screen.horizontal" })
                CollectionAssert.Contains(goals, goal);
            // Steps with an unknown goal could never finish, so they are dropped, and so are guides left empty.
            var partial = TutorialScript.Parse("{\"guides\":[{\"title\":\"A\",\"steps\":[{\"goal\":\"fly\"}]},{\"title\":\"B\",\"steps\":[{\"goal\":\"screen.placed\"},{\"goal\":\"\"}]}]}");
            Assert.AreEqual(1, partial.guides.Length); Assert.AreEqual("B", partial.guides[0].title);
            Assert.AreEqual(1, partial.guides[0].steps.Length);
            Assert.AreEqual(0, TutorialScript.Parse("{}").guides.Length);
        }

        [Test]
        public void MessageFileCoversEveryGestureAndHasAClipForEach()
        {
            var asset = Resources.Load<TextAsset>("tutorial");
            Assert.IsNotNull(asset, "Resources/tutorial.json");
            var script = TutorialScript.Parse(asset.text);
            Assert.AreEqual(3, script.repetitions);
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            var taught = new HashSet<string>();
            foreach (var step in script.steps)
            {
                Assert.IsTrue(CommandProtocol.TryParseGesture(step.gesture, "right", out var command, out var error), step.gesture + ": " + error);
                Assert.AreEqual(step.gesture, command.sourceGesture, "Use the catalogue's own name, not an alias");
                Assert.IsTrue(taught.Add(step.gesture), step.gesture + " is taught twice");
                Assert.IsNotEmpty(step.title); Assert.IsNotEmpty(step.perform); Assert.IsNotEmpty(step.usage);
                Assert.IsTrue(File.Exists("Assets/ARVisualizer/Media/Tutorial/" + step.video + ".mp4"), step.video);
            }
            foreach (var binding in catalogue.gestures) Assert.IsTrue(taught.Contains(binding.gesture), binding.gesture + " has no tutorial step");
            Assert.IsTrue(taught.Contains(script.exitGesture), "The way out must have been taught");
            // Broken or partial files fall back to the built-in wording.
            Assert.AreEqual("WELCOME", TutorialScript.Parse("{ not json").choice.heading);
            var partial = TutorialScript.Parse("{\"repetitions\":0,\"steps\":[{\"gesture\":\"\"},{\"gesture\":\"CLICK\"}]}");
            Assert.AreEqual(1, partial.repetitions); Assert.AreEqual(1, partial.steps.Length);
            Assert.IsNotEmpty(partial.messages.completeHint);
        }

        [Test]
        public void SavedHudHasTheTutorialLayoutAndClips()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab");
            var tutorial = hud.GetComponent<TutorialHUD>();
            Assert.IsNotNull(tutorial, "Run Tools > AR Visualizer > Prepare tutorial mode");
            Assert.IsTrue(tutorial.HasRequiredReferences);
            Assert.IsTrue(tutorial.startWithModeChoice);
            var clips = new SerializedObject(tutorial).FindProperty("clips");
            Assert.AreEqual(TutorialScript.Parse(Resources.Load<TextAsset>("tutorial").text).steps.Length, clips.arraySize);
            foreach (var name in new[] { "Tutorial Mode Button", "Free Mode Button" })
            {
                var square = (RectTransform)hud.transform.Find("Tutorial/Tutorial Slide/Mode Choice/" + name);
                Assert.IsNotNull(square, name);
                Assert.AreEqual(square.sizeDelta.x, square.sizeDelta.y, name + " is square");
                Assert.GreaterOrEqual(square.sizeDelta.x, 240, name + " is large");
                Assert.AreEqual(1, square.GetComponent<Button>().onClick.GetPersistentEventCount());
            }
            Assert.Less(hud.transform.Find("Tutorial/Tutorial Slide/Mode Choice/Tutorial Mode Button").localPosition.x, 0);
            Assert.Greater(hud.transform.Find("Tutorial/Tutorial Slide/Mode Choice/Free Mode Button").localPosition.x, 0);
            var video = (RectTransform)hud.transform.Find("Tutorial/Tutorial Slide/Lesson/Video Panel");
            Assert.AreEqual(new Vector2(0, 1), video.anchorMin, "The clip sits in the top-left corner");
            var message = (RectTransform)hud.transform.Find("Tutorial/Tutorial Slide/Lesson/Message Panel");
            Assert.AreEqual(new Vector2(0.5f, 0), message.anchorMin, "Guidance floats at the lower centre");
            Assert.AreEqual(0, ((RectTransform)hud.transform.Find("Tutorial/Tutorial Return/Return Bar")).anchorMin.x, "The return bar is on the left edge");
            Assert.Less(hud.transform.Find("Tutorial").GetSiblingIndex(), hud.transform.Find("Aim Position").GetSiblingIndex());
            // Goggle mode: a tip sits just above the two small buttons, each of which has a hold bar.
            var tip = (RectTransform)hud.transform.Find("Tutorial/Tutorial Slide/Lesson/Hold Tip");
            Assert.IsNotNull(tip, "Run Tools > AR Visualizer > Prepare tutorial mode");
            Assert.IsFalse(tip.gameObject.activeSelf, "Shown by the tutorial in goggle mode only");
            StringAssert.Contains("3 s", tip.GetComponentInChildren<Text>(true).text);
            var skip = (RectTransform)hud.transform.Find("Tutorial/Tutorial Slide/Lesson/Skip Gesture Button");
            Assert.Greater(tip.anchoredPosition.y, skip.anchoredPosition.y + skip.sizeDelta.y - 1);
            Assert.LessOrEqual(tip.sizeDelta.y, 30, "The tip is small");
            foreach (var name in new[] { "Skip Gesture Button", "Exit Tutorial Button" })
            {
                var fill = (RectTransform)hud.transform.Find("Tutorial/Tutorial Slide/Lesson/" + name + "/Hold Track/Hold Progress");
                Assert.IsNotNull(fill, name); Assert.AreEqual(0, fill.anchorMax.x, name + " starts empty");
            }
        }

        static CommandReply Gesture(ARVisualizerApp app, string name, TrackedHand hand = TrackedHand.Right)
        {
            Assert.IsTrue(CommandProtocol.TryParseGesture(name, hand == TrackedHand.Left ? "left" : "right", out var command, out var error), error);
            CommandReply reply = null;
            app.Execute(command, result => reply = result);
            Assert.IsNotNull(reply, name + " should be answered immediately");
            return reply;
        }

        static void Aim(ARVisualizerApp app, Vector3 point)
        {
            app.RightHand.SetEditorSample(app.ARCamera.WorldToViewportPoint(point), null);
            // Any command refreshes what the hand points at.
            app.Execute(new VisualizerCommand { command = "status", hand = "right" }, reply => { });
        }

        // A finished step shows DONE on one frame and the next step on a later one.
        static IEnumerator Frames() { for (int i = 0; i < 4; i++) yield return null; }

        static void Capture(ARVisualizerApp app, string name)
        {
            Directory.CreateDirectory("Logs");
            if (!app.GoggleModeEnabled)
            {
                // The phone HUD is a screen-space overlay.
                ScreenCapture.CaptureScreenshot("Logs/" + name + ".png");
                return;
            }
            // Render the eye at a readable resolution; batch mode's Game view can be very small.
            var eye = app.GoggleHUD.Stereo.LeftEye;
            var originalTarget = eye.targetTexture;
            var target = new RenderTexture(1280, 960, 24);
            target.Create();
            var previous = RenderTexture.active;
            var capture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                eye.targetTexture = target;
                eye.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                capture.Apply();
                File.WriteAllBytes("Logs/" + name + ".png", capture.EncodeToPNG());
            }
            finally
            {
                eye.targetTexture = originalTarget;
                RenderTexture.active = previous;
                target.Release(); Object.Destroy(target); Object.Destroy(capture);
            }
        }

        [UnityTest]
        public IEnumerator GogglePresentationRestoresPhoneLayout()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            yield return new EnterPlayMode();
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            var tutorial = app.Tutorial;
            tutorial.ChooseTutorial(); tutorial.CompleteTransition();
            var hud = tutorial.transform;
            var phoneFonts = new Dictionary<Text, (int size, int minimum, int maximum)>();
            foreach (var text in hud.GetComponentsInChildren<Text>(true))
                phoneFonts[text] = (text.fontSize, text.resizeTextMinSize, text.resizeTextMaxSize);
            var video = (RectTransform)hud.Find("Tutorial/Tutorial Slide/Lesson/Video Panel");
            var phonePosition = video.anchoredPosition;
            var phoneSize = video.sizeDelta;
            app.SendLocal("goggle.enter");
            int frame = Time.frameCount;
            while (Time.frameCount <= frame + 1) yield return null;
            Assert.IsTrue(app.GoggleModeEnabled);
            foreach (var entry in phoneFonts)
            {
                Assert.AreEqual(entry.Value.size + 2, entry.Key.fontSize, entry.Key.name);
                Assert.AreEqual(entry.Value.minimum + 2, entry.Key.resizeTextMinSize);
                Assert.AreEqual(entry.Value.maximum + 2, entry.Key.resizeTextMaxSize);
            }
            Assert.AreEqual(phonePosition + Vector2.down * (video.rect.height * 0.5f), video.anchoredPosition);
            Assert.AreEqual(phoneSize, video.sizeDelta, "Move the video without enlarging the card");
            app.SendLocal("goggle.enter");
            foreach (var entry in phoneFonts)
                Assert.AreEqual(entry.Value.size + 2, entry.Key.fontSize, "Repeated enter does not compound the increase");
            Capture(app, "ARVisualizer-tutorial-goggles");
            tutorial.ExitTutorial(); tutorial.CompleteTransition();
            var visualizerHud = tutorial.GetComponent<VisualizerHUD>();
            Assert.IsFalse(visualizerHud.IsHUDVisible, "Entering free mode in goggles hides the HUD");
            visualizerHud.SetHUDVisible(true);
            tutorial.EnterFreeMode();
            Assert.IsTrue(visualizerHud.IsHUDVisible, "Already in free mode: keep a manually shown HUD visible");
            frame = Time.frameCount;
            while (Time.frameCount <= frame + 1) yield return null;
            Capture(app, "ARVisualizer-hud-goggles");
            app.SendLocal("goggle.exit");
            Assert.IsFalse(app.GoggleModeEnabled);
            tutorial.ChooseTutorial(); tutorial.CompleteTransition();
            tutorial.ExitTutorial(); tutorial.CompleteTransition();
            Assert.IsTrue(visualizerHud.IsHUDVisible, "Phone-mode tutorial exit preserves HUD visibility");
            Assert.AreEqual(phonePosition, video.anchoredPosition);
            foreach (var entry in phoneFonts)
            {
                Assert.AreEqual(entry.Value.size, entry.Key.fontSize, "Phone font restored: " + entry.Key.name);
                Assert.AreEqual(entry.Value.minimum, entry.Key.resizeTextMinSize);
                Assert.AreEqual(entry.Value.maximum, entry.Key.resizeTextMaxSize);
            }
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        [UnityTest]
        public IEnumerator TutorialRunsFromStartupChoiceToFreeModeAndBack()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            yield return new EnterPlayMode();
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(app != null && ARSession.state == ARSessionState.SessionTracking);
            var tutorial = app.Tutorial;
            Assert.IsNotNull(tutorial, "The scene HUD inherits the tutorial from its prefab");
            tutorial.settleSeconds = 0;
            var hud = tutorial.transform;
            var free = hud.Find("Safe Area").GetComponent<CanvasGroup>();
            var choice = hud.Find("Tutorial/Tutorial Slide/Mode Choice");
            var lesson = hud.Find("Tutorial/Tutorial Slide/Lesson");
            yield return null;

            // Start-up: the two squares cover a hidden, untouchable free-mode HUD, and gestures do nothing.
            Assert.AreEqual(TutorialHUD.Mode.Choice, tutorial.CurrentMode);
            Assert.IsTrue(choice.gameObject.activeInHierarchy); Assert.IsFalse(lesson.gameObject.activeSelf);
            Assert.AreEqual(0, free.alpha); Assert.IsFalse(free.blocksRaycasts);
            Gesture(app, "GRAB_AND_RELEASE");
            Assert.AreEqual(0, app.ScreenCount); Assert.IsFalse(app.IsPlacing, "Gestures must not act behind the choice");
            Capture(app, "ARVisualizer-tutorial-choice");
            choice.Find("Tutorial Mode Button").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(TutorialHUD.Mode.Lesson, tutorial.CurrentMode);
            Assert.IsTrue(lesson.gameObject.activeInHierarchy); Assert.IsFalse(choice.gameObject.activeSelf);
            Assert.AreEqual(0, free.alpha);

            var script = tutorial.Script;
            var title = lesson.Find("Message Panel/Title").GetComponent<Text>();
            var body = lesson.Find("Message Panel/Body").GetComponent<Text>();
            var leftBar = hud.Find("Tutorial Left Hand/Progress Bar");
            var rightBar = hud.Find("Tutorial Right Hand/Progress Bar");
            int Lit(Transform bar)
            {
                int lit = 0;
                foreach (Transform third in bar) if (third.GetComponent<Graphic>().color.g > 0.99f && third.GetComponent<Graphic>().color.r < 0.5f) lit++;
                return lit;
            }
            app.RightHand.SetEditorSample(new Vector2(0.62f, 0.55f), null);
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null);
            for (int index = 0; index < script.steps.Length; index++)
            {
                var step = script.steps[index];
                string wrong = script.steps[(index + 1) % script.steps.Length].gesture;
                Assert.AreEqual(index, tutorial.Session.StepIndex);
                Assert.AreEqual(step.title, title.text);
                StringAssert.Contains(step.perform, body.text);
                Assert.IsTrue(lesson.Find("Video Panel").gameObject.activeSelf, step.video + " clip is assigned and shown");

                // A different gesture is reported above that hand and earns nothing.
                Gesture(app, wrong, TrackedHand.Left);
                StringAssert.Contains(wrong.Replace('_', ' '), tutorial.PopupText(TrackedHand.Left));
                Assert.AreEqual(0, tutorial.Session.Correct(TrackedHand.Left));
                for (int count = 1; count <= 3; count++)
                {
                    Assert.AreEqual(TutorialPhase.Practise, tutorial.Session.Phase);
                    Gesture(app, step.gesture);
                    yield return null;
                    Assert.AreEqual(count, Lit(rightBar), "One third of the right hand's bar per correct gesture");
                    Assert.AreEqual(0, Lit(leftBar));
                    Assert.IsNotEmpty(tutorial.PopupText(TrackedHand.Right));
                }
                Assert.AreEqual(TutorialPhase.Apply, tutorial.Session.Phase);
                StringAssert.Contains(step.usage, body.text, "After three, the panel explains what the gesture does");
                if (index == 0)
                {
                    // The bar follows a visible hand's marker and waits at its dock otherwise.
                    var right = (RectTransform)hud.Find("Tutorial Right Hand");
                    Assert.That(Vector2.Distance(right.anchorMin, app.RightHand.ViewportPoint), Is.LessThan(0.001f));
                    Assert.That(((RectTransform)hud.Find("Tutorial Left Hand")).anchorMin.x, Is.LessThan(0.5f));
                    Capture(app, "ARVisualizer-tutorial-lesson");
                    yield return null;
                }
                Gesture(app, step.gesture, TrackedHand.Left);
                yield return null;
            }
            Assert.AreEqual(0, app.ScreenCount, "Practice gestures never reached the visualiser");

            // The guided tasks follow: gestures now act for real and the tutorial watches what happens.
            Assert.AreEqual(TutorialHUD.Mode.Guide, tutorial.CurrentMode);
            tutorial.guideDoneSeconds = 0;
            string Goal() => tutorial.CurrentGuideStep != null ? tutorial.CurrentGuideStep.goal : "";
            Assert.AreEqual(script.guides[0].title, title.text);
            StringAssert.Contains(script.guides[0].steps[0].text, body.text);
            Assert.IsTrue(lesson.Find("Video Panel").gameObject.activeSelf, "Each step shows its gesture clip");
            Assert.IsTrue(lesson.Find("Skip Gesture Button").gameObject.activeSelf);
            Assert.IsFalse(tutorial.TryHandleGesture("SWIPE_UP", TrackedHand.Right, out _), "Gestures reach the visualiser");
            Assert.AreEqual("screen.placed", Goal());

            // Guide 1: a screen appears, then goes. Placement itself has smoke coverage, so register a prefab.
            var prefab = AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab");
            ScreenSurface Place(int id)
            {
                var placed = Object.Instantiate(prefab); placed.Initialize(id, true);
                ((IList<ScreenSurface>)app.Screens).Add(placed);
                var camera = app.ARCamera.transform;
                placed.transform.SetPositionAndRotation(camera.position + camera.forward * 0.45f, camera.rotation);
                return placed;
            }
            var screen = Place(901);
            yield return Frames();
            Assert.AreEqual("screen.deleted", Goal());
            Assert.IsTrue(app.DeleteScreen(screen));
            yield return Frames();

            // Guide 2: with no screen left, it first asks for one; then select, resize and keep.
            Assert.AreEqual(1, tutorial.Guides.GuideIndex); Assert.AreEqual("screen.exists", Goal());
            screen = Place(902);
            yield return Frames();
            Assert.AreEqual("edit.resize", Goal());
            var edge = new Vector2(0.99f, 0.9f);
            Aim(app, screen.WorldPoint(edge));
            int steps = screen.SizeSteps;
            Assert.IsTrue(Gesture(app, "PINCH_AND_PULL").ok);
            yield return Frames();
            Assert.AreEqual("screen.resized", Goal());
            Assert.IsTrue(Gesture(app, "SWIPE_UP").ok); Assert.Greater(screen.SizeSteps, steps);
            yield return Frames();
            Assert.AreEqual("edit.kept", Goal());
            Assert.AreEqual(3, Lit(rightBar), "The bar under each hand shows the steps done in this guide");
            Assert.IsTrue(Gesture(app, "GRAB_AND_RELEASE").ok);
            yield return Frames();

            // Guide 3: this screen can rotate, so the preparation step is passed over. A cancelled edit starts again.
            Assert.AreEqual(2, tutorial.Guides.GuideIndex); Assert.AreEqual("edit.rotate", Goal());
            Aim(app, screen.WorldPoint(edge));
            Assert.IsTrue(Gesture(app, "PINCH_AND_ROTATE").ok);
            yield return Frames();
            Assert.AreEqual("screen.rotated", Goal());
            Assert.IsTrue(Gesture(app, "CRUMPLE_AND_TOSS").ok);
            yield return Frames();
            Assert.AreEqual("edit.rotate", Goal(), "Cancelling returns to selecting the screen");
            Assert.IsTrue(Gesture(app, "PINCH_AND_ROTATE").ok);
            yield return Frames();
            Assert.IsTrue(Gesture(app, "SWIPE_RIGHT").ok);
            yield return Frames();
            Assert.AreEqual("edit.kept", Goal());
            Assert.IsTrue(Gesture(app, "GRAB_AND_RELEASE").ok);
            yield return Frames();

            // Guide 4: settings open and close.
            Assert.AreEqual(3, tutorial.Guides.GuideIndex); Assert.AreEqual("settings.open", Goal());
            Aim(app, screen.WorldPoint(edge));
            Assert.IsTrue(Gesture(app, "WIGGLE_FINGERS").ok);
            yield return Frames();
            Assert.AreEqual("settings.closed", Goal());
            Assert.IsTrue(Gesture(app, "WIGGLE_FINGERS").ok);
            yield return Frames();

            // Guide 5: the tutorial opens the web browser on the empty screen, which then turns upright and back.
            Assert.AreEqual(4, tutorial.Guides.GuideIndex); Assert.AreEqual("screen.vertical", Goal());
            var browser = screen.GetComponent<ScreenBrowser>();
            Assert.IsTrue(browser != null && browser.IsOpen);
            deadline = Time.realtimeSinceStartup + 10;
            while (!browser.IsReady && Time.realtimeSinceStartup < deadline) yield return null;
            Aim(app, screen.WorldPoint(new Vector2(0.5f, 0.5f)));
            Assert.IsTrue(Gesture(app, "POINT_AND_ROTATE").ok); Assert.IsTrue(browser.IsVertical);
            yield return Frames();
            Assert.AreEqual("screen.horizontal", Goal());
            Aim(app, screen.WorldPoint(new Vector2(0.5f, 0.5f)));
            Assert.IsTrue(Gesture(app, "POINT_AND_ROTATE").ok); Assert.IsFalse(browser.IsVertical);
            yield return Frames();

            Assert.AreEqual(TutorialHUD.Mode.Complete, tutorial.CurrentMode);
            Assert.AreEqual(script.messages.completeTitle, title.text);
            Assert.IsFalse(lesson.Find("Video Panel").gameObject.activeSelf);
            Assert.IsFalse(hud.Find("Tutorial Right Hand").gameObject.activeSelf);
            app.DeleteScreen(screen);
            Gesture(app, "SWIPE_UP");
            Assert.AreEqual(TutorialHUD.Mode.Complete, tutorial.CurrentMode);

            // Swipe left: the tutorial leaves to the left while the free HUD arrives from the right.
            Gesture(app, "SWIPE_LEFT");
            Assert.AreEqual(TutorialHUD.Mode.Free, tutorial.CurrentMode);
            yield return null; yield return null;
            var slide = (RectTransform)hud.Find("Tutorial/Tutorial Slide");
            var safe = (RectTransform)hud.Find("Safe Area");
            if (tutorial.IsTransitioning)
            {
                Assert.Less(slide.anchoredPosition.x, 0); Assert.Greater(safe.anchoredPosition.x, 0);
                Assert.AreEqual(1, free.alpha); Assert.IsFalse(free.blocksRaycasts, "Not clickable until it has arrived");
            }
            deadline = Time.realtimeSinceStartup + 5;
            while (tutorial.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(tutorial.IsTransitioning);
            Assert.AreEqual(Vector2.zero, safe.anchoredPosition);
            Assert.IsTrue(free.blocksRaycasts && free.interactable);
            Assert.IsFalse(slide.gameObject.activeSelf);
            Assert.IsTrue(hud.Find("Tutorial/Tutorial Return/Return Bar").gameObject.activeInHierarchy);
            Capture(app, "ARVisualizer-tutorial-free");

            // Free mode: gestures act again, and a brief pass over the bar does not reopen the tutorial.
            app.RightHand.SetEditorSample(new Vector2(0.5f, 0.5f), null);
            Assert.IsFalse(tutorial.TryHandleGesture("CLICK", TrackedHand.Right, out _));
            var zone = (RectTransform)hud.Find("Tutorial/Tutorial Return/Pointing Area");
            Vector2 onBar = app.ARCamera.ScreenToViewportPoint(RectTransformUtility.WorldToScreenPoint(null, zone.TransformPoint(zone.rect.center)));
            app.RightHand.SetEditorSample(onBar, null);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.Greater(tutorial.ReturnDwell, 0); Assert.AreEqual(TutorialHUD.Mode.Free, tutorial.CurrentMode);
            Assert.IsTrue(hud.Find("Tutorial/Tutorial Return/Return Hint").gameObject.activeSelf);
            app.RightHand.SetEditorSample(new Vector2(0.5f, 0.5f), null);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(0, tutorial.ReturnDwell, 0.0001f);
            app.RightHand.SetEditorSample(onBar, null);
            deadline = Time.realtimeSinceStartup + script.returnHoldSeconds + 3;
            while (tutorial.CurrentMode == TutorialHUD.Mode.Free && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(TutorialHUD.Mode.Lesson, tutorial.CurrentMode, "Holding the bar reopens the tutorial");
            Assert.AreEqual(0, tutorial.Session.StepIndex);
            deadline = Time.realtimeSinceStartup + 5;
            while (tutorial.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(0, free.alpha); Assert.IsTrue(slide.gameObject.activeSelf);

            // Goggles: resting the ray on a small button presses it with no gesture, once per visit, and a tip says so.
            var holdTip = lesson.Find("Hold Tip");
            Assert.IsFalse(holdTip.gameObject.activeSelf, "The tip is for goggle mode only");
            script.buttonHoldSeconds = 0.8f;
            bool entered = false;
            app.Execute(new VisualizerCommand { command = "goggle.enter", hand = "right" }, reply => entered = reply.ok);
            Assert.IsTrue(entered);
            yield return null; yield return null;
            Assert.IsTrue(holdTip.gameObject.activeInHierarchy);
            StringAssert.Contains("0.8 s", holdTip.GetComponentInChildren<Text>().text);
            var skipRect = (RectTransform)lesson.Find("Skip Gesture Button");
            var skipFill = (RectTransform)skipRect.Find("Hold Track/Hold Progress");
            float fullest = 0;
            deadline = Time.realtimeSinceStartup + 6;
            while (tutorial.Session.StepIndex == 0 && Time.realtimeSinceStartup < deadline)
            {
                app.RightHand.SetEditorSample(app.ARCamera.WorldToViewportPoint(skipRect.TransformPoint(skipRect.rect.center)), null);
                yield return null;
                fullest = Mathf.Max(fullest, skipFill.anchorMax.x);
            }
            Assert.AreEqual(1, tutorial.Session.StepIndex, "Pointing at Skip for the hold time skips the gesture");
            Assert.Greater(fullest, 0.2f, "The bar in the button fills during the hold");
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.AreEqual(1, tutorial.Session.StepIndex, "The ray must leave the button before it presses again");
            Assert.AreEqual(0, skipFill.anchorMax.x);
            app.RightHand.SetEditorSample(new Vector2(0.5f, 0.5f), null);
            app.Execute(new VisualizerCommand { command = "goggle.exit", hand = "right" }, reply => { });
            yield return null; yield return null;
            Assert.IsFalse(holdTip.gameObject.activeSelf);
            Assert.AreEqual(TutorialHUD.Mode.Lesson, tutorial.CurrentMode);

            // The small buttons skip one gesture or leave at any time.
            lesson.Find("Skip Gesture Button").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(2, tutorial.Session.StepIndex);
            // Skipping the last gesture begins the guides, where the same button skips a whole guide.
            while (tutorial.CurrentMode == TutorialHUD.Mode.Lesson) tutorial.SkipGesture();
            Assert.AreEqual(TutorialHUD.Mode.Guide, tutorial.CurrentMode); Assert.AreEqual(0, tutorial.Guides.GuideIndex);
            tutorial.SkipGesture();
            Assert.AreEqual(1, tutorial.Guides.GuideIndex);
            lesson.Find("Exit Tutorial Button").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(TutorialHUD.Mode.Free, tutorial.CurrentMode);
            tutorial.CompleteTransition();
            Assert.IsTrue(free.blocksRaycasts);
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }
    }
}
