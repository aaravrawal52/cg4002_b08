using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class HUDToggleAuthoring
    {
        public const string PrefabPath = "Assets/ARVisualizer/Prefabs/HUDToggleButton.prefab";
        [MenuItem("Tools/AR Visualizer/Open HUD toggle prefab")]
        public static void OpenPrefab() => AssetDatabase.OpenAsset(EnsurePrefab());

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var root = new GameObject("HUD Toggle Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HUDVisibilityToggle));
            root.layer = 5;
            try
            {
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(24, -22);
                rect.sizeDelta = new Vector2(64, 64);
                root.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.075f, 0.96f);
                var button = root.GetComponent<Button>();
                button.targetGraphic = root.GetComponent<Image>();
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var toggle = root.GetComponent<HUDVisibilityToggle>();
                UnityEventTools.AddPersistentListener(button.onClick, toggle.ToggleHUD);
                Label(root.transform, "Caption", "HUD", 12, new Vector2(0, 0.53f), Vector2.one);
                var state = Label(root.transform, "State", "-", 28, Vector2.zero, new Vector2(1, 0.65f));
                var settings = new SerializedObject(toggle);
                settings.FindProperty("stateLabel").objectReferenceValue = state;
                settings.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Text Label(Transform parent, string name, string text, int size, Vector2 min, Vector2 max)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.gameObject.layer = 5;
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = min;
            label.rectTransform.anchorMax = max;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text; label.fontSize = size; label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        public static void ConfigureHUD(VisualizerHUD hud)
        {
            var safe = hud.transform.Find("Safe Area");
            if (safe == null) return;
            if (hud.GetComponentInChildren<HUDVisibilityToggle>(true) == null)
            {
                PrefabUtility.InstantiatePrefab(EnsurePrefab(), safe);
                // Reserve the toggle's square beside the default header, without rebuilding the HUD.
                var top = safe.Find("Top Bar") as RectTransform;
                if (top != null && Mathf.Approximately(top.offsetMin.x, 24))
                    top.offsetMin = new Vector2(104, top.offsetMin.y);
            }
            var settings = new SerializedObject(hud);
            var content = settings.FindProperty("hideableContent");
            if (content.arraySize == 0)
            {
                var roots = new[] { "Safe Area/Top Bar", "Safe Area/Placement Panel", "Safe Area/Controls", "Aim Position" }
                    .Select(path => hud.transform.Find(path)).Where(t => t != null).ToArray();
                content.arraySize = roots.Length;
                for (int i = 0; i < roots.Length; ++i) content.GetArrayElementAtIndex(i).objectReferenceValue = roots[i].gameObject;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static void PrepareScene()
        {
            VisualizerHUDAuthoring.EnsurePrefab();
            var root = PrefabUtility.LoadPrefabContents(VisualizerHUDAuthoring.PrefabPath);
            try { ConfigureHUD(root.GetComponent<VisualizerHUD>()); PrefabUtility.SaveAsPrefabAsset(root, VisualizerHUDAuthoring.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var scene = EditorSceneManager.OpenScene(VisualizerSetup.ScenePath);
            ConfigureHUD(Object.FindAnyObjectByType<VisualizerHUD>());
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_HUD_TOGGLE_READY");
        }
    }
}
