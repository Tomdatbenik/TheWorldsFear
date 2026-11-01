using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TWF.EditorTools
{
    // Exports the character rig (BodyRenderer.Bones) for every direction and frame of the sprite sheet
    // as JSON, for the pixelart-gen pipeline: it turns these into OpenPose guides so generated art
    // follows our exact skeleton. Writes to ../pixelart-gen/poses next to this repo if that exists.
    public static class PoseExporter
    {
        static readonly string[] Directions = { "down", "up", "left", "right" };
        static readonly string[] Frames = { "idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack0", "attack1" };

        [MenuItem("TWF/Export Poses for pixelart-gen")]
        public static void Export()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../pixelart-gen"));
            string dir = Directory.Exists(repo) ? Path.Combine(repo, "poses") : Path.Combine(Application.dataPath, "../ArtPipeline/poses");
            Directory.CreateDirectory(dir);
            foreach (bool female in new[] { false, true })
            {
                string path = Path.Combine(dir, female ? "female.json" : "male.json");
                File.WriteAllText(path, Json(female));
                Debug.Log($"Exported poses -> {path}");
            }
        }

        static BodyRenderer.Pose PoseFor(string direction, int f)
        {
            var facing = direction == "down" ? BodyRenderer.Facing.Front : direction == "up" ? BodyRenderer.Facing.Back : BodyRenderer.Facing.Side;
            var p = new BodyRenderer.Pose { facing = facing };
            if (f < 2) p.breath = f * 2;                                                          // idle: breath in, out
            else if (f < 6) { p.step = new[] { 1, 0, -1, 0 }[f - 2]; p.bob = f % 2 == 1 ? 1 : 0; } // walk cycle
            else p.step = f == 6 ? 1 : -1;                                                        // attack: lunge stance (no swing pose yet)
            return p;
        }

        static string Json(bool female)
        {
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            // Bones are in design units: a 64x96 grid, y up (BodyRenderer draws them at x1.5).
            sb.Append("{\n  \"body\": \"").Append(female ? "female" : "male").Append("\",\n  \"width\": 64,\n  \"height\": 96,\n  \"frames\": [\n");
            bool first = true;
            foreach (var d in Directions)
            for (int f = 0; f < Frames.Length; f++)
            {
                var pose = PoseFor(d, f);
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {\"direction\": \"").Append(d).Append("\", \"frame\": \"").Append(Frames[f])
                  .Append("\", \"facing\": \"").Append(pose.facing.ToString().ToLowerInvariant())
                  .Append("\", \"mirrored\": ").Append(d == "left" ? "true" : "false").Append(", \"bones\": [");
                bool fb = true;
                foreach (var b in BodyRenderer.Bones(female, pose))
                {
                    if (!fb) sb.Append(", ");
                    fb = false;
                    sb.Append("{\"name\": \"").Append(b.name).Append("\", \"a\": [").Append(F(b.a.x)).Append(", ").Append(F(b.a.y))
                      .Append("], \"b\": [").Append(F(b.b.x)).Append(", ").Append(F(b.b.y)).Append("]}");
                }
                sb.Append("]}");
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }
    }
}
