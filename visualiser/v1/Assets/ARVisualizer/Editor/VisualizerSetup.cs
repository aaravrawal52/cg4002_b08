using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace ARVisualizer.Editor
{
    public static class VisualizerSetup
    {
        public const string ScenePath = "Assets/ARVisualizer/Scenes/ARVisualizer.unity";

        [MenuItem("Tools/AR Visualizer/Create or open scene")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath))
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath);
                VisualizerHUDAuthoring.EnsureSceneHUD(Object.FindAnyObjectByType<ARVisualizerApp>());
                ScreenAuthoring.ConfigureApp(Object.FindAnyObjectByType<ARVisualizerApp>());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                Configure();
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = new GameObject("AR Visualizer");
            app.AddComponent<ARVisualizerApp>();
            var serialized = new SerializedObject(app.GetComponent<ARVisualizerApp>());
            serialized.FindProperty("surfaceVisualizationPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MobileARTemplateAssets/Prefabs/ARFeatheredOcclusionPlane.prefab");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            VisualizerHUDAuthoring.EnsureSceneHUD(app.GetComponent<ARVisualizerApp>());
            ScreenAuthoring.ConfigureApp(app.GetComponent<ARVisualizerApp>());
            EditorSceneManager.SaveScene(scene, ScenePath);
            Configure();
        }

        [MenuItem("Tools/AR Visualizer/Configure iPhone")]
        public static void Configure()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.cameraUsageDescription = "Use the camera to point at, touch and place screens on real surfaces.";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            // Preserve existing scenes, but make this experience the startup scene.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("AR Visualizer configured. Open the scene and press Play, or build for iOS.");
        }

        public static void ValidateProject()
        {
            CreateScene();
            ScreenAppAuthoring.PrepareAssets();
            if (Resources.Load<Shader>("VisualizerScreen") == null || Resources.Load<Shader>("VisualizerLine") == null)
                throw new BuildFailedException("Visualizer shaders are missing.");
            if (SceneManager.GetActiveScene().GetRootGameObjects().Count(g => g.GetComponent<ARVisualizerApp>() != null) != 1)
                throw new BuildFailedException("Scene must contain exactly one ARVisualizerApp.");
            var app = Object.FindAnyObjectByType<ARVisualizerApp>();
            if (new SerializedObject(app).FindProperty("surfaceVisualizationPrefab").objectReferenceValue == null)
                throw new BuildFailedException("Assign the sample scene's plane visualization prefab to ARVisualizerApp.");
            if (app.GetComponentInChildren<VisualizerHUD>(true) is not { HasRequiredReferences: true })
                throw new BuildFailedException("The saved HUD is missing required references.");
            Debug.Log("AR_VISUALIZER_VALIDATION_PASSED");
        }

        public static void ExportIOS()
        {
            ValidateProject();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/iOS",
                target = BuildTarget.iOS,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("iOS export failed: " + report.summary.result);
            Debug.Log("AR_VISUALIZER_IOS_EXPORT_PASSED");
        }
    }

    public sealed class VisualizerIOSPostprocess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 1000;
        public void OnPostprocessBuild(BuildReport report)
        {
#if UNITY_IOS
            if (report.summary.platform != BuildTarget.iOS) return;
            string path = report.summary.outputPath;
            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string framework = project.GetUnityFrameworkTargetGuid();
            foreach (string name in new[] { "Vision.framework", "ARKit.framework", "CoreVideo.framework", "ImageIO.framework", "QuartzCore.framework", "Photos.framework", "AVFoundation.framework" })
                project.AddFrameworkToProject(framework, name, false);
            // Scope ARC to this plugin; other native dependencies keep their own compiler settings.
            string plugin = project.FindFileGuidByProjectPath("Libraries/ARVisualizer/Plugins/iOS/AVHandTracking.mm");
            if (string.IsNullOrEmpty(plugin))
                throw new BuildFailedException("AVHandTracking.mm was not included in the Xcode project.");
            project.SetCompileFlagsForFile(framework, plugin, new System.Collections.Generic.List<string> { "-fobjc-arc" });
            string photosPlugin = project.FindFileGuidByProjectPath("Libraries/ARVisualizer/Plugins/iOS/AVPhotoLibrary.mm");
            if (string.IsNullOrEmpty(photosPlugin)) throw new BuildFailedException("AVPhotoLibrary.mm was not included in the Xcode project.");
            project.SetCompileFlagsForFile(framework, photosPlugin, new System.Collections.Generic.List<string> { "-fobjc-arc" });
            PrepareSymbolTools(project, framework);
            PrepareSymbolTools(project, project.GetUnityMainTargetGuid());
            project.WriteToFile(projectPath);
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSLocalNetworkUsageDescription", "Receive visualiser commands from a controller on your Wi-Fi network.");
            plist.root.SetString("NSPhotoLibraryUsageDescription", "Browse your photos and videos in AR and display selected items on your virtual screens.");
            plist.root.SetBoolean("PHPhotoLibraryPreventAutomaticLimitedAccessAlert", true);
            plist.WriteToFile(plistPath);
#endif
        }
#if UNITY_IOS
        static void PrepareSymbolTools(PBXProject project, string target)
        {
            // Windows exports / archive transfers may drop Unix executable bits. Repair
            // them on the Mac at build time, before either target's symbol-processing phase.
            // No outputs: run on every build, including after another copy of the export.
            const string name = "AR Visualizer Prepare Symbol Tools";
            const string script = "set -eu\n" +
                "for tool in process_symbols.sh usymtool usymtoolarm64; do\n" +
                "    tool_path=\"$PROJECT_DIR/$tool\"\n" +
                "    if [ -f \"$tool_path\" ]; then\n" +
                "        /bin/chmod u+x \"$tool_path\"\n" +
                "    fi\n" +
                "done\n";
            if (string.IsNullOrEmpty(project.GetShellScriptBuildPhaseForTarget(target, name, "/bin/sh", script)))
                project.InsertShellScriptBuildPhase(0, target, name, "/bin/sh", script);
        }
#endif
    }
}
