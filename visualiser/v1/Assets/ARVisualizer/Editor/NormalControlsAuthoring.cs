using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class NormalControlsAuthoring
    {
        [MenuItem("Tools/AR Visualizer/Prepare normal controls and flat screens")]
        public static void PrepareScene()
        {
            VisualizerHUDAuthoring.EnsurePrefab();
            var root = PrefabUtility.LoadPrefabContents(VisualizerHUDAuthoring.PrefabPath);
            try { ConfigureHUD(root.GetComponent<VisualizerHUD>()); PrefabUtility.SaveAsPrefabAsset(root, VisualizerHUDAuthoring.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            ScreenAuthoring.EnsurePrefab();
            var scene = EditorSceneManager.OpenScene(VisualizerSetup.ScenePath);
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            VisualizerHUDAuthoring.EnsureSceneHUD(app);
            ScreenAuthoring.ConfigureApp(app);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_NORMAL_CONTROLS_READY");
        }

        public static void ConfigureHUD(VisualizerHUD hud)
        {
            var safe = hud.transform.Find("Safe Area");
            if (safe == null) return;
            if (safe.Find("Normal Actions") != null) { UpgradePlacementAndSettings(hud); ConfigureHoldButton(hud); return; }
            var controls = hud.GetComponent<NormalScreenControls>() ?? hud.gameObject.AddComponent<NormalScreenControls>();
            var fields = new SerializedObject(controls);
            void Bind(string name, Object value) => fields.FindProperty(name).objectReferenceValue = value;
            var row = Rect("Normal Actions", safe, new Vector2(584, 64), new Vector2(-24, 162));
            row.anchorMin = row.anchorMax = new Vector2(1, 0); row.pivot = new Vector2(1, 0);
            Bind("root", row.gameObject);
            Bind("leftClick", Button("Left Click Button", row, new Vector2(0, 0), "LEFT CLICK", hud.LeftClick));
            Bind("rightClick", Button("Right Click Button", row, new Vector2(200, 0), "RIGHT CLICK", hud.RightClick));
            Bind("settings", Button("Screen Settings Button", row, new Vector2(400, 0), "SCREEN SETTINGS", controls.ToggleSettings));
            var panel = Rect("Screen Settings Panel", row, new Vector2(584, 222), new Vector2(0, 76));
            panel.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.075f, 0.97f);
            Bind("panel", panel.gameObject);
            Bind("selectionLabel", Label("Selected Screen", panel, new Vector2(16, 176), new Vector2(552, 34), "SCREEN SETTINGS", 22));
            Button("Choose App Button", panel, new Vector2(0, 96), "CHOOSE APP", controls.ChooseApp);
            Bind("shrink", Button("Smaller Button", panel, new Vector2(200, 96), "SMALLER", controls.Shrink));
            Bind("grow", Button("Larger Button", panel, new Vector2(400, 96), "LARGER", controls.Grow));
            Bind("counterclockwise", Button("Rotate Left Button", panel, new Vector2(0, 20), "ROTATE LEFT", controls.RotateCounterclockwise));
            Bind("clockwise", Button("Rotate Right Button", panel, new Vector2(200, 20), "ROTATE RIGHT", controls.RotateClockwise));
            Button("Done Button", panel, new Vector2(400, 20), "DONE", controls.Close);
            fields.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
            UpgradePlacementAndSettings(hud);
            ConfigureHoldButton(hud);
        }

        static void ConfigureHoldButton(VisualizerHUD hud)
        {
            var button = hud.transform.Find("Safe Area/Normal Actions/Left Click Button").GetComponent<UnityEngine.UI.Button>();
            if (button.GetComponent<PointerHoldButton>() != null) return;
            var hold = button.gameObject.AddComponent<PointerHoldButton>();
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; --i)
                if (button.onClick.GetPersistentTarget(i) == hud && button.onClick.GetPersistentMethodName(i) == "LeftClick")
                    UnityEventTools.RemovePersistentListener(button.onClick, i);
            UnityEventTools.AddPersistentListener(button.onClick, hold.Click);
        }

        static void UpgradePlacementAndSettings(VisualizerHUD hud)
        {
            var panel = (RectTransform)hud.transform.Find("Safe Area/Normal Actions/Screen Settings Panel");
            // Upgrade once; later preparations preserve the user's authored geometry and events.
            if (panel == null || panel.Find("Delete Screen Button") != null) return;
            var controls = hud.GetComponent<NormalScreenControls>();
            panel.sizeDelta += new Vector2(0, 76);
            foreach (RectTransform child in panel)
                if (child.name != "Done Button") child.anchoredPosition += new Vector2(0, 76);
            var delete = Button("Delete Screen Button", panel, new Vector2(0, 20), "DELETE SCREEN", controls.DeleteScreen);
            delete.targetGraphic.color = new Color(0.52f, 0.12f, 0.13f, 0.97f);
            var row = hud.transform.Find("Safe Area/Controls");
            var pointer = (RectTransform)row.Find("Pointer Button");
            var mode = row.Find("Mode Button").GetComponent<UnityEngine.UI.Button>();
            var modeRect = (RectTransform)mode.transform;
            pointer.anchorMin = pointer.anchorMax = modeRect.anchorMin = modeRect.anchorMax = Vector2.zero;
            pointer.pivot = modeRect.pivot = Vector2.zero;
            pointer.anchoredPosition = new Vector2(200, 20); pointer.sizeDelta = new Vector2(184, 72);
            modeRect.anchoredPosition = new Vector2(400, 20); modeRect.sizeDelta = new Vector2(184, 72);
            for (int i = mode.onClick.GetPersistentEventCount() - 1; i >= 0; --i)
                if (mode.onClick.GetPersistentTarget(i) == hud && mode.onClick.GetPersistentMethodName(i) == "TogglePlaceMode")
                    UnityEventTools.RemovePersistentListener(mode.onClick, i);
            bool bound = false;
            for (int i = 0; i < mode.onClick.GetPersistentEventCount(); ++i)
                bound |= mode.onClick.GetPersistentTarget(i) == hud && mode.onClick.GetPersistentMethodName(i) == "PlacementAction";
            if (!bound) UnityEventTools.AddPersistentListener(mode.onClick, hud.PlacementAction);
            mode.GetComponentInChildren<Text>().text = "PLACE SCREEN";
            row.Find("Place Screen Button").gameObject.SetActive(false);
            row.Find("Undo Button").gameObject.SetActive(false);
        }
        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        static Text Label(string name, Transform parent, Vector2 position, Vector2 size, string caption, int fontSize)
        {
            var label = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = caption; label.fontSize = fontSize; label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white; label.raycastTarget = false; return label;
        }
        static Button Button(string name, Transform parent, Vector2 position, string caption, UnityAction action)
        {
            var rect = Rect(name, parent, new Vector2(184, 64), position);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.08f, 0.16f, 0.19f, 0.97f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors; colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f); button.colors = colors;
            UnityEventTools.AddPersistentListener(button.onClick, action);
            Label("Caption", rect, new Vector2(6, 4), new Vector2(172, 56), caption, 16);
            return button;
        }
    }
}
