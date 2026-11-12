using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Authored UI only. Move and resize the targets directly in ScreenAppView.prefab.</summary>
    public sealed class ScreenAppView : MonoBehaviour
    {
        public GameObject timerPanel, flashcardPanel;
        [Tooltip("Left to right: ten minutes, one minute, ten seconds, one second.")]
        public Button[] timeDigits;
        public Text[] digitLabels;
        public Text timerStatus, timerToggleLabel;
        public Button timerToggle, timerReset;
        public Text cardWord, cardAnswer, cardCounter, cardRevealLabel;
        public Button cardBody, cardPrevious, cardReveal, cardNext;
        Button[] buttons;
        void Awake() => buttons = GetComponentsInChildren<Button>(true);

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

        public int DigitAt(ScreenSurface screen, Vector2 uv)
        {
            var button = ButtonAt(screen, uv);
            return System.Array.IndexOf(timeDigits, button);
        }

        public void Highlight(Button left, Button right, int leftDigit = -1, int rightDigit = -1)
        {
            foreach (var button in buttons)
                button.targetGraphic.color = button == left || button == right
                    || (leftDigit >= 0 && button == timeDigits[leftDigit]) || (rightDigit >= 0 && button == timeDigits[rightDigit])
                    ? new Color(0.18f, 0.52f, 0.48f) : new Color(0.08f, 0.15f, 0.2f);
        }
    }
}
