using System;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    public enum TimerPhase { Ready, Running, Paused, Finished }

    /// <summary>One look for the timer. It owns its digits and decides the shape of the screen face.</summary>
    [Serializable]
    public sealed class TimerStyle
    {
        public string title = "Blocks";
        public GameObject root;
        [Tooltip("Width / height of the screen face while this look is shown.")]
        [Min(0.2f)] public float aspect = 16f / 9;
        [Tooltip("Corner rounding as a fraction of the shorter side. 0.5 makes a circle or capsule.")]
        [Range(0, 0.5f)] public float cornerRadius = 0.08f;
        public RoundedRectGraphic backdrop;
        [Tooltip("Left to right: ten minutes, one minute, ten seconds, one second.")]
        public Button[] digits = new Button[4];
        public Text[] digitLabels = new Text[4];
        public Text status;
        [Tooltip("Dimmed every half second while counting down.")]
        public Graphic[] separators = Array.Empty<Graphic>();
        [Tooltip("Recoloured for the running, paused and finished states.")]
        public Graphic[] accents = Array.Empty<Graphic>();
        [Tooltip("Optional arc that empties as time runs out.")]
        public RingGraphic ring;
        [Tooltip("Optional bar whose right (or top) anchor follows the remaining time.")]
        public RectTransform bar;
        public bool barVertical;
        public Color accent = new Color(0.3f, 0.86f, 0.8f);
    }

    /// <summary>Authored UI only. Move, resize and recolour the targets directly in ScreenAppView.prefab.</summary>
    public sealed class ScreenAppView : MonoBehaviour
    {
        public const float CanvasWidth = 960;

        [HideInInspector] public int layoutVersion;
        public GameObject timerPanel, flashcardPanel, puzzlePanel;

        [Header("Timer looks (swipe left / right)")]
        public TimerStyle[] timerStyles = Array.Empty<TimerStyle>();
        public Color digitColor = new Color(0.96f, 0.98f, 1);
        public Color mutedColor = new Color(0.58f, 0.66f, 0.76f);
        public Color pausedColor = new Color(1, 0.74f, 0.3f);
        public Color finishedColor = new Color(1, 0.43f, 0.38f);

        [Header("German flashcards")]
        [Min(0.2f)] public float cardAspect = 16f / 9;
        [Range(0, 0.5f)] public float cardCornerRadius = 0.07f;
        public RoundedRectGraphic cardBackdrop;
        public Button cardBody;
        public Text cardWord, cardAnswer, cardHint, cardCounter, cardKind;
        [Tooltip("Tinted with the colour of the noun's article.")]
        public Graphic[] cardAccents = Array.Empty<Graphic>();
        [Tooltip("Its right anchor follows the position in the deck.")]
        public RectTransform cardProgress;
        public Color masculineColor = new Color(0.38f, 0.66f, 1);
        public Color feminineColor = new Color(1, 0.48f, 0.64f);
        public Color neuterColor = new Color(0.4f, 0.86f, 0.58f);
        public Color otherColor = new Color(1, 0.78f, 0.36f);

        [Header("Slider puzzle")]
        [Min(0.2f)] public float puzzleAspect = 960f / 1100;
        [Range(0, 0.5f)] public float puzzleCornerRadius = 0.06f;
        public RoundedRectGraphic puzzleBackdrop;
        [Tooltip("The board. A click or fingertip touch starts, pauses or resumes.")]
        public Button puzzleBody;
        [Tooltip("Square parent of the tiles. They are sized and placed for the current board at runtime.")]
        public RectTransform puzzleBoard;
        [Tooltip("Tiles 1 to 24 in order; a 5x5 board uses them all.")]
        public RoundedRectGraphic[] puzzleTiles = Array.Empty<RoundedRectGraphic>();
        public Text[] puzzleLabels = Array.Empty<Text>();
        public Text puzzleSize, puzzleMoves, puzzleTime;
        [Tooltip("Covers the board whenever the game is not being played.")]
        public GameObject puzzleOverlay;
        public Text puzzleBanner, puzzleHint;
        [Tooltip("Optional: one chip per board size, smallest first. The current size is lit.")]
        public RoundedRectGraphic[] puzzleChips = Array.Empty<RoundedRectGraphic>();
        public Text[] puzzleChipLabels = Array.Empty<Text>();
        [Tooltip("Accent for 3x3, 4x4 and 5x5.")]
        public Color[] puzzleColors = { new Color(0.3f, 0.86f, 0.8f), new Color(0.68f, 0.6f, 1), new Color(1, 0.74f, 0.36f) };
        public Color puzzleTileColor = new Color(0.1f, 0.15f, 0.23f);
        public Color puzzleDarkInk = new Color(0.03f, 0.05f, 0.08f);
        [Min(0)] public float puzzleGap = 12;

        public int TimerStyleIndex { get; private set; }
        public TimerStyle ActiveStyle => timerStyles[TimerStyleIndex];
        public float FaceAspect { get; private set; } = 16f / 9;
        public float FaceCornerRadius { get; private set; }
        /// <summary>False for a prefab saved before the looks existed. Rebuild it from the Tools menu.</summary>
        public bool IsComplete => timerPanel != null && flashcardPanel != null && timerStyles != null && timerStyles.Length > 0
            && cardBody != null && cardWord != null && cardAnswer != null && cardHint != null
            && cardCounter != null && cardKind != null && cardProgress != null;
        /// <summary>False for a prefab saved before the slider puzzle existed. The Tools menu adds it.</summary>
        public bool HasPuzzle => puzzlePanel != null && puzzleBody != null && puzzleBoard != null && puzzleOverlay != null
            && puzzleTiles != null && puzzleTiles.Length >= SlidingPuzzle.MaximumSize * SlidingPuzzle.MaximumSize - 1
            && puzzleLabels != null && puzzleLabels.Length >= puzzleTiles.Length && puzzleSize != null && puzzleMoves != null
            && puzzleTime != null && puzzleBanner != null && puzzleHint != null;

        const float AnswerFadeSeconds = 0.18f;
        const float TileSlideRate = 22;
        static readonly string[] Numerals = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

        Button[] buttons;
        Color[] restingColors;
        Color accent = Color.white;
        float answerShown;
        bool answerWanted;
        int shownSize, shownMoves = -1, shownSeconds = -1, shownPhase = -1;
        int shownCard = -1;

        void Awake() => Cache();

        void Cache()
        {
            if (buttons != null) return;
            buttons = GetComponentsInChildren<Button>(true);
            restingColors = new Color[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i].targetGraphic != null) restingColors[i] = buttons[i].targetGraphic.color;
        }

        public void ShowTimer(int style)
        {
            Cache();
            int count = timerStyles.Length;
            TimerStyleIndex = ((style % count) + count) % count;
            flashcardPanel.SetActive(false);
            if (puzzlePanel != null) puzzlePanel.SetActive(false);
            timerPanel.SetActive(true);
            for (int i = 0; i < count; i++) timerStyles[i].root.SetActive(i == TimerStyleIndex);
            SetFace(ActiveStyle.aspect, ActiveStyle.cornerRadius, ActiveStyle.backdrop);
        }

        public void ShowFlashcards()
        {
            Cache();
            timerPanel.SetActive(false);
            if (puzzlePanel != null) puzzlePanel.SetActive(false);
            flashcardPanel.SetActive(true);
            shownCard = -1;
            SetFace(cardAspect, cardCornerRadius, cardBackdrop);
        }

        public void ShowPuzzle()
        {
            Cache();
            timerPanel.SetActive(false);
            flashcardPanel.SetActive(false);
            puzzlePanel.SetActive(true);
            shownSize = 0;
            shownMoves = shownSeconds = shownPhase = -1;
            SetFace(puzzleAspect, puzzleCornerRadius, puzzleBackdrop);
        }

        // The canvas keeps its width; its height and outline follow the look.
        void SetFace(float aspect, float cornerRadius, RoundedRectGraphic backdrop)
        {
            FaceAspect = Mathf.Max(0.2f, aspect);
            FaceCornerRadius = Mathf.Clamp(cornerRadius, 0, 0.5f);
            var size = new Vector2(CanvasWidth, CanvasWidth / FaceAspect);
            ((RectTransform)transform).sizeDelta = size;
            if (backdrop != null) backdrop.Radius = FaceCornerRadius * Mathf.Min(size.x, size.y);
        }

        public Button ButtonAt(ScreenSurface screen, Vector2 uv)
        {
            var point = screen.WorldPoint(uv);
            foreach (var button in buttons)
            {
                var rect = (RectTransform)button.transform;
                if (button.isActiveAndEnabled && button.interactable
                    && rect.rect.Contains(rect.InverseTransformPoint(point))) return button;
            }
            return null;
        }

        public int DigitAt(ScreenSurface screen, Vector2 uv) => Array.IndexOf(ActiveStyle.digits, ButtonAt(screen, uv));

        /// <summary>The digit a hand is aiming at, including the gaps and margins around the digits.</summary>
        public int NearestDigit(ScreenSurface screen, Vector2 uv)
        {
            var point = screen.WorldPoint(uv);
            var digits = ActiveStyle.digits;
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < digits.Length; i++)
            {
                var rect = (RectTransform)digits[i].transform;
                Vector2 local = rect.InverseTransformPoint(point);
                var bounds = rect.rect;
                float x = Mathf.Max(bounds.xMin - local.x, 0f, local.x - bounds.xMax);
                float y = Mathf.Max(bounds.yMin - local.y, 0f, local.y - bounds.yMax);
                float distance = x * x + y * y;
                if (distance >= best) continue;
                best = distance;
                nearest = i;
            }
            return nearest;
        }

        public void Highlight(Button left, Button right)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var graphic = buttons[i].targetGraphic;
                if (graphic == null) continue;
                var resting = restingColors[i];
                if (buttons[i] != left && buttons[i] != right) graphic.color = resting;
                // Digits without a tile glow; tiles and the card take a tint of the accent.
                else if (resting.a < 0.05f) graphic.color = new Color(accent.r, accent.g, accent.b, 0.2f);
                else graphic.color = Color.Lerp(resting, accent, buttons[i] == cardBody || buttons[i] == puzzleBody ? 0.07f : 0.3f);
            }
        }

        public void RenderTimer(int seconds, float fraction, TimerPhase phase, double clock)
        {
            var style = ActiveStyle;
            int minutes = seconds / 60;
            int remainder = seconds % 60;
            style.digitLabels[0].text = Numerals[minutes / 10 % 10];
            style.digitLabels[1].text = Numerals[minutes % 10];
            style.digitLabels[2].text = Numerals[remainder / 10];
            style.digitLabels[3].text = Numerals[remainder % 10];

            float pulse = 0.5f + 0.5f * Mathf.Sin((float)(clock % 1.2 / 1.2) * Mathf.PI * 2);
            accent = phase == TimerPhase.Paused ? pausedColor
                : phase == TimerPhase.Finished ? Color.Lerp(finishedColor, Color.white, pulse * 0.4f) : style.accent;
            foreach (var graphic in style.accents) Tint(graphic, accent, graphic.color.a);
            float colon = phase == TimerPhase.Running && clock % 1 >= 0.5 ? 0.25f : 1;
            foreach (var separator in style.separators) Tint(separator, accent, colon);
            var ink = phase == TimerPhase.Finished ? accent : digitColor;
            foreach (var label in style.digitLabels) label.color = ink;

            float remaining = phase == TimerPhase.Finished ? 1 : Mathf.Clamp01(fraction);
            if (style.ring != null) style.ring.FillAmount = remaining;
            if (style.bar != null) Fill(style.bar, style.barVertical ? new Vector2(1, remaining) : new Vector2(remaining, 1));
            style.status.text = phase == TimerPhase.Finished ? "Time's up!  ·  Click to reset"
                : phase == TimerPhase.Running ? "Click to pause"
                : phase == TimerPhase.Paused ? "Paused  ·  Click to resume"
                : seconds > 0 ? "Swipe to set  ·  Click to start" : "Swipe up on a digit to set a time";
            style.status.color = phase == TimerPhase.Ready ? mutedColor : accent;
        }

        public void RenderCard(string german, string english, int index, int count, bool answerVisible)
        {
            accent = ArticleColor(german, out string kind);
            foreach (var graphic in cardAccents) Tint(graphic, accent, graphic.color.a);
            cardKind.text = kind;
            cardKind.color = accent;
            cardWord.text = count == 0 ? "Add words to the vocabulary file" : german;
            cardCounter.text = count == 0 ? "No cards" : (index + 1) + " / " + count;
            cardAnswer.text = count == 0 ? "" : english;
            cardHint.text = count == 0 ? "" : "Swipe up to reveal";
            Fill(cardProgress, new Vector2(count == 0 ? 0 : (index + 1f) / count, 1));

            answerWanted = answerVisible && count > 0;
            // A new card never shows its answer while the previous one fades.
            if (index != shownCard || !Application.isPlaying) answerShown = answerWanted ? 1 : 0;
            shownCard = index;
            ApplyAnswerFade();
        }

        /// <summary>
        /// Call every frame while the puzzle is open, and after each move. Tiles glide to their places;
        /// a delta of zero only updates the texts and colours.
        /// </summary>
        public void RenderPuzzle(SlidingPuzzle puzzle, double now, float deltaTime)
        {
            int size = puzzle.Size, used = size * size - 1, phase = (int)puzzle.Phase;
            bool resized = size != shownSize;
            bool solved = puzzle.Phase == PuzzlePhase.Solved;
            accent = puzzleColors.Length == 0 ? digitColor
                : puzzleColors[Mathf.Clamp(size - SlidingPuzzle.MinimumSize, 0, puzzleColors.Length - 1)];

            float pitch = (puzzleBoard.rect.width + puzzleGap) / size;
            float cell = pitch - puzzleGap;
            bool snap = resized || !Application.isPlaying;
            float blend = 1 - Mathf.Exp(-TileSlideRate * Mathf.Max(0, deltaTime));
            for (int i = 0; i < puzzleTiles.Length; i++)
            {
                var tile = puzzleTiles[i];
                var rect = tile.rectTransform;
                if (resized)
                {
                    tile.gameObject.SetActive(i < used);
                    if (i < used)
                    {
                        rect.sizeDelta = Vector2.one * cell;
                        tile.Radius = cell * 0.12f;
                        puzzleLabels[i].fontSize = Mathf.RoundToInt(cell * 0.5f);
                    }
                }
                if (i >= used) continue;

                int index = puzzle.IndexOf(i + 1);
                var target = new Vector2((index % size - (size - 1) * 0.5f) * pitch, ((size - 1) * 0.5f - index / size) * pitch);
                var position = snap ? target : Vector2.Lerp(rect.anchoredPosition, target, blend);
                if ((position - target).sqrMagnitude < 0.25f) position = target;
                if (rect.anchoredPosition != position) rect.anchoredPosition = position;
                // Tiles already in their final place take the accent; a solved board is fully lit.
                tile.color = Color.Lerp(puzzleTileColor, accent, solved ? 0.85f : index == i ? 0.42f : 0);
                puzzleLabels[i].color = solved ? puzzleDarkInk : digitColor;
            }

            puzzleSize.color = accent;
            if (resized)
            {
                puzzleSize.text = size + " × " + size;
                for (int i = 0; i < puzzleChips.Length; i++)
                {
                    bool current = i == size - SlidingPuzzle.MinimumSize;
                    Tint(puzzleChips[i], current ? accent : Color.white, current ? 1 : 0.08f);
                    if (i < puzzleChipLabels.Length) Tint(puzzleChipLabels[i], current ? puzzleDarkInk : mutedColor, 1);
                }
            }
            bool moved = puzzle.Moves != shownMoves;
            if (moved) puzzleMoves.text = puzzle.Moves + (puzzle.Moves == 1 ? " MOVE" : " MOVES");
            int seconds = (int)Math.Min(puzzle.Elapsed(now), 99 * 60 + 59);
            if (seconds != shownSeconds) puzzleTime.text = (seconds / 60).ToString("D2") + ":" + (seconds % 60).ToString("D2");

            bool waiting = puzzle.Phase != PuzzlePhase.Playing;
            if (puzzleOverlay.activeSelf != waiting) puzzleOverlay.SetActive(waiting);
            if (waiting && (phase != shownPhase || resized || moved))
            {
                puzzleBanner.color = accent;
                puzzleBanner.text = solved ? "SOLVED!" : puzzle.Phase == PuzzlePhase.Paused ? "PAUSED" : "READY";
                puzzleHint.text = solved
                    ? puzzle.Moves + (puzzle.Moves == 1 ? " move in " : " moves in ") + puzzleTime.text + "\nSwipe left: new game  ·  Swipe up / down: size"
                    : puzzle.Phase == PuzzlePhase.Paused
                        ? "Click to resume\nSwipe up / down: size  ·  Swipe left: reset"
                        : "Click to start\nSwipe up / down to change the size";
            }
            shownSize = size;
            shownMoves = puzzle.Moves;
            shownSeconds = seconds;
            shownPhase = phase;
        }

        /// <summary>Call every frame while the flashcards are open.</summary>
        public void AnimateCard(float deltaTime)
        {
            float target = answerWanted ? 1 : 0;
            if (Mathf.Approximately(answerShown, target)) return;
            answerShown = Mathf.MoveTowards(answerShown, target, deltaTime / AnswerFadeSeconds);
            ApplyAnswerFade();
        }

        void ApplyAnswerFade()
        {
            Tint(cardAnswer, Color.Lerp(accent, Color.white, 0.5f), answerShown);
            Tint(cardHint, mutedColor, 1 - answerShown);
        }

        Color ArticleColor(string german, out string kind)
        {
            string word = (german ?? "").TrimStart().ToLowerInvariant();
            if (word.StartsWith("der ", StringComparison.Ordinal)) { kind = "DER  ·  MASCULINE"; return masculineColor; }
            if (word.StartsWith("die ", StringComparison.Ordinal)) { kind = "DIE  ·  FEMININE"; return feminineColor; }
            if (word.StartsWith("das ", StringComparison.Ordinal)) { kind = "DAS  ·  NEUTER"; return neuterColor; }
            kind = "GERMAN";
            return otherColor;
        }

        static void Tint(Graphic graphic, Color tint, float alpha)
        {
            if (graphic == null) return;
            tint.a = alpha;
            graphic.color = tint;
        }

        static void Fill(RectTransform bar, Vector2 anchorMax)
        {
            if (bar != null && bar.anchorMax != anchorMax) bar.anchorMax = anchorMax;
        }
    }
}
