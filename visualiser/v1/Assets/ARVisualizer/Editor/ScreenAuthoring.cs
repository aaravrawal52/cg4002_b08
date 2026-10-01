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
            if (existing != null)
            {
                var mesh = existing.transform.Find("Body")?.GetComponent<MeshFilter>();
                if (mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.vertexCount == 4
                    && existing.GetComponent<Collider>() == null && existing.transform.Find("Media Face") == null
                    && existing.GetComponent<ScreenMedia>() != null) return existing;
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try { ConfigureFlatScreen(contents); return PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath); }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            Directory.CreateDirectory("Assets/ARVisualizer/Materials");
            Directory.CreateDirectory("Assets/ARVisualizer/Prefabs");
            AssetDatabase.Refresh();
            var root = new GameObject("Screen", typeof(ScreenSurface));
            try { ConfigureFlatScreen(root); return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { Object.DestroyImmediate(root); }
        }

        static void ConfigureFlatScreen(GameObject root)
        {
            const string materialPath = "Assets/ARVisualizer/Materials/ScreenMedia.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("ARVisualizer/Screen Media"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                var body = root.transform.Find("Body");
                if (body == null) { body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer)).transform; body.SetParent(root.transform, false); }
                body.GetComponent<MeshFilter>().sharedMesh = quad.GetComponent<MeshFilter>().sharedMesh;
                var renderer = body.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                body.localPosition = Vector3.back * ScreenSurface.SurfaceOffset;
                var surface = root.GetComponent<ScreenSurface>();
                body.localScale = new Vector3(surface.Width, surface.Height, 1);
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                var oldFace = root.transform.Find("Media Face");
                if (oldFace != null) Object.DestroyImmediate(oldFace.gameObject);
                var media = root.GetComponent<ScreenMedia>() ?? root.AddComponent<ScreenMedia>();
                var mediaSettings = new SerializedObject(media);
                mediaSettings.FindProperty("mediaFace").objectReferenceValue = renderer;
                mediaSettings.ApplyModifiedPropertiesWithoutUndo();
                var settings = new SerializedObject(root.GetComponent<ScreenSurface>());
                settings.FindProperty("body").objectReferenceValue = body;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            finally { Object.DestroyImmediate(quad); }
        }

        public static void ConfigureApp(ARVisualizerApp app)
        {
            var prefab = EnsurePrefab();
            var settings = new SerializedObject(app);
            if (settings.FindProperty("screenPrefab").objectReferenceValue == null)
            {
                settings.FindProperty("screenPrefab").objectReferenceValue = prefab.GetComponent<ScreenSurface>();
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
            EditorGUILayout.HelpBox("Size Steps controls width in 1.6 cm increments. Photos and videos set the height from their aspect ratio; black screens use 16:9. Body is one quad, 1 mm above its mounting surface. Ray and fingertip coordinates follow the current shape. The front points along local -Z; UV (0,0) is bottom-left.", MessageType.Info);
            DrawDefaultInspector();
            EditorGUILayout.LabelField("Size", $"{screen.Width * 100:0.#} x {screen.Height * 100:0.#} cm (flat)");
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
