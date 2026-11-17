using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ARVisualizer.Editor
{
    /// <summary>
    /// Adds the tutorial layout to the saved HUD once. Later runs keep manual layout edits and only
    /// refresh the list of demonstration clips.
    /// </summary>
    public static class TutorialAuthoring
    {
        public const string MediaFolder = "Assets/ARVisualizer/Media/Tutorial";
        public const int LayoutVersion = 2;

        static readonly Color PanelColor = new Color(0.035f, 0.055f, 0.075f, 0.91f);
        static readonly Color ButtonColor = new Color(0.08f, 0.16f, 0.19f, 0.97f);
        static readonly Color Accent = new Color(0.4f, 1, 0.8f);
        static readonly Color Warning = new Color(1, 0.72f, 0.3f);
        static readonly Color Muted = new Color(0.61f, 0.7f, 0.77f);
        static readonly Color Idle = new Color(1, 1, 1, 0.18f);
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f), TopLeft = new Vector2(0, 1), BottomLeft = Vector2.zero;

        [MenuItem("Tools/AR Visualizer/Prepare tutorial mode")]
        public static void PrepareAssets()
        {
            var hud = PrefabUtility.LoadPrefabContents(VisualizerHUDAuthoring.PrefabPath);
            try
            {
                ConfigureHUD(hud.GetComponent<VisualizerHUD>());
                PrefabUtility.SaveAsPrefabAsset(hud, VisualizerHUDAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_TUTORIAL_READY");
        }

        static VideoClip[] FindClips() => !AssetDatabase.IsValidFolder(MediaFolder) ? new VideoClip[0]
            : AssetDatabase.FindAssets("t:VideoClip", new[] { MediaFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<VideoClip>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(clip => clip != null).OrderBy(clip => clip.name).ToArray();

        public static bool NeedsUpgrade()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualizerHUDAuthoring.PrefabPath);
            if (prefab == null) return false;
            var tutorial = prefab.GetComponent<TutorialHUD>();
            if (tutorial == null || tutorial.LayoutVersion < LayoutVersion) return true;
            var saved = new SerializedObject(tutorial).FindProperty("clips");
            var found = FindClips();
            if (saved.arraySize != found.Length) return true;
            for (int i = 0; i < found.Length; i++)
                if (saved.GetArrayElementAtIndex(i).objectReferenceValue != found[i]) return true;
            return false;
        }

        public static void ConfigureHUD(VisualizerHUD hud)
        {
            var tutorial = hud.GetComponent<TutorialHUD>();
            if (tutorial == null) tutorial = hud.gameObject.AddComponent<TutorialHUD>();
            var fields = new SerializedObject(tutorial);
            int saved = fields.FindProperty("layoutVersion").intValue;
            if (saved < 1) BuildLayout(hud, tutorial, fields);
            // Version 2 adds the goggle-mode hold tip and progress bars to a layout of any age, as saved.
            if (saved < 2) AddHoldButtons(hud, fields);
            fields.FindProperty("layoutVersion").intValue = LayoutVersion;
            fields.FindProperty("script").objectReferenceValue = Resources.Load<TextAsset>("tutorial");
            var clips = FindClips();
            var savedClips = fields.FindProperty("clips");
            savedClips.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) savedClips.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        // Goggle mode: a thin bar in each small button fills while a ray rests on it, under a tip that says so.
        static void AddHoldButtons(VisualizerHUD hud, SerializedObject fields)
        {
            var lesson = hud.transform.Find("Tutorial/Tutorial Slide/Lesson");
            var skip = lesson != null ? lesson.Find("Skip Gesture Button") as RectTransform : null;
            var exit = lesson != null ? lesson.Find("Exit Tutorial Button") as RectTransform : null;
            if (skip == null || exit == null) return; // A renamed layout keeps working by gesture and touch.
            var text = TutorialScript.Parse(Resources.Load<TextAsset>("tutorial")?.text);
            fields.FindProperty("exitButton").objectReferenceValue = exit.GetComponent<Button>();
            // Match the buttons' saved shape (a squircle once the iOS style has been applied).
            var shape = skip.GetComponent<Image>();
            Image Shaped(Image image, float cornerScale)
            {
                if (shape == null || shape.sprite == null) return image;
                image.sprite = shape.sprite;
                image.type = shape.type;
                image.pixelsPerUnitMultiplier = shape.pixelsPerUnitMultiplier * cornerScale;
                return image;
            }

            RectTransform Bar(RectTransform button)
            {
                var old = button.Find("Hold Track");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var track = Rect("Hold Track", button, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                track.anchorMax = new Vector2(1, 0);
                track.offsetMin = new Vector2(14, 5);
                track.offsetMax = new Vector2(-14, 8);
                Shaped(Paint(track, Idle), 12);
                var fill = Stretch("Hold Progress", track);
                fill.anchorMax = new Vector2(0, 1);
                Shaped(Paint(fill, Accent), 12);
                return fill;
            }
            fields.FindProperty("skipHoldFill").objectReferenceValue = Bar(skip);
            fields.FindProperty("exitHoldFill").objectReferenceValue = Bar(exit);

            var previous = lesson.Find("Hold Tip");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var corner = new Vector2(1, 0);
            var tip = Rect("Hold Tip", lesson, corner, corner, new Vector2(Mathf.Max(136, skip.sizeDelta.x), 26),
                new Vector2(skip.anchoredPosition.x, skip.anchoredPosition.y + skip.sizeDelta.y + 8));
            Shaped(Paint(tip, new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.96f)), 1.6f);
            var label = Label("Text", tip, Centre, Centre, new Vector2(tip.sizeDelta.x - 12, 22), Vector2.zero,
                text.messages.holdTip.Replace("{dwell}", text.buttonHoldSeconds.ToString("0.#")), 13, Warning);
            var captionFont = skip.GetComponentInChildren<Text>(true);
            if (captionFont != null) { label.font = captionFont.font; label.fontStyle = captionFont.fontStyle; }
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 11; label.resizeTextMaxSize = 13;
            fields.FindProperty("holdTipPanel").objectReferenceValue = tip.gameObject;
            fields.FindProperty("holdTip").objectReferenceValue = label;
            tip.gameObject.SetActive(false);
        }

        static void BuildLayout(VisualizerHUD hud, TutorialHUD tutorial, SerializedObject fields)
        {
            void Bind(string field, Object value) => fields.FindProperty(field).objectReferenceValue = value;
            var script = TutorialScript.Parse(Resources.Load<TextAsset>("tutorial")?.text);
            var text = script.messages;
            var root = hud.transform;
            var aim = root.Find("Aim Position");

            // Everything sits beside the free-mode HUD (Safe Area), so either can slide away whole.
            var container = Stretch("Tutorial", root);
            Bind("container", container);
            Bind("freeRoot", root.Find("Safe Area"));
            var slide = Stretch("Tutorial Slide", container);
            Bind("tutorialRoot", slide);

            // Start-up choice: two large squares.
            var choice = Stretch("Mode Choice", slide);
            Bind("choicePanel", choice.gameObject);
            Paint(choice, new Color(0.02f, 0.03f, 0.045f, 0.55f)).raycastTarget = true;
            Bind("choiceHeading", Label("Heading", choice, Centre, Centre, new Vector2(700, 22), new Vector2(0, 236), script.choice.heading, 13, Accent));
            Bind("choiceSubheading", Label("Subheading", choice, Centre, Centre, new Vector2(700, 40), new Vector2(0, 200), script.choice.subheading, 28, Color.white));
            RectTransform Square(string name, float x, Color stripe, string title, string caption, UnityAction action, string titleField, string captionField)
            {
                var square = Rect(name, choice, Centre, Centre, new Vector2(300, 300), new Vector2(x, -10));
                MakeButton(square, new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.95f), action);
                var top = Rect("Accent", square, TopLeft, TopLeft, new Vector2(300, 6), Vector2.zero);
                Paint(top, stripe);
                Bind(titleField, Label("Title", square, Centre, Centre, new Vector2(280, 120), new Vector2(0, 26), title, 40, Color.white));
                Bind(captionField, Label("Caption", square, Centre, Centre, new Vector2(260, 48), new Vector2(0, -96), caption, 14, Muted));
                return square;
            }
            Square("Tutorial Mode Button", -166, Accent, script.choice.tutorialTitle, script.choice.tutorialCaption, tutorial.ChooseTutorial, "tutorialTitle", "tutorialCaption");
            Square("Free Mode Button", 166, Muted, script.choice.freeTitle, script.choice.freeCaption, tutorial.ChooseFree, "freeTitle", "freeCaption");
            Bind("choiceHint", Label("Hint", choice, Centre, Centre, new Vector2(700, 24), new Vector2(0, -196), script.choice.hint, 13, Muted));

            // Lesson: looping clip top-left, guidance at the lower centre.
            var lesson = Stretch("Lesson", slide);
            Bind("lessonPanel", lesson.gameObject);
            var video = Rect("Video Panel", lesson, TopLeft, TopLeft, new Vector2(212, 376), new Vector2(24, -24));
            Paint(video, PanelColor);
            Bind("videoPanel", video.gameObject);
            Bind("watchLabel", Label("Heading", video, TopLeft, TopLeft, new Vector2(188, 20), new Vector2(12, -8), text.watch, 12, Accent, TextAnchor.MiddleLeft));
            var area = Rect("Video Area", video, TopLeft, TopLeft, new Vector2(188, 330), new Vector2(12, -34));
            var picture = Stretch("Video", area);
            var image = picture.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            var fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 9f / 16;
            Bind("videoImage", image);
            Bind("videoFitter", fitter);

            var message = Rect("Message Panel", lesson, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(780, 178), new Vector2(0, 24));
            Paint(message, PanelColor);
            Bind("stepLabel", Label("Step", message, TopLeft, TopLeft, new Vector2(300, 18), new Vector2(20, -12), "GESTURE 1 OF 12  ·  PRACTISE", 12, Accent, TextAnchor.MiddleLeft));
            Bind("statusLabel", Label("Status", message, Vector2.one, Vector2.one, new Vector2(430, 18), new Vector2(-20, -12), text.notConnected, 12, Warning, TextAnchor.MiddleRight));
            Bind("titleLabel", Label("Title", message, TopLeft, TopLeft, new Vector2(740, 34), new Vector2(20, -34), "Click", 26, Color.white, TextAnchor.MiddleLeft));
            var body = Label("Body", message, TopLeft, TopLeft, new Vector2(740, 78), new Vector2(20, -72),
                script.steps.Length > 0 ? script.steps[0].perform : "", 16, new Color(0.86f, 0.9f, 0.94f), TextAnchor.UpperLeft);
            body.lineSpacing = 1.08f;
            body.resizeTextForBestFit = true; body.resizeTextMinSize = 11; body.resizeTextMaxSize = 16;
            Bind("bodyLabel", body);
            int pips = Mathf.Max(1, script.steps.Length);
            float pitch = 740f / pips;
            var saved = fields.FindProperty("stepPips");
            saved.arraySize = pips;
            for (int i = 0; i < pips; i++)
            {
                var pip = Rect("Step " + (i + 1), message, BottomLeft, BottomLeft, new Vector2(pitch - 8, 5), new Vector2(20 + i * pitch, 12));
                saved.GetArrayElementAtIndex(i).objectReferenceValue = Paint(pip, i == 0 ? Color.white : Idle);
            }

            RectTransform Small(string name, float y, string caption, UnityAction action, string labelField)
            {
                var rect = Rect(name, lesson, new Vector2(1, 0), new Vector2(1, 0), new Vector2(136, 44), new Vector2(-24, y));
                MakeButton(rect, ButtonColor, action);
                Bind(labelField, Label("Caption", rect, Centre, Centre, new Vector2(128, 40), Vector2.zero, caption, 12, Color.white));
                return rect;
            }
            Bind("skipButton", Small("Skip Gesture Button", 76, text.skip, tutorial.SkipGesture, "skipLabel").GetComponent<Button>());
            Small("Exit Tutorial Button", 24, text.exit, tutorial.ExitTutorial, "exitLabel");
            lesson.gameObject.SetActive(false);

            // Free mode: a slim bar on the left edge reopens the tutorial after a steady point.
            var back = Stretch("Tutorial Return", container);
            Bind("returnRoot", back);
            var left = new Vector2(0, 0.5f);
            Bind("returnZone", Rect("Pointing Area", back, left, left, new Vector2(70, 200), Vector2.zero));
            var bar = Rect("Return Bar", back, left, left, new Vector2(18, 164), new Vector2(4, 0));
            MakeButton(bar, new Color(Warning.r, Warning.g, Warning.b, 0.85f), tutorial.EnterTutorial);
            var fill = Stretch("Hold Progress", bar);
            fill.anchorMax = new Vector2(1, 0);
            Paint(fill, Color.white);
            Bind("returnFill", fill);
            var hint = Rect("Return Hint", back, left, left, new Vector2(210, 48), new Vector2(30, 0));
            Paint(hint, new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.96f));
            Bind("returnHintPanel", hint.gameObject);
            Bind("returnLabel", Label("Label", hint, TopLeft, TopLeft, new Vector2(186, 16), new Vector2(12, -6), text.returnLabel, 12, Accent, TextAnchor.MiddleLeft));
            Bind("returnHint", Label("Countdown", hint, TopLeft, TopLeft, new Vector2(186, 22), new Vector2(12, -22), "Keep pointing  3 s", 15, Color.white, TextAnchor.MiddleLeft));
            hint.gameObject.SetActive(false);
            back.gameObject.SetActive(false);

            // Per-hand feedback follows the aim markers, which belong to the canvas rather than the safe area.
            BuildHand(fields.FindProperty("leftHand"), "Tutorial Left Hand", root, new Vector2(0.24f, 0.52f), text.handLeft);
            BuildHand(fields.FindProperty("rightHand"), "Tutorial Right Hand", root, new Vector2(0.76f, 0.52f), text.handRight);
            if (aim != null)
            {
                // Draw the hand markers above the tutorial panels.
                int index = aim.GetSiblingIndex();
                container.SetSiblingIndex(index);
                root.Find("Tutorial Left Hand").SetSiblingIndex(index + 1);
                root.Find("Tutorial Right Hand").SetSiblingIndex(index + 2);
            }
        }

        static void BuildHand(SerializedProperty widget, string name, Transform root, Vector2 dock, string caption)
        {
            var hand = Rect(name, root, dock, Centre, Vector2.zero, Vector2.zero);
            widget.FindPropertyRelative("root").objectReferenceValue = hand;
            widget.FindPropertyRelative("dock").vector2Value = dock;
            var top = new Vector2(0.5f, 1);
            var bar = Rect("Progress Bar", hand, Centre, top, new Vector2(14, 52), new Vector2(0, -20));
            Paint(bar, PanelColor);
            var segments = widget.FindPropertyRelative("segments");
            segments.arraySize = 3;
            var bottom = new Vector2(0.5f, 0);
            for (int i = 0; i < 3; i++)
                segments.GetArrayElementAtIndex(i).objectReferenceValue =
                    Paint(Rect("Third " + (i + 1), bar, bottom, bottom, new Vector2(8, 12), new Vector2(0, 4 + i * 16)), Idle);
            widget.FindPropertyRelative("caption").objectReferenceValue =
                Label("Caption", hand, Centre, top, new Vector2(170, 14), new Vector2(0, -76), caption, 10, Muted);
            var popup = Rect("Popup", hand, Centre, bottom, new Vector2(250, 30), new Vector2(0, 22));
            widget.FindPropertyRelative("popupPanel").objectReferenceValue = Paint(popup, Accent);
            var message = Label("Message", popup, Centre, Centre, new Vector2(240, 26), Vector2.zero, "Correct  1 / 3", 13, new Color(0.035f, 0.055f, 0.075f));
            message.resizeTextForBestFit = true; message.resizeTextMinSize = 9; message.resizeTextMaxSize = 13;
            widget.FindPropertyRelative("popupText").objectReferenceValue = message;
            popup.gameObject.SetActive(false);
            hand.gameObject.SetActive(false);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rect = Rect(name, parent, Vector2.zero, Centre, Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            return rect;
        }

        static Image Paint(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position,
            string caption, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = Rect(name, parent, anchor, pivot, size, position).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = caption;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        static Button MakeButton(RectTransform rect, Color color, UnityAction action)
        {
            var image = Paint(rect, color);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.75f, 1, 0.92f);
            colors.pressedColor = new Color(0.5f, 0.9f, 0.75f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            UnityEventTools.AddVoidPersistentListener(button.onClick, action);
            return button;
        }
    }
}
