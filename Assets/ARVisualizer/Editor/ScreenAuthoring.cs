using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARVisualizer.Editor
{
    public static class ScreenAuthoring
    {
        public const string PrefabPath = "Assets/ARVisualizer/Prefabs/Screen.prefab";
        [MenuItem("Tools/AR Visualizer/Open screen prefab")]
        public static void OpenPrefab() => AssetDatabase.OpenAsset(EnsurePrefab());

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            Directory.CreateDirectory("Assets/ARVisualizer/Materials");
            Directory.CreateDirectory("Assets/ARVisualizer/Prefabs");
            AssetDatabase.Refresh();
            const string materialPath = "Assets/ARVisualizer/Materials/ScreenBlack.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("ARVisualizer/Screen"));
                material.SetColor("_BaseColor", Color.black);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var root = new GameObject("Screen", typeof(BoxCollider), typeof(ScreenSurface));
            try
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = Vector3.back * (ScreenSurface.Thickness / 2);
                body.transform.localScale = new Vector3(0.16f, 0.09f, ScreenSurface.Thickness);
                body.GetComponent<Renderer>().sharedMaterial = material;
                var collider = root.GetComponent<BoxCollider>();
                collider.center = body.transform.localPosition;
                collider.size = body.transform.localScale;
                var settings = new SerializedObject(root.GetComponent<ScreenSurface>());
                settings.FindProperty("body").objectReferenceValue = body.transform;
                settings.FindProperty("bounds").objectReferenceValue = collider;
                settings.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static void ConfigureApp(ARVisualizerApp app)
        {
            var settings = new SerializedObject(app);
            if (settings.FindProperty("screenPrefab").objectReferenceValue == null)
            {
                settings.FindProperty("screenPrefab").objectReferenceValue = EnsurePrefab().GetComponent<ScreenSurface>();
                settings.ApplyModifiedProperties();
            }
        }

        public static void PrepareScreenScene()
        {
            var scene = EditorSceneManager.OpenScene(VisualizerSetup.ScenePath);
            ConfigureApp(Object.FindAnyObjectByType<ARVisualizerApp>());
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_SCREEN_PREFAB_READY");
        }
    }

    [CustomEditor(typeof(ScreenSurface))]
    public sealed class ScreenSurfaceInspector : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var screen = (ScreenSurface)target;
            EditorGUILayout.HelpBox("Size Steps keeps the 16:9 ratio and 0.5 cm thickness. Body uses a unit mesh. The front face points along local -Z. UV (0,0) is the bottom-left as seen from the front.", MessageType.Info);
            DrawDefaultInspector();
            EditorGUILayout.LabelField("Size", $"{screen.Width * 100:0.#} x {screen.Height * 100:0.#} x 0.5 cm");
            if (!Application.isPlaying) return;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Screen ID", screen.Id);
                EditorGUILayout.Toggle("Can Rotate", screen.CanRotate);
                EditorGUILayout.Toggle("Adjusting", screen.IsAdjusting);
                EditorGUILayout.EnumPopup("Input", screen.InputKind);
                EditorGUILayout.Vector2Field("Point (UV)", screen.InputUV);
                EditorGUILayout.Vector2Field("Point (metres)", screen.InputMetres);
            }
        }
    }
}
