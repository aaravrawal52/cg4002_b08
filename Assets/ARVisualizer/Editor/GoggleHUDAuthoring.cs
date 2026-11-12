using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARVisualizer.Editor
{
    public static class GoggleHUDAuthoring
    {
        public static void PrepareScene()
        {
            VisualizerHUDAuthoring.EnsurePrefab();
            var root = PrefabUtility.LoadPrefabContents(VisualizerHUDAuthoring.PrefabPath);
            try
            {
                if (root.GetComponent<GoggleHUD>() == null) root.AddComponent<GoggleHUD>();
                if (root.GetComponent<StereoGoggles>() == null) root.AddComponent<StereoGoggles>();
                PrefabUtility.SaveAsPrefabAsset(root, VisualizerHUDAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var scene = EditorSceneManager.OpenScene(VisualizerSetup.ScenePath);
            VisualizerHUDAuthoring.EnsureSceneHUD(Object.FindAnyObjectByType<ARVisualizerApp>());
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AR_VISUALIZER_GOGGLE_HUD_READY");
        }
    }
}
