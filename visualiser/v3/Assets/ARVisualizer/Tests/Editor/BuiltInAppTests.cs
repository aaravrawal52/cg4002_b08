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
    public sealed class BuiltInAppTests
    {
        [Test]
        public void CountdownPausesResumesAndFinishesWithoutFrameDrift()
        {
            var timer = new CountdownTimer();
            timer.Toggle(10);
            Assert.AreEqual(60, timer.Seconds(10.1));
            timer.Toggle(11.25);
            Assert.AreEqual(59, timer.Seconds(100));
            timer.Toggle(100);
            Assert.AreEqual(1, timer.Seconds(158));
            Assert.AreEqual(0, timer.Seconds(158.75));
            Assert.IsTrue(timer.Finished);
            Assert.IsFalse(timer.IsRunning);
            timer.Toggle(200);
            Assert.IsFalse(timer.IsRunning, "Zero needs a reset or a new duration");
            timer.Reset();
            Assert.AreEqual(60, timer.Seconds(250));
            Assert.IsFalse(timer.Finished);
        }

        [Test]
        public void ProgressAndPauseStateFollowTheCountdown()
        {
            var timer = new CountdownTimer();
            Assert.AreEqual(1, timer.Fraction(0));
            Assert.IsFalse(timer.IsPaused, "A full, unstarted timer is ready rather than paused");
            timer.Toggle(0);
            Assert.That(timer.Fraction(15), Is.EqualTo(0.75f).Within(0.0001f));
            timer.Toggle(30);
            Assert.IsTrue(timer.IsPaused);
            Assert.That(timer.Fraction(500), Is.EqualTo(0.5f).Within(0.0001f));
            timer.Adjust(10, 500);
            Assert.IsFalse(timer.IsPaused, "Editing makes the shown time the new full duration");
            Assert.AreEqual(1, timer.Fraction(500));
            timer.Toggle(500);
            Assert.AreEqual(0, timer.Fraction(600));
            Assert.IsTrue(timer.Finished && !timer.IsPaused);
            timer.Adjust(-1000, 600);
            Assert.AreEqual(0, timer.Fraction(600), "A zero duration has nothing left to show");
        }

        [Test]
        public void EverySoundEffectIsShortAudibleAndFreeOfClicks()
        {
            foreach (AppSound sound in System.Enum.GetValues(typeof(AppSound)))
            {
                var samples = AppSounds.Samples(sound);
                Assert.That(samples.Length, Is.InRange(AppSounds.SampleRate / 50, AppSounds.SampleRate), sound + " lasts under a second");
                float peak = 0;
                foreach (float sample in samples)
                {
                    Assert.IsTrue(float.IsFinite(sample) && Mathf.Abs(sample) <= 1, sound + " stays within range");
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }
                Assert.That(peak, Is.InRange(0.15f, 0.95f), sound + " is audible without clipping");
                Assert.That(Mathf.Abs(samples[0]), Is.LessThan(0.02f), sound + " starts from silence");
                Assert.That(Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(0.02f), sound + " ends in silence");
                CollectionAssert.AreEqual(samples, AppSounds.Samples(sound), sound + " sounds the same every time");
            }
            Assert.AreNotEqual(AppSounds.Samples(AppSound.Up), AppSounds.Samples(AppSound.Down));
        }

        [Test]
        public void SavedAppViewHasNoButtonsBesideTheDigitsAndCard()
        {
            var view = AssetDatabase.LoadAssetAtPath<ScreenAppView>("Assets/ARVisualizer/Resources/ScreenAppView.prefab");
            Assert.IsNotNull(view);
            Assert.IsTrue(view.IsComplete, "Run Tools > AR Visualizer > Prepare timer and flashcard apps");
            Assert.GreaterOrEqual(view.timerStyles.Length, 3);
            // Four digit targets per look, plus the card and the puzzle board: no Start, Reset, Previous, Reveal or Next.
            Assert.AreEqual(view.timerStyles.Length * 4 + 2, view.GetComponentsInChildren<Button>(true).Length);
            Assert.IsTrue(view.HasPuzzle, "Run Tools > AR Visualizer > Prepare timer and flashcard apps");
            Assert.AreEqual(24, view.puzzleTiles.Length);
            var board = ((RectTransform)view.puzzleBody.transform).rect;
            Assert.Greater(board.width * board.height / (ScreenAppView.CanvasWidth * ScreenAppView.CanvasWidth / view.puzzleAspect), 0.75f,
                "The board fills most of the puzzle face");
            var shapes = new HashSet<string>();
            foreach (var style in view.timerStyles)
            {
                Assert.AreEqual(4, style.digits.Length, style.title);
                float height = ScreenAppView.CanvasWidth / style.aspect, area = 0;
                for (int i = 0; i < 4; i++)
                {
                    Assert.IsNotNull(style.digitLabels[i], style.title);
                    Assert.IsTrue(style.digits[i].transform.IsChildOf(style.root.transform), style.title);
                    var rect = ((RectTransform)style.digits[i].transform).rect;
                    area += rect.width * rect.height;
                }
                Assert.Greater(area / (ScreenAppView.CanvasWidth * height), 0.2f, style.title + " digits are the main content");
                Assert.IsNotNull(style.status, style.title);
                Assert.IsTrue(style.ring != null || style.bar != null, style.title + " shows the remaining time");
                shapes.Add(style.aspect.ToString("F2") + "/" + style.cornerRadius.ToString("F2"));
            }
            Assert.AreEqual(view.timerStyles.Length, shapes.Count, "Each look has its own screen shape");
            var card = ((RectTransform)view.cardBody.transform);
            Assert.AreEqual(Vector2.zero, card.anchorMin); Assert.AreEqual(Vector2.one, card.anchorMax);
            Assert.Less(-card.sizeDelta.x, 60, "The card fills the face apart from a slim margin");
        }

        [Test]
        public void SavedMediaPanelIsCompact()
        {
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARVisualizer/Resources/ScreenAppMenu.prefab").transform;
            var panel = (RectTransform)menu.Find("Photo library");
            var viewport = (RectTransform)panel.Find("Media viewport");
            Assert.Greater(viewport.rect.width * viewport.rect.height / (panel.rect.width * panel.rect.height), 0.75f,
                "Thumbnails take most of the panel");
            float top = panel.rect.yMax;
            foreach (var name in new[] { "Title", "Refresh library", "Close library" })
            {
                var rect = (RectTransform)panel.Find(name);
                Assert.That(rect.anchoredPosition.y + rect.rect.height * 0.5f, Is.EqualTo(top).Within(12), name + " shares the header row");
            }
            var tile = (RectTransform)viewport.Find("Content/Media 1");
            var thumbnail = (RectTransform)tile.Find("Thumbnail area");
            Assert.Greater(thumbnail.rect.width * thumbnail.rect.height / (tile.rect.width * tile.rect.height), 0.75f);
        }

        [Test]
        public void EditingPausesClampsAndUpdatesTheResetDuration()
        {
            var timer = new CountdownTimer();
            timer.Toggle(0);
            timer.Adjust(10, 5.5);
            Assert.AreEqual(65, timer.Seconds(100));
            Assert.IsFalse(timer.IsRunning);
            timer.Toggle(100); timer.Toggle(110); timer.Reset();
            Assert.AreEqual(65, timer.Seconds(120));
            timer.Adjust(-1000, 120);
            Assert.AreEqual(0, timer.Seconds(120));
            timer.Adjust(10000, 120);
            Assert.AreEqual(5999, timer.Seconds(120));
        }

        [Test]
        public void GestureContextPreservesExplicitCommandsAndHandIdentity()
        {
            Assert.IsTrue(CommandProtocol.TryParseGesture("swipe_up", "left", out var gesture, out _));
            Assert.AreEqual("gesture.up", gesture.command);
            Assert.AreEqual("left", gesture.hand);
            Assert.AreEqual("app.increase", CommandProtocol.CommandForAppGesture("timer", gesture.sourceGesture));
            Assert.AreEqual("app.reveal", CommandProtocol.CommandForAppGesture("flashcards", gesture.sourceGesture));
            Assert.IsNull(CommandProtocol.CommandForAppGesture("photos", gesture.sourceGesture));
            Assert.IsTrue(CommandProtocol.TryParse("left.settings.enter", out var explicitCommand, out _));
            Assert.IsNull(explicitCommand.sourceGesture);
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            foreach (var entry in catalogue.appGestures)
                Assert.IsTrue(CommandProtocol.TryParse("right." + entry.command, out _, out var error), error);
        }

        static int Inversions(SlidingPuzzle puzzle)
        {
            int count = 0, cells = puzzle.Size * puzzle.Size;
            for (int i = 0; i < cells; i++)
                for (int j = i + 1; j < cells; j++)
                    if (puzzle.TileAt(i) != 0 && puzzle.TileAt(j) != 0 && puzzle.TileAt(i) > puzzle.TileAt(j)) count++;
            return count;
        }

        [Test]
        public void PuzzleDealsAreShuffledCompleteAndSolvable([Values(3, 4, 5)] int size, [Values(1, 7, 42)] int seed)
        {
            var puzzle = new SlidingPuzzle(size, seed);
            Assert.AreEqual(size, puzzle.Size);
            Assert.AreEqual(PuzzlePhase.Ready, puzzle.Phase);
            Assert.IsFalse(puzzle.IsSolved);
            var seen = new HashSet<int>();
            for (int i = 0; i < size * size; i++) seen.Add(puzzle.TileAt(i));
            Assert.AreEqual(size * size, seen.Count);
            Assert.AreEqual(0, puzzle.TileAt(puzzle.BlankIndex));
            // Odd widths need an even inversion count; even widths also count the gap's row from the bottom.
            int parity = Inversions(puzzle) + (size % 2 == 0 ? size - puzzle.BlankIndex / size : 0);
            Assert.AreEqual(size % 2 == 0 ? 1 : 0, parity % 2, "The deal must be reachable from the solved board");
        }

        [Test]
        public void PuzzleSlidesOnlyWhilePlayingAndCountsPlayingTime()
        {
            // The gap is in the middle of the bottom row; tile 8 is to its right.
            var puzzle = new SlidingPuzzle(3, new[] { 1, 2, 3, 4, 5, 6, 7, 0, 8 });
            Assert.IsFalse(puzzle.Slide(-1, 0, 0), "Ready: swipes do not move tiles");
            puzzle.Toggle(10);
            Assert.AreEqual(PuzzlePhase.Playing, puzzle.Phase);
            Assert.IsFalse(puzzle.Slide(0, -1, 11), "Nothing is below the bottom row to slide up");
            Assert.IsFalse(puzzle.Resize(1) || puzzle.Reset(), "A game in progress is protected");
            Assert.IsTrue(puzzle.Slide(0, 1, 12), "Tile 5 slides down into the gap");
            Assert.AreEqual(4, puzzle.BlankIndex); Assert.AreEqual(5, puzzle.TileAt(7));
            Assert.IsTrue(puzzle.Slide(0, -1, 13)); Assert.AreEqual(2, puzzle.Moves);
            puzzle.Toggle(20);
            Assert.AreEqual(PuzzlePhase.Paused, puzzle.Phase);
            Assert.AreEqual(10, puzzle.Elapsed(500), "The clock stops while paused");
            Assert.IsFalse(puzzle.Slide(-1, 0, 500));
            puzzle.Toggle(600);
            Assert.IsTrue(puzzle.Slide(-1, 0, 605), "Tile 8 slides left into the gap");
            Assert.IsTrue(puzzle.IsSolved); Assert.AreEqual(PuzzlePhase.Solved, puzzle.Phase);
            Assert.AreEqual(3, puzzle.Moves); Assert.AreEqual(15, puzzle.Elapsed(9999));
            Assert.IsFalse(puzzle.Slide(1, 0, 9999), "A solved board stays solved");
            puzzle.Toggle(10000);
            Assert.AreEqual(PuzzlePhase.Playing, puzzle.Phase, "Click on a solved board starts the next game");
            Assert.IsFalse(puzzle.IsSolved); Assert.AreEqual(0, puzzle.Moves); Assert.AreEqual(0, puzzle.Elapsed(10000));
        }

        [Test]
        public void PuzzleSizeAndResetChangeOnlyWhenNotPlaying()
        {
            var puzzle = new SlidingPuzzle(3, 5);
            Assert.IsFalse(puzzle.Resize(-1), "3 x 3 is the smallest board");
            Assert.IsTrue(puzzle.Resize(1)); Assert.AreEqual(4, puzzle.Size);
            Assert.IsTrue(puzzle.Resize(1)); Assert.AreEqual(5, puzzle.Size);
            Assert.IsFalse(puzzle.Resize(1), "5 x 5 is the largest board");
            Assert.AreEqual(0, puzzle.TileAt(puzzle.BlankIndex));
            puzzle.Toggle(0); puzzle.Toggle(3);
            Assert.IsTrue(puzzle.Reset());
            Assert.AreEqual(PuzzlePhase.Ready, puzzle.Phase); Assert.AreEqual(0, puzzle.Elapsed(50));
            Assert.AreEqual(5, puzzle.Size, "Reset keeps the board size");
            Assert.AreEqual(3, new SlidingPuzzle(1).Size); Assert.AreEqual(5, new SlidingPuzzle(9).Size);
        }

        static CommandReply Send(ARVisualizerApp app, string name, TrackedHand hand = TrackedHand.Right, bool gesture = false)
        {
            var command = new VisualizerCommand { command = name, hand = hand == TrackedHand.Left ? "left" : "right" };
            if (gesture) Assert.IsTrue(CommandProtocol.TryParseGesture(name, command.hand, out command, out _));
            CommandReply reply = null;
            app.Execute(command, result => reply = result);
            Assert.IsNotNull(reply);
            return reply;
        }

        static void Aim(ARVisualizerApp app, Vector3 point, TrackedHand hand = TrackedHand.Right, Vector3? tip = null)
        {
            app.Hands.GetHand(hand).SetEditorSample(app.ARCamera.WorldToViewportPoint(point), tip);
            Send(app, "status", hand);
        }
        static Vector3 Centre(Component component)
        {
            var rect = (RectTransform)component.transform;
            return rect.TransformPoint(rect.rect.center);
        }
        static void Draw(ARVisualizerApp app, string capture = null)
        {
            Canvas.ForceUpdateCanvases();
            var camera = app.ARCamera; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24); target.Create();
            try
            {
                camera.targetTexture = target; camera.Render();
                if (capture == null) return;
                RenderTexture.active = target;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/" + capture + ".png", image.EncodeToPNG());
                Object.Destroy(image);
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; target.Release(); Object.Destroy(target); }
        }

        static void Choose(ARVisualizerApp app, ScreenSurface screen, string title)
        {
            // The larger chooser covers the face centre; the outer border is still reachable.
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.9f)));
            Assert.IsTrue(Send(app, "app.choose").ok);
            Draw(app);
            var option = app.ScreenApps.transform.Find("Screen launcher/App options/" + title).GetComponent<Button>();
            Aim(app, Centre(option));
            Assert.AreEqual(title, app.ScreenApps.TargetName);
            Assert.IsTrue(Send(app, "ui.click").ok);
        }

        [UnityTest]
        public IEnumerator TimerAndFlashcardsWorkThroughTheScreenInputPath()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            // These checks exercise free mode; the start-up tutorial choice has its own tests.
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            yield return ExerciseApps();
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        static IEnumerator ExerciseApps()
        {
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(app != null && ARSession.state == ARSessionState.SessionTracking);
            // Register real screen prefabs at a deterministic viewing pose; placement itself has smoke coverage.
            var prefab = AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab");
            var screen = Object.Instantiate(prefab); screen.Initialize(801, true);
            ((IList<ScreenSurface>)app.Screens).Add(screen);
            var camera = app.ARCamera.transform;
            screen.transform.SetPositionAndRotation(camera.position + camera.forward * 0.45f, camera.rotation);
            Send(app, "pointer.on");
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.9f)));
            Assert.IsTrue(app.ScreenApps.IsChooserOpen, "Empty screen opens the chooser automatically");
            Draw(app, "ARVisualizer-app-chooser");
            var optionRect = (RectTransform)app.ScreenApps.transform.Find("Screen launcher/App options/Timer");
            Assert.Greater(optionRect.rect.width * optionRect.lossyScale.x, screen.Width * 0.85f);
            Choose(app, screen, "Timer");
            var content = screen.GetComponent<ScreenAppContent>();
            Assert.AreEqual("timer", content.AppId);
            Assert.IsTrue(content.IsOpen);
            int[] steps = { 600, 60, 10, 1 };
            for (int i = 0; i < 4; i++)
            {
                Aim(app, Centre(content.View.ActiveStyle.digits[i]));
                int before = content.Timer.PresetSeconds;
                Assert.IsTrue(Send(app, "SWIPE_UP", gesture: true).ok);
                Assert.AreEqual(before + steps[i], content.Timer.PresetSeconds);
                Assert.AreEqual(AppSound.Up, content.LastSound);
                Assert.IsFalse(app.AdjustModeEnabled);
                Assert.IsTrue(Send(app, "SWIPE_DOWN", gesture: true).ok);
                Assert.AreEqual(before, content.Timer.PresetSeconds);
                Assert.AreEqual(AppSound.Down, content.LastSound);
            }
            Draw(app, "ARVisualizer-timer");
            // The gaps and margins belong to the nearest digit, so the hand need not be exact.
            Aim(app, screen.WorldPoint(new Vector2(0.01f, 0.5f)));
            Assert.IsTrue(Send(app, "SWIPE_UP", gesture: true).ok);
            Assert.AreEqual(660, content.Timer.PresetSeconds);
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.5f)));
            Assert.IsTrue(Send(app, "SWIPE_DOWN", gesture: true).ok);
            Assert.AreEqual(659, content.Timer.PresetSeconds);
            Send(app, "SWIPE_UP", gesture: true);
            Aim(app, screen.WorldPoint(new Vector2(0.01f, 0.5f)));
            Send(app, "SWIPE_DOWN", gesture: true);
            Assert.AreEqual(60, content.Timer.PresetSeconds);

            // Left / right swipes change the look and the shape of the screen, never the time.
            Assert.GreaterOrEqual(content.TimerStyleCount, 3);
            float width = screen.Width; var centre = screen.transform.position;
            for (int step = 1; step <= content.TimerStyleCount; step++)
            {
                Aim(app, screen.WorldPoint(new Vector2(0.5f, 0.5f)));
                Assert.IsTrue(Send(app, "SWIPE_RIGHT", gesture: true).ok);
                Assert.AreEqual(step % content.TimerStyleCount, content.TimerStyleIndex);
                Assert.AreEqual(AppSound.Look, content.LastSound);
                var look = content.View.ActiveStyle;
                for (int i = 0; i < content.TimerStyleCount; i++)
                    Assert.AreEqual(i == content.TimerStyleIndex, content.View.timerStyles[i].root.activeInHierarchy);
                Assert.That(screen.Width / screen.Height, Is.EqualTo(look.aspect).Within(0.001f), look.title);
                Assert.That(screen.GetComponent<ScreenMedia>().CornerRadius, Is.EqualTo(look.cornerRadius).Within(0.0001f), look.title);
                Assert.AreEqual(width, screen.Width); Assert.AreEqual(centre, screen.transform.position);
                var face = (RectTransform)content.View.transform;
                Assert.That(face.rect.height * face.lossyScale.y, Is.EqualTo(screen.Height).Within(0.0001f), look.title);
                Assert.AreEqual(60, content.Timer.PresetSeconds, look.title);
                for (int i = 0; i < 4; i++)
                {
                    Aim(app, Centre(look.digits[i]));
                    Assert.IsTrue(Send(app, "SWIPE_UP", gesture: true).ok, look.title);
                    Assert.AreEqual(60 + steps[i], content.Timer.PresetSeconds, look.title + " digit " + i);
                    Assert.IsTrue(Send(app, "SWIPE_DOWN", gesture: true).ok, look.title);
                }
                Draw(app, "ARVisualizer-timer-" + look.title.ToLowerInvariant());
            }
            Assert.AreEqual(0, content.TimerStyleIndex, "The looks wrap around");
            Aim(app, screen.WorldPoint(new Vector2(0.5f, 0.5f)));
            Send(app, "SWIPE_LEFT", gesture: true);
            Assert.AreEqual(content.TimerStyleCount - 1, content.TimerStyleIndex);
            Send(app, "SWIPE_RIGHT", gesture: true);
            Assert.AreEqual(0, content.TimerStyleIndex);

            // Click starts and pauses; there are no buttons on the face.
            Aim(app, Centre(content.View.ActiveStyle.digits[1]));
            Assert.IsTrue(Send(app, "CLICK", gesture: true).ok);
            Assert.IsTrue(content.Timer.IsRunning);
            Assert.AreEqual(TimerPhase.Running, content.Phase);
            app.RightHand.SetEditorSample(new Vector2(0.9f, 0.9f), null);
            Send(app, "status");
            Assert.AreNotEqual(screen, app.RayScreen);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.Less(content.Timer.Seconds(Time.realtimeSinceStartupAsDouble), 60);
            Aim(app, Centre(content.View.ActiveStyle.digits[3]));
            Assert.IsTrue(Send(app, "CLICK", gesture: true).ok);
            Assert.AreEqual(TimerPhase.Paused, content.Phase);
            Send(app, "app.reset");
            Assert.IsFalse(content.Timer.IsRunning);
            Assert.AreEqual(60, content.Timer.Seconds(Time.realtimeSinceStartupAsDouble));
            Assert.AreEqual(TimerPhase.Ready, content.Phase);

            // Explicit settings commands keep their meaning; gesture rotation remains available there.
            Assert.IsTrue(Send(app, "settings.enter").ok);
            int preset = content.Timer.PresetSeconds;
            Send(app, "SWIPE_UP", gesture: true);
            Assert.AreEqual(preset, content.Timer.PresetSeconds);
            Assert.IsTrue(Send(app, "SWIPE_RIGHT", gesture: true).ok);
            Send(app, "adjust.exit");
            screen.transform.rotation = camera.rotation;

            // Touch activates once per contact, even across missing depth samples.
            var touch = Centre(content.View.ActiveStyle.digits[3]);
            Aim(app, touch, tip: touch); yield return null; yield return null;
            Assert.AreEqual(preset + 1, content.Timer.PresetSeconds);
            Aim(app, touch); yield return null;
            Aim(app, touch, tip: touch); yield return null; yield return null;
            Assert.AreEqual(preset + 1, content.Timer.PresetSeconds);
            Aim(app, touch, tip: touch + screen.FrontNormal * 0.06f); yield return null;
            Aim(app, touch, tip: touch); yield return null; yield return null;
            Assert.AreEqual(preset + 2, content.Timer.PresetSeconds);
            Aim(app, touch, tip: touch + screen.FrontNormal * 0.06f); yield return null;
            Aim(app, touch);

            Choose(app, screen, "German flashcards");
            Assert.AreEqual("flashcards", content.AppId);
            Assert.AreEqual(32, content.CardCount);
            Assert.AreEqual("der Apfel", content.View.cardWord.text);
            Aim(app, Centre(content.View.cardBody));
            Send(app, "SWIPE_UP", gesture: true);
            Assert.AreEqual("apple", content.View.cardAnswer.text);
            Assert.AreEqual(AppSound.Reveal, content.LastSound);
            int heard = content.SoundsPlayed;
            Send(app, "SWIPE_UP", gesture: true);
            Assert.AreEqual(heard, content.SoundsPlayed, "Revealing a visible answer again stays quiet");
            Assert.That(screen.Width / screen.Height, Is.EqualTo(content.View.cardAspect).Within(0.001f));
            StringAssert.StartsWith("DER", content.View.cardKind.text);
            Assert.AreEqual("1 / 32", content.View.cardCounter.text);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(content.View.cardAnswer.color.a, Is.EqualTo(1).Within(0.001f), "The answer fades in");
            Assert.That(content.View.cardHint.color.a, Is.EqualTo(0).Within(0.001f));
            Draw(app, "ARVisualizer-flashcards");
            Send(app, "SWIPE_RIGHT", gesture: true);
            Assert.AreEqual(1, content.CardIndex); Assert.IsFalse(content.AnswerVisible);
            Assert.AreEqual(AppSound.Next, content.LastSound);
            Assert.AreEqual(0, content.View.cardAnswer.color.a, "The next card never flashes its answer");
            StringAssert.StartsWith("DAS", content.View.cardKind.text);
            Send(app, "SWIPE_LEFT", gesture: true);
            Assert.AreEqual(0, content.CardIndex);
            Assert.AreEqual(AppSound.Previous, content.LastSound);
            Send(app, "CLICK", gesture: true); Assert.IsTrue(content.AnswerVisible);
            Send(app, "SWIPE_DOWN", gesture: true); Assert.IsFalse(content.AnswerVisible);
            Assert.AreEqual(AppSound.Hide, content.LastSound);
            Send(app, "app.previous"); Assert.AreEqual(31, content.CardIndex);
            Send(app, "app.next"); Assert.AreEqual(0, content.CardIndex);

            Assert.IsTrue(Send(app, "goggle.enter").ok);
            Aim(app, Centre(content.View.cardBody));
            Send(app, "SWIPE_UP", gesture: true); Assert.IsTrue(content.AnswerVisible);
            Send(app, "goggle.exit");

            var second = Object.Instantiate(prefab); second.Initialize(802, true);
            ((IList<ScreenSurface>)app.Screens).Add(second);
            second.transform.SetPositionAndRotation(screen.transform.position + camera.right * 0.19f, camera.rotation);
            var other = second.GetComponent<ScreenAppContent>();
            Assert.IsTrue(other.Open("timer", app));
            Send(app, "pointer.on", TrackedHand.Left);
            Aim(app, Centre(other.View.ActiveStyle.digits[0]), TrackedHand.Left);
            Assert.IsTrue(Send(app, "SWIPE_UP", TrackedHand.Left, true).ok);
            Assert.AreEqual(660, other.Timer.PresetSeconds);
            Assert.AreEqual("flashcards", content.AppId); Assert.AreEqual(0, content.CardIndex);
            Aim(app, Centre(content.View.cardBody));
            Send(app, "app.next"); Assert.AreEqual(1, content.CardIndex);
            Assert.AreEqual(660, other.Timer.PresetSeconds);

            // Replacing and clearing an app release its visible content and re-enable blank-screen selection.
            var media = screen.GetComponent<ScreenMedia>();
            Assert.IsTrue(media.ShowPhoto(new Texture2D(8, 4)));
            Assert.IsFalse(content.IsOpen);
            Assert.AreEqual("photos", media.AppId);
            Assert.AreEqual(0, media.CornerRadius, "Photos and videos keep a plain rectangle");
            media.Clear(); app.ScreenApps.Close();
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.9f)));
            Assert.IsTrue(app.ScreenApps.IsChooserOpen);
            Send(app, "screens.clear");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SliderPuzzleFollowsClicksAndSwipes()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            // These checks exercise free mode; the start-up tutorial choice has its own tests.
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(app != null && ARSession.state == ARSessionState.SessionTracking);
            var screen = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab"));
            screen.Initialize(811, true);
            ((IList<ScreenSurface>)app.Screens).Add(screen);
            var camera = app.ARCamera.transform;
            screen.transform.SetPositionAndRotation(camera.position + camera.forward * 0.5f, camera.rotation);
            Send(app, "pointer.on");
            Aim(app, screen.WorldPoint(new Vector2(0.99f, 0.9f)));
            Choose(app, screen, "Slider puzzle");
            var content = screen.GetComponent<ScreenAppContent>();
            Assert.AreEqual("puzzle", content.AppId);
            var view = content.View;
            Assert.IsTrue(view.puzzlePanel.activeInHierarchy && !view.timerPanel.activeSelf && !view.flashcardPanel.activeSelf);
            Assert.That(screen.Width / screen.Height, Is.EqualTo(view.puzzleAspect).Within(0.001f));
            var face = screen.WorldPoint(new Vector2(0.5f, 0.4f));
            Aim(app, face);
            Assert.AreEqual(PuzzlePhase.Ready, content.Puzzle.Phase);
            Assert.IsTrue(view.puzzleOverlay.activeSelf);
            Draw(app, "ARVisualizer-puzzle-ready");

            // Not playing: up / down change the size, left deals again, and tiles stay put.
            Assert.IsTrue(Send(app, "SWIPE_UP", gesture: true).ok); Assert.AreEqual(4, content.Puzzle.Size);
            Assert.AreEqual(AppSound.Up, content.LastSound);
            Assert.IsTrue(Send(app, "SWIPE_UP", gesture: true).ok); Assert.AreEqual(5, content.Puzzle.Size);
            Assert.IsFalse(Send(app, "SWIPE_UP", gesture: true).ok);
            Assert.AreEqual(AppSound.Blocked, content.LastSound);
            for (int i = 0; i < 24; i++) Assert.IsTrue(view.puzzleTiles[i].gameObject.activeSelf);
            Assert.AreEqual("5 × 5", view.puzzleSize.text);
            Send(app, "SWIPE_DOWN", gesture: true); Send(app, "SWIPE_DOWN", gesture: true);
            Assert.AreEqual(3, content.Puzzle.Size);
            Assert.IsFalse(Send(app, "SWIPE_DOWN", gesture: true).ok);
            Assert.IsFalse(view.puzzleTiles[8].gameObject.activeSelf);
            Assert.IsFalse(Send(app, "SWIPE_RIGHT", gesture: true).ok, "Right has no meaning until the game starts");
            Assert.IsTrue(Send(app, "SWIPE_LEFT", gesture: true).ok);
            Assert.AreEqual(PuzzlePhase.Ready, content.Puzzle.Phase);
            Assert.AreEqual(AppSound.Shuffle, content.LastSound);

            // Click starts; a swipe then slides the tile beside the gap in that direction.
            Assert.IsTrue(Send(app, "CLICK", gesture: true).ok);
            Assert.AreEqual(PuzzlePhase.Playing, content.Puzzle.Phase);
            Assert.AreEqual(AppSound.Start, content.LastSound);
            Assert.IsFalse(view.puzzleOverlay.activeSelf);
            int gap = content.Puzzle.BlankIndex;
            bool hasTileBelow = gap / 3 < 2;
            Assert.AreEqual(hasTileBelow, Send(app, "SWIPE_UP", gesture: true).ok);
            Assert.AreEqual(hasTileBelow ? AppSound.Slide : AppSound.Blocked, content.LastSound);
            Assert.AreEqual(hasTileBelow ? gap + 3 : gap, content.Puzzle.BlankIndex);
            Assert.AreEqual(3, content.Puzzle.Size, "Swipes never resize a game in progress");
            gap = content.Puzzle.BlankIndex;
            bool hasTileLeft = gap % 3 > 0;
            Assert.AreEqual(hasTileLeft, Send(app, "SWIPE_RIGHT", gesture: true).ok);
            Assert.AreEqual(hasTileLeft ? gap - 1 : gap, content.Puzzle.BlankIndex);
            int moves = content.Puzzle.Moves;
            Assert.AreEqual((hasTileBelow ? 1 : 0) + (hasTileLeft ? 1 : 0), moves);
            yield return new WaitForSecondsRealtime(0.6f);
            for (int tile = 1; tile <= 8; tile++)
            {
                int index = content.Puzzle.IndexOf(tile);
                var rect = view.puzzleTiles[tile - 1].rectTransform;
                float pitch = (view.puzzleBoard.rect.width + view.puzzleGap) / 3;
                Assert.That(Vector2.Distance(rect.anchoredPosition, new Vector2((index % 3 - 1) * pitch, (1 - index / 3) * pitch)),
                    Is.LessThan(0.01f), "Tile " + tile + " rests in its cell");
                Assert.That(rect.rect.width, Is.EqualTo(pitch - view.puzzleGap).Within(0.01f));
            }
            Draw(app, "ARVisualizer-puzzle-playing");

            // Pause protects the board; left then deals a new game.
            Aim(app, face);
            Assert.IsTrue(Send(app, "CLICK", gesture: true).ok);
            Assert.AreEqual(PuzzlePhase.Paused, content.Puzzle.Phase);
            Assert.AreEqual(AppSound.Pause, content.LastSound);
            Assert.AreEqual("PAUSED", view.puzzleBanner.text);
            Assert.AreEqual(moves, content.Puzzle.Moves);
            Assert.IsTrue(Send(app, "CLICK", gesture: true).ok);
            Assert.AreEqual(PuzzlePhase.Playing, content.Puzzle.Phase);
            Assert.IsFalse(Send(app, "app.reset").ok, "Reset waits for a pause");
            Send(app, "CLICK", gesture: true);
            Assert.IsTrue(Send(app, "SWIPE_LEFT", gesture: true).ok);
            Assert.AreEqual(PuzzlePhase.Ready, content.Puzzle.Phase); Assert.AreEqual(0, content.Puzzle.Moves);
            Assert.IsTrue(Send(app, "SWIPE_UP", gesture: true).ok);
            Assert.AreEqual(4, content.Puzzle.Size);
            Draw(app, "ARVisualizer-puzzle-4x4");

            // The size is kept for this screen; other apps and a cleared screen return to their own shapes.
            Assert.IsTrue(content.Open("puzzle", app)); Assert.AreEqual(4, content.Puzzle.Size);
            Assert.IsTrue(content.Open("timer", app));
            Assert.IsFalse(view.puzzlePanel.activeSelf);
            Assert.That(screen.Width / screen.Height, Is.EqualTo(view.ActiveStyle.aspect).Within(0.001f));

            // A countdown ticks through its last seconds, rings at zero and falls quiet when reset.
            content.Timer.Adjust(2 - content.Timer.PresetSeconds, Time.realtimeSinceStartupAsDouble);
            var middle = new Vector2(0.5f, 0.5f);
            Assert.IsTrue(content.Execute("app.primary", middle, out _));
            Assert.AreEqual(AppSound.Start, content.LastSound);
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(AppSound.Tick, content.LastSound);
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsTrue(content.Timer.Finished);
            Assert.AreEqual(AppSound.Alarm, content.LastSound);
            Assert.IsTrue(content.Execute("app.primary", middle, out _));
            Assert.AreEqual(AppSound.Reset, content.LastSound);
            int rung = content.SoundsPlayed;
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.AreEqual(rung, content.SoundsPlayed, "A reset timer stays silent");
            Send(app, "screens.clear");
            yield return null;
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }

        [UnityTest]
        public IEnumerator EnlargedChooserKeepsDualHandInteractionsIndependent()
        {
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
            foreach (var receiver in Object.FindObjectsByType<GestureWebSocketReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
            // These checks exercise free mode; the start-up tutorial choice has its own tests.
            foreach (var tutorial in Object.FindObjectsByType<TutorialHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tutorial.startWithModeChoice = false;
            yield return new EnterPlayMode();
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (ARSession.state != ARSessionState.SessionTracking && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(ARSessionState.SessionTracking, ARSession.state);
            yield return DualHandInteractionChecks.Exercise(app, includePlacement: false);
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/ARVisualizer/Scenes/ARVisualizer.unity");
        }
    }
}
