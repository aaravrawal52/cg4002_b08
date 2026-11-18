using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class DebugHUDAuthoring
    {
        [MenuItem("Tools/AR Visualizer/Prepare compact debug HUD")]
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
            Debug.Log("AR_VISUALIZER_COMPACT_DEBUG_HUD_READY");
        }

        // One-time migration; later runs leave the user's saved RectTransforms intact.
        public static void ConfigureHUD(VisualizerHUD hud)
        {
            if (hud.GetComponent<GestureDebugHUD>() != null) { UpgradeGestures(hud.GetComponent<GestureDebugHUD>()); return; }
            var safe = hud.transform.Find("Safe Area");
            if (safe == null) return;
            NormalControlsAuthoring.ConfigureHUD(hud);
            CompactControls(safe);
            var debug = hud.gameObject.AddComponent<GestureDebugHUD>();
            var fields = new SerializedObject(debug);
            void Bind(string field, Object value) => fields.FindProperty(field).objectReferenceValue = value;
            var root = Rect("Gesture Controls", safe, new Vector2(136, 44), new Vector2(-16, 16));
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(1, 0);
            Bind("root", root.gameObject);
            var hudFields = new SerializedObject(hud);
            var hidden = hudFields.FindProperty("hideableContent");
            hidden.GetArrayElementAtIndex(hidden.arraySize++).objectReferenceValue = root.gameObject;
            hudFields.ApplyModifiedPropertiesWithoutUndo();
            Bind("toggle", Button("Gestures Button", root, new Vector2(136, 44), Vector2.zero, "GESTURES", debug.TogglePanel));
            var panel = Rect("Gesture Panel", root, new Vector2(424, 344), new Vector2(-436, 114));
            panel.pivot = new Vector2(1, 0);
            panel.anchoredPosition = new Vector2(136 - 436, 114);
            panel.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.075f, 0.97f);
            Bind("panel", panel.gameObject);
            Label("Heading", panel, new Vector2(400, 24), new Vector2(12, 310), "TEST GESTURES", 15);
            Bind("leftHand", Button("Left Hand", panel, new Vector2(196, 36), new Vector2(12, 264), "LEFT HAND", debug.SelectLeftHand));
            Bind("rightHand", Button("Right Hand", panel, new Vector2(196, 36), new Vector2(216, 264), "RIGHT HAND", debug.SelectRightHand));
            string[] gestures = { "SWIPE_UP", "SWIPE_DOWN", "SWIPE_LEFT", "SWIPE_RIGHT", "ZOOM_IN", "ZOOM_OUT", "WRIGGLE_FINGERS", "CRUMPLE_AND_TOSS" };
            for (int i = 0; i < gestures.Length; i++)
            {
                string caption = gestures[i] == "CRUMPLE_AND_TOSS" ? "CRUMPLE AND TOSS" : gestures[i].Replace('_', ' ');
                var button = Button(gestures[i], panel, new Vector2(196, 40), new Vector2(12 + (i % 2) * 204, 212 - (i / 2) * 48), caption, null);
                UnityEventTools.AddStringPersistentListener(button.onClick, debug.TriggerGesture, gestures[i]);
            }
            var result = Label("Result", panel, new Vector2(400, 44), new Vector2(12, 12), "Choose a hand, aim its ray, then tap a gesture.", 12);
            result.supportRichText = false;
            Bind("result", result);
            fields.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
            UpgradeGestures(debug);
        }

        static void UpgradeGestures(GestureDebugHUD debug)
        {
            var fields = new SerializedObject(debug);
            if (fields.FindProperty("gestureLayoutVersion").intValue >= 3) return;
            var panel = (RectTransform)((GameObject)fields.FindProperty("panel").objectReferenceValue).transform;
            panel.sizeDelta = new Vector2(424, 416);
            foreach (var button in panel.GetComponentsInChildren<Button>(true))
                if (button.onClick.GetPersistentEventCount() > 0 && button.onClick.GetPersistentMethodName(0) == "TriggerGesture")
                    Object.DestroyImmediate(button.gameObject);
            ((RectTransform)panel.Find("Heading")).anchoredPosition = new Vector2(12, 382);
            ((RectTransform)panel.Find("Left Hand")).anchoredPosition = new Vector2(12, 336);
            ((RectTransform)panel.Find("Right Hand")).anchoredPosition = new Vector2(216, 336);
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            for (int i = 0; i < catalogue.gestures.Length; i++)
            {
                string gesture = catalogue.gestures[i].gesture;
                var button = Button(gesture, panel, new Vector2(196, 40), new Vector2(12 + (i % 2) * 204, 284 - (i / 2) * 44), gesture.Replace('_', ' '), null);
                UnityEventTools.AddStringPersistentListener(button.onClick, debug.TriggerGesture, gesture);
            }
            fields.FindProperty("gestureLayoutVersion").intValue = 3;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CompactControls(Transform safe)
        {
            var row = (RectTransform)safe.Find("Normal Actions");
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(1, 0);
            row.sizeDelta = new Vector2(424, 44); row.anchoredPosition = new Vector2(-16, 78);
            ResizeButton(row.Find("Left Click Button"), new Vector2(136, 44), Vector2.zero);
            ResizeButton(row.Find("Right Click Button"), new Vector2(136, 44), new Vector2(144, 0));
            ResizeButton(row.Find("Screen Settings Button"), new Vector2(136, 44), new Vector2(288, 0));
            var controls = (RectTransform)safe.Find("Controls");
            controls.anchorMin = controls.anchorMax = controls.pivot = new Vector2(1, 0);
            controls.sizeDelta = new Vector2(424, 60); controls.anchoredPosition = new Vector2(-16, 16);
            ResizeButton(controls.Find("Pointer Button"), new Vector2(136, 44), Vector2.zero);
            ResizeButton(controls.Find("Mode Button"), new Vector2(136, 44), new Vector2(144, 0));
            // Goggle mode has its own visible button row, including these normally hidden controls.
            ResizeButton(controls.Find("Place Screen Button"), new Vector2(136, 44), new Vector2(288, 0));
            ResizeButton(controls.Find("Undo Button"), new Vector2(136, 44), new Vector2(288, 0));
            controls.Find("Controls Heading").gameObject.SetActive(false);
            var size = (RectTransform)controls.Find("Screen Size");
            SetRect(size, new Vector2(424, 14), new Vector2(0, 46));
            size.GetComponent<Text>().fontSize = 10;

            var settings = (RectTransform)row.Find("Screen Settings Panel");
            SetRect(settings, new Vector2(424, 200), new Vector2(0, 52));
            var title = (RectTransform)settings.Find("Selected Screen");
            SetRect(title, new Vector2(400, 28), new Vector2(12, 164)); title.GetComponent<Text>().fontSize = 16;
            string[] buttons = { "Choose App Button", "Smaller Button", "Larger Button", "Rotate Left Button", "Rotate Right Button", "Move Screen Button" };
            for (int i = 0; i < buttons.Length; i++)
                ResizeButton(settings.Find(buttons[i]), new Vector2(128, 40), new Vector2(12 + (i % 3) * 136, 108 - (i / 3) * 48));
            ResizeButton(settings.Find("Delete Screen Button"), new Vector2(264, 40), new Vector2(12, 12));
            ResizeButton(settings.Find("Done Button"), new Vector2(128, 40), new Vector2(284, 12));

            var info = (RectTransform)safe.Find("Placement Panel");
            SetRect(info, new Vector2(376, 120), new Vector2(16, 16));
            string[] labels = { "Mode Heading", "Target Status", "Surface Details", "Feedback" };
            float[] tops = { 94, 64, 40, 6 }; int[] heights = { 20, 28, 20, 32 }; int[] fonts = { 10, 20, 12, 11 };
            for (int i = 0; i < labels.Length; i++)
            {
                var label = info.Find(labels[i]).GetComponent<Text>();
                SetRect(label.rectTransform, new Vector2(348, heights[i]), new Vector2(14, tops[i]));
                label.fontSize = fonts[i];
            }
        }

        static void SetRect(RectTransform rect, Vector2 size, Vector2 position)
        { rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero; rect.sizeDelta = size; rect.anchoredPosition = position; }
        static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false); SetRect(rect, size, position); return rect;
        }
        static Text Label(string name, Transform parent, Vector2 size, Vector2 position, string caption, int fontSize)
        {
            var text = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = caption;
            text.fontSize = fontSize; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false; return text;
        }
        static Button Button(string name, Transform parent, Vector2 size, Vector2 position, string caption, UnityAction action)
        {
            var rect = Rect(name, parent, size, position);
            rect.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.16f, 0.19f, 0.97f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (action != null) UnityEventTools.AddPersistentListener(button.onClick, action);
            Label("Caption", rect, size, Vector2.zero, caption, 14);
            ResizeButton(rect, size, position); return button;
        }
        static void ResizeButton(Transform transform, Vector2 size, Vector2 position)
        {
            var rect = (RectTransform)transform; SetRect(rect, size, position);
            var text = transform.GetComponentInChildren<Text>(true); text.fontSize = 13;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4, 2); text.rectTransform.offsetMax = new Vector2(-4, -2);
        }
    }
}
