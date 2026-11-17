using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TWF.Tests
{
    // One automated check of the game. A static method carrying this attribute shows up as a test in Unity's Test
    // Runner (Window > General > Test Runner) and in `unity test`:
    //   void        -> an EditMode test (world generation, assets, settings: nothing has to run)
    //   IEnumerator -> a PlayMode test (it loads a scene, lets frames pass, looks at the result)
    // The game's code lives in the default assembly, which a test assembly cannot reference, so the checks are written
    // here, beside the editor tools, and Assets/Tests only lists and runs them.
    // To add a test: write a static method in any class of this folder, put [TwfCheck("Group/What it proves")] on it,
    // and fail with Check.That / Check.Fail. Play checks start with `yield return Play.Scene("World")`.
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class TwfCheckAttribute : System.Attribute
    {
        public string Name { get; }
        public TwfCheckAttribute(string name) { Name = name; }
    }

    public class CheckFailed : Exception
    {
        public CheckFailed(string message) : base(message) { }
    }

    public static class Check
    {
        public static void Fail(string message) => throw new CheckFailed(message);

        public static void That(bool ok, string message)
        {
            if (!ok) Fail(message);
        }

        public static void Near(float value, float expected, float tolerance, string what) =>
            That(Mathf.Abs(value - expected) <= tolerance, $"{what}: {value:0.###}, expected {expected:0.###} (within {tolerance:0.###})");

        public static void AtMost(float value, float max, string what) => That(value <= max, $"{what}: {value:0.###}, allowed at most {max:0.###}");
        public static void AtLeast(float value, float min, string what) => That(value >= min, $"{what}: {value:0.###}, needed at least {min:0.###}");

        // The seeds the world checks run on: the legacy world and two others, so a rule holds for every hero's world.
        public static readonly int[] Seeds = { WorldGen.LegacySeed, 12345, 987654 };

        // Runs a world-generation check on each seed and puts the seed back.
        public static void EachSeed(Action<int> body)
        {
            int before = WorldGen.Seed;
            try
            {
                foreach (int seed in Seeds)
                {
                    WorldGen.SetSeed(seed);
                    body(seed);
                }
            }
            finally { WorldGen.SetSeed(before); }
        }
    }

    // Helpers for checks that run the game.
    public static class Play
    {
        // Loads a scene on a throwaway hero (nothing is written to the real save) and lets it settle.
        public static IEnumerator Scene(string name, float settle = 2f)
        {
            Profile.Sandbox = true;
            Time.timeScale = 1f;
            if (SceneManager.GetActiveScene().name != name)
            {
                // By path, so scenes left out of the build (the debug scene) open too.
                string path = "Assets/Scenes/" + name + ".unity";
                Check.That(File.Exists(path), $"Scene '{path}' does not exist");
                var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
                while (!op.isDone) yield return null;
            }
            Profile.Sandbox = true;
            yield return Wait(settle);
            Profile.Sandbox = true;
            Check.That(HeroController.Instance != null, $"No hero in scene '{name}'");
            HeroController.Instance.Combatant.Invulnerable = true;
        }

        public static IEnumerator Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        // Waits until the condition holds; fails with `what` after the timeout.
        public static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until) Check.Fail($"Timed out after {timeout:0}s waiting for: {what}");
                yield return null;
            }
        }

        public static Vector2 HeroAt => HeroController.Instance.transform.G();

        public static void MoveHero(Vector2 to)
        {
            var hero = HeroController.Instance;
            var body = hero.GetComponent<GroundBody>();
            if (body != null) body.position = to; else hero.transform.SetG(to);
        }

        // A walkable spot of the open world whose monsters are about `level`, in the direction given.
        public static Vector2 SpotAtLevel(int level, Vector2 dir)
        {
            dir.Normalize();
            for (int r = 100; r < 8000; r += 20)
            {
                var p = dir * r;
                if (WorldGen.LevelAt(p) < level) continue;
                for (int d = 0; d < 60; d++)
                {
                    int x = Mathf.FloorToInt(p.x) + d, y = Mathf.FloorToInt(p.y);
                    if (WorldGen.Walkable(WorldGen.CellAt(x, y)) && WorldGen.CellAt(x, y) != Cell.Stairs) return new Vector2(x + 0.5f, y + 0.5f);
                }
            }
            Check.Fail($"No walkable spot of level {level} toward {dir}");
            return default;
        }

        // ---------- Looking at the picture ----------

        public struct Picture
        {
            public float brightness;     // mean luminance, 0..1
            public float contrast;       // standard deviation of the luminance
            public float pink;           // share of pixels in the missing-shader magenta
            public float black;          // share of pixels that are pure black
            public string file;          // where the picture was saved
        }

        // Renders what the game camera sees and measures it. The picture is saved under Logs/TestShots so a failing
        // test can be looked at.
        public static Picture Shoot(string name, int width = 480, int height = 270)
        {
            var cam = Camera.main;
            Check.That(cam != null, "No main camera to render with");
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var beforeTarget = cam.targetTexture;
            var beforeActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            cam.targetTexture = beforeTarget;
            RenderTexture.active = beforeActive;
            RenderTexture.ReleaseTemporary(rt);

            var px = tex.GetPixels32();
            double sum = 0, sumSq = 0; int pink = 0, black = 0;
            foreach (var c in px)
            {
                float l = (0.3f * c.r + 0.59f * c.g + 0.11f * c.b) / 255f;
                sum += l; sumSq += l * l;
                if (c.r > 235 && c.b > 235 && c.g < 40) pink++;
                if (c.r < 3 && c.g < 3 && c.b < 3) black++;
            }
            double mean = sum / px.Length;
            var pic = new Picture
            {
                brightness = (float)mean,
                contrast = (float)Math.Sqrt(Math.Max(0, sumSq / px.Length - mean * mean)),
                pink = pink / (float)px.Length,
                black = black / (float)px.Length,
            };
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TestShots");
            Directory.CreateDirectory(dir);
            pic.file = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(pic.file, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            return pic;
        }

        // A picture of a working scene: something is drawn, nothing is missing its shader, it is not one flat colour.
        public static void LooksRight(Picture p, float minBrightness, string what)
        {
            Check.AtMost(p.pink, 0.002f, $"{what}: share of missing-shader pink pixels ({p.file})");
            Check.AtMost(p.black, 0.6f, $"{what}: share of pure black pixels ({p.file})");
            Check.AtLeast(p.brightness, minBrightness, $"{what}: brightness ({p.file})");
            Check.AtLeast(p.contrast, 0.02f, $"{what}: contrast - the picture is one flat colour ({p.file})");
        }

        // Frame times over a stretch of play, in milliseconds.
        public static IEnumerator Frames(float seconds, List<float> into)
        {
            for (int i = 0; i < 20; i++) yield return null;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                into.Add(Time.unscaledDeltaTime * 1000f);
            }
            into.Sort();
        }

        public static float Percentile(List<float> sorted, float q) => sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * q))];
    }
}
