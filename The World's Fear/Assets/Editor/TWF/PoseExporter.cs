using System.Collections.Generic;
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
                ExportRigImages(Path.Combine(dir, "..", "rig", female ? "female" : "male"), female);
                Debug.Log($"Exported poses -> {path}");
            }
        }

        // Per direction (idle pose): the bare base render and its part-id map, at the sprite resolution.
        // The pipeline seeds the AI base from the render, then cuts the result into parts and bones.
        static void ExportRigImages(string dir, bool female)
        {
            Directory.CreateDirectory(dir);
            var look = new Appearance { body = female ? 1 : 0 };
            foreach (var d in new[] { "down", "up", "right" })
            {
                var res = BodyRenderer.FigureResult(look, null, PoseFor(d, 0));
                var px = BodyRenderer.Finish(res);
                var color = new Texture2D(BodyRenderer.W, BodyRenderer.H, TextureFormat.RGBA32, false);
                var parts = new Texture2D(BodyRenderer.W, BodyRenderer.H, TextureFormat.RGBA32, false);
                for (int x = 0; x < BodyRenderer.W; x++)
                for (int y = 0; y < BodyRenderer.H; y++)
                {
                    color.SetPixel(x, y, px[x, y]);
                    int id = res.part[x, y];
                    parts.SetPixel(x, y, new Color32((byte)Mathf.Min(id, 255), 0, 0, (byte)(id > 0 ? 255 : 0)));
                }
                File.WriteAllBytes(Path.Combine(dir, d + "_base.png"), color.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(dir, d + "_parts.png"), parts.EncodeToPNG());
                File.WriteAllText(Path.Combine(dir, d + "_parts.json"), SlotsJson(res));
                Object.DestroyImmediate(color);
                Object.DestroyImmediate(parts);
                foreach (var (name, gear) in Items())
                    ExportItem(Path.Combine(dir, "items", name), d, look, gear);
            }
        }

        // Every equipment piece alone on the bare body: its render (the shape and colors the AI starts
        // from), its mask (where it's visible) and the part map (for the bone cut).
        static IEnumerable<(string, BodyRenderer.Gear)> Items()
        {
            BodyRenderer.Gear Bare() => new BodyRenderer.Gear { bareChest = true, trousers = false };
            foreach (ChestStyle c in System.Enum.GetValues(typeof(ChestStyle)))
                yield return ("chest_" + c, new BodyRenderer.Gear { chest = c, trousers = false });
            var t = Bare(); t.trousers = true; yield return ("legs_Trousers", t);
            var sk = Bare(); sk.skirt = true; yield return ("legs_Skirt", sk);
            foreach (HelmStyle h in System.Enum.GetValues(typeof(HelmStyle)))
                if (h != HelmStyle.None) { var g = Bare(); g.helm = h; yield return ("helm_" + h, g); }
            foreach (GloveStyle gl in System.Enum.GetValues(typeof(GloveStyle)))
                if (gl != GloveStyle.None) { var g = Bare(); g.gloves = gl; yield return ("gloves_" + gl, g); }
            foreach (BootStyle b in System.Enum.GetValues(typeof(BootStyle)))
                { var g = Bare(); g.boots = b; yield return ("boots_" + b, g); }
            var cape = Bare(); cape.cape = true; yield return ("cape", cape);
        }

        static void ExportItem(string dir, string direction, Appearance look, BodyRenderer.Gear gear)
        {
            Directory.CreateDirectory(dir);
            string name = Path.GetFileName(dir);
            string slot = name == "legs_Skirt" ? "skirt" : name.Split('_')[0];   // the renderer tags the skirt as its own slot
            var res = BodyRenderer.FigureResult(look, gear, PoseFor(direction, 0));
            var px = BodyRenderer.Finish(res);
            var color = new Texture2D(BodyRenderer.W, BodyRenderer.H, TextureFormat.RGBA32, false);
            var mask = new Texture2D(BodyRenderer.W, BodyRenderer.H, TextureFormat.RGBA32, false);
            var parts = new Texture2D(BodyRenderer.W, BodyRenderer.H, TextureFormat.RGBA32, false);
            for (int x = 0; x < BodyRenderer.W; x++)
            for (int y = 0; y < BodyRenderer.H; y++)
            {
                int id = res.part[x, y];
                bool on = id > 0 && res.parts[id - 1].slot == slot;
                color.SetPixel(x, y, px[x, y]);
                mask.SetPixel(x, y, on ? Color.white : Color.black);
                parts.SetPixel(x, y, new Color32((byte)Mathf.Min(id, 255), 0, 0, (byte)(id > 0 ? 255 : 0)));
            }
            File.WriteAllBytes(Path.Combine(dir, direction + "_render.png"), color.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(dir, direction + "_mask.png"), mask.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(dir, direction + "_parts.png"), parts.EncodeToPNG());
            File.WriteAllText(Path.Combine(dir, direction + "_parts.json"), SlotsJson(res));
            Object.DestroyImmediate(color);
            Object.DestroyImmediate(mask);
            Object.DestroyImmediate(parts);
        }

        // Slot of every part id ("" = body), so the pipeline can keep hair on the hair bone and so on.
        static string SlotsJson(BodyRenderer.Result res)
        {
            // [slot, group] per part id; id 0 is empty.
            var sb = new StringBuilder("[[\"\", \"\"]");
            foreach (var p in res.parts) sb.Append(", [\"").Append(p.slot ?? "").Append("\", \"").Append(p.group ?? "").Append("\"]");
            return sb.Append("]").ToString();
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
            sb.Append("{\n  \"body\": \"").Append(female ? "female" : "male").Append("\",\n  \"width\": 64,\n  \"height\": 96,\n  \"scale\": ").Append(F(BodyRenderer.S)).Append(",\n  \"frames\": [\n");
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
