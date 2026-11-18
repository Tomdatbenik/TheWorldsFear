using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TWF.EditorTools
{
    // iOS build: generates an Xcode project in <project>/Builds/iOS.
    // Open Builds/iOS/Unity-iPhone.xcodeproj in Xcode, pick your iPhone, press Run.
    // Headless: Unity -batchmode -quit -projectPath ... -executeMethod TWF.EditorTools.IosBuild.Run
    public static class IosBuild
    {
        public const string BundleId = "com.IceWollow.TheWorldsFear";

        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "iOS"));

        [MenuItem("TWF/Build/iOS (Xcode project)")]
        public static void Run()
        {
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, BundleId);
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Output,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            });
            var s = report.summary;
            // Unity can report Succeeded even when post-processing failed (e.g. Editor started before
            // the iOS module was installed), so also require the Xcode project to exist.
            bool ok = s.result == BuildResult.Succeeded
                      && Directory.Exists(Path.Combine(Output, "Unity-iPhone.xcodeproj"));
            Debug.Log("[IosBuild] result=" + (ok ? "Succeeded" : "Failed") + " (unity=" + s.result + ") out=" + Output + " errors=" + s.totalErrors + " time=" + s.totalTime);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
