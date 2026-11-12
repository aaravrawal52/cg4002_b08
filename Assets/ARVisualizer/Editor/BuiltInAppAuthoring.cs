using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    /// <summary>Creates saved, editable prefabs once; subsequent runs preserve Inspector edits.</summary>
    public static class BuiltInAppAuthoring
    {
        public const string ViewPath = "Assets/ARVisualizer/Resources/ScreenAppView.prefab";
        const string MaterialPath = "Assets/ARVisualizer/Materials/ScreenAppUI.mat";

        [MenuItem("Tools/AR Visualizer/Prepare timer and flashcard apps")]
        public static void PrepareAssets()
        {
            ScreenAppAuthoring.PrepareAssets();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath) == null) BuildView();
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
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_BUILT_IN_APPS_READY");
        }

        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static Text Label(string name, Transform parent, Vector2 size, Vector2 position, string value, int fontSize)
        {
            var text = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        static Button Button(string name, Transform parent, Vector2 size, Vector2 position, string title, int fontSize = 32)
        {
            var rect = Rect(name, parent, size, position);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.15f, 0.2f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None; // Both hand highlights are supplied by the view.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (!string.IsNullOrEmpty(title)) Label("Caption", rect, size - new Vector2(16, 8), Vector2.zero, title, fontSize);
            return button;
        }

        static void BuildView()
        {
            var root = Rect("Screen App View", null, new Vector2(960, 540), Vector2.zero).gameObject;
            try
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                // Input uses the screen's existing ray/tip path, including both eyes in goggles.
                root.AddComponent<Image>().color = new Color(0.025f, 0.045f, 0.065f);
                var view = root.AddComponent<ScreenAppView>();
                var timer = Rect("Timer", root.transform, new Vector2(960, 540), Vector2.zero);
                view.timerPanel = timer.gameObject;
                Label("Title", timer, new Vector2(800, 48), new Vector2(0, 223), "TIMER", 32);
                Label("Minutes", timer, new Vector2(400, 30), new Vector2(-220, 170), "MINUTES", 20);
                Label("Seconds", timer, new Vector2(400, 30), new Vector2(220, 170), "SECONDS", 20);
                view.timeDigits = new Button[4]; view.digitLabels = new Text[4];
                float[] x = { -318, -120, 120, 318 };
                string[] steps = { "10 min", "1 min", "10 sec", "1 sec" };
                for (int i = 0; i < 4; i++)
                {
                    var digit = Button("Digit " + (i + 1) + " - " + steps[i], timer, new Vector2(180, 185), new Vector2(x[i], 55), "");
                    view.timeDigits[i] = digit;
                    view.digitLabels[i] = Label("Value", digit.transform, new Vector2(168, 130), new Vector2(0, 15), i == 1 ? "1" : "0", 108);
                    Label("Step", digit.transform, new Vector2(168, 30), new Vector2(0, -65), "+ / - " + steps[i], 20);
                }
                Label("Separator", timer, new Vector2(45, 140), new Vector2(0, 70), ":", 90);
                view.timerStatus = Label("Status", timer, new Vector2(900, 65), new Vector2(0, -94), "Swipe up / down on a digit to set time", 26);
                view.timerToggle = Button("Start pause", timer, new Vector2(380, 85), new Vector2(-215, -188), "Start");
                view.timerToggleLabel = view.timerToggle.GetComponentInChildren<Text>();
                view.timerReset = Button("Reset", timer, new Vector2(380, 85), new Vector2(215, -188), "Reset");

                var cards = Rect("German Flashcards", root.transform, new Vector2(960, 540), Vector2.zero);
                view.flashcardPanel = cards.gameObject;
                view.cardCounter = Label("Card counter", cards, new Vector2(850, 50), new Vector2(0, 221), "GERMAN  /  1 / 32", 30);
                view.cardBody = Button("Vocabulary card", cards, new Vector2(880, 285), new Vector2(0, 30), "");
                view.cardWord = Label("German", view.cardBody.transform, new Vector2(830, 135), new Vector2(0, 45), "der Apfel", 72);
                view.cardWord.resizeTextForBestFit = true; view.cardWord.resizeTextMinSize = 30; view.cardWord.resizeTextMaxSize = 72;
                view.cardAnswer = Label("English", view.cardBody.transform, new Vector2(830, 88), new Vector2(0, -70), "Click to reveal the English meaning", 30);
                view.cardPrevious = Button("Previous card", cards, new Vector2(270, 85), new Vector2(-305, -188), "Previous");
                view.cardReveal = Button("Reveal answer", cards, new Vector2(270, 85), new Vector2(0, -188), "Reveal");
                view.cardNext = Button("Next card", cards, new Vector2(270, 85), new Vector2(305, -188), "Next");
                view.cardRevealLabel = view.cardReveal.GetComponentInChildren<Text>();
                cards.gameObject.SetActive(false);
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (material == null)
                {
                    material = new Material(Resources.Load<Shader>("OccludedScreenUI"));
                    AssetDatabase.CreateAsset(material, MaterialPath);
                }
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                { graphic.material = material; graphic.raycastTarget = false; }
                root.transform.localScale = Vector3.one * (0.16f / 960);
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
                if (settings.FindProperty("builtInAppsLayoutVersion").intValue >= 1) return;
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
                Add("timer", "Timer"); Add("flashcards", "German flashcards");
                settings.FindProperty("launcherWidthFraction").floatValue = 1;
                settings.FindProperty("builtInAppsLayoutVersion").intValue = 1;
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
}
