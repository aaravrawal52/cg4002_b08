using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class GestureHistoryAuthoring
    {
        [MenuItem("Tools/AR Visualizer/Add gesture history to HUD prefab")]
        public static void PreparePrefab()
        {
            VisualizerHUDAuthoring.EnsurePrefab();
            var root = PrefabUtility.LoadPrefabContents(VisualizerHUDAuthoring.PrefabPath);
            try
            {
                ConfigureHUD(root.GetComponent<VisualizerHUD>());
                PrefabUtility.SaveAsPrefabAsset(root, VisualizerHUDAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_GESTURE_HISTORY_READY");
        }

        public static void ConfigureHUD(VisualizerHUD hud)
        {
            var safe = hud.transform.Find("Safe Area");
            if (safe == null || hud.GetComponent<GestureHistoryHUD>() != null) return;
            var history = hud.gameObject.AddComponent<GestureHistoryHUD>();
            var panel = Rect("Gesture History", safe, new Vector2(24, -180), new Vector2(342, 246));
            var background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.035f, 0.055f, 0.075f, 0.91f);
            background.raycastTarget = false;
            Label("Heading", panel, "WI-FI GESTURES", 13, new Color(0.4f, 1, 0.8f), -12);
            var empty = Label("Empty State", panel, "Waiting for gestures...", 16, new Color(0.61f, 0.7f, 0.77f), -46);
            empty.gameObject.SetActive(false);
            var settings = new SerializedObject(history);
            settings.FindProperty("panel").objectReferenceValue = panel.gameObject;
            settings.FindProperty("emptyLabel").objectReferenceValue = empty;
            var rows = settings.FindProperty("rows");
            rows.arraySize = 6;
            string[] preview = { "SWIPE UP", "ZOOM IN", "SWIPE LEFT", "WRIGGLE FINGERS", "SWIPE DOWN", "CRUMPLE AND TOSS" };
            for (int i = 0; i < rows.arraySize; ++i)
                rows.GetArrayElementAtIndex(i).objectReferenceValue = Label("Gesture " + (i + 1), panel,
                    "--:--:--   " + (i % 2 == 0 ? "R" : "L") + "   " + preview[i], 15, Color.white, -46 - i * 31);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Text Label(string name, RectTransform parent, string text, int size, Color color, float top)
        {
            var rect = Rect(name, parent, new Vector2(14, top), new Vector2(314, 27));
            // Stretch with the panel width so resizing it in the Editor retains the margins.
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = new Vector2(-28, 27);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.color = color;
            label.text = text;
            label.alignment = TextAnchor.MiddleLeft;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }
    }
}
