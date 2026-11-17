using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TWF.TestRunner
{
    // Lists the game's PlayMode checks (static IEnumerator methods marked [TwfCheck] under Assets/Editor/TWF/Tests) as
    // tests: each loads a scene, plays it and looks at the result. They run in the editor only.
    // The player's save files are copied aside before the first test and put back after the last.
    public class TwfPlayChecks
    {
        static Dictionary<string, MethodInfo> checks;

        static Dictionary<string, MethodInfo> Checks()
        {
            if (checks != null) return checks;
            checks = new Dictionary<string, MethodInfo>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Assembly-CSharp-Editor") continue;
                foreach (var type in asm.GetTypes())
                    foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var attr = m.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().Name == "TwfCheckAttribute");
                        if (attr == null || m.ReturnType != typeof(IEnumerator)) continue;
                        checks[(string)attr.GetType().GetProperty("Name").GetValue(attr)] = m;
                    }
            }
            return checks;
        }

        static IEnumerable<string> Names() => Checks().Keys.OrderBy(k => k, StringComparer.Ordinal);

        string backupDir;

        [OneTimeSetUp]
        public void BackUpSaves()
        {
            backupDir = Path.Combine(Application.temporaryCachePath, "twf-test-saves");
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            Directory.CreateDirectory(backupDir);
            foreach (var f in Directory.GetFiles(Application.persistentDataPath, "*.json"))
                File.Copy(f, Path.Combine(backupDir, Path.GetFileName(f)), true);
        }

        [OneTimeTearDown]
        public void RestoreSaves()
        {
            Time.timeScale = 1f;
            if (backupDir == null || !Directory.Exists(backupDir)) return;
            foreach (var f in Directory.GetFiles(Application.persistentDataPath, "*.json"))
                if (!File.Exists(Path.Combine(backupDir, Path.GetFileName(f)))) File.Delete(f);
            foreach (var f in Directory.GetFiles(backupDir, "*.json"))
                File.Copy(f, Path.Combine(Application.persistentDataPath, Path.GetFileName(f)), true);
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator PlayMode([ValueSource(nameof(Names))] string check)
        {
            var run = (IEnumerator)Checks()[check].Invoke(null, null);
            // Stepped by hand so that nested steps run too and a failure names the check.
            var stack = new Stack<IEnumerator>();
            stack.Push(run);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e) { Assert.Fail(e.Message + "\n" + e.StackTrace); yield break; }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator inner) stack.Push(inner);
                else yield return top.Current;
            }
        }
    }
}
