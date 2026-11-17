#if UNITY_IOS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.iOS.Xcode;

namespace ARVisualizer.Tests
{
    public sealed class IOSBuildTests
    {
        [TestCase("UnityFramework")]
        [TestCase("Unity-iPhone")]
        public void SymbolToolPreparationPrecedesProcessingAndIsNotDuplicated(string targetName)
        {
            var project = new PBXProject();
            project.ReadFromFile(Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines/iOSSupport/Trampoline/Unity-iPhone.xcodeproj/project.pbxproj"));
            string target = targetName == "Unity-iPhone" ? project.GetUnityMainTargetGuid() : project.GetUnityFrameworkTargetGuid();
            Assert.IsNotEmpty(target);
            string symbols = project.AddShellScriptBuildPhase(target, "Unity Process symbols for " + targetName,
                "/bin/sh", "\"$PROJECT_DIR/process_symbols.sh\"");
            var originalPhases = project.GetAllBuildPhasesForTarget(target);
            // Authoring hooks live in Unity's predefined Editor assembly.
            var hook = Type.GetType("ARVisualizer.Editor.VisualizerIOSPostprocess, Assembly-CSharp-Editor", true)
                .GetMethod("PrepareSymbolTools", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(hook);
            hook.Invoke(null, new object[] { project, target });
            hook.Invoke(null, new object[] { project, target });
            var roundTrip = new PBXProject();
            roundTrip.ReadFromString(project.WriteToString());
            var phases = roundTrip.GetAllBuildPhasesForTarget(target);
            Assert.AreEqual(originalPhases.Length + 1, phases.Length, "Repeated processing must add only one phase");
            Assert.AreEqual("AR Visualizer Prepare Symbol Tools", roundTrip.GetBuildPhaseName(phases[0]));
            CollectionAssert.AreEqual(originalPhases, phases.Skip(1).ToArray(), "Preserve Unity's existing build order");
            Assert.Greater(Array.IndexOf(phases, symbols), 0, "Permissions must be repaired before symbols run");
        }
    }
}
#endif
