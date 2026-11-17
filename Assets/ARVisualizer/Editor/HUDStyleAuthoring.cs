using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ARVisualizer.Editor
{
    /// <summary>
    /// One-time iOS restyle of the saved HUDs: squircle panels and buttons, the system typeface, larger
    /// minimum text sizes and a tighter phone layout. Later runs keep manual layout edits; they only
    /// re-link an SF Pro font asset if one is imported later.
    /// </summary>
    public static class HUDStyleAuthoring
    {
        public const int StyleVersion = 1;
        public const string SpritePath = "Assets/ARVisualizer/UI/Squircle.png";
        const string PhoneControlsPath = "Assets/ARVisualizer/Prefabs/PhoneControls.prefab";
        const string TogglePath = "Assets/ARVisualizer/Prefabs/HUDToggleButton.prefab";
        const string ModeButtonPath = "Assets/ARVisualizer/Prefabs/DisplayModeButton.prefab";
        // Corner extent of the 9-sliced squircle texture, in pixels.
        const int SpriteSize = 128, SpriteBorder = 48;
        const float Exponent = 5;

        enum Kind { Phone, World, View }

        static string[] Prefabs => new[]
        {
            VisualizerHUDAuthoring.PrefabPath, PhoneControlsPath, ScreenAppAuthoring.MenuPath, BuiltInAppAuthoring.ViewPath
        };

        [MenuItem("Tools/AR Visualizer/Apply iOS HUD style")]
        public static void ApplyFromMenu() => Apply(force: false);

        public static bool NeedsUpgrade()
        {
            var font = FindSFProFont();
            foreach (var path in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var typeface = prefab.GetComponent<HUDTypeface>();
                if (typeface == null || typeface.StyleVersion < StyleVersion) return true;
                if (font != null && typeface.FontAsset != font) return true;
            }
            return false;
        }

        public static void Apply(bool force)
        {
            var sprite = EnsureSprite();
            var font = FindSFProFont();
            // Nested buttons are styled in their own prefabs, once, with the HUD that contains them.
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(VisualizerHUDAuthoring.PrefabPath);
            var hudTypeface = hud != null ? hud.GetComponent<HUDTypeface>() : null;
            if (force || hudTypeface == null || hudTypeface.StyleVersion < StyleVersion)
                foreach (var path in new[] { TogglePath, ModeButtonPath })
                    Edit(path, root =>
                    {
                        TypeTree(root.transform, Kind.Phone, false);
                        if (path == TogglePath) ((RectTransform)root.transform).sizeDelta = new Vector2(56, 56);
                        ShapeTree(root.transform, Kind.Phone, sprite, false);
                    });
            foreach (var path in Prefabs)
            {
                var kind = path == ScreenAppAuthoring.MenuPath ? Kind.World : path == BuiltInAppAuthoring.ViewPath ? Kind.View : Kind.Phone;
                Edit(path, root =>
                {
                    var typeface = root.GetComponent<HUDTypeface>();
                    if (typeface == null) typeface = root.AddComponent<HUDTypeface>();
                    var fields = new SerializedObject(typeface);
                    fields.FindProperty("font").objectReferenceValue = font;
                    bool restyle = force || fields.FindProperty("styleVersion").intValue < StyleVersion;
                    fields.FindProperty("styleVersion").intValue = StyleVersion;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    if (!restyle) return;
                    // Sizes first, then the explicit compact layout, then corners for the final sizes.
                    TypeTree(root.transform, kind, true);
                    if (path == VisualizerHUDAuthoring.PrefabPath) CompactHUD(root, sprite);
                    if (path == PhoneControlsPath) CompactPhoneControls(root);
                    ShapeTree(root.transform, kind, sprite, true);
                });
            }
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_HUD_STYLE_READY" + (font != null ? " / font " + font.name : " / system font at runtime"));
        }

        static void Edit(string path, Action<GameObject> change)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { change(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>An imported SF Pro (or SF Pro Text) regular font, if the project has one.</summary>
        static Font FindSFProFont()
        {
            var fonts = AssetDatabase.FindAssets("t:Font").Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Select(path => (path, name: Path.GetFileNameWithoutExtension(path).Replace(" ", "").Replace("-", "").ToLowerInvariant()))
                .Where(entry => entry.name.StartsWith("sfpro", StringComparison.Ordinal) || entry.name.StartsWith("sfuitext", StringComparison.Ordinal))
                .ToArray();
            string[] preferred = { "sfprotextregular", "sfpro", "sfproregular", "sfprodisplayregular", "sfuitextregular" };
            foreach (var name in preferred)
                foreach (var entry in fonts)
                    if (entry.name == name) return AssetDatabase.LoadAssetAtPath<Font>(entry.path);
            return fonts.Length > 0 ? AssetDatabase.LoadAssetAtPath<Font>(fonts[0].path) : null;
        }

        // A 9-sliced white squircle. Its corners are quarter superellipses, like iOS continuous corners.
        static Sprite EnsureSprite()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite != null) return sprite;
            Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            const int samples = 4;
            for (int y = 0; y < SpriteSize; y++)
                for (int x = 0; x < SpriteSize; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float px = x + (sx + 0.5f) / samples, py = y + (sy + 0.5f) / samples;
                            float dx = Mathf.Max(0, Mathf.Max(SpriteBorder - px, px - (SpriteSize - SpriteBorder))) / SpriteBorder;
                            float dy = Mathf.Max(0, Mathf.Max(SpriteBorder - py, py - (SpriteSize - SpriteBorder))) / SpriteBorder;
                            if (Mathf.Pow(dx, Exponent) + Mathf.Pow(dy, Exponent) <= 1) inside++;
                        }
                    texture.SetPixel(x, y, new Color(1, 1, 1, inside / (float)(samples * samples)));
                }
            File.WriteAllBytes(SpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = Vector4.one * SpriteBorder;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }

        static bool InNestedPrefab(Transform transform, Transform root) =>
            transform != root && PrefabUtility.IsPartOfPrefabInstance(transform.gameObject)
            && PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject) != root.gameObject;

        static void ShapeTree(Transform root, Kind kind, Sprite sprite, bool skipNested)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (skipNested && InNestedPrefab(image.transform, root)) continue;
                if (image.transform.GetComponentsInParent<Transform>(true).Any(parent => parent.name == "Aim Position")) continue;
                if (image.sprite != null && image.sprite != sprite && !IsDefaultSprite(image.sprite)) continue;
                float radius = CornerRadius(image, kind);
                if (radius > 0) Round(image, sprite, radius);
            }
        }

        static void TypeTree(Transform root, Kind kind, bool skipNested)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (skipNested && InNestedPrefab(text.transform, root)) continue;
                bool onButton = text.GetComponentInParent<Button>(true) != null;
                if (kind == Kind.Phone)
                {
                    text.fontSize = ReadableSize(text.fontSize);
                    if (text.resizeTextForBestFit) text.resizeTextMaxSize = Mathf.Max(text.resizeTextMaxSize, text.fontSize);
                    if (onButton && IsShouty(text.text) && text.GetComponentInParent<HUDVisibilityToggle>(true) == null)
                        text.text = HUDTypeface.TitleCase(text.text);
                    if (onButton)
                    {
                        // Long captions shrink a little rather than clip.
                        text.resizeTextForBestFit = true;
                        text.resizeTextMinSize = Mathf.Min(12, text.fontSize);
                        text.resizeTextMaxSize = text.fontSize;
                    }
                }
                if (kind != Kind.View && (onButton || IsSectionLabel(text))) text.fontStyle = FontStyle.Bold;
            }
        }

        static bool IsDefaultSprite(Sprite candidate) => candidate.name == "UISprite" || candidate.name == "Background";

        /// <summary>Canvas units. Pills for slim bars, gentler squircles for panels than for buttons.</summary>
        static float CornerRadius(Image image, Kind kind)
        {
            var rect = image.rectTransform;
            float width = Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x) ? rect.sizeDelta.x : float.PositiveInfinity;
            float height = Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y) ? rect.sizeDelta.y : float.PositiveInfinity;
            float shorter = Mathf.Min(width, height), longer = Mathf.Max(width, height);
            if (float.IsInfinity(shorter) || shorter <= 0) return 0; // Full-screen overlays stay square.
            if (shorter <= 8) return shorter * 0.5f;
            bool world = kind != Kind.Phone;
            if (image.GetComponent<Button>() != null)
                return Mathf.Clamp(shorter * (shorter <= 120 ? 0.25f : 0.12f), 6, world ? 40 : 28);
            if (shorter <= 36 && longer >= shorter * 2) return shorter * 0.5f;
            return Mathf.Clamp(shorter * (world ? 0.06f : 0.1f), world ? 16 : 10, world ? 48 : 20);
        }

        static void Round(Image image, Sprite sprite, float radius)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            // With 100 pixels per unit on both sprite and canvas, the corner spans border / multiplier units.
            image.pixelsPerUnitMultiplier = SpriteBorder / Mathf.Max(0.5f, radius);
        }

        static int ReadableSize(int size) => size switch
        {
            <= 11 => 13, 12 => 14, 13 => 15, 14 => 15, 15 => 16, 16 => 17,
            >= 22 and <= 25 => 22, 26 => 24, >= 27 and <= 30 => 26, >= 36 => 34, _ => size
        };

        static bool IsShouty(string value) => value.Any(char.IsLetter) && value == value.ToUpperInvariant();

        static bool IsSectionLabel(Text text) => text.fontSize <= 16 && IsShouty(text.text) && text.text.Length <= 32
            && !text.name.StartsWith("Gesture ", StringComparison.Ordinal) && text.name != "Empty State";


        static RectTransform Place(Transform root, string path, Vector2 position, Vector2 size)
        {
            var rect = root.Find(path) as RectTransform;
            if (rect == null) { Debug.LogWarning("HUD style: " + path + " not found; skipped"); return null; }
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static void SetFont(Transform root, string path, int size, bool bold = false, int minimum = 0)
        {
            var text = root.Find(path)?.GetComponent<Text>();
            if (text == null) return;
            text.fontSize = size;
            if (bold) text.fontStyle = FontStyle.Bold;
            text.resizeTextForBestFit = minimum > 0;
            if (minimum <= 0) return;
            text.resizeTextMinSize = minimum;
            text.resizeTextMaxSize = size;
        }

        static void Label(Transform root, string path, Vector2 position, Vector2 size, int fontSize, bool bold = false, int minimum = 0)
        {
            if (Place(root, path, position, size) != null) SetFont(root, path, fontSize, bold, minimum);
        }

        static void CompactPhoneControls(GameObject root)
        {
            var mode = root.GetComponentInChildren<DisplayModeButton>(true);
            if (mode == null) return;
            var rect = (RectTransform)mode.transform;
            rect.anchoredPosition = new Vector2(0, -16);
            rect.sizeDelta = new Vector2(56, 56);
        }

        // Tighter phone layout (1280 x 720 reference) with every label at 13 units or more.
        static void CompactHUD(GameObject hud, Sprite sprite)
        {
            var root = hud.transform;
            var toggle = hud.GetComponentInChildren<HUDVisibilityToggle>(true);
            if (toggle != null)
            {
                var rect = (RectTransform)toggle.transform;
                rect.anchoredPosition = new Vector2(20, -16);
                rect.sizeDelta = new Vector2(56, 56);
            }
            var settings = new SerializedObject(hud.GetComponent<VisualizerHUD>());
            foreach (var (field, caption) in new[]
            {
                ("pointerOnCaption", "Pointer On"), ("pointerOffCaption", "Pointer Off"), ("enterPlaceCaption", "Enter Place"),
                ("exitPlaceCaption", "Exit Place"), ("placeScreenCaption", "Place Screen"), ("placeHereCaption", "Place Here"),
                ("moveHereCaption", "Move Here"), ("cancelMoveCaption", "Cancel Move")
            }) settings.FindProperty(field).stringValue = caption;
            settings.ApplyModifiedPropertiesWithoutUndo();

            // Top bar: one 56-unit row below the HUD and mode buttons.
            var top = root.Find("Safe Area/Top Bar") as RectTransform;
            if (top != null)
            {
                top.offsetMin = new Vector2(20, -136);
                top.offsetMax = new Vector2(-20, -80);
                Label(root, "Safe Area/Top Bar/Brand", new Vector2(126, 36), new Vector2(220, 28), 22, true);
                Label(root, "Safe Area/Top Bar/Subtitle", new Vector2(126, 14), new Vector2(220, 16), 13, true);
                Label(root, "Safe Area/Top Bar/Status/AR Status", new Vector2(130, 37), new Vector2(260, 22), 16, false, 13);
                Label(root, "Safe Area/Top Bar/Status/Hand Status", new Vector2(390, 37), new Vector2(230, 22), 16, false, 13);
                Label(root, "Safe Area/Top Bar/Status/Screen Count", new Vector2(620, 37), new Vector2(190, 22), 16, true, 13);
                Label(root, "Safe Area/Top Bar/Status/Wi-Fi Status", new Vector2(357.5f, 15), new Vector2(715, 20), 14, false, 12);
            }

            Place(root, "Safe Area/Gesture History", new Vector2(20, -148), new Vector2(300, 188));
            Label(root, "Safe Area/Gesture History/Heading", new Vector2(14, -10), new Vector2(-28, 20), 14, true);
            Label(root, "Safe Area/Gesture History/Empty State", new Vector2(14, -36), new Vector2(-28, 22), 15);
            for (int i = 1; i <= 6; i++)
                Label(root, "Safe Area/Gesture History/Gesture " + i, new Vector2(14, -36 - (i - 1) * 24), new Vector2(-28, 22), 15);

            Place(root, "Safe Area/Placement Panel", new Vector2(16, 16), new Vector2(320, 104));
            Label(root, "Safe Area/Placement Panel/Mode Heading", new Vector2(14, 82), new Vector2(292, 16), 13, true);
            Label(root, "Safe Area/Placement Panel/Target Status", new Vector2(14, 56), new Vector2(292, 24), 19, true, 15);
            Label(root, "Safe Area/Placement Panel/Surface Details", new Vector2(14, 38), new Vector2(292, 18), 14, false, 12);
            Label(root, "Safe Area/Placement Panel/Feedback", new Vector2(14, 4), new Vector2(292, 32), 14, false, 12);

            // Bottom-right column: 120 x 40 buttons, 6 apart, 372 wide.
            Place(root, "Safe Area/Controls", new Vector2(-16, 16), new Vector2(372, 56));
            string[] controls = { "Pointer Button", "Mode Button", "Place Screen Button", "Undo Button" };
            float[] columns = { 0, 126, 252, 252 };
            for (int i = 0; i < controls.Length; i++)
                Place(root, "Safe Area/Controls/" + controls[i], new Vector2(columns[i], 0), new Vector2(120, 40));
            Label(root, "Safe Area/Controls/Screen Size", new Vector2(0, 42), new Vector2(372, 14), 13);
            Place(root, "Safe Area/Normal Actions", new Vector2(-16, 76), new Vector2(372, 40));
            string[] actions = { "Left Click Button", "Right Click Button", "Screen Settings Button" };
            for (int i = 0; i < actions.Length; i++)
                Place(root, "Safe Area/Normal Actions/" + actions[i], new Vector2(i * 126, 0), new Vector2(120, 40));

            const string panel = "Safe Area/Normal Actions/Screen Settings Panel";
            Place(root, panel, new Vector2(0, 46), new Vector2(372, 180));
            Label(root, panel + "/Selected Screen", new Vector2(12, 146), new Vector2(348, 26), 17, true, 13);
            string[,] grid =
            {
                { "Choose App Button", "Smaller Button", "Larger Button" },
                { "Rotate Left Button", "Rotate Right Button", "Move Screen Button" }
            };
            for (int row = 0; row < 2; row++)
                for (int column = 0; column < 3; column++)
                    Place(root, panel + "/" + grid[row, column], new Vector2(12 + column * 118, 100 - row * 44), new Vector2(112, 38));
            Place(root, panel + "/Delete Screen Button", new Vector2(12, 12), new Vector2(230, 38));
            Place(root, panel + "/Done Button", new Vector2(248, 12), new Vector2(112, 38));

            // Gesture test panel sits left of the column, clear of the settings panel.
            Place(root, "Safe Area/Gesture Controls", new Vector2(-16, 16), new Vector2(120, 40));
            Place(root, "Safe Area/Gesture Controls/Gestures Button", Vector2.zero, new Vector2(120, 40));
            const string tests = "Safe Area/Gesture Controls/Gesture Panel";
            if (Place(root, tests, new Vector2(-264, 0), new Vector2(372, 376)) != null)
            {
                Label(root, tests + "/Heading", new Vector2(12, 344), new Vector2(348, 22), 16, true);
                Place(root, tests + "/Left Hand", new Vector2(12, 300), new Vector2(171, 36));
                Place(root, tests + "/Right Hand", new Vector2(189, 300), new Vector2(171, 36));
                var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
                for (int i = 0; i < catalogue.gestures.Length; i++)
                    Place(root, tests + "/" + catalogue.gestures[i].gesture,
                        new Vector2(12 + i % 2 * 177, 256 - i / 2 * 40), new Vector2(171, 34));
                Label(root, tests + "/Result", new Vector2(12, 8), new Vector2(348, 42), 14, false, 12);
            }
            Place(root, "Safe Area/Gesture Guide/Guide Strip", new Vector2(-4, 0), new Vector2(16, 150));

            CompactTutorial(root, sprite);
        }

        static void CompactTutorial(Transform root, Sprite sprite)
        {
            const string choice = "Tutorial/Tutorial Slide/Mode Choice";
            Label(root, choice + "/Heading", new Vector2(0, 214), new Vector2(700, 20), 14, true);
            Label(root, choice + "/Subheading", new Vector2(0, 184), new Vector2(700, 34), 26, true);
            Label(root, choice + "/Hint", new Vector2(0, -170), new Vector2(700, 22), 15);
            foreach (var (square, x) in new[] { ("Tutorial Mode Button", -142f), ("Free Mode Button", 142f) })
            {
                string path = choice + "/" + square;
                if (Place(root, path, new Vector2(x, -10), new Vector2(264, 264)) == null) continue;
                var image = root.Find(path).GetComponent<Image>();
                Round(image, sprite, 32);
                // A square-cornered stripe would poke out of the squircle; use a short grabber instead.
                var accent = root.Find(path + "/Accent") as RectTransform;
                if (accent != null)
                {
                    accent.anchorMin = accent.anchorMax = accent.pivot = new Vector2(0.5f, 1);
                    accent.anchoredPosition = new Vector2(0, -14);
                    accent.sizeDelta = new Vector2(40, 5);
                    Round(accent.GetComponent<Image>(), sprite, 2.5f);
                }
                Label(root, path + "/Title", new Vector2(0, 22), new Vector2(240, 100), 34, true);
                Label(root, path + "/Caption", new Vector2(0, -84), new Vector2(224, 44), 15, false, 12);
            }

            const string lesson = "Tutorial/Tutorial Slide/Lesson";
            Place(root, lesson + "/Video Panel", new Vector2(20, -20), new Vector2(196, 352));
            Label(root, lesson + "/Video Panel/Heading", new Vector2(12, -8), new Vector2(172, 18), 14, true);
            Place(root, lesson + "/Video Panel/Video Area", new Vector2(10, -30), new Vector2(176, 312));
            const string message = lesson + "/Message Panel";
            if (Place(root, message, new Vector2(0, 16), new Vector2(760, 150)) != null)
            {
                Label(root, message + "/Step", new Vector2(18, -10), new Vector2(340, 18), 14, true);
                Label(root, message + "/Status", new Vector2(-18, -10), new Vector2(360, 18), 14, false, 12);
                Label(root, message + "/Title", new Vector2(18, -30), new Vector2(724, 30), 24, true);
                Label(root, message + "/Body", new Vector2(18, -62), new Vector2(724, 62), 17, false, 13);
                var pips = root.Find(message).Cast<Transform>().Where(child => child.name.StartsWith("Step ", StringComparison.Ordinal)).ToArray();
                float pitch = 724f / Mathf.Max(1, pips.Length);
                for (int i = 0; i < pips.Length; i++)
                {
                    var pip = (RectTransform)pips[i];
                    pip.anchoredPosition = new Vector2(18 + i * pitch, 10);
                    pip.sizeDelta = new Vector2(pitch - 6, 4);
                    Round(pip.GetComponent<Image>(), sprite, 2);
                }
            }
            foreach (var (button, y) in new[] { ("Skip Gesture Button", 62f), ("Exit Tutorial Button", 16f) })
            {
                Place(root, lesson + "/" + button, new Vector2(-20, y), new Vector2(128, 40));
                Label(root, lesson + "/" + button + "/Caption", Vector2.zero, new Vector2(120, 36), 15, true, 12);
            }

            const string back = "Tutorial/Tutorial Return";
            Place(root, back + "/Return Bar", new Vector2(4, 0), new Vector2(16, 150));
            var fill = root.Find(back + "/Return Bar/Hold Progress")?.GetComponent<Image>();
            if (fill != null) Round(fill, sprite, 8);
            Place(root, back + "/Return Hint", new Vector2(28, 0), new Vector2(200, 46));
            Label(root, back + "/Return Hint/Label", new Vector2(12, -6), new Vector2(176, 16), 13, true);
            Label(root, back + "/Return Hint/Countdown", new Vector2(12, -22), new Vector2(176, 20), 16);

            foreach (var hand in new[] { "Tutorial Left Hand", "Tutorial Right Hand" })
            {
                Place(root, hand + "/Progress Bar", new Vector2(0, -18), new Vector2(12, 46));
                for (int i = 0; i < 3; i++)
                    Place(root, hand + "/Progress Bar/Third " + (i + 1), new Vector2(0, 4 + i * 14), new Vector2(6, 12));
                Label(root, hand + "/Caption", new Vector2(0, -70), new Vector2(170, 16), 13, true);
                Place(root, hand + "/Popup", new Vector2(0, 20), new Vector2(280, 30));
                Label(root, hand + "/Popup/Message", Vector2.zero, new Vector2(268, 26), 15, true, 12);
            }
        }
    }
}
