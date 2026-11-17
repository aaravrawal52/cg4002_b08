using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Editor
{
    public static class BrowserAuthoring
    {
        public const string ViewPath = "Assets/ARVisualizer/Resources/ScreenBrowserView.prefab";
        [MenuItem("Tools/AR Visualizer/Prepare browser app")]
        public static void PrepareAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<ScreenBrowserView>(ViewPath) == null) BuildView();
            BindDocumentControls();
            var screen = PrefabUtility.LoadPrefabContents(ScreenAuthoring.PrefabPath);
            try
            {
                var browser = screen.GetComponent<ScreenBrowser>();
                if (browser == null) browser = screen.AddComponent<ScreenBrowser>();
                var fields = new SerializedObject(browser);
                if (fields.FindProperty("viewPrefab").objectReferenceValue == null)
                    fields.FindProperty("viewPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScreenBrowserView>(ViewPath);
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(screen, ScreenAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(screen); }
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_BROWSER_READY");
        }

        // Add references to existing editable controls without rebuilding the user's layout.
        static void BindDocumentControls()
        {
            var root = PrefabUtility.LoadPrefabContents(ViewPath);
            try
            {
                var view = root.GetComponent<ScreenBrowserView>();
                if (view.homeButton == null) view.homeButton = root.transform.Find("Toolbar/Home")?.GetComponent<Button>();
                if (view.addressButton == null) view.addressButton = view.address.GetComponentInParent<Button>();
                PrefabUtility.SaveAsPrefabAsset(root, ViewPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void BuildView()
        {
            var root = new GameObject("Browser View", typeof(RectTransform), typeof(Canvas), typeof(ScreenBrowserView));
            try
            {
                root.layer = 5;
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(1280, 800);
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                var view = root.GetComponent<ScreenBrowserView>();
                var page = Rect("Web page", rect, 0, 0, 1280, 704);
                view.page = page.gameObject.AddComponent<RawImage>(); view.page.color = Color.white;
                var toolbar = Rect("Toolbar", rect, 0, 704, 1280, 96);
                Paint(toolbar, new Color(0.035f, 0.055f, 0.075f));
                Button(view, toolbar, "Back", "back", 8, 32, 72, 56);
                Button(view, toolbar, "Next", "forward", 88, 32, 72, 56);
                Button(view, toolbar, "Reload", "reload", 168, 32, 100, 56);
                Button(view, toolbar, "Home", "home", 276, 32, 88, 56);
                var address = Button(view, toolbar, "Address / search", "address", 372, 32, 756, 56);
                view.address = address.GetComponentInChildren<Text>(); view.address.alignment = TextAnchor.MiddleLeft;
                Button(view, toolbar, "Keys", "keyboard", 1136, 32, 136, 56);
                view.status = Label("Status", toolbar, "Swipe up/down to scroll · left/right for history", 12, 2, 1256, 26, 18);

                var keyboard = Rect("Keyboard", rect, 0, 0, 1280, 360);
                Paint(keyboard, new Color(0.035f, 0.055f, 0.075f));
                view.keyboard = keyboard.gameObject;
                view.keyboardHeading = Label("Keyboard heading", keyboard, "ADDRESS / SEARCH", 16, 322, 1248, 32, 24);
                string[] rows = { "1234567890-+", "qwertyuiop@/", "asdfghjkl.:?", "zxcvbnm_,&=!" };
                for (int row = 0; row < rows.Length; row++)
                    for (int col = 0; col < rows[row].Length; col++)
                    {
                        string key = rows[row][col].ToString();
                        Button(view, keyboard, key, "key:" + key, 12 + col * 105, 256 - row * 62, 97, 54);
                    }
                Button(view, keyboard, "Shift", "shift", 12, 8, 144, 54);
                Button(view, keyboard, "Space", "key: ", 164, 8, 344, 54);
                Button(view, keyboard, "Backspace", "key:Backspace", 516, 8, 240, 54);
                Button(view, keyboard, "Enter / Go", "key:Enter", 764, 8, 240, 54);
                Button(view, keyboard, "Hide keys", "done", 1012, 8, 256, 54);
                keyboard.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, ViewPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }
        static Image Paint(RectTransform rect, Color color) { var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image; }
        static Text Label(string name, Transform parent, string text, float x, float y, float w, float h, int size)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.text = text;
            label.fontSize = size; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow; label.raycastTarget = false;
            return label;
        }
        static Button Button(ScreenBrowserView view, Transform parent, string title, string action, float x, float y, float w, float h)
        {
            var rect = Rect(title, parent, x, y, w, h);
            var graphic = Paint(rect, new Color(0.08f, 0.16f, 0.19f));
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
            var label = Label("Label", rect, title, 8, 2, w - 16, h - 4, 22);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            UnityEventTools.AddStringPersistentListener(button.onClick, view.InvokeAction, action);
            return button;
        }
    }
}
