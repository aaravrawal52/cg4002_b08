using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>One persistent timer or flashcard session per screen, shared by both hands.</summary>
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
        public int CardCount => cards.Length;
        public string AppId => media != null ? media.AppId : "";
        public bool IsOpen => View != null && View.gameObject.activeSelf;

        static readonly int[] DigitSteps = { 600, 60, 10, 1 };
        ScreenSurface surface;
        ScreenMedia media;
        ARVisualizerApp app;
        Card[] cards = Array.Empty<Card>();
        readonly bool[] touching = new bool[2];
        readonly Button[] hovered = new Button[2];
        readonly int[] selectedDigit = { -1, -1 };
        public int SelectedDigit(TrackedHand hand) => selectedDigit[hand == TrackedHand.Left ? 0 : 1];

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
            if (id != "timer" && id != "flashcards") return false;
            if (viewPrefab == null) viewPrefab = Resources.Load<ScreenAppView>("ScreenAppView");
            if (viewPrefab == null) return false;
            app = owner;
            media.BeginApp(id);
            if (View == null)
            {
                View = Instantiate(viewPrefab, transform);
                BindButtons();
            }
            Timer = new CountdownTimer();
            selectedDigit[0] = selectedDigit[1] = -1;
            CardIndex = 0;
            AnswerVisible = false;
            if (id == "flashcards") LoadCards();
            View.timerPanel.SetActive(id == "timer");
            View.flashcardPanel.SetActive(id == "flashcards");
            View.gameObject.SetActive(true);
            Layout();
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
            for (int i = 0; i < View.timeDigits.Length; i++)
            {
                int digit = i;
                View.timeDigits[i].onClick.AddListener(() => AdjustDigit(digit, 1));
            }
            View.timerToggle.onClick.AddListener(() => { Timer.Toggle(Now); Refresh(); });
            View.timerReset.onClick.AddListener(() => { Timer.Reset(); Refresh(); });
            View.cardBody.onClick.AddListener(Flip);
            View.cardReveal.onClick.AddListener(Flip);
            View.cardPrevious.onClick.AddListener(() => NextCard(-1));
            View.cardNext.onClick.AddListener(() => NextCard(1));
        }

        static double Now => Time.realtimeSinceStartupAsDouble;
        void AdjustDigit(int digit, int direction) { Timer.Adjust(DigitSteps[digit] * direction, Now); Refresh(); }
        void Flip() { AnswerVisible = !AnswerVisible; Refresh(); }
        void NextCard(int direction)
        {
            if (cards.Length == 0) return;
            CardIndex = (CardIndex + direction + cards.Length) % cards.Length;
            AnswerVisible = false;
            Refresh();
        }

        public bool Execute(string command, Vector2 uv, out string message)
        {
            message = "This action is unavailable in this app";
            if (!IsOpen) return false;
            if (AppId == "timer")
            {
                int hand = app.CurrentHand == TrackedHand.Left ? 0 : 1;
                if (command == "app.increase" || command == "app.decrease")
                {
                    int digit = selectedDigit[hand] >= 0 ? selectedDigit[hand] : View.DigitAt(surface, uv);
                    if (digit < 0) digit = 1;
                    AdjustDigit(digit, command == "app.increase" ? 1 : -1);
                }
                else if (command == "app.previous" || command == "app.next")
                {
                    int current = selectedDigit[hand] >= 0 ? selectedDigit[hand] : View.DigitAt(surface, uv);
                    if (current < 0) current = 1;
                    selectedDigit[hand] = (current + (command == "app.next" ? 1 : 3)) % 4;
                }
                else if (command == "app.startpause" || command == "app.primary") Timer.Toggle(Now);
                else if (command == "app.reset") Timer.Reset();
                else return false;
                Refresh();
                message = "Timer / " + View.timerStatus.text;
                return true;
            }
            if (AppId != "flashcards" || cards.Length == 0) return false;
            switch (command)
            {
                case "app.previous": NextCard(-1); break;
                case "app.next": NextCard(1); break;
                case "app.reveal": AnswerVisible = true; break;
                case "app.hide": AnswerVisible = false; break;
                case "app.primary": AnswerVisible = !AnswerVisible; break;
                default: return false;
            }
            Refresh();
            message = "German / card " + (CardIndex + 1) + " of " + cards.Length;
            return true;
        }

        void Update()
        {
            if (!IsOpen) return;
            Layout();
            Refresh();
            UpdateHand(TrackedHand.Left, 0);
            UpdateHand(TrackedHand.Right, 1);
            View.Highlight(hovered[0], hovered[1], AppId == "timer" ? selectedDigit[0] : -1, AppId == "timer" ? selectedDigit[1] : -1);
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
            hovered[index] = button;
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
                int seconds = Timer.Seconds(Now);
                string digits = (seconds / 60).ToString("D2") + (seconds % 60).ToString("D2");
                for (int i = 0; i < 4; i++) View.digitLabels[i].text = digits[i].ToString();
                View.timerStatus.text = Timer.Finished ? "Time's up!" : Timer.IsRunning ? "Counting down" : "Swipe up / down on a digit to set time";
                View.timerStatus.color = Timer.Finished ? new Color(1, 0.75f, 0.25f) : Color.white;
                View.timerToggleLabel.text = Timer.IsRunning ? "Pause" : "Start";
            }
            else
            {
                View.cardCounter.text = "GERMAN  /  " + (cards.Length == 0 ? "No cards" : (CardIndex + 1) + " / " + cards.Length);
                View.cardWord.text = cards.Length == 0 ? "Add words to the vocabulary file" : cards[CardIndex].german;
                View.cardAnswer.text = AnswerVisible && cards.Length > 0 ? cards[CardIndex].english : "Click to reveal the English meaning";
                View.cardRevealLabel.text = AnswerVisible ? "Hide" : "Reveal";
            }
        }

        void Close()
        {
            Timer.Reset();
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
