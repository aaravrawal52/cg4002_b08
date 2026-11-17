using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ARVisualizer
{
    /// <summary>
    /// Start-up choice, gesture tutorial, the guided tasks that follow it and the way back in. Geometry
    /// stays in the HUD prefab; wording lives in Resources/tutorial.json and the rules in
    /// <see cref="TutorialSession"/> and <see cref="TutorialGuideProgress"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AR Visualizer/Tutorial HUD")]
    public sealed class TutorialHUD : MonoBehaviour
    {
        public enum Mode { Choice, Lesson, Guide, Complete, Free }

        [Serializable]
        public sealed class HandWidget
        {
            [Tooltip("Follows this hand's marker, or waits at its dock while the hand is out of view.")]
            public RectTransform root;
            [Tooltip("Bottom to top. One lights up for each correct gesture.")]
            public Graphic[] segments = Array.Empty<Graphic>();
            public Text caption;
            public Graphic popupPanel;
            public Text popupText;
            public Vector2 dock = new Vector2(0.5f, 0.5f);
        }

        [Tooltip("Show the Tutorial / Free choice when the app starts. Off goes straight to free mode.")]
        public bool startWithModeChoice = true;
        [Tooltip("After a new message appears, gestures are ignored for this long so a repeated detection cannot skip it.")]
        [Min(0)] public float settleSeconds = 1;
        [Tooltip("A finished guide step shows DONE for this long before the next step appears.")]
        [Range(0, 3)] public float guideDoneSeconds = 0.9f;
        [SerializeField, HideInInspector] int layoutVersion;
        [Tooltip("Messages and gesture order. Defaults to Resources/tutorial.json.")]
        [SerializeField] TextAsset script;
        [Tooltip("Demonstration clips, matched to each step's \"video\" name.")]
        [SerializeField] VideoClip[] clips = Array.Empty<VideoClip>();

        [Header("Sliding roots")]
        [SerializeField] RectTransform container;
        [SerializeField] RectTransform tutorialRoot;
        [Tooltip("The free-mode HUD (Safe Area). It waits off-screen to the right during the tutorial.")]
        [SerializeField] RectTransform freeRoot;
        [SerializeField, Range(0.1f, 2)] float slideSeconds = 0.55f;

        [Header("Start-up choice")]
        [SerializeField] GameObject choicePanel;
        [SerializeField] Text choiceHeading, choiceSubheading, tutorialTitle, tutorialCaption, freeTitle, freeCaption, choiceHint;

        [Header("Lesson")]
        [SerializeField] GameObject lessonPanel;
        [SerializeField] GameObject videoPanel;
        [SerializeField] Text watchLabel;
        [SerializeField] RawImage videoImage;
        [SerializeField] AspectRatioFitter videoFitter;
        [SerializeField] Text stepLabel, statusLabel, titleLabel, bodyLabel, skipLabel, exitLabel;
        [SerializeField] Button skipButton;
        [Tooltip("One per gesture, in order.")]
        [SerializeField] Graphic[] stepPips = Array.Empty<Graphic>();

        [Header("Goggle mode: point and hold")]
        [Tooltip("In goggles, resting a hand's ray on Skip or Exit for the hold time presses it without a gesture.")]
        [SerializeField] Button exitButton;
        [Tooltip("Their right anchors follow the hold, from empty to full.")]
        [SerializeField] RectTransform skipHoldFill, exitHoldFill;
        [Tooltip("Small tip above the two buttons. Shown in goggle mode only.")]
        [SerializeField] GameObject holdTipPanel;
        [SerializeField] Text holdTip;

        [Header("Hand feedback")]
        [SerializeField] HandWidget leftHand = new HandWidget();
        [SerializeField] HandWidget rightHand = new HandWidget();
        [SerializeField, Range(0.5f, 5)] float popupSeconds = 1.6f;

        [Header("Return bar (free mode)")]
        [SerializeField] RectTransform returnRoot;
        [Tooltip("Pointing anywhere in this area counts; it can be wider than the visible bar.")]
        [SerializeField] RectTransform returnZone;
        [SerializeField] RectTransform returnFill;
        [SerializeField] GameObject returnHintPanel;
        [SerializeField] Text returnLabel, returnHint;

        [Header("Colours")]
        [SerializeField] Color accentColor = new Color(0.4f, 1, 0.8f);
        [SerializeField] Color warningColor = new Color(1, 0.72f, 0.3f);
        [SerializeField] Color idleColor = new Color(1, 1, 1, 0.18f);
        [SerializeField] Color darkTextColor = new Color(0.035f, 0.055f, 0.075f);

        public Mode CurrentMode { get; private set; } = Mode.Free;
        public TutorialSession Session { get; private set; }
        public TutorialScript Script { get; private set; }
        /// <summary>Position in the guided tasks. Null until the gesture lessons are finished.</summary>
        public TutorialGuideProgress Guides { get; private set; }
        public TutorialScript.Guide CurrentGuide => CurrentMode == Mode.Guide && Guides != null && !Guides.Finished
            ? Script.guides[Guides.GuideIndex] : null;
        public TutorialScript.GuideStep CurrentGuideStep => CurrentGuide != null ? CurrentGuide.steps[Guides.StepIndex] : null;
        public bool IsTransitioning => !Mathf.Approximately(blend, CurrentMode == Mode.Free ? 1 : 0);
        public int LayoutVersion => layoutVersion;
        /// <summary>Seconds a tracked hand has pointed at the return bar.</summary>
        public float ReturnDwell { get; private set; }
        /// <summary>Goggle mode: seconds a ray has rested on the Skip or Exit button.</summary>
        public float ButtonDwell { get; private set; }
        public string PopupText(TrackedHand hand) => Widget(hand).popupText != null && Time.unscaledTime < popupUntil[Index(hand)]
            ? Widget(hand).popupText.text : "";
        public bool HasRequiredReferences => container != null && tutorialRoot != null && freeRoot != null && choicePanel != null
            && lessonPanel != null && videoPanel != null && stepLabel != null && titleLabel != null && bodyLabel != null
            && statusLabel != null && leftHand.root != null && rightHand.root != null && returnRoot != null && returnZone != null;

        ARVisualizerApp app;
        GestureWebSocketReceiver receiver;
        CanvasGroup freeGroup;
        Canvas canvas;
        VideoPlayer player;
        RenderTexture videoTexture;
        float blend = 1; // 0: tutorial HUD in place. 1: free-mode HUD in place.
        float settleUntil;
        readonly float[] popupUntil = new float[2];
        // Guided tasks: what the visualiser looked like when the current step, or edit, began.
        readonly Dictionary<ScreenSurface, (int steps, Quaternion rotation)> editSnapshot = new Dictionary<ScreenSurface, (int, Quaternion)>();
        Button dwellButton;
        bool dwellSpent;
        int baselineScreens;
        float stepDoneAt = -1;
        bool browserSetupFailed;
        Color stepColor = Color.white;
        Vector2 phoneVideoPosition;
        bool videoLowered;

        /// <summary>Move the whole video card, keeping its heading and clip together without resizing it.</summary>
        public void SetGoggleLayout(bool enabled)
        {
            if (videoPanel == null) return;
            var rect = (RectTransform)videoPanel.transform;
            if (enabled)
            {
                if (!videoLowered) phoneVideoPosition = rect.anchoredPosition;
                rect.anchoredPosition = phoneVideoPosition + Vector2.down * (rect.rect.height * 0.5f);
            }
            else if (videoLowered) rect.anchoredPosition = phoneVideoPosition;
            videoLowered = enabled;
        }

        static int Index(TrackedHand hand) => hand == TrackedHand.Left ? 0 : 1;
        HandWidget Widget(TrackedHand hand) => hand == TrackedHand.Left ? leftHand : rightHand;

        public void Initialize(ARVisualizerApp application)
        {
            app = application;
            canvas = GetComponent<Canvas>();
            if (!HasRequiredReferences)
            {
                Debug.LogError("The saved HUD has no tutorial layout. Run Tools > AR Visualizer > Prepare tutorial mode.", this);
                enabled = false;
                return;
            }
            if (script == null) script = Resources.Load<TextAsset>("tutorial");
            Script = TutorialScript.Parse(script != null ? script.text : null);
            foreach (var candidate in FindObjectsByType<GestureWebSocketReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.CommandReceiver == app.Communication) { receiver = candidate; break; }
            freeGroup = freeRoot.GetComponent<CanvasGroup>();
            if (freeGroup == null) freeGroup = freeRoot.gameObject.AddComponent<CanvasGroup>();
            stepColor = stepLabel.color;
            // A HUD saved before the hold tip existed still gets the behaviour, without the tip and bars.
            if (exitButton == null && exitLabel != null) exitButton = exitLabel.GetComponentInParent<Button>(true);
            CreatePlayer();
            ApplyStaticText();
            Session = NewSession();
            CurrentMode = startWithModeChoice ? Mode.Choice : Mode.Free;
            blend = CurrentMode == Mode.Free ? 1 : 0;
            if (CurrentMode != Mode.Free) EnablePointers();
            RefreshPanels();
            ApplySlide();
        }

        TutorialSession NewSession()
        {
            var gestures = new string[Script.steps.Length];
            for (int i = 0; i < gestures.Length; i++) gestures[i] = Script.steps[i].gesture;
            return new TutorialSession(gestures, Script.repetitions, Script.exitGesture);
        }

        void CreatePlayer()
        {
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.source = VideoSource.VideoClip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.aspectRatio = VideoAspectRatio.Stretch;
            player.audioOutputMode = VideoAudioOutputMode.None;
            videoTexture = new RenderTexture(360, 640, 0) { name = "Tutorial video" };
            player.targetTexture = videoTexture;
            if (videoImage != null) videoImage.texture = videoTexture;
        }

        void ApplyStaticText()
        {
            var choice = Script.choice;
            var text = Script.messages;
            SetText(choiceHeading, Fill(choice.heading));
            SetText(choiceSubheading, Fill(choice.subheading));
            SetText(tutorialTitle, Fill(choice.tutorialTitle));
            SetText(tutorialCaption, Fill(choice.tutorialCaption));
            SetText(freeTitle, Fill(choice.freeTitle));
            SetText(freeCaption, Fill(choice.freeCaption));
            SetText(choiceHint, Fill(choice.hint));
            SetText(skipLabel, Fill(text.skip));
            SetText(exitLabel, Fill(text.exit));
            SetText(returnLabel, Fill(text.returnLabel));
        }

        // These methods appear in Button > On Click in Unity's Inspector.
        public void ChooseTutorial() => EnterTutorial();
        public void ChooseFree() => EnterFreeMode();
        public void ExitTutorial() => EnterFreeMode();

        public void EnterTutorial()
        {
            if (!enabled || app == null) return;
            Session = NewSession();
            Guides = null;
            stepDoneAt = -1;
            CurrentMode = Session.Phase == TutorialPhase.Complete ? Mode.Complete : Mode.Lesson;
            settleUntil = Time.unscaledTime + settleSeconds;
            popupUntil[0] = popupUntil[1] = 0;
            ReturnDwell = 0;
            EnablePointers();
            RefreshPanels();
        }

        public void EnterFreeMode()
        {
            if (!enabled || app == null) return;
            if (CurrentMode != Mode.Free && app.GoggleModeEnabled)
                GetComponent<VisualizerHUD>()?.SetHUDVisible(false);
            CurrentMode = Mode.Free;
            ReturnDwell = 0;
            RefreshPanels();
        }

        public void ShowModeChoice()
        {
            if (!enabled || app == null) return;
            CurrentMode = Mode.Choice;
            EnablePointers();
            RefreshPanels();
        }

        public void SkipGesture()
        {
            // During the guided tasks the same button leaves the current guide.
            if (CurrentMode == Mode.Guide && Guides != null)
            {
                Guides.SkipGuide();
                EnterGuideStep();
                return;
            }
            if (CurrentMode != Mode.Lesson) return;
            Apply(Session.Skip(), TrackedHand.Right, "", out _);
        }

        /// <summary>Finishes any slide immediately, for example before a screenshot.</summary>
        public void CompleteTransition()
        {
            blend = CurrentMode == Mode.Free ? 1 : 0;
            ApplySlide();
        }

        // The tutorial cannot be followed without seeing where each hand is.
        void EnablePointers()
        {
            // Enabling restarts a pointer's tracking, so leave one that is already on alone.
            if (!app.LeftHand.PointerEnabled) app.LeftHand.SetPointerEnabled(true);
            if (!app.RightHand.PointerEnabled) app.RightHand.SetPointerEnabled(true);
        }

        /// <summary>
        /// Gives the tutorial first refusal on a detected gesture. Returns false when the gesture should
        /// act on the visualiser as usual.
        /// </summary>
        public bool TryHandleGesture(string gesture, TrackedHand hand, out string message)
        {
            message = "";
            if (!enabled || app == null || Session == null) return false;
            gesture = TutorialSession.Normalize(gesture);
            if (CurrentMode == Mode.Free && !IsTransitioning) return false;
            // A click on one of the tutorial's own buttons is a button press, not practice.
            if (gesture == "CLICK" && PointsAtButton(hand)) return false;
            message = "Tutorial / changing mode";
            if (IsTransitioning) return true;
            message = "Choose tutorial mode or free mode first";
            if (CurrentMode == Mode.Choice) return gesture != "CLICK";
            message = "Tutorial / read the message, then carry on";
            if (Time.unscaledTime < settleUntil) return true;
            // Guided tasks are done for real: every gesture acts on the visualiser, and the tutorial watches the result.
            if (CurrentMode == Mode.Guide) return false;
            Apply(Session.Handle(gesture, hand), hand, gesture, out message);
            return true;
        }

        bool PointsAtButton(TrackedHand hand)
        {
            if (!app.GoggleModeEnabled) return false;
            using var scope = app.UseHand(hand);
            var button = app.GoggleHUD.HoveredButton;
            return button != null && button.transform.IsChildOf(container);
        }

        void Apply(TutorialVerdict verdict, TrackedHand hand, string gesture, out string message)
        {
            var text = Script.messages;
            switch (verdict)
            {
                case TutorialVerdict.Wrong:
                    message = Fill(text.wrong).Replace("{gesture}", gesture.Replace('_', ' '));
                    ShowPopup(hand, message, false);
                    return;
                case TutorialVerdict.Correct:
                    message = Fill(text.correct).Replace("{count}", Session.Correct(hand).ToString());
                    ShowPopup(hand, message, true);
                    return;
                case TutorialVerdict.Learned:
                    message = Fill(text.learned);
                    ShowPopup(hand, message, true);
                    break;
                case TutorialVerdict.NextStep:
                    message = Fill(text.next);
                    ShowPopup(hand, message, true);
                    break;
                case TutorialVerdict.Finished:
                    message = Fill(text.completeTitle);
                    CurrentMode = Mode.Complete;
                    if (Script.guides.Length > 0)
                    {
                        // Every gesture is known; now use them on real screens.
                        settleUntil = Time.unscaledTime + settleSeconds;
                        StartGuides();
                        if (CurrentGuide != null) message = CurrentGuide.title;
                        return;
                    }
                    break;
                case TutorialVerdict.Exit:
                    message = "Free mode";
                    EnterFreeMode();
                    return;
                default:
                    message = CurrentMode == Mode.Complete ? Fill(text.completeHint) : "";
                    return;
            }
            settleUntil = Time.unscaledTime + settleSeconds;
            RefreshPanels();
        }

        void StartGuides()
        {
            var counts = new int[Script.guides.Length];
            for (int i = 0; i < counts.Length; i++) counts[i] = Script.guides[i].steps.Length;
            Guides = new TutorialGuideProgress(counts);
            CurrentMode = Mode.Guide;
            browserSetupFailed = false;
            EnterGuideStep();
        }

        // Shows the current step, first passing over preparation steps whose goal already holds.
        void EnterGuideStep()
        {
            CurrentMode = Mode.Guide;
            while (!Guides.Finished)
            {
                var step = CurrentGuideStep;
                if (!step.skipIfMet) break;
                RunSetup(step);
                if (!GoalMet(step.goal)) break;
                Guides.CompleteStep();
            }
            stepDoneAt = -1;
            if (Guides.Finished)
            {
                CurrentMode = Mode.Complete;
                settleUntil = Time.unscaledTime + settleSeconds;
            }
            else baselineScreens = app.ScreenCount;
            RefreshPanels();
        }

        // Called every frame during the guided tasks: a step ends when its goal is seen in the visualiser.
        void WatchGuide()
        {
            var step = CurrentGuideStep;
            if (step == null) return;
            if (stepDoneAt >= 0)
            {
                if (Time.unscaledTime - stepDoneAt < guideDoneSeconds) return;
                Guides.CompleteStep();
                EnterGuideStep();
                return;
            }
            RunSetup(step);
            if (GoalMet(step.goal))
            {
                // Sizes and angles are compared with how the screens were when the edit began.
                if (step.goal == "edit.resize" || step.goal == "edit.rotate") TakeEditSnapshot();
                stepDoneAt = Time.unscaledTime;
                SetText(stepLabel, Fill(Script.messages.guideLabel) + "  ·  " + Fill(Script.messages.guideDone));
                stepLabel.color = accentColor;
                return;
            }
            // A cancelled edit, or one confirmed without a change, starts again from selecting the screen.
            bool editStep = step.goal == "screen.resized" || step.goal == "screen.rotated" || step.goal == "edit.kept";
            if (!editStep || HandEditing(TrackedHand.Left, null) || HandEditing(TrackedHand.Right, null) || EditChanged()) return;
            for (int back = Guides.StepIndex - 1; back >= 0; back--)
            {
                string goal = CurrentGuide.steps[back].goal;
                if (goal != "edit.resize" && goal != "edit.rotate") continue;
                Guides.Retreat(back);
                EnterGuideStep();
                return;
            }
        }

        bool GoalMet(string goal)
        {
            switch (goal)
            {
                case "screen.placed": return app.ScreenCount > baselineScreens;
                case "screen.deleted": return app.ScreenCount < baselineScreens;
                case "screen.exists": return app.ScreenCount > 0;
                case "screen.flat":
                    foreach (var screen in app.Screens) if (screen != null && screen.isActiveAndEnabled && screen.CanRotate) return true;
                    return false;
                case "edit.resize": return HandEditing(TrackedHand.Left, ScreenEditMode.Resize) || HandEditing(TrackedHand.Right, ScreenEditMode.Resize);
                case "edit.rotate": return HandEditing(TrackedHand.Left, ScreenEditMode.Rotate) || HandEditing(TrackedHand.Right, ScreenEditMode.Rotate);
                case "screen.resized": return EditChanged(rotation: false);
                case "screen.rotated": return EditChanged(size: false);
                case "edit.kept": return !HandEditing(TrackedHand.Left, null) && !HandEditing(TrackedHand.Right, null) && EditChanged();
                case "settings.open": return SettingsOpen();
                case "settings.closed": return !SettingsOpen();
                case "app.browser": return BrowserOpen(false);
                case "screen.vertical": return BrowserOpen(true);
                case "screen.horizontal": return BrowserOpen(false) && !BrowserOpen(true);
                default: return false;
            }
        }

        bool HandEditing(TrackedHand side, ScreenEditMode? mode)
        {
            using var scope = app.UseHand(side);
            return app.AdjustModeEnabled && (mode == null || app.EditMode == mode.Value);
        }

        bool SettingsOpen()
        {
            var left = app.MenuFor(TrackedHand.Left);
            var right = app.MenuFor(TrackedHand.Right);
            return (left != null && left.IsSettingsOpen) || (right != null && right.IsSettingsOpen);
        }

        bool BrowserOpen(bool vertical)
        {
            foreach (var screen in app.Screens)
            {
                var browser = screen != null ? screen.GetComponent<ScreenBrowser>() : null;
                if (browser != null && browser.IsOpen && (!vertical || browser.IsVertical)) return true;
            }
            return false;
        }

        void TakeEditSnapshot()
        {
            editSnapshot.Clear();
            foreach (var screen in app.Screens)
                if (screen != null) editSnapshot[screen] = (screen.SizeSteps, screen.transform.localRotation);
        }

        bool EditChanged(bool size = true, bool rotation = true)
        {
            foreach (var screen in app.Screens)
            {
                if (screen == null || !editSnapshot.TryGetValue(screen, out var before)) continue;
                if (size && screen.SizeSteps != before.steps) return true;
                if (rotation && Quaternion.Angle(screen.transform.localRotation, before.rotation) > 0.5f) return true;
            }
            return false;
        }

        // "browser": vertical mode needs a web browser or PDF screen, so open one on the newest empty screen.
        void RunSetup(TutorialScript.GuideStep step)
        {
            if (step.setup != "browser" || browserSetupFailed || BrowserOpen(false)) return;
            for (int i = app.Screens.Count - 1; i >= 0; i--)
            {
                var screen = app.Screens[i];
                if (screen == null || !screen.isActiveAndEnabled || screen.IsAdjusting) continue;
                var media = screen.GetComponent<ScreenMedia>();
                if (media == null || !media.IsBlank) continue;
                var browser = screen.GetComponent<ScreenBrowser>();
                if (browser == null) browser = screen.gameObject.AddComponent<ScreenBrowser>();
                // The app chooser that opens on an empty screen would otherwise stay over the page.
                app.MenuFor(TrackedHand.Left)?.Close();
                app.MenuFor(TrackedHand.Right)?.Close();
                browserSetupFailed = !browser.Open(app);
                return;
            }
        }

        void ShowPopup(TrackedHand hand, string message, bool good)
        {
            var widget = Widget(hand);
            if (widget.popupText == null) return;
            widget.popupText.text = message;
            widget.popupText.color = darkTextColor;
            if (widget.popupPanel != null) widget.popupPanel.color = good ? accentColor : warningColor;
            popupUntil[Index(hand)] = Time.unscaledTime + popupSeconds;
        }

        string Fill(string template)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var step = CurrentStep;
            bool guiding = Guides != null && !Guides.Finished;
            return template.Replace("{total}", Script.steps.Length.ToString())
                .Replace("{guides}", Script.guides.Length.ToString())
                .Replace("{guide}", (guiding ? Guides.GuideIndex + 1 : Mathf.Max(1, Script.guides.Length)).ToString())
                .Replace("{actions}", (guiding ? Guides.StepCount : 1).ToString())
                .Replace("{action}", (guiding ? Guides.StepIndex + 1 : 1).ToString())
                .Replace("{step}", Mathf.Min(Session != null ? Session.StepIndex + 1 : 1, Mathf.Max(1, Script.steps.Length)).ToString())
                .Replace("{required}", Script.repetitions.ToString())
                .Replace("{hold}", Script.returnHoldSeconds.ToString("0.#"))
                .Replace("{dwell}", Script.buttonHoldSeconds.ToString("0.#"))
                .Replace("{perform}", step != null ? step.perform : "")
                .Replace("{usage}", step != null ? step.usage : "");
        }

        TutorialScript.Step CurrentStep => Session != null && Session.StepIndex < Script.steps.Length ? Script.steps[Session.StepIndex] : null;

        void RefreshPanels()
        {
            choicePanel.SetActive(CurrentMode == Mode.Choice);
            bool lesson = CurrentMode == Mode.Lesson || CurrentMode == Mode.Guide || CurrentMode == Mode.Complete;
            lessonPanel.SetActive(lesson);
            var text = Script.messages;
            var step = CurrentMode == Mode.Lesson ? CurrentStep : null;
            var guideStep = CurrentGuideStep;
            string video = step != null ? step.video : guideStep != null ? guideStep.video : null;
            videoPanel.SetActive(!string.IsNullOrEmpty(video) && Play(video));
            if (string.IsNullOrEmpty(video) && player != null) player.Stop();
            if (skipButton != null) skipButton.gameObject.SetActive(CurrentMode == Mode.Lesson || CurrentMode == Mode.Guide);
            SetText(skipLabel, Fill(CurrentMode == Mode.Guide ? text.skipGuide : text.skip));
            stepLabel.color = stepColor;
            if (!lesson) return;

            if (CurrentMode == Mode.Complete)
            {
                SetText(stepLabel, Fill(text.completeLabel));
                SetText(titleLabel, Fill(text.completeTitle));
                SetText(bodyLabel, Fill(text.complete) + "\n" + Fill(text.completeHint));
            }
            else if (CurrentMode == Mode.Guide)
            {
                // The title names the task; the body says what to do now, beside a clip of the gesture to use.
                SetText(stepLabel, Fill(text.guideLabel));
                SetText(titleLabel, CurrentGuide.title);
                SetText(bodyLabel, string.IsNullOrEmpty(guideStep.title) ? guideStep.text : guideStep.title + "\n" + guideStep.text);
                SetText(watchLabel, Fill(text.guideWatch));
            }
            else
            {
                bool apply = Session.Phase == TutorialPhase.Apply;
                SetText(stepLabel, Fill(text.stepLabel) + "  ·  " + Fill(apply ? text.applyLabel : text.practiseLabel));
                SetText(titleLabel, step != null ? step.title : "");
                SetText(bodyLabel, Fill(apply ? text.apply : text.practise));
                SetText(watchLabel, Fill(text.watch));
            }
            // One pip per gesture during the lessons, one per guide during the guided tasks.
            bool guides = CurrentMode == Mode.Guide;
            int pips = guides ? Script.guides.Length : Script.steps.Length;
            int reached = guides ? Guides.GuideIndex : Session.StepIndex;
            for (int i = 0; i < stepPips.Length; i++)
            {
                if (stepPips[i] == null) continue;
                stepPips[i].gameObject.SetActive(i < pips);
                stepPips[i].color = i < reached ? accentColor : i == reached ? Color.white : idleColor;
            }
        }

        bool Play(string clipName)
        {
            var clip = Array.Find(clips, candidate => candidate != null
                && string.Equals(candidate.name, clipName, StringComparison.OrdinalIgnoreCase));
            if (clip == null || player == null) return false;
            if (player.clip != clip)
            {
                player.clip = clip;
                if (videoFitter != null && clip.height > 0) videoFitter.aspectRatio = (float)clip.width / clip.height;
            }
            if (!player.isPlaying) player.Play();
            return true;
        }

        void Update()
        {
            if (app == null || Session == null) return;
            float target = CurrentMode == Mode.Free ? 1 : 0;
            blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, slideSeconds));
            ApplySlide();
            RefreshStatus();
            if (CurrentMode == Mode.Guide && !IsTransitioning) WatchGuide();
            RefreshHoldButtons();
            RefreshHand(TrackedHand.Left);
            RefreshHand(TrackedHand.Right);
            RefreshReturnBar();
        }

        // The tutorial leaves to the left while the free-mode HUD arrives from the right, and back again.
        void ApplySlide()
        {
            float eased = Mathf.SmoothStep(0, 1, blend);
            float width = ((RectTransform)transform).rect.width;
            // Share the free HUD's safe-area fitting, in phone and goggle layouts alike.
            if (container.anchorMin != freeRoot.anchorMin) container.anchorMin = freeRoot.anchorMin;
            if (container.anchorMax != freeRoot.anchorMax) container.anchorMax = freeRoot.anchorMax;
            Place(tutorialRoot, -width * eased);
            Place(freeRoot, width * (1 - eased));
            Place(returnRoot, width * (1 - eased));
            if (tutorialRoot.gameObject.activeSelf != eased < 1) tutorialRoot.gameObject.SetActive(eased < 1);
            if (returnRoot.gameObject.activeSelf != eased > 0) returnRoot.gameObject.SetActive(eased > 0);
            float alpha = eased > 0 ? 1 : 0;
            bool usable = blend >= 1;
            if (freeGroup.alpha != alpha) freeGroup.alpha = alpha;
            if (freeGroup.interactable != usable) freeGroup.interactable = usable;
            if (freeGroup.blocksRaycasts != usable) freeGroup.blocksRaycasts = usable;
        }

        static void Place(RectTransform rect, float x)
        {
            var position = new Vector2(x, 0);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
        }

        void RefreshStatus()
        {
            bool waiting = receiver != null && receiver.isActiveAndEnabled && !receiver.IsConnected;
            if (statusLabel.gameObject.activeSelf != waiting) statusLabel.gameObject.SetActive(waiting);
            if (waiting) { statusLabel.text = Script.messages.notConnected; statusLabel.color = warningColor; }
        }

        void RefreshHand(TrackedHand side)
        {
            var widget = Widget(side);
            bool show = (CurrentMode == Mode.Lesson || CurrentMode == Mode.Guide) && blend < 0.5f;
            if (widget.root.gameObject.activeSelf != show) widget.root.gameObject.SetActive(show);
            if (!show) return;

            var pointer = app.Hands.GetHand(side);
            bool tracked = pointer.IsTracked;
            Vector2 point = tracked ? pointer.ViewportPoint : widget.dock;
            if (tracked && app.GoggleModeEnabled && !app.GoggleHUD.ViewportToPanel(point, out point))
            {
                tracked = false;
                point = widget.dock;
            }
            if (widget.root.anchorMin != point || widget.root.anchorMax != point) widget.root.anchorMin = widget.root.anchorMax = point;
            var text = Script.messages;
            string handName = side == TrackedHand.Left ? text.handLeft : text.handRight;
            if (tracked) SetText(widget.caption, handName);
            else
            {
                // Built once per hand: an untracked hand would otherwise allocate this every frame.
                int index = Index(side);
                if (hiddenCaptionFor[index] != handName) { hiddenCaptionFor[index] = handName; hiddenCaption[index] = handName + "  ·  " + text.handHidden; }
                SetText(widget.caption, hiddenCaption[index]);
            }

            // Lessons: one part per correct gesture by this hand. Guided tasks: the steps of the guide done so far.
            bool guiding = CurrentMode == Mode.Guide && Guides != null && !Guides.Finished;
            int done = guiding ? Guides.StepIndex + (stepDoneAt >= 0 ? 1 : 0) : Session.Correct(side);
            int needed = guiding ? Mathf.Max(1, Guides.StepCount) : Session.Required;
            for (int i = 0; i < widget.segments.Length; i++)
                if (widget.segments[i] != null)
                    widget.segments[i].color = i * needed < done * widget.segments.Length ? accentColor : idleColor;

            float left = popupUntil[Index(side)] - Time.unscaledTime;
            bool popup = left > 0 && widget.popupPanel != null;
            if (widget.popupPanel != null && widget.popupPanel.gameObject.activeSelf != popup) widget.popupPanel.gameObject.SetActive(popup);
            if (!popup) return;
            float alpha = Mathf.Clamp01(left / 0.3f);
            SetAlpha(widget.popupPanel, alpha * 0.95f);
            SetAlpha(widget.popupText, alpha);
        }

        readonly string[] hiddenCaptionFor = new string[2], hiddenCaption = new string[2];

        // Goggle mode: a ray resting on Skip or Exit presses it after a few seconds, with no gesture at all.
        void RefreshHoldButtons()
        {
            bool active = app.GoggleModeEnabled && !IsTransitioning
                && (CurrentMode == Mode.Lesson || CurrentMode == Mode.Guide || CurrentMode == Mode.Complete);
            Button pointed = null;
            if (active)
            {
                pointed = PointedHoldButton(TrackedHand.Right);
                if (pointed == null) pointed = PointedHoldButton(TrackedHand.Left);
            }
            if (pointed != dwellButton)
            {
                dwellButton = pointed;
                ButtonDwell = 0;
                dwellSpent = false;
            }
            else if (pointed != null && !dwellSpent) ButtonDwell += Time.unscaledDeltaTime;

            float hold = Script.buttonHoldSeconds;
            bool counting = pointed != null && !dwellSpent;
            SetHoldFill(skipHoldFill, counting && pointed == skipButton ? ButtonDwell / hold : 0);
            SetHoldFill(exitHoldFill, counting && pointed == exitButton ? ButtonDwell / hold : 0);
            if (holdTipPanel != null && holdTipPanel.activeSelf != active) holdTipPanel.SetActive(active);
            if (active && holdTip != null)
            {
                int seconds = Mathf.CeilToInt(Mathf.Max(0, hold - ButtonDwell));
                if (!counting) { if (shownHoldSeconds != -1) { shownHoldSeconds = -1; holdTip.text = Fill(Script.messages.holdTip); } }
                else if (seconds != shownHoldSeconds || pointed != shownHoldButton)
                {
                    shownHoldSeconds = seconds;
                    var caption = pointed == skipButton ? skipLabel : exitLabel;
                    holdTip.text = Fill(Script.messages.holdCountdown).Replace("{button}", caption != null ? caption.text : "")
                        .Replace("{seconds}", seconds.ToString());
                }
                shownHoldButton = pointed;
            }
            else shownHoldSeconds = -2;
            if (!counting || ButtonDwell < hold) return;
            // One press per visit: the ray has to leave the button before it can press again.
            dwellSpent = true;
            ButtonDwell = 0;
            pointed.onClick.Invoke();
        }
        int shownHoldSeconds = -2;
        Button shownHoldButton;

        Button PointedHoldButton(TrackedHand side)
        {
            using var scope = app.UseHand(side);
            // The centre fallback is not a hand, so it cannot hold a button.
            if (!app.PointerReady || !app.Hand.IsTracked) return null;
            var button = app.GoggleHUD.HoveredButton;
            if (button == null || !button.isActiveAndEnabled) return null;
            return button == skipButton || button == exitButton ? button : null;
        }

        static void SetHoldFill(RectTransform fill, float amount)
        {
            if (fill == null) return;
            var anchor = new Vector2(Mathf.Clamp01(amount), 1);
            if (fill.anchorMax != anchor) fill.anchorMax = anchor;
        }

        // Pointing a tracked hand at the left-edge bar for a few seconds reopens the tutorial.
        void RefreshReturnBar()
        {
            float hold = Script.returnHoldSeconds;
            bool over = false;
            if (CurrentMode == Mode.Free && !IsTransitioning)
            {
                Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                over = PointsAtReturnBar(TrackedHand.Left, camera) || PointsAtReturnBar(TrackedHand.Right, camera);
                ReturnDwell = over ? ReturnDwell + Time.unscaledDeltaTime : Mathf.Max(0, ReturnDwell - Time.unscaledDeltaTime * 2);
            }
            else ReturnDwell = 0;

            if (returnFill != null)
            {
                var fill = new Vector2(1, Mathf.Clamp01(ReturnDwell / hold));
                if (returnFill.anchorMax != fill) returnFill.anchorMax = fill;
            }
            bool hint = ReturnDwell > 0;
            if (returnHintPanel != null && returnHintPanel.activeSelf != hint) returnHintPanel.SetActive(hint);
            if (hint) SetText(returnHint, Fill(Script.messages.returnHint).Replace("{seconds}", Mathf.CeilToInt(Mathf.Max(0, hold - ReturnDwell)).ToString()));
            if (over && ReturnDwell >= hold) EnterTutorial();
        }

        bool PointsAtReturnBar(TrackedHand side, Camera camera)
        {
            using var scope = app.UseHand(side);
            // The centre fallback is not a hand, so it cannot hold the bar.
            if (!app.PointerReady || !app.Hand.IsTracked) return false;
            Vector2 point = app.ARCamera.ViewportToScreenPoint(app.PointerViewportPoint);
            return RectTransformUtility.RectangleContainsScreenPoint(returnZone, point, camera);
        }

        static void SetText(Text label, string value)
        {
            if (label != null && label.text != value) label.text = value;
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        void OnDestroy()
        {
            if (videoTexture == null) return;
            if (player != null) player.targetTexture = null;
            videoTexture.Release();
            Destroy(videoTexture);
        }
    }
}
