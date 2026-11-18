using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class GestureUXAuthoring
    {
        [MenuItem("Tools/AR Visualizer/Prepare gesture controls and guide")]
        public static void PrepareAssets()
        {
            var hud = PrefabUtility.LoadPrefabContents(VisualizerHUDAuthoring.PrefabPath);
            try
            {
                DebugHUDAuthoring.ConfigureHUD(hud.GetComponent<VisualizerHUD>());
                ConfigureGuide(hud.GetComponent<VisualizerHUD>());
                PrefabUtility.SaveAsPrefabAsset(hud, VisualizerHUDAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
            const string menuPath = "Assets/ARVisualizer/Resources/ScreenAppMenu.prefab";
            var menu = PrefabUtility.LoadPrefabContents(menuPath);
            try
            {
                ConfigureOptions(menu.GetComponent<ScreenAppMenu>());
                PrefabUtility.SaveAsPrefabAsset(menu, menuPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(menu); }
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_GESTURE_UX_READY");
        }

        public static void ConfigureGuide(VisualizerHUD hud)
        {
            if (hud.GetComponent<GestureGuideHUD>() != null) return;
            var guide = hud.gameObject.AddComponent<GestureGuideHUD>();
            var fields = new SerializedObject(guide);
            void Bind(string field, Object value) => fields.FindProperty(field).objectReferenceValue = value;
            var safe = hud.transform.Find("Safe Area");
            var root = Rect("Gesture Guide", safe, Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            Bind("root", root.gameObject);
            var strip = Rect("Guide Strip", root, new Vector2(18, 164), new Vector2(-4, 0));
            strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(1, 0.5f);
            Paint(strip, new Color(0.23f, 0.83f, 0.65f, 0.85f));
            for (int i = 0; i < 3; i++) Paint(Rect("Dot " + i, strip, new Vector2(4, 4), new Vector2(7, 66 + i * 12)), Color.white);
            Bind("strip", strip);
            var panel = Rect("Guide Panel", root, new Vector2(688, 460), new Vector2(-36, 0));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0.5f);
            Paint(panel, new Color(0.025f, 0.045f, 0.065f, 0.96f));
            Bind("panel", panel);
            Label("Heading", panel, new Vector2(644, 32), new Vector2(22, 412), "GESTURE GUIDE", 24);
            Bind("contextLabel", Label("Context", panel, new Vector2(644, 40), new Vector2(22, 368), "Point at a screen or a flat surface", 16));
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            var actionFields = fields.FindProperty("actions"); actionFields.arraySize = catalogue.gestures.Length;
            string[] icons = { "X", "+ -", "< >", "< o >", "...", "OK", "*", "UP", "DN", "<", ">" };
            for (int i = 0; i < catalogue.gestures.Length; i++)
            {
                var row = Rect(catalogue.gestures[i].gesture, panel, new Vector2(316, 52), new Vector2(22 + i % 2 * 330, 310 - i / 2 * 56));
                var badge = Rect("Symbol", row, new Vector2(44, 44), new Vector2(0, 4));
                Paint(badge, new Color(0.1f, 0.3f, 0.3f));
                var symbol = Label("Icon", badge, new Vector2(44, 44), Vector2.zero, icons[i], 18); symbol.alignment = TextAnchor.MiddleCenter;
                Label("Gesture", row, new Vector2(262, 23), new Vector2(54, 28), catalogue.gestures[i].gesture.Replace('_', ' '), 15);
                actionFields.GetArrayElementAtIndex(i).objectReferenceValue = Label("Action", row, new Vector2(262, 28), new Vector2(54, 0), catalogue.gestures[i].guide, 13);
            }
            Label("Dismiss hint", panel, new Vector2(644, 22), new Vector2(22, 6), "Point away to close  /  Grab confirms  /  Crumple cancels", 13);
            var edit = Label("Gesture Edit Status", safe, new Vector2(940, 44), new Vector2(0, 12), "", 17);
            edit.rectTransform.anchorMin = edit.rectTransform.anchorMax = edit.rectTransform.pivot = new Vector2(0.5f, 0);
            edit.alignment = TextAnchor.MiddleCenter;
            edit.color = new Color(0.5f, 1, 0.8f);
            Bind("editLabel", edit);
            fields.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false); root.gameObject.SetActive(false); edit.gameObject.SetActive(false);
        }

        static void ConfigureOptions(ScreenAppMenu menu)
        {
            var fields = new SerializedObject(menu);
            if (fields.FindProperty("screenOptions").objectReferenceValue != null) return;
            var launcher = (Canvas)fields.FindProperty("launcher").objectReferenceValue;
            var template = (Button)fields.FindProperty("optionTemplate").objectReferenceValue;
            var panel = Rect("Screen Options", launcher.transform, new Vector2(560, 364), new Vector2(0, 8));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0);
            panel.pivot = new Vector2(0.5f, 0);
            Paint(panel, new Color(0.025f, 0.045f, 0.065f, 0.97f));
            string[] names = { "Choose app", "Move", "Resize", "Rotate", "Delete screen", "Done" };
            string[] commands = { "app.choose", "move.enter", "edit.resize", "edit.rotate", "screen.delete", "menu.back" };
            for (int i = 0; i < names.Length; i++)
            {
                var button = Object.Instantiate(template, panel);
                button.name = names[i]; button.gameObject.SetActive(true);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                rect.sizeDelta = new Vector2(536, 48); rect.anchoredPosition = new Vector2(12, 304 - 58 * i);
                var text = button.GetComponentInChildren<Text>(true); text.text = names[i]; text.fontSize = 25;
                UnityEventTools.AddStringPersistentListener(button.onClick, menu.RunScreenOption, commands[i]);
            }
            fields.FindProperty("screenOptions").objectReferenceValue = panel;
            fields.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        static void Paint(RectTransform rect, Color color) => rect.gameObject.AddComponent<Image>().color = color;
        static Text Label(string name, Transform parent, Vector2 size, Vector2 position, string caption, int fontSize)
        {
            var text = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = caption;
            text.fontSize = fontSize; text.color = Color.white; text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            return text;
        }
    }
}
