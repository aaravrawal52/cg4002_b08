using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    /// <summary>Creates the default HUD once. Existing prefabs and scene layouts are never rebuilt.</summary>
    public static class VisualizerHUDAuthoring
    {
        public const string PrefabPath = "Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab";

        [MenuItem("Tools/AR Visualizer/Open HUD prefab")]
        public static void OpenPrefab() => AssetDatabase.OpenAsset(EnsurePrefab());

        [MenuItem("Tools/AR Visualizer/Add editable HUD to current scene")]
        public static void AddToCurrentScene()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play mode before editing the saved HUD."); return; }
            var scene = SceneManager.GetActiveScene();
            var app = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ARVisualizerApp>(true)).FirstOrDefault();
            if (app == null) { Debug.LogWarning("Open the ARVisualizer scene first."); return; }
            Selection.activeGameObject = EnsureSceneHUD(app).gameObject;
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        // Batch migration entry point, also safe to call more than once.
        public static void PrepareEditableScene()
        {
            var scene = EditorSceneManager.OpenScene(VisualizerSetup.ScenePath);
            var app = scene.GetRootGameObjects().Select(g => g.GetComponent<ARVisualizerApp>()).First(a => a != null);
            EnsureSceneHUD(app);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_EDITABLE_HUD_READY");
        }

        public static VisualizerHUD EnsureSceneHUD(ARVisualizerApp app)
        {
            if (app.GetComponent<HandPointerSource>() == null) Undo.AddComponent<HandPointerSource>(app.gameObject);
            if (app.GetComponent<CommunicationManager>() == null) Undo.AddComponent<CommunicationManager>(app.gameObject);
            if (app.GetComponent<PhonePointerClicks>() == null) Undo.AddComponent<PhonePointerClicks>(app.gameObject);
            var settings = new SerializedObject(app);
            var hud = settings.FindProperty("hud").objectReferenceValue as VisualizerHUD;
            if (hud == null) hud = app.GetComponentInChildren<VisualizerHUD>(true);
            if (hud == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(EnsurePrefab(), app.transform);
                Undo.RegisterCreatedObjectUndo(instance, "Add editable AR HUD");
                hud = instance.GetComponent<VisualizerHUD>();
            }
            settings.FindProperty("hud").objectReferenceValue = hud;
            settings.ApplyModifiedProperties();
            HUDToggleAuthoring.ConfigureHUD(hud);
            DisplayModeAuthoring.ConfigureHUD(hud);
            DisplayModeAuthoring.EnsureSceneOverlay(app, hud);
            NormalControlsAuthoring.ConfigureHUD(hud);
            if (hud.GetComponent<GoggleHUD>() == null) Undo.AddComponent<GoggleHUD>(hud.gameObject);
            if (hud.GetComponent<StereoGoggles>() == null) Undo.AddComponent<StereoGoggles>(hud.gameObject);

            // EventSystem belongs to the scene so additional UI prefabs do not create duplicates.
            if (!app.gameObject.scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<EventSystem>(true) != null))
            {
                var events = new GameObject("HUD Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(app.transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                Undo.RegisterCreatedObjectUndo(events, "Add HUD event system");
            }
            return hud;
        }

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            var root = new Builder().Build();
            try { return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { Object.DestroyImmediate(root); }
        }

        sealed class Builder
        {
            readonly Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            readonly Color mint = new Color(0.4f, 1, 0.8f);
            readonly Color muted = new Color(0.61f, 0.7f, 0.77f);
            readonly Color panel = new Color(0.035f, 0.055f, 0.075f, 0.91f);
            SerializedObject bindings;

            public GameObject Build()
            {
                var root = new GameObject("Landscape HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                    typeof(GraphicRaycaster), typeof(VisualizerHUD));
                root.layer = 5;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.matchWidthOrHeight = 1;
                var hud = root.GetComponent<VisualizerHUD>();
                bindings = new SerializedObject(hud);
                var safe = Rect("Safe Area", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                safe.gameObject.AddComponent<SafeAreaFitter>();

                var top = Box("Top Bar", safe, new Vector2(0, 1), Vector2.one, new Vector2(24, -94), new Vector2(-24, -22), panel);
                Label("Brand", top, "FIELD  /  AR", 25, Color.white, new Vector2(22, 22), new Vector2(270, 58));
                Label("Subtitle", top, "SURFACE VISUALISER", 11, muted, new Vector2(23, 6), new Vector2(290, 27));
                var status = Rect("Status", top, new Vector2(1, 0), Vector2.one, new Vector2(-740, 0), new Vector2(-18, 0));
                Bind("tracking", Label("AR Status", status, "AR tracking", 14, mint, new Vector2(0, 33), new Vector2(260, 61)));
                Bind("hand", Label("Hand Status", status, "Show your index knuckle", 14, Color.white, new Vector2(275, 33), new Vector2(505, 61)));
                Bind("network", Label("Wi-Fi Status", status, "WI-FI  Listening  /  192.168.1.42:7777", 13, muted, new Vector2(0, 8), new Vector2(715, 32)));
                var counter = Label("Screen Count", status, "00 SCREENS", 15, Color.white, new Vector2(525, 33), new Vector2(715, 61));
                counter.alignment = TextAnchor.MiddleRight;
                Bind("counter", counter);

                var info = Box("Placement Panel", safe, Vector2.zero, Vector2.zero, new Vector2(24, 24), new Vector2(500, 150), panel);
                Bind("section", Label("Mode Heading", info, "VIEW MODE", 11, muted, new Vector2(20, 92), new Vector2(446, 115)));
                Bind("target", Label("Target Status", info, "Enter place mode", 23, Color.white, new Vector2(20, 52), new Vector2(446, 90)));
                Bind("detail", Label("Surface Details", info, "0 surfaces  /  highlights hidden", 13, mint, new Vector2(20, 29), new Vector2(446, 53)));
                Bind("feedback", Label("Feedback", info, "Enter place mode to highlight surfaces and add screens", 12, muted, new Vector2(20, 6), new Vector2(450, 30)));

                var controls = Rect("Controls", safe, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-608, 24), new Vector2(-24, 150));
                Label("Controls Heading", controls, "LOCAL COMMANDS", 11, muted, new Vector2(0, 98), new Vector2(584, 126));
                var pointer = Button("Pointer Button", controls, new Vector2(0, 20), new Vector2(136, 92), "POINTER  ON", panel, Color.white, hud.TogglePointer);
                Bind("pointerLabel", pointer.GetComponentInChildren<Text>());
                var mode = Button("Mode Button", controls, new Vector2(148, 20), new Vector2(284, 92), "PLACE SCREEN", panel, Color.white, hud.PlacementAction);
                Bind("modeLabel", mode.GetComponentInChildren<Text>());
                var place = Button("Place Screen Button", controls, new Vector2(296, 20), new Vector2(460, 92), "PLACE SCREEN", mint, panel, hud.PlaceScreen);
                place.interactable = false;
                Bind("place", place);
                var undo = Button("Undo Button", controls, new Vector2(472, 20), new Vector2(584, 92), "UNDO", panel, Color.white, hud.UndoScreen);
                undo.interactable = false;
                Bind("undo", undo);
                Bind("sizeLabel", Label("Screen Size", controls, "16 x 9 cm  /  FLAT 16:9", 12, muted, Vector2.zero, new Vector2(584, 22)));

                var aim = Rect("Aim Position", root.transform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
                var cursor = Rect("Aim Marker", aim, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-12, -12), new Vector2(12, 12));
                Bind("aimPosition", aim);
                Bind("cursorVisual", cursor.gameObject);
                Bind("cursorIndicator", Box("Dot", cursor, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-3, -3), new Vector2(3, 3), mint).GetComponent<Image>());
                Box("Left", cursor, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-3, -1), new Vector2(3, 1), Color.white);
                Box("Right", cursor, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-3, -1), new Vector2(3, 1), Color.white);
                Box("Top", cursor, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-1, -3), new Vector2(1, 3), Color.white);
                Box("Bottom", cursor, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-1, -3), new Vector2(1, 3), Color.white);
                bindings.ApplyModifiedPropertiesWithoutUndo();
                root.AddComponent<GoggleHUD>();
                root.AddComponent<StereoGoggles>();
                HUDToggleAuthoring.ConfigureHUD(hud);
                DisplayModeAuthoring.ConfigureHUD(hud);
                NormalControlsAuthoring.ConfigureHUD(hud);
                return root;
            }

            void Bind(string field, Object value) => bindings.FindProperty(field).objectReferenceValue = value;

            static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
            {
                var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.gameObject.layer = 5;
                rect.SetParent(parent, false);
                rect.anchorMin = min; rect.anchorMax = max;
                rect.offsetMin = lower; rect.offsetMax = upper;
                return rect;
            }

            static RectTransform Box(string name, Transform parent, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper, Color color)
            {
                var rect = Rect(name, parent, min, max, lower, upper);
                var image = rect.gameObject.AddComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
                return rect;
            }

            Text Label(string name, Transform parent, string text, int size, Color color, Vector2 lower, Vector2 upper)
            {
                var rect = Rect(name, parent, Vector2.zero, Vector2.zero, lower, upper);
                var label = rect.gameObject.AddComponent<Text>();
                label.font = font; label.text = text; label.fontSize = size; label.color = color;
                label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.raycastTarget = false;
                return label;
            }

            Button Button(string name, Transform parent, Vector2 lower, Vector2 upper, string text, Color background, Color foreground, UnityEngine.Events.UnityAction click)
            {
                var rect = Box(name, parent, Vector2.zero, Vector2.zero, lower, upper, background);
                rect.GetComponent<Image>().raycastTarget = true;
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = rect.GetComponent<Image>();
                var colors = button.colors;
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
                button.colors = colors;
                UnityEventTools.AddPersistentListener(button.onClick, click);
                var label = Label("Caption", rect, text, 16, foreground, Vector2.zero, Vector2.zero);
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(6, 4);
                label.rectTransform.offsetMax = new Vector2(-6, -4);
                label.alignment = TextAnchor.MiddleCenter;
                return button;
            }
        }
    }

    [CustomEditor(typeof(VisualizerHUD))]
    public sealed class VisualizerHUDInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Expand this HUD in the Hierarchy. Select an Image, Text, or Button and use the Rect tool (T) to move or resize it. Shapes, sprites, typography and On Click events are saved in the prefab.", MessageType.Info);
            var hud = (VisualizerHUD)target;
            if (!hud.HasRequiredReferences)
                EditorGUILayout.HelpBox("One or more live UI references are missing. Reassign them below; optional decorations can be added or removed freely.", MessageType.Warning);
            if (!Application.isPlaying)
            {
                EditorGUILayout.LabelField("Preview content", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                PreviewButton(hud, "View", VisualizerHUD.PreviewState.View);
                PreviewButton(hud, "Scan", VisualizerHUD.PreviewState.Scanning);
                PreviewButton(hud, "No hand", VisualizerHUD.PreviewState.HandMissing);
                PreviewButton(hud, "Ready", VisualizerHUD.PreviewState.ReadyToPlace);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("Preview updates live labels, state colors, buttons and marker visibility. Ctrl+Z restores the previous content.", MessageType.None);
            }
            DrawDefaultInspector();
        }

        static void PreviewButton(VisualizerHUD hud, string caption, VisualizerHUD.PreviewState state)
        {
            if (!GUILayout.Button(caption)) return;
            var objects = hud.GetComponentsInChildren<Component>(true).Cast<Object>()
                .Concat(hud.GetComponentsInChildren<Transform>(true).Select(t => (Object)t.gameObject)).ToArray();
            Undo.RecordObjects(objects, "Preview AR HUD");
            hud.Preview(state);
            foreach (var obj in objects)
            {
                EditorUtility.SetDirty(obj);
                if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
            SceneView.RepaintAll();
        }
    }
}
