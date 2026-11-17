using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>One persistent timer, flashcard or puzzle session per screen, shared by both hands.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ScreenMedia))]
    public sealed class ScreenAppContent : MonoBehaviour
    {
        [Serializable] public sealed class Card { public string german, english; }
        [Serializable] public sealed class Deck { public Card[] cards; }
        [SerializeField] ScreenAppView viewPrefab;
        [SerializeField] TextAsset germanVocabulary;
        [SerializeField, Min(0.001f)] float faceOffset = 0.003f;

        public CountdownTimer Timer { get; private set; } = new CountdownTimer();
        public ScreenAppView View { get; private set; }
        public int CardIndex { get; private set; }
        public bool AnswerVisible { get; private set; }
        /// <summary>The slider puzzle. Reopening the app deals a new game at the last board size.</summary>
        public SlidingPuzzle Puzzle { get; private set; } = new SlidingPuzzle();
        public int CardCount => cards.Length;
        public string AppId => media != null ? media.AppId : "";
        public bool IsOpen => View != null && View.gameObject.activeSelf;
        /// <summary>The timer's current look. It is kept when this screen's timer is reopened.</summary>
        public int TimerStyleIndex { get; private set; }
        public int TimerStyleCount => View != null ? View.timerStyles.Length : 0;
        public TimerPhase Phase => Timer.Finished ? TimerPhase.Finished : Timer.IsRunning ? TimerPhase.Running
            : Timer.IsPaused ? TimerPhase.Paused : TimerPhase.Ready;
        /// <summary>The most recent sound effect, whether or not the volume let it be heard.</summary>
        public AppSound? LastSound { get; private set; }
        public int SoundsPlayed { get; private set; }

        // A finished countdown rings a few times, then waits quietly to be reset.
        const int AlarmRepeats = 5;
        const double AlarmInterval = 0.9;
        const int CountdownTicks = 3;
        AudioSource speaker;
        int tickedSecond = -1, alarmsLeft;
        double nextAlarm;

        static readonly int[] DigitSteps = { 600, 60, 10, 1 };
        /// <summary>Apps drawn by ScreenAppView on the screen face itself.</summary>
        public static bool IsBuiltIn(string id) => id == "timer" || id == "flashcards" || id == "puzzle";
        ScreenSurface surface;
        ScreenMedia media;
        ARVisualizerApp app;
        Card[] cards = Array.Empty<Card>();
        readonly bool[] touching = new bool[2];
        readonly Button[] hovered = new Button[2];

        void Awake()
        {
            surface = GetComponent<ScreenSurface>();
            media = GetComponent<ScreenMedia>();
            media.Cleared += Close;
            surface.HandPointerClicked += OnClick;
            surface.HandInputChanged += OnInputChanged;
        }

        public bool Open(string id, ARVisualizerApp owner)
        {
            if (!IsBuiltIn(id)) return false;
            if (viewPrefab == null) viewPrefab = Resources.Load<ScreenAppView>("ScreenAppView");
            if (viewPrefab == null) return false;
            if (!viewPrefab.IsComplete)
            {
                Debug.LogError("ScreenAppView.prefab predates the timer looks. Run Tools > AR Visualizer > Prepare timer and flashcard apps.");
                return false;
            }
            if (id == "puzzle" && !viewPrefab.HasPuzzle)
            {
                Debug.LogError("ScreenAppView.prefab predates the slider puzzle. Run Tools > AR Visualizer > Prepare timer and flashcard apps.");
                return false;
            }
            app = owner;
            media.BeginApp(id);
            if (View == null)
            {
                View = Instantiate(viewPrefab, transform);
                BindButtons();
            }
            Timer = new CountdownTimer();
            CardIndex = 0;
            AnswerVisible = false;
            if (id == "flashcards") LoadCards();
            if (id == "puzzle") Puzzle = new SlidingPuzzle(Puzzle.Size);
            View.gameObject.SetActive(true);
            if (id == "timer") View.ShowTimer(TimerStyleIndex);
            else if (id == "puzzle") View.ShowPuzzle();
            else View.ShowFlashcards();
            TimerStyleIndex = View.TimerStyleIndex;
            ApplyFace();
            Refresh();
            return true;
        }

        void LoadCards()
        {
            if (germanVocabulary == null) germanVocabulary = Resources.Load<TextAsset>("german_vocabulary");
            try { cards = JsonUtility.FromJson<Deck>(germanVocabulary != null ? germanVocabulary.text : "{}")?.cards ?? Array.Empty<Card>(); }
            catch (ArgumentException) { cards = Array.Empty<Card>(); }
            cards = Array.FindAll(cards, card => card != null && !string.IsNullOrWhiteSpace(card.german)
                && !string.IsNullOrWhiteSpace(card.english));
        }

        void BindButtons()
        {
            foreach (var style in View.timerStyles)
                for (int i = 0; i < style.digits.Length; i++)
                {
                    int digit = i;
                    style.digits[i].onClick.AddListener(() => AdjustDigit(digit, 1));
                }
            View.cardBody.onClick.AddListener(Flip);
            if (View.puzzleBody != null) View.puzzleBody.onClick.AddListener(() => { TogglePuzzle(); Refresh(); });
        }

        static double Now => Time.realtimeSinceStartupAsDouble;
        void AdjustDigit(int digit, int direction)
        {
            int before = Timer.Seconds(Now);
            Timer.Adjust(DigitSteps[digit] * direction, Now);
            // The limits (00:00 and 99:59) answer with a dull knock instead of a step.
            Play(Timer.PresetSeconds == before ? AppSound.Blocked : direction > 0 ? AppSound.Up : AppSound.Down);
            Refresh();
        }
        void Flip() { ShowAnswer(!AnswerVisible); Refresh(); }
        void ShowAnswer(bool visible)
        {
            if (AnswerVisible == visible) return;
            AnswerVisible = visible;
            Play(visible ? AppSound.Reveal : AppSound.Hide);
        }
        void NextCard(int direction)
        {
            if (cards.Length == 0) return;
            CardIndex = (CardIndex + direction + cards.Length) % cards.Length;
            AnswerVisible = false;
            Play(direction > 0 ? AppSound.Next : AppSound.Previous);
            Refresh();
        }
        void TogglePuzzle()
        {
            bool wasPlaying = Puzzle.Phase == PuzzlePhase.Playing;
            Puzzle.Toggle(Now);
            Play(wasPlaying ? AppSound.Pause : AppSound.Start);
        }
        void ToggleTimer()
        {
            // With no buttons on the face, the same action also clears a finished countdown.
            if (Timer.Finished) { ResetTimer(); return; }
            bool wasRunning = Timer.IsRunning;
            Timer.Toggle(Now);
            Play(Timer.IsRunning ? AppSound.Start : wasRunning ? AppSound.Pause : AppSound.Blocked);
        }
        void ResetTimer()
        {
            Timer.Reset();
            Silence(); // Cut a ringing alarm short.
            Play(AppSound.Reset);
        }

        void Play(AppSound sound)
        {
            LastSound = sound;
            ++SoundsPlayed;
            float volume = AppSounds.MasterVolume;
            if (volume <= 0 || !Application.isPlaying) return;
            if (speaker == null)
            {
                // A child, so the video player's own AudioSource on this screen is left alone.
                var holder = new GameObject("App sounds");
                holder.transform.SetParent(transform, false);
                speaker = holder.AddComponent<AudioSource>();
                speaker.playOnAwake = false;
                // Mostly positional: with headphones a sound comes from its own screen.
                speaker.spatialBlend = 0.6f;
                speaker.dopplerLevel = 0;
                speaker.minDistance = 1;
                speaker.maxDistance = 12;
            }
            speaker.PlayOneShot(AppSounds.Clip(sound), Mathf.Clamp01(volume));
        }

        void Silence() { if (speaker != null) speaker.Stop(); }

        // The last seconds tick; reaching zero rings until reset, a few times at most.
        void TimerSounds(double now)
        {
            int seconds = Timer.Seconds(now);
            if (Timer.IsRunning)
            {
                if (tickedSecond >= 0 && seconds != tickedSecond && seconds <= CountdownTicks) Play(AppSound.Tick);
                tickedSecond = seconds;
                alarmsLeft = AlarmRepeats;
                nextAlarm = 0;
                return;
            }
            tickedSecond = -1;
            if (!Timer.Finished) alarmsLeft = 0;
            else if (alarmsLeft > 0 && now >= nextAlarm)
            {
                --alarmsLeft;
                nextAlarm = now + AlarmInterval;
                Play(AppSound.Alarm);
            }
        }

        /// <summary>Shows another timer look and gives the screen that look's shape. The countdown carries on.</summary>
        public bool SetTimerStyle(int index)
        {
            if (!IsOpen || AppId != "timer") return false;
            View.ShowTimer(index);
            TimerStyleIndex = View.TimerStyleIndex;
            ApplyFace();
            Refresh();
            return true;
        }

        // The screen keeps its width and anchored centre; its height and outline follow the app.
        void ApplyFace()
        {
            media.SetFaceShape(View.FaceAspect, View.FaceCornerRadius);
            Layout();
        }

        public bool Execute(string command, Vector2 uv, out string message)
        {
            message = "This action is unavailable in this app";
            if (!IsOpen) return false;
            if (AppId == "timer")
            {
                if (command == "app.increase" || command == "app.decrease")
                {
                    // Swipes change the digit this hand points towards; the gaps belong to the nearest digit.
                    int digit = View.NearestDigit(surface, uv);
                    if (digit < 0) return false;
                    AdjustDigit(digit, command == "app.increase" ? 1 : -1);
                }
                else if (command == "app.previous" || command == "app.next")
                {
                    if (SetTimerStyle(TimerStyleIndex + (command == "app.next" ? 1 : -1))) Play(AppSound.Look);
                }
                else if (command == "app.startpause" || command == "app.primary") ToggleTimer();
                else if (command == "app.reset") ResetTimer();
                else return false;
                Refresh();
                int seconds = Timer.Seconds(Now);
                message = "Timer / " + View.ActiveStyle.title + " / " + (seconds / 60).ToString("D2") + ":"
                    + (seconds % 60).ToString("D2") + " " + Phase.ToString().ToLowerInvariant();
                return true;
            }
            if (AppId == "puzzle") return ExecutePuzzle(command, out message);
            if (AppId != "flashcards" || cards.Length == 0) return false;
            switch (command)
            {
                case "app.previous": NextCard(-1); break;
                case "app.next": NextCard(1); break;
                case "app.reveal": ShowAnswer(true); break;
                case "app.hide": ShowAnswer(false); break;
                case "app.primary": ShowAnswer(!AnswerVisible); break;
                default: return false;
            }
            Refresh();
            message = "German / card " + (CardIndex + 1) + " of " + cards.Length;
            return true;
        }

        // While playing, the four swipes slide a tile into the gap. Otherwise up / down change the
        // board size and left deals a new game, so a stray swipe never disturbs a game in progress.
        bool ExecutePuzzle(string command, out string message)
        {
            message = "This action is unavailable in this app";
            int x = command == "app.next" ? 1 : command == "app.previous" ? -1 : 0;
            int y = command == "app.decrease" ? 1 : command == "app.increase" ? -1 : 0;
            if (command == "app.primary" || command == "app.startpause") TogglePuzzle();
            else if (command == "app.reset")
            {
                message = "Pause the puzzle before resetting it";
                if (!Puzzle.Reset()) { Play(AppSound.Blocked); return false; }
                Play(AppSound.Shuffle);
            }
            else if (x == 0 && y == 0) return false;
            else if (Puzzle.Phase == PuzzlePhase.Playing)
            {
                message = "No tile can slide that way";
                if (!Puzzle.Slide(x, y, Now)) { Play(AppSound.Blocked); return false; }
                Play(Puzzle.Phase == PuzzlePhase.Solved ? AppSound.Solved : AppSound.Slide);
            }
            else if (y != 0)
            {
                message = "The puzzle is already " + Puzzle.Size + " x " + Puzzle.Size;
                if (!Puzzle.Resize(-y)) { Play(AppSound.Blocked); return false; }
                Play(y < 0 ? AppSound.Up : AppSound.Down);
            }
            else if (x < 0)
            {
                Puzzle.Reset();
                Play(AppSound.Shuffle);
            }
            else
            {
                message = "Click to play / swipe up or down for size, left to reset";
                Play(AppSound.Blocked);
                return false;
            }
            Refresh();
            message = "Puzzle / " + Puzzle.Size + " x " + Puzzle.Size + " / " + Puzzle.Moves + " moves / "
                + Puzzle.Phase.ToString().ToLowerInvariant();
            return true;
        }

        void Update()
        {
            if (!IsOpen) return;
            Layout();
            if (AppId == "timer") { Refresh(); TimerSounds(Now); }
            else if (AppId == "puzzle") View.RenderPuzzle(Puzzle, Now, Time.unscaledDeltaTime);
            else View.AnimateCard(Time.unscaledDeltaTime);
            UpdateHand(TrackedHand.Left, 0);
            UpdateHand(TrackedHand.Right, 1);
            View.Highlight(hovered[0], hovered[1]);
        }

        void Layout()
        {
            View.transform.localPosition = Vector3.back * faceOffset;
            View.transform.localRotation = Quaternion.identity;
            View.transform.localScale = Vector3.one * (surface.Width / ((RectTransform)View.transform).rect.width);
        }

        bool CanInteract(TrackedHand hand) => app != null && app.CanInteractWithScreenApp(surface, hand);

        void OnInputChanged(ScreenSurface screen, TrackedHand hand, ScreenInputKind kind, Vector2 uv)
        {
            if (IsOpen) UpdateHand(hand, hand == TrackedHand.Left ? 0 : 1);
        }

        void UpdateHand(TrackedHand hand, int index)
        {
            hovered[index] = null;
            var pointer = app != null ? app.Hands.GetHand(hand) : null;
            // Missing marker/depth samples do not rearm a held fingertip. Require withdrawal.
            if (touching[index] && pointer != null && pointer.HasTipDepth
                && !surface.TryTouch(pointer.TipWorldPosition, app.ScreenTouchTolerance * 2, out _, out _)) touching[index] = false;
            if (!CanInteract(hand)) return;
            var input = surface.GetHandInput(hand);
            if (input.Kind == ScreenInputKind.None) return;
            var button = View.ButtonAt(surface, input.UV);
            // A ray lights the digit its swipes would change, even from a gap or margin.
            hovered[index] = AppId == "timer" && input.Kind == ScreenInputKind.Ray
                ? View.ActiveStyle.digits[View.NearestDigit(surface, input.UV)] : button;
            if (input.Kind != ScreenInputKind.Touch || touching[index] || button == null) return;
            touching[index] = true;
            button.onClick.Invoke();
        }

        void OnClick(ScreenSurface screen, TrackedHand hand, PointerEventData.InputButton button, Vector2 uv)
        {
            if (!IsOpen || !CanInteract(hand) || touching[hand == TrackedHand.Left ? 0 : 1]
                || surface.GetHandInput(hand).Kind == ScreenInputKind.Touch) return;
            if (button == PointerEventData.InputButton.Left) View.ButtonAt(surface, uv)?.onClick.Invoke();
        }

        void Refresh()
        {
            if (!IsOpen) return;
            if (AppId == "timer")
            {
                double now = Now;
                int seconds = Timer.Seconds(now);
                View.RenderTimer(seconds, Timer.Fraction(now), Phase, now);
            }
            else if (AppId == "puzzle") View.RenderPuzzle(Puzzle, Now, 0);
            else
            {
                var card = cards.Length > 0 ? cards[CardIndex] : null;
                View.RenderCard(card?.german, card?.english, CardIndex, cards.Length, AnswerVisible);
            }
        }

        void Close()
        {
            Timer.Reset();
            Silence();
            alarmsLeft = 0;
            tickedSecond = -1;
            if (View != null) View.gameObject.SetActive(false);
            Array.Clear(hovered, 0, hovered.Length);
        }

        void OnDestroy()
        {
            if (media != null) media.Cleared -= Close;
            if (surface != null) surface.HandPointerClicked -= OnClick;
            if (surface != null) surface.HandInputChanged -= OnInputChanged;
        }
    }
}
