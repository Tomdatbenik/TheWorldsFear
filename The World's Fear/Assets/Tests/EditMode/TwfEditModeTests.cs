using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace TWF.TestRunner
{
    // Lists the game's EditMode checks (static void methods marked [TwfCheck] under Assets/Editor/TWF/Tests) as tests.
    // The checks themselves cannot live here: a test assembly cannot reference the game's default assembly, so they are
    // found by name at run time. See Assets/Editor/TWF/Tests/Check.cs for how to add one.
    public class TwfChecks
    {
        public static IEnumerable<(string name, MethodInfo method)> Find(bool play)
        {
            var found = new List<(string, MethodInfo)>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Assembly-CSharp-Editor") continue;
                foreach (var type in asm.GetTypes())
                    foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var attr = m.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().Name == "TwfCheckAttribute");
                        if (attr == null || (m.ReturnType == typeof(IEnumerator)) != play) continue;
                        found.Add(((string)attr.GetType().GetProperty("Name").GetValue(attr), m));
                    }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return found;
        }

        static IEnumerable<TestCaseData> Cases() => Find(false).Select(c => new TestCaseData(c.method).SetName(c.name));

        [TestCaseSource(nameof(Cases))]
        public void EditMode(MethodInfo check)
        {
            try { check.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null) { Assert.Fail(e.InnerException.Message + "\n" + e.InnerException.StackTrace); }
        }
    }
}
