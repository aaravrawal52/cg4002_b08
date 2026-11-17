using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    /// <summary>
    /// Creates saved, editable prefabs. A missing or older-generation ScreenAppView.prefab is rebuilt;
    /// one at the current layout version keeps every Inspector edit.
    /// </summary>
    public static class BuiltInAppAuthoring
    {
        public const string ViewPath = "Assets/ARVisualizer/Resources/ScreenAppView.prefab";
        /// <summary>
        /// 2: button-free faces, four timer looks with their own screen shapes, full-face flashcard.
        /// 3: slider puzzle panel, added to a version 2 prefab without touching the rest.
        /// </summary>
        public const int ViewLayoutVersion = 3;
        /// <summary>4: the chooser also lists the local PDF viewer.</summary>
        public const int AppRegistryVersion = 4;
        const string MaterialPath = "Assets/ARVisualizer/Materials/ScreenAppUI.mat";

        static readonly Color Ink = new Color(0.96f, 0.98f, 1);
        static readonly Color Muted = new Color(0.58f, 0.66f, 0.76f);
        static readonly Color Faint = new Color(1, 1, 1, 0.08f);
        static readonly string[] Steps = { "10 min", "1 min", "10 sec", "1 sec" };

        [MenuItem("Tools/AR Visualizer/Prepare timer and flashcard apps")]
        public static void PrepareAssets()
        {
            ScreenAppAuthoring.PrepareAssets();
            if (TutorialAuthoring.NeedsUpgrade()) TutorialAuthoring.PrepareAssets();
            var saved = AssetDatabase.LoadAssetAtPath<ScreenAppView>(ViewPath);
            if (saved == null || saved.layoutVersion < 2) BuildView();
            else if (saved.layoutVersion < ViewLayoutVersion) AddPuzzleToSavedView();
            UpgradeMenu();
            var root = PrefabUtility.LoadPrefabContents(ScreenAuthoring.PrefabPath);
            try
            {
                var content = root.GetComponent<ScreenAppContent>();
                if (content == null) content = root.AddComponent<ScreenAppContent>();
                var settings = new SerializedObject(content);
                if (settings.FindProperty("viewPrefab").objectReferenceValue == null)
                    settings.FindProperty("viewPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScreenAppView>(ViewPath);
                if (settings.FindProperty("germanVocabulary").objectReferenceValue == null)
                    settings.FindProperty("germanVocabulary").objectReferenceValue = Resources.Load<TextAsset>("german_vocabulary");
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, ScreenAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            // Last, so freshly built or upgraded layouts are styled too.
            if (HUDStyleAuthoring.NeedsUpgrade()) HUDStyleAuthoring.Apply(force: false);
            AssetDatabase.SaveAssets();
            BrowserAuthoring.PrepareAssets();
            Debug.Log("AR_VISUALIZER_BUILT_IN_APPS_READY");
        }

        static bool ViewNeedsBuilding()
        {
            var view = AssetDatabase.LoadAssetAtPath<ScreenAppView>(ViewPath);
            return view == null || view.layoutVersion < ViewLayoutVersion;
        }

        static bool RegistryNeedsUpgrade()
        {
            var menu = AssetDatabase.LoadAssetAtPath<ScreenAppMenu>(ScreenAppAuthoring.MenuPath);
            return menu != null && new SerializedObject(menu).FindProperty("builtInAppsLayoutVersion").intValue < AppRegistryVersion;
        }

        internal static bool PrefabsOutdated => ViewNeedsBuilding() || RegistryNeedsUpgrade() || ScreenAppAuthoring.MenuNeedsUpgrade()
            || TutorialAuthoring.NeedsUpgrade() || HUDStyleAuthoring.NeedsUpgrade()
            || AssetDatabase.LoadAssetAtPath<GameObject>(ScreenAuthoring.PrefabPath)?.GetComponent<ScreenEditAura>() == null
            || AssetDatabase.LoadAssetAtPath<ScreenBrowserView>(BrowserAuthoring.ViewPath) == null;

        // Scripts can arrive (for example from version control) before the prefabs they describe.
        // Bring saved prefabs up to date once, without needing the menu command.
        [InitializeOnLoadMethod]
        static void UpgradeOutdatedPrefabs()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (PrefabsOutdated) PrepareAssets();
            };
        }

        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static RectTransform Stretch(string name, Transform parent, float inset = 0)
        {
            var rect = Rect(name, parent, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
            return rect;
        }

        static Text Label(string name, Transform parent, Vector2 size, Vector2 position, string value, int fontSize,
            Color color, FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        static void BestFit(Text text, int minimum)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minimum;
            text.resizeTextMaxSize = text.fontSize;
        }

        static RoundedRectGraphic Box(RectTransform rect, Color color, float radius, float shade = 1)
        {
            var box = rect.gameObject.AddComponent<RoundedRectGraphic>();
            box.color = color;
            box.Radius = radius;
            box.BottomTint = new Color(shade, shade, Mathf.Lerp(shade, 1, 0.35f), 1);
            return box;
        }

        static RingGraphic Ring(string name, Transform parent, float diameter, Vector2 position, float thickness, Color color)
        {
            var ring = Rect(name, parent, Vector2.one * diameter, position).gameObject.AddComponent<RingGraphic>();
            ring.color = color;
            ring.Thickness = thickness;
            return ring;
        }

        static Button Target(RectTransform rect, Graphic graphic)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None; // Both hand highlights are supplied by the view.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        static TimerStyle Style(string title, Transform timer, float aspect, float cornerRadius, Color accent, Color background)
        {
            var root = Stretch(title, timer);
            var size = new Vector2(ScreenAppView.CanvasWidth, ScreenAppView.CanvasWidth / aspect);
            return new TimerStyle
            {
                title = title, root = root.gameObject, aspect = aspect, cornerRadius = cornerRadius, accent = accent,
                backdrop = Box(Stretch("Backdrop", root), background, cornerRadius * Mathf.Min(size.x, size.y), 0.7f)
            };
        }

        // The tile is the gesture, click and fingertip target. A transparent tile only glows when pointed at.
        static void Digit(TimerStyle style, int index, Vector2 size, Vector2 position, Color tile, float radius,
            int fontSize, float glyphScale = 1, float lift = 0, int captionSize = 0)
        {
            var rect = Rect("Digit " + (index + 1) + " - " + Steps[index], style.root.transform, size, position);
            style.digits[index] = Target(rect, Box(rect, tile, radius, 0.82f));
            var value = Label("Value", rect, size / glyphScale, new Vector2(0, lift), index == 1 ? "1" : "0", fontSize, Ink, FontStyle.Bold);
            value.alignByGeometry = true;
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            value.verticalOverflow = VerticalWrapMode.Overflow;
            value.rectTransform.localScale = Vector3.one * glyphScale;
            style.digitLabels[index] = value;
            if (captionSize > 0)
                Label("Step", rect, new Vector2(size.x, captionSize + 10), new Vector2(0, captionSize + 12 - size.y * 0.5f),
                    Steps[index].ToUpperInvariant(), captionSize, Muted, FontStyle.Bold);
        }

        static Graphic[] Colon(TimerStyle style, Vector2 position, float spread, float dot)
        {
            var colon = Rect("Colon", style.root.transform, new Vector2(dot, spread * 2 + dot), position);
            var dots = new Graphic[2];
            for (int i = 0; i < 2; i++)
                dots[i] = Box(Rect(i == 0 ? "Upper dot" : "Lower dot", colon, Vector2.one * dot, new Vector2(0, i == 0 ? spread : -spread)),
                    style.accent, dot * 0.5f);
            style.separators = dots;
            return dots;
        }

        static Graphic Bar(TimerStyle style, Vector2 size, Vector2 position)
        {
            var track = Rect("Progress", style.root.transform, size, position);
            float radius = Mathf.Min(size.x, size.y) * 0.5f;
            Box(track, Faint, radius);
            var remaining = Stretch("Remaining", track);
            style.bar = remaining;
            return Box(remaining, style.accent, radius);
        }

        static Text Status(TimerStyle style, Vector2 size, Vector2 position, int fontSize)
        {
            var status = Label("Status", style.root.transform, size, position, "Swipe to set  ·  Click to start", fontSize, Muted);
            BestFit(status, 14);
            return style.status = status;
        }

        // 16:9 with softly rounded corners: four large tiles and a slim bar.
        static TimerStyle Blocks(Transform timer)
        {
            var style = Style("Blocks", timer, 16f / 9, 0.08f, new Color(0.3f, 0.86f, 0.8f), new Color(0.035f, 0.055f, 0.09f));
            float[] x = { -345, -127, 127, 345 };
            for (int i = 0; i < 4; i++)
                Digit(style, i, new Vector2(206, 392), new Vector2(x[i], 44), new Color(0.085f, 0.125f, 0.19f), 30, 300, 1, 14, 22);
            var dots = Colon(style, new Vector2(0, 52), 62, 28);
            Status(style, new Vector2(880, 40), new Vector2(0, -192), 28);
            style.accents = new[] { dots[0], dots[1], Bar(style, new Vector2(880, 14), new Vector2(0, -236)) };
            return style;
        }

        // A circular screen: the time sits inside a ring that empties clockwise.
        static TimerStyle Orbit(Transform timer)
        {
            var accent = new Color(0.68f, 0.6f, 1);
            var style = Style("Orbit", timer, 1, 0.5f, accent, new Color(0.05f, 0.045f, 0.11f));
            var root = style.root.transform;
            Ring("Track", root, 920, Vector2.zero, 26, Faint);
            style.ring = Ring("Remaining", root, 920, Vector2.zero, 26, accent);
            var minor = Ring("Minute ticks", root, 852, Vector2.zero, 12, new Color(1, 1, 1, 0.16f));
            minor.Dashes = 60; minor.DashFill = 0.1f;
            var major = Ring("Five-minute ticks", root, 852, Vector2.zero, 22, new Color(1, 1, 1, 0.42f));
            major.Dashes = 12; major.DashFill = 0.035f;
            float[] x = { -273, -103, 103, 273 };
            var glow = new Color(accent.r, accent.g, accent.b, 0);
            for (int i = 0; i < 4; i++) Digit(style, i, new Vector2(166, 320), new Vector2(x[i], 16), glow, 28, 280);
            var dots = Colon(style, new Vector2(0, 16), 52, 24);
            Label("Minutes", root, new Vector2(300, 36), new Vector2(-188, 198), "MIN", 28, Muted, FontStyle.Bold);
            Label("Seconds", root, new Vector2(300, 36), new Vector2(188, 198), "SEC", 28, Muted, FontStyle.Bold);
            Status(style, new Vector2(560, 44), new Vector2(0, -206), 30);
            style.accents = new[] { style.ring, dots[0], dots[1] };
            return style;
        }

        // A wide pill: a small ring in the left cap and the time along the bar.
        static TimerStyle Capsule(Transform timer)
        {
            var accent = new Color(0.74f, 0.92f, 0.38f);
            var style = Style("Capsule", timer, 3.2f, 0.5f, accent, new Color(0.04f, 0.07f, 0.075f));
            var root = style.root.transform;
            var cap = new Vector2(-330, 0);
            Ring("Track", root, 210, cap, 22, Faint);
            style.ring = Ring("Remaining", root, 210, cap, 22, accent);
            var hub = Box(Rect("Hub", root, Vector2.one * 36, cap), accent, 18);
            float[] x = { -108, 24, 186, 318 };
            var glow = new Color(accent.r, accent.g, accent.b, 0);
            for (int i = 0; i < 4; i++) Digit(style, i, new Vector2(126, 196), new Vector2(x[i], 16), glow, 24, 210);
            var dots = Colon(style, new Vector2(105, 16), 38, 20);
            Status(style, new Vector2(520, 30), new Vector2(105, -116), 21);
            style.accents = new Graphic[] { style.ring, hub, dots[0], dots[1] };
            return style;
        }

        // Portrait: minutes above seconds, split by the progress bar.
        static TimerStyle Tower(Transform timer)
        {
            var style = Style("Tower", timer, 0.75f, 0.09f, new Color(0.4f, 0.72f, 1), new Color(0.04f, 0.06f, 0.105f));
            var tile = new Color(0.08f, 0.115f, 0.185f);
            for (int i = 0; i < 4; i++)
                Digit(style, i, new Vector2(400, 470), new Vector2(i % 2 == 0 ? -212 : 212, i < 2 ? 330 : -200), tile, 44, 300, 1.5f, 22, 26);
            Status(style, new Vector2(860, 50), new Vector2(0, -510), 34);
            style.accents = new[] { Bar(style, new Vector2(824, 16), new Vector2(0, 65)) };
            return style;
        }

        static void BuildCards(ScreenAppView view, Transform parent)
        {
            var cards = Stretch("German Flashcards", parent);
            view.flashcardPanel = cards.gameObject;
            var accent = view.masculineColor;
            view.cardBackdrop = Box(Stretch("Backdrop", cards), new Color(0.035f, 0.05f, 0.085f), view.cardCornerRadius * 540, 0.7f);
            // The whole card is the click and fingertip target; swipes browse or reveal.
            var card = Stretch("Vocabulary card", cards, 18);
            view.cardBody = Target(card, Box(card, new Color(0.09f, 0.125f, 0.19f), 30, 0.8f));
            var stripe = Box(Rect("Article stripe", card, new Vector2(12, 380), new Vector2(-428, 0)), accent, 6);
            view.cardKind = Label("Kind", card, new Vector2(500, 40), new Vector2(-150, 205), "DER  ·  MASCULINE", 26, accent,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            view.cardCounter = Label("Card counter", card, new Vector2(240, 40), new Vector2(280, 205), "1 / 32", 26, Muted,
                FontStyle.Normal, TextAnchor.MiddleRight);
            view.cardWord = Label("German", card, new Vector2(800, 190), new Vector2(0, 62), "der Apfel", 132, Ink, FontStyle.Bold);
            BestFit(view.cardWord, 48);
            var divider = Box(Rect("Divider", card, new Vector2(96, 6), new Vector2(0, -52)), accent, 3);
            var answer = Color.Lerp(accent, Color.white, 0.5f);
            answer.a = 0; // Faded in when revealed.
            view.cardAnswer = Label("English", card, new Vector2(800, 110), new Vector2(0, -128), "apple", 68, answer);
            BestFit(view.cardAnswer, 30);
            view.cardHint = Label("Hint", card, new Vector2(800, 110), new Vector2(0, -128), "Swipe up to reveal", 30, Muted);
            var track = Rect("Progress", card, new Vector2(800, 8), new Vector2(0, -216));
            Box(track, Faint, 4);
            view.cardProgress = Stretch("Seen", track);
            view.cardProgress.anchorMax = new Vector2(1f / 32, 1);
            view.cardAccents = new Graphic[] { stripe, divider, Box(view.cardProgress, accent, 4) };
            cards.gameObject.SetActive(false);
        }

        // 960 x 1100: a slim header (size, moves, time) above a square board of numbered tiles.
        static void BuildPuzzle(ScreenAppView view, Transform parent)
        {
            var puzzle = Stretch("Slider Puzzle", parent);
            view.puzzlePanel = puzzle.gameObject;
            if (view.puzzleColors == null || view.puzzleColors.Length == 0)
                view.puzzleColors = new[] { new Color(0.3f, 0.86f, 0.8f), new Color(0.68f, 0.6f, 1), new Color(1, 0.74f, 0.36f) };
            var accent = view.puzzleColors[0];
            float height = ScreenAppView.CanvasWidth / view.puzzleAspect;
            view.puzzleBackdrop = Box(Stretch("Backdrop", puzzle), new Color(0.035f, 0.05f, 0.085f),
                view.puzzleCornerRadius * Mathf.Min(ScreenAppView.CanvasWidth, height), 0.7f);
            float header = height * 0.5f - 70;
            view.puzzleSize = Label("Size", puzzle, new Vector2(260, 60), new Vector2(-310, header), "3 × 3", 46, accent,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            view.puzzleMoves = Label("Moves", puzzle, new Vector2(320, 60), new Vector2(0, header), "0 MOVES", 30, Muted, FontStyle.Bold);
            view.puzzleTime = Label("Time", puzzle, new Vector2(260, 60), new Vector2(310, header), "00:00", 46, Ink,
                FontStyle.Bold, TextAnchor.MiddleRight);

            // The whole board is the click and fingertip target; swipes slide the tiles.
            var well = Rect("Board", puzzle, Vector2.one * 920, new Vector2(0, 480 - height * 0.5f));
            var dark = new Color(0.02f, 0.03f, 0.055f);
            view.puzzleBody = Target(well, Box(well, dark, 36));
            view.puzzleBoard = Stretch("Tiles", well, 14);
            const int tiles = SlidingPuzzle.MaximumSize * SlidingPuzzle.MaximumSize - 1;
            view.puzzleTiles = new RoundedRectGraphic[tiles];
            view.puzzleLabels = new Text[tiles];
            // Saved as a solved 3 x 3 for previewing; the remaining tiles wait for the larger boards.
            float pitch = (920 - 28 + view.puzzleGap) / 3, cell = pitch - view.puzzleGap;
            for (int i = 0; i < tiles; i++)
            {
                var tile = Rect("Tile " + (i + 1), view.puzzleBoard, Vector2.one * cell, new Vector2((i % 3 - 1) * pitch, (1 - i / 3) * pitch));
                view.puzzleTiles[i] = Box(tile, Color.Lerp(view.puzzleTileColor, accent, 0.42f), cell * 0.12f, 0.8f);
                var number = Label("Number", tile, Vector2.zero, Vector2.zero, (i + 1).ToString(), Mathf.RoundToInt(cell * 0.5f), Ink, FontStyle.Bold);
                number.rectTransform.anchorMin = Vector2.zero;
                number.rectTransform.anchorMax = Vector2.one;
                number.alignByGeometry = true;
                number.horizontalOverflow = HorizontalWrapMode.Overflow;
                number.verticalOverflow = VerticalWrapMode.Overflow;
                view.puzzleLabels[i] = number;
                if (i >= 8) tile.gameObject.SetActive(false);
            }

            var overlay = Stretch("Overlay", well);
            view.puzzleOverlay = overlay.gameObject;
            Box(overlay, new Color(dark.r, dark.g, dark.b, 0.82f), 36);
            view.puzzleBanner = Label("Banner", overlay, new Vector2(860, 140), new Vector2(0, 170), "READY", 120, accent, FontStyle.Bold);
            view.puzzleHint = Label("Hint", overlay, new Vector2(860, 130), new Vector2(0, 20),
                "Click to start\nSwipe up / down to change the size", 36, Ink);
            view.puzzleHint.lineSpacing = 1.25f;
            BestFit(view.puzzleHint, 20);
            view.puzzleChips = new RoundedRectGraphic[3];
            view.puzzleChipLabels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                var chip = Rect("Size " + (i + 3), overlay, new Vector2(190, 68), new Vector2((i - 1) * 215, -140));
                view.puzzleChips[i] = Box(chip, i == 0 ? accent : Faint, 34);
                view.puzzleChipLabels[i] = Label("Caption", chip, new Vector2(190, 68), Vector2.zero, (i + 3) + " × " + (i + 3), 32,
                    i == 0 ? view.puzzleDarkInk : Muted, FontStyle.Bold);
            }
            puzzle.gameObject.SetActive(false);
        }

        static void ApplyMaterial(Transform subtree)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Resources.Load<Shader>("OccludedScreenUI"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            foreach (var graphic in subtree.GetComponentsInChildren<Graphic>(true))
            { graphic.material = material; graphic.raycastTarget = false; }
        }

        // Keeps every edit made to the saved timer and flashcard layouts.
        static void AddPuzzleToSavedView()
        {
            var root = PrefabUtility.LoadPrefabContents(ViewPath);
            try
            {
                var view = root.GetComponent<ScreenAppView>();
                if (view.puzzlePanel == null)
                {
                    BuildPuzzle(view, root.transform);
                    ApplyMaterial(view.puzzlePanel.transform);
                }
                view.layoutVersion = ViewLayoutVersion;
                PrefabUtility.SaveAsPrefabAsset(root, ViewPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void BuildView()
        {
            var root = Rect("Screen App View", null, new Vector2(ScreenAppView.CanvasWidth, 540), Vector2.zero).gameObject;
            try
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                // Input uses the screen's existing ray/tip path, including both eyes in goggles.
                var view = root.AddComponent<ScreenAppView>();
                var timer = Stretch("Timer", root.transform);
                view.timerPanel = timer.gameObject;
                view.timerStyles = new[] { Blocks(timer), Orbit(timer), Capsule(timer), Tower(timer) };
                for (int i = 1; i < view.timerStyles.Length; i++) view.timerStyles[i].root.SetActive(false);
                BuildCards(view, root.transform);
                BuildPuzzle(view, root.transform);
                view.layoutVersion = ViewLayoutVersion;
                ApplyMaterial(root.transform);
                root.transform.localScale = Vector3.one * (0.16f / ScreenAppView.CanvasWidth);
                PrefabUtility.SaveAsPrefabAsset(root, ViewPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void UpgradeMenu()
        {
            var root = PrefabUtility.LoadPrefabContents(ScreenAppAuthoring.MenuPath);
            try
            {
                var settings = new SerializedObject(root.GetComponent<ScreenAppMenu>());
                int registryVersion = settings.FindProperty("builtInAppsLayoutVersion").intValue;
                if (registryVersion >= AppRegistryVersion) return;
                var registry = settings.FindProperty("apps");
                void Add(string id, string title)
                {
                    for (int i = 0; i < registry.arraySize; i++)
                        if (registry.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == id) return;
                    var entry = registry.GetArrayElementAtIndex(registry.arraySize++);
                    entry.FindPropertyRelative("id").stringValue = id;
                    entry.FindPropertyRelative("title").stringValue = title;
                    entry.FindPropertyRelative("launch.m_PersistentCalls.m_Calls").ClearArray();
                }
                Add("timer", "Timer"); Add("flashcards", "German flashcards"); Add("puzzle", "Slider puzzle"); Add("browser", "Browser");
                Add("pdf", "PDF viewer");
                settings.FindProperty("builtInAppsLayoutVersion").intValue = AppRegistryVersion;
                if (registryVersion >= 1)
                {
                    // The enlarged chooser layout is already saved; only the app list was behind.
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, ScreenAppAuthoring.MenuPath);
                    return;
                }
                settings.FindProperty("launcherWidthFraction").floatValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var launcher = root.transform.Find("Screen launcher");
                void Resize(Transform button, Vector2 size, Vector2 position)
                {
                    var rect = (RectTransform)button; rect.sizeDelta = size; rect.anchoredPosition = position;
                    var label = button.GetComponentInChildren<Text>(true); label.fontSize = 30;
                    label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                    label.rectTransform.offsetMin = new Vector2(8, 4); label.rectTransform.offsetMax = new Vector2(-8, -4);
                }
                Resize(launcher.Find("Choose app"), new Vector2(560, 76), new Vector2(0, 38));
                var options = (RectTransform)launcher.Find("App options");
                options.sizeDelta = new Vector2(560, 88); options.anchoredPosition = new Vector2(0, 88);
                options.GetComponent<VerticalLayoutGroup>().spacing = 8;
                Resize(options.Find("App option template"), new Vector2(560, 88), Vector2.zero);
                PrefabUtility.SaveAsPrefabAsset(root, ScreenAppAuthoring.MenuPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    /// <summary>A player is never built with app prefabs older than the scripts that drive them.</summary>
    public sealed class BuiltInAppPrefabCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuiltInAppAuthoring.PrefabsOutdated) BuiltInAppAuthoring.PrepareAssets();
        }
    }
}
