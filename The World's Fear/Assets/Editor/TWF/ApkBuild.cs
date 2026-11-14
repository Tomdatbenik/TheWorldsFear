using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TWF.EditorTools
{
    // Headless Android build: Unity.exe -batchmode -quit -projectPath ... -executeMethod TWF.EditorTools.ApkBuild.Run
    public static class ApkBuild
    {
        public const string Output = "C:/Users/tomva/Downloads/TheWorldsFear.apk";

        public static void Run()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log("[ApkBuild] result=" + s.result + " size=" + s.totalSize + " errors=" + s.totalErrors + " time=" + s.totalTime);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
