using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class ScreenAppAuthoring
    {
        public const string MenuPath = "Assets/ARVisualizer/Resources/ScreenAppMenu.prefab";
        [MenuItem("Tools/AR Visualizer/Prepare scrolling media controls")]
        public static void PrepareScene() { PrepareAssets(); NormalControlsAuthoring.PrepareScene(); }
        [MenuItem("Tools/AR Visualizer/Open screen apps prefab")]
        public static void Open() { PrepareAssets(); AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath)); }
        public static void PrepareAssets()
        {
            AddMediaFace();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath) == null) BuildMenu();
            var root = PrefabUtility.LoadPrefabContents(MenuPath);
            try { UpgradeScrollingMenu(root); PrefabUtility.SaveAsPrefabAsset(root, MenuPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_SCREEN_APPS_READY");
        }
        static void AddMediaFace() => ScreenAuthoring.EnsurePrefab();
        static readonly Color Background = new Color(0.035f, 0.055f, 0.075f, 0.97f);
        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        static Text Label(string name, Transform parent, Vector2 size, Vector2 position, string text, int fontSize = 24)
        {
            var label = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.text = text; label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.raycastTarget = false; return label;
        }
        static Button Button(string name, Transform parent, Vector2 size, Vector2 position, string title)
        {
            var rect = Rect(name, parent, size, position);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.1f, 0.18f, 0.22f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(0.4f, 1, 0.8f); colors.selectedColor = colors.normalColor;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label("Caption", rect, size - new Vector2(12, 4), Vector2.zero, title); return button;
        }
        static Canvas Canvas(string name, Transform parent, Vector2 size)
        {
            var rect = Rect(name, parent, size, Vector2.zero);
            var canvas = rect.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            rect.gameObject.AddComponent<GraphicRaycaster>(); return canvas;
        }
        static void BuildMenu()
        {
            var root = new GameObject("Screen App Menu", typeof(ScreenAppMenu));
            try
            {
                var settings = new SerializedObject(root.GetComponent<ScreenAppMenu>());
                void Bind(string name, Object value) => settings.FindProperty(name).objectReferenceValue = value;
                var launcher = Canvas("Screen launcher", root.transform, new Vector2(600, 180));
                ((RectTransform)launcher.transform).pivot = new Vector2(0.5f, 1);
                Bind("launcher", launcher);
                Bind("dropdown", Button("Choose app", launcher.transform, new Vector2(580, 70), new Vector2(0, -35), "Choose app  v"));
                var options = Rect("App options", launcher.transform, new Vector2(580, 75), new Vector2(0, -115));
                var layout = options.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childForceExpandHeight = false; layout.childControlHeight = false;
                var option = Button("App option template", options, new Vector2(580, 70), Vector2.zero, "Photos & videos");
                option.gameObject.SetActive(false); Bind("optionTemplate", option); Bind("options", options);
                var controls = Rect("Playback controls", launcher.transform, new Vector2(600, 70), new Vector2(0, -35));
                Bind("playbackControls", controls.gameObject);
                Bind("browse", Button("Choose media", controls, new Vector2(210, 70), new Vector2(-185, 0), "Choose media"));
                Bind("pause", Button("Play pause", controls, new Vector2(155, 70), Vector2.zero, "Pause"));
                Bind("clear", Button("Close app", controls, new Vector2(190, 70), new Vector2(185, 0), "Close app"));
                var browser = Canvas("Photo library", root.transform, new Vector2(960, 640)); Bind("browser", browser);
                browser.gameObject.AddComponent<Image>().color = Background;
                Bind("heading", Label("Title", browser.transform, new Vector2(720, 60), new Vector2(-65, 276), "Photos & videos", 30));
                Bind("close", Button("Close library", browser.transform, new Vector2(110, 55), new Vector2(405, 276), "Close"));
                for (int i = 0; i < 6; ++i)
                {
                    var tile = Button("Media " + (i + 1), browser.transform, new Vector2(288, 186), new Vector2((i % 3 - 1) * 304, 140 - i / 3 * 204), "");
                    Object.DestroyImmediate(tile.transform.Find("Caption").gameObject);
                    var thumb = Rect("Thumbnail", tile.transform, new Vector2(258, 136), new Vector2(0, 14)).gameObject.AddComponent<RawImage>();
                    thumb.raycastTarget = false;
                    var fit = thumb.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    // Thumbnail fits inside an authored wrapper, so portrait images do not cover the caption.
                    var wrapper = Rect("Thumbnail area", tile.transform, new Vector2(264, 136), new Vector2(0, 18));
                    thumb.transform.SetParent(wrapper, false);
                    var caption = Label("Caption", tile.transform, new Vector2(274, 40), new Vector2(0, -70), "Photo", 18);
                }
                Bind("status", Label("Status", browser.transform, new Vector2(880, 60), new Vector2(0, -204), "Point and click to select", 22));
                Bind("refresh", Button("Refresh library", browser.transform, new Vector2(300, 56), new Vector2(0, -276), "Refresh / retry"));
                settings.ApplyModifiedPropertiesWithoutUndo();
                UpgradeScrollingMenu(root);
                launcher.transform.localScale = browser.transform.localScale = Vector3.one * 0.001f;
                launcher.gameObject.SetActive(false); browser.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, MenuPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void UpgradeScrollingMenu(GameObject root)
        {
            var browser = root.transform.Find("Photo library");
            if (browser.Find("Media viewport") != null) return; // Preserve later prefab edits.
            var settings = new SerializedObject(root.GetComponent<ScreenAppMenu>());
            var grid = root.AddComponent<MediaLibraryGrid>();
            var input = root.AddComponent<ScreenMenuInput>();
            settings.FindProperty("libraryGrid").objectReferenceValue = grid;
            settings.FindProperty("menuInput").objectReferenceValue = input;
            settings.ApplyModifiedPropertiesWithoutUndo();
            foreach (var name in new[] { "Previous page", "Next page" })
                if (browser.Find(name) != null) Object.DestroyImmediate(browser.Find(name).gameObject);
            var viewport = Rect("Media viewport", browser, new Vector2(912, 408), new Vector2(0, 36));
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport, new Vector2(0, 408), Vector2.zero);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
            var fields = new SerializedObject(grid);
            fields.FindProperty("scroll").objectReferenceValue = scroll;
            var array = fields.FindProperty("tiles"); array.arraySize = 12;
            for (int i = 0; i < 12; ++i)
            {
                var existing = browser.Find("Media " + (i + 1));
                var tile = existing != null ? existing.GetComponent<UnityEngine.UI.Button>()
                    : Object.Instantiate(content.GetChild(0).GetComponent<UnityEngine.UI.Button>(), content);
                tile.transform.SetParent(content, false); tile.name = "Media " + (i + 1);
                var rect = (RectTransform)tile.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(8 + i % 3 * 304, -(i / 3) * 204);
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("button").objectReferenceValue = tile;
                element.FindPropertyRelative("thumbnail").objectReferenceValue = tile.GetComponentInChildren<RawImage>(true);
                element.FindPropertyRelative("caption").objectReferenceValue = tile.transform.Find("Caption").GetComponent<Text>();
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            browser.Find("Status").GetComponent<Text>().text = "Hold left click and swipe up / down";
            var launcher = (RectTransform)root.transform.Find("Screen launcher");
            launcher.pivot = new Vector2(0.5f, 0); launcher.sizeDelta = new Vector2(600, 120);
            void SmallButton(Transform button, Vector2 size, Vector2 position)
            {
                var rect = (RectTransform)button; rect.sizeDelta = size; rect.anchoredPosition = position;
                var label = button.Find("Caption").GetComponent<Text>(); label.fontSize = 20;
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(5, 2); label.rectTransform.offsetMax = new Vector2(-5, -2);
            }
            var choose = (RectTransform)launcher.Find("Choose app");
            choose.anchorMin = choose.anchorMax = new Vector2(0.5f, 0);
            SmallButton(choose, new Vector2(320, 48), new Vector2(0, 24));
            var options = (RectTransform)launcher.Find("App options");
            options.anchorMin = options.anchorMax = new Vector2(0.5f, 0); options.pivot = new Vector2(0.5f, 0);
            options.sizeDelta = new Vector2(320, 48); options.anchoredPosition = new Vector2(0, 58);
            options.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            SmallButton(options.Find("App option template"), new Vector2(320, 48), Vector2.zero);
            var playback = (RectTransform)launcher.Find("Playback controls");
            playback.anchorMin = playback.anchorMax = new Vector2(0.5f, 0);
            playback.sizeDelta = new Vector2(600, 48); playback.anchoredPosition = new Vector2(0, 24);
            SmallButton(playback.Find("Choose media"), new Vector2(210, 48), new Vector2(-185, 0));
            SmallButton(playback.Find("Play pause"), new Vector2(155, 48), Vector2.zero);
            SmallButton(playback.Find("Close app"), new Vector2(190, 48), new Vector2(185, 0));
        }
    }
}
