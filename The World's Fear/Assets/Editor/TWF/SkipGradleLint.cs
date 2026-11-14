using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace TWF.EditorTools
{
    // Gradle's lint step packs the whole unityLibrary into one archive, which grew past the
    // 4 GB zip limit ("Archive's size exceeds the limit of 4GByte"). Lint is useless for a
    // Unity export, so every lint task is switched off in the generated Gradle project.
    public class SkipGradleLint : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 999;

        const string Marker = "// TWF: skip lint";
        const string Snippet = "\n" + Marker + "\ntasks.configureEach { t -> if (t.name.toLowerCase().contains(\"lint\")) t.enabled = false }\n";

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            Patch(Path.Combine(unityLibraryPath, "build.gradle"));
            Patch(Path.Combine(unityLibraryPath, "..", "launcher", "build.gradle"));
        }

        static void Patch(string file)
        {
            if (!File.Exists(file)) return;
            string s = File.ReadAllText(file);
            if (s.Contains(Marker)) return;
            File.WriteAllText(file, s + Snippet);
            Debug.Log("[SkipGradleLint] patched " + file);
        }
    }
}
