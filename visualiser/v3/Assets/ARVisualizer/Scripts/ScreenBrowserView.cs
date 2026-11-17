using System;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>
    /// Editable browser chrome. All hit regions follow the saved RectTransforms, authored in landscape.
    /// At runtime the page and toolbar stretch, so the same layout also fits the vertical (portrait) face.
    /// </summary>
    public sealed class ScreenBrowserView : MonoBehaviour
    {
        public RawImage page;
        public Text address, status, keyboardHeading;
        public GameObject keyboard;
        [Tooltip("Home becomes Files while reading a PDF; Address becomes a filename label.")]
        public Button homeButton, addressButton;

        [Header("Pop-up keyboard")]
        [Tooltip("Keyboard width as a multiple of the screen width. It opens just below the screen.")]
        [Range(0.5f, 3)] public float keyboardWidthScale = 1.5f;
        [Tooltip("Gap between the screen's bottom edge and the keyboard, in canvas units.")]
        [Min(0)] public float keyboardGap = 20;
        [Tooltip("Caption size of single-character keys, in the keyboard's own units (it is authored 1280 wide).")]
        [Range(12, 60)] public int keyCaptionSize = 38;
        [Tooltip("Caption size of Shift, Space, Backspace, Enter and Hide.")]
        [Range(12, 60)] public int wideKeyCaptionSize = 28;

        public event Action<string> ActionInvoked;
        Button[] buttons;
        Color[] colors;
        Text[] letterLabels = Array.Empty<Text>();
        Vector2 landscapeSize;
        public RectTransform Rect => (RectTransform)transform;
        /// <summary>The authored (horizontal) canvas size.</summary>
        public Vector2 LandscapeSize { get { Cache(); return landscapeSize; } }
        public bool IsVertical { get; private set; }
        public void InvokeAction(string action) => ActionInvoked?.Invoke(action);

        /// <summary>Swaps the canvas between its authored landscape size and the same size turned on its side.</summary>
        public void SetVertical(bool vertical)
        {
            Cache();
            IsVertical = vertical;
            Rect.sizeDelta = vertical ? new Vector2(landscapeSize.y, landscapeSize.x) : landscapeSize;
            PlaceKeyboard();
        }

        /// <summary>Upper-case letter keys while Shift is on.</summary>
        public void SetShift(bool on)
        {
            Cache();
            foreach (var label in letterLabels) label.text = on ? label.text.ToUpperInvariant() : label.text.ToLowerInvariant();
        }

        /// <summary>The keyboard's area in face coordinates (0–1 is the face), or an empty rect while hidden.</summary>
        public Rect KeyboardRegion
        {
            get
            {
                Cache();
                if (!keyboard.activeSelf) return default;
                var corners = new Vector3[4];
                ((RectTransform)keyboard.transform).GetWorldCorners(corners);
                var size = Rect.rect.size;
                Vector2 min = ((Vector2)Rect.InverseTransformPoint(corners[0]) - Rect.rect.min) / size;
                Vector2 max = ((Vector2)Rect.InverseTransformPoint(corners[2]) - Rect.rect.min) / size;
                return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        public void SetDocumentMode(bool document)
        {
            if (homeButton != null) homeButton.GetComponentInChildren<Text>().text = document ? "Files" : "Home";
            if (addressButton != null) addressButton.interactable = !document;
        }

        void Awake() => Cache();
        void Cache()
        {
            if (buttons != null) return;
            buttons = GetComponentsInChildren<Button>(true);
            colors = new Color[buttons.Length];
            for (int i = 0; i < buttons.Length; i++) colors[i] = buttons[i].targetGraphic.color;
            landscapeSize = Rect.sizeDelta;
            MakeResponsive();
            EnlargeKeyCaptions();
            PlaceKeyboard();
        }

        // Keep the authored landscape geometry, but let the page and toolbar follow the canvas size:
        // the page fills everything under the toolbar, the address field takes the spare width, and
        // controls right of it stay on the right.
        void MakeResponsive()
        {
            var toolbar = address != null ? address.GetComponentInParent<Button>(true)?.transform.parent as RectTransform : null;
            if (page == null || toolbar == null || toolbar.parent != transform) return;
            Reanchor(page.rectTransform, Vector2.zero, Vector2.one);
            Reanchor(toolbar, new Vector2(0, 1), Vector2.one);
            var field = (RectTransform)address.GetComponentInParent<Button>(true).transform;
            Reanchor(address.rectTransform, Vector2.zero, Vector2.one);
            float centre = field.anchoredPosition.x + field.rect.center.x;
            foreach (RectTransform child in toolbar)
            {
                if (child == field || child == status?.rectTransform) Reanchor(child, Vector2.zero, new Vector2(1, 0));
                else
                {
                    float x = child.anchoredPosition.x + child.rect.center.x;
                    var side = new Vector2(x > centre ? 1 : 0, 0);
                    Reanchor(child, side, side);
                }
            }
        }

        static void Reanchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            var size = ((RectTransform)rect.parent).rect.size;
            Vector2 min = rect.anchorMin * size + rect.offsetMin, max = rect.anchorMax * size + rect.offsetMax;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = min - anchorMin * size;
            rect.offsetMax = max - anchorMax * size;
        }

        void EnlargeKeyCaptions()
        {
            if (keyboard == null) return;
            var letters = new System.Collections.Generic.List<Text>();
            foreach (var button in keyboard.GetComponentsInChildren<Button>(true))
            {
                var label = button.GetComponentInChildren<Text>(true);
                if (label == null) continue;
                bool single = label.text.Trim().Length == 1;
                label.fontSize = single ? keyCaptionSize : wideKeyCaptionSize;
                label.fontStyle = single ? FontStyle.Normal : FontStyle.Bold;
                label.resizeTextForBestFit = !single;
                label.resizeTextMinSize = Mathf.Min(14, wideKeyCaptionSize);
                label.resizeTextMaxSize = label.fontSize;
                label.alignment = TextAnchor.MiddleCenter;
                // Fill the key so the larger caption is centred and not clipped.
                var rect = label.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(2, 1); rect.offsetMax = new Vector2(-2, -1);
                if (single && char.IsLetter(label.text.Trim()[0])) letters.Add(label);
            }
            letterLabels = letters.ToArray();
        }

        /// <summary>The keyboard hangs below the face, centred, at <see cref="keyboardWidthScale"/> times its width.</summary>
        public void PlaceKeyboard()
        {
            if (keyboard == null) return;
            var rect = (RectTransform)keyboard.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -keyboardGap);
            float width = Mathf.Max(1, rect.rect.width);
            rect.localScale = Vector3.one * (keyboardWidthScale * Rect.rect.width / width);
        }

        Vector3 WorldPoint(Vector2 uv) => Rect.TransformPoint(Rect.rect.min + Vector2.Scale(uv, Rect.rect.size));

        static bool Contains(RectTransform rect, Vector3 world) => rect.rect.Contains(rect.InverseTransformPoint(world));

        public Button ButtonAt(Vector2 uv)
        {
            Cache();
            var world = WorldPoint(uv);
            // Last siblings (the keyboard) take precedence over the page beneath them.
            for (int i = buttons.Length - 1; i >= 0; i--)
                if (buttons[i].isActiveAndEnabled && buttons[i].IsInteractable()
                    && Contains((RectTransform)buttons[i].transform, world)) return buttons[i];
            return null;
        }

        public bool TryPagePoint(Vector2 uv, out Vector2 point)
        {
            Cache();
            point = default;
            var world = WorldPoint(uv);
            if (keyboard.activeSelf && Contains((RectTransform)keyboard.transform, world)) return false;
            var rect = page.rectTransform;
            var local = (Vector2)rect.InverseTransformPoint(world);
            if (!rect.rect.Contains(local)) return false;
            // Screen UV starts at bottom-left; Vuplex input starts at top-left.
            point = new Vector2((local.x - rect.rect.xMin) / rect.rect.width,
                1 - (local.y - rect.rect.yMin) / rect.rect.height);
            return true;
        }

        public void Highlight(Button left, Button right)
        {
            Cache();
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].targetGraphic.color = buttons[i] == left || buttons[i] == right
                    ? new Color(0.15f, 0.65f, 0.5f) : colors[i];
        }
    }
}
