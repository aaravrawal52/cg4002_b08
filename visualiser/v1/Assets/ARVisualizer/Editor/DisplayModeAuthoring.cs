using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class DisplayModeAuthoring
    {
        public const string PrefabPath = "Assets/ARVisualizer/Prefabs/DisplayModeButton.prefab";
        public const string OverlayPath = "Assets/ARVisualizer/Prefabs/PhoneControls.prefab";
        [MenuItem("Tools/AR Visualizer/Open display mode button prefab")]
        public static void Open() => AssetDatabase.OpenAsset(EnsurePrefab());
        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponentInChildren<ModeSwitchIcon>(true) != null) return existing;
            var root = existing != null ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("Display Mode Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(DisplayModeButton));
            root.layer = 5;
            try
            {
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
                rect.anchoredPosition = new Vector2(0, -14); rect.sizeDelta = new Vector2(64, 64);
                var background = root.GetComponent<Image>(); background.color = new Color(0.035f, 0.055f, 0.075f, 0.96f);
                var button = root.GetComponent<Button>(); button.targetGraphic = background;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = button.colors; colors.highlightedColor = new Color(0.4f, 1, 0.8f); button.colors = colors;
                var mode = root.GetComponent<DisplayModeButton>();
                if (button.onClick.GetPersistentEventCount() == 0) UnityEventTools.AddPersistentListener(button.onClick, mode.ToggleMode);
                var oldCaption = root.transform.Find("Caption");
                if (oldCaption != null) Object.DestroyImmediate(oldCaption.gameObject);
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(ModeSwitchIcon)).GetComponent<ModeSwitchIcon>();
                icon.gameObject.layer = 5; icon.transform.SetParent(root.transform, false);
                icon.rectTransform.anchorMin = Vector2.zero; icon.rectTransform.anchorMax = Vector2.one;
                icon.rectTransform.offsetMin = new Vector2(10, 10); icon.rectTransform.offsetMax = new Vector2(-10, -10);
                icon.color = Color.white; icon.raycastTarget = false;
                var settings = new SerializedObject(mode); settings.FindProperty("icon").objectReferenceValue = icon;
                settings.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { if (existing != null) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
        }
        static GameObject EnsureOverlay()
        {
            EnsurePrefab();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPath);
            if (existing != null) return existing;
            var root = new GameObject("Phone Controls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = 5;
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30010;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 1;
                var safe = new GameObject("Safe Area", typeof(RectTransform), typeof(SafeAreaFitter)).GetComponent<RectTransform>();
                safe.gameObject.layer = 5; safe.SetParent(root.transform, false);
                safe.anchorMin = Vector2.zero; safe.anchorMax = Vector2.one; safe.offsetMin = safe.offsetMax = Vector2.zero;
                PrefabUtility.InstantiatePrefab(EnsurePrefab(), safe);
                return PrefabUtility.SaveAsPrefabAsset(root, OverlayPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        public static void EnsureSceneOverlay(ARVisualizerApp app, VisualizerHUD hud)
        {
            var button = app.GetComponentInChildren<DisplayModeButton>(true);
            if (button == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(EnsureOverlay(), app.transform);
                button = instance.GetComponentInChildren<DisplayModeButton>(true);
            }
            var settings = new SerializedObject(button);
            settings.FindProperty("hud").objectReferenceValue = hud; settings.ApplyModifiedPropertiesWithoutUndo();
        }
        public static void ConfigureHUD(VisualizerHUD hud)
        {
            var safe = hud.transform.Find("Safe Area");
            if (safe == null) return;
            var oldButton = hud.GetComponentInChildren<DisplayModeButton>(true);
            if (oldButton != null) Object.DestroyImmediate(oldButton.gameObject);
            // Move only the original header to a second row. Preserve custom layouts.
            var top = safe.Find("Top Bar") as RectTransform;
            if (top != null && Mathf.Approximately(top.offsetMin.y, -94) && Mathf.Approximately(top.offsetMax.y, -22))
            {
                top.offsetMin = new Vector2(top.offsetMin.x, -164);
                top.offsetMax = new Vector2(top.offsetMax.x, -92);
            }
        }
        public static void PrepareScene()
        {
            EnsureOverlay();
            VisualizerHUDAuthoring.EnsurePrefab();
            var root = PrefabUtility.LoadPrefabContents(VisualizerHUDAuthoring.PrefabPath);
            try { ConfigureHUD(root.GetComponent<VisualizerHUD>()); PrefabUtility.SaveAsPrefabAsset(root, VisualizerHUDAuthoring.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var scene = EditorSceneManager.OpenScene(VisualizerSetup.ScenePath);
            VisualizerHUDAuthoring.EnsureSceneHUD(Object.FindAnyObjectByType<ARVisualizerApp>());
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_DISPLAY_MODE_READY");
        }
    }
}
