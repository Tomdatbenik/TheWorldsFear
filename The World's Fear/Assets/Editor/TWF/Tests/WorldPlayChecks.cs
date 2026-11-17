using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF.Tests
{
    // The open world, running: what loads, what spawns, how it is drawn and how fast.
    public static class WorldPlayChecks
    {
        [TwfCheck("World/Loads with a hero, a camera and terrain")]
        static IEnumerator Loads()
        {
            yield return Play.Scene("World", 4f);
            Check.That(ChunkWorld.Instance != null, "No ChunkWorld in the World scene");
            Check.That(Camera.main != null, "No main camera");
            Check.That(Object.FindObjectsByType<MonsterCamp>().Length > 0, "No monster camps were placed round the town");
            Check.That(TerrainHeight.World != null, "The world's height function is not set");
        }

        [TwfCheck("World/The picture is drawn by day and by night, in every biome")]
        static IEnumerator Picture()
        {
            yield return Play.Scene("World", 3f);
            float timeBefore = DayNight.Time01;
            // By the town at noon, then at midnight (the lantern and the moon: dark, never black or flat).
            Play.MoveHero(new Vector2(-18f, 56f));
            DayNight.SetTime(0.33f);
            yield return Play.Wait(4f);
            yield return new WaitForEndOfFrame();
            Play.LooksRight(Play.Shoot("world_forest_noon"), 0.1f, "The forest by the town at noon");
            DayNight.SetTime(0.85f);
            yield return Play.Wait(2f);
            yield return new WaitForEndOfFrame();
            Play.LooksRight(Play.Shoot("world_forest_midnight"), 0.01f, "The forest by the town at midnight");
            // Each biome's own ground, cliffs and scenery, at noon.
            DayNight.SetTime(0.33f);
            var seen = new HashSet<Biome>();
            for (int i = 0; i < 80 && seen.Count < 4; i++)
            {
                var spot = Play.SpotAtLevel(6 + i % 10 * 4, new Vector2(Mathf.Cos(i * 0.61f), Mathf.Sin(i * 0.61f)));
                var biome = WorldGen.BiomeAt(spot);
                if (biome == Biome.Village || !seen.Add(biome)) continue;
                Play.MoveHero(spot);
                yield return Play.Wait(5f);
                yield return new WaitForEndOfFrame();
                Play.LooksRight(Play.Shoot("world_" + biome.ToString().ToLowerInvariant() + "_noon"), 0.05f, $"The {biome} country at noon (at {spot})");
            }
            Check.AtLeast(seen.Count, 3, "biomes found to look at");
            DayNight.SetTime(timeBefore);
        }

        [TwfCheck("World/Monsters live in the far country")]
        static IEnumerator MonstersFarOut()
        {
            yield return Play.Scene("World", 3f);
            foreach (var dir in new[] { new Vector2(0.8f, 0.6f), new Vector2(-0.6f, 0.8f) })
            {
                var spot = Play.SpotAtLevel(37, dir);
                Play.MoveHero(spot);
                yield return Play.Wait(6f);
                int camps = 0;
                foreach (var c in Object.FindObjectsByType<MonsterCamp>())
                    if (Vector2.Distance(c.transform.G(), spot) < 95f) camps++;
                // Broken country used to drop most of its camps: about 19 stood here before, with 10 caches.
                Check.AtLeast(camps, 30, $"monster camps within 95 tiles of {spot}");
                int caches = 0;
                foreach (var c in Object.FindObjectsByType<DungeonChest>())
                    if (c.title == "Hidden cache" && Vector2.Distance(c.transform.G(), spot) < 95f) caches++;
                Check.AtMost(caches, 5, $"hidden caches within 95 tiles of {spot}");
                // Walk up to the nearest camp: its pack has to appear.
                MonsterCamp nearest = null; float best = float.MaxValue;
                foreach (var c in Object.FindObjectsByType<MonsterCamp>())
                {
                    float d = Vector2.Distance(c.transform.G(), spot);
                    if (d < best) { best = d; nearest = c; }
                }
                Play.MoveHero(nearest.transform.G() + Vector2.right);
                yield return Play.Until(() => Object.FindObjectsByType<MonsterBrain>().Length > 0, 8f, $"a pack to spawn at the camp at {nearest.transform.G()}");
            }
        }

        [TwfCheck("World/The hero stands on the ground, slopes included")]
        static IEnumerator HeroOnGround()
        {
            yield return Play.Scene("World", 3f);
            var spot = Play.SpotAtLevel(20, new Vector2(0.3f, -0.95f));
            Play.MoveHero(spot);
            yield return Play.Wait(4f);
            // Slope and bent-ground tiles near the spot, where height used to be off by up to half a level.
            var places = new List<Vector2> { spot };
            int sx = Mathf.FloorToInt(spot.x), sy = Mathf.FloorToInt(spot.y);
            for (int r = 1; r < 40 && places.Count < 6; r++)
                for (int k = 0; k < 8 && places.Count < 6; k++)
                {
                    int x = sx + (k % 3 - 1) * r, y = sy + (k / 3 - 1) * r;
                    if (WorldGen.SurfaceAt(x, y, out _, out _, out _, out _)) places.Add(new Vector2(x + 0.5f, y + 0.5f));
                }
            foreach (var p in places)
            {
                Play.MoveHero(p);
                yield return Play.Wait(1.2f);
                float y = HeroController.Instance.transform.position.y;
                Check.Near(y, TerrainHeight.At(Play.HeroAt), 0.1f, $"hero height at {p}");
            }
        }

        [TwfCheck("World/Trees and rocks cast their shadow at their foot")]
        static IEnumerator DecorShadows()
        {
            yield return Play.Scene("World", 4f);
            int cards = 0, casting = 0, feet = 0; var who = new HashSet<string>();
            foreach (var b in Object.FindObjectsByType<Billboard>())
            {
                var sr = b.GetComponent<SpriteRenderer>();
                var root = b.transform.parent;
                if (sr == null || root == null || root.gameObject.layer != LayerMask.NameToLayer("Walls") || root.GetComponent<EnvSprite>() != null) continue;
                cards++;
                if (sr.shadowCastingMode != ShadowCastingMode.Off) { casting++; who.Add(root.name + (root.parent != null ? " in " + root.parent.name : "")); }
                if (root.Find("ShadowShape") != null) feet++;
            }
            Check.AtLeast(cards, 10, "trees and rocks found round the town");
            Check.That(casting == 0, $"{casting} of {cards} tree/rock cards cast their own shadow (it lands a crown's length away, on top of the tree): {string.Join(", ", who)}");
            Check.AtLeast(feet / (float)cards, 0.95f, "share of trees and rocks with a shadow shape at their foot");
        }

        // A dungeon's stairs go down through a hole in the ground. A path patch laid round the gate once covered the
        // hole like a lid, so the stairs were drawn under the floor.
        [TwfCheck("World/Dungeon stairwells are open: no ground drawn over the stairs")]
        static IEnumerator DungeonStairwell()
        {
            yield return Play.Scene("World", 3f);
            Vector2Int gate = default; bool found = false;
            for (int r = 1; r < 30 && !found; r++)
                for (int cx = -r; cx <= r && !found; cx++)
                for (int cy = -r; cy <= r && !found; cy++)
                    if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cy)) == r && WorldGen.HasDungeon(cx, cy, out gate)) found = true;
            Check.That(found, "No dungeon within 30 chunks of the town");
            Play.MoveHero((Vector2)gate + new Vector2(0.5f, -8f));
            yield return Play.Until(() => Object.FindAnyObjectByType<DungeonGate>() != null, 15f, $"the dungeon gate at {gate} to appear");
            yield return Play.Wait(2f);
            var gateObj = Object.FindAnyObjectByType<DungeonGate>();
            var hole = WorldGen.Stairwell(gate);
            float groundY = WorldGen.ElevAt(gate.x, gate.y) * WorldGen.LevelHeight;
            int lids = 0; string worst = "";
            foreach (var mf in Object.FindObjectsByType<MeshFilter>())
            {
                var mesh = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null || !mr.enabled || mf.transform.IsChildOf(gateObj.transform) || !mesh.isReadable) continue;
                if (mr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                var b = mr.bounds;
                if (b.max.x < hole.xMin || b.min.x > hole.xMax || b.max.z < hole.yMin || b.min.z > hole.yMax) continue;
                var v = mesh.vertices; var t = mesh.triangles; var m = mf.transform.localToWorldMatrix; int here = 0;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 p0 = m.MultiplyPoint3x4(v[t[i]]), p1 = m.MultiplyPoint3x4(v[t[i + 1]]), p2 = m.MultiplyPoint3x4(v[t[i + 2]]);
                    var c = (p0 + p1 + p2) / 3f;
                    // Lying flat at ground level, well inside the hole.
                    if (c.x < hole.xMin + 0.4f || c.x > hole.xMax - 0.4f || c.z < hole.yMin + 0.4f || c.z > hole.yMax - 0.4f) continue;
                    if (Mathf.Abs(c.y - groundY) > 0.15f || Mathf.Abs(Vector3.Cross(p1 - p0, p2 - p0).normalized.y) < 0.9f) continue;
                    here++;
                }
                if (here > 0) { lids += here; worst += $" {mf.name}({here})"; }
            }
            Play.MoveHero((Vector2)gate + new Vector2(3.5f, -3f));
            yield return Play.Wait(1.5f);
            yield return new WaitForEndOfFrame();
            var shot = Play.Shoot("world_dungeon_gate");
            Check.That(lids == 0, $"{lids} ground triangles lie over the stairwell of the dungeon at {gate}:{worst} ({shot.file})");
        }

        [TwfCheck("World/Loot on the floor does not pile up lights")]
        static IEnumerator LootLightBudget()
        {
            yield return Play.Scene("World", 3f);
            var made = new List<GameObject>();
            var lights = new List<Light>();
            for (int i = 0; i < 40; i++)
            {
                var go = new GameObject("TestLoot");
                go.transform.position = Ground.On(Play.HeroAt + new Vector2(Mathf.Cos(i * 0.7f), Mathf.Sin(i * 0.7f)) * (1f + i * 0.2f));
                made.Add(go);
                var l = PointLights.AttachLoot(go.transform, Color.yellow, 3f, 1.5f, Vector3.up);
                if (l != null) lights.Add(l);
            }
            yield return Play.Wait(0.6f);
            int lit = lights.FindAll(l => l.enabled).Count;
            Check.AtMost(lit, RenderAssets.LootLights, "lit loot lights with 40 items on the floor");
            if (RenderAssets.LootLights > 0)
            {
                Check.That(lit == Mathf.Min(RenderAssets.LootLights, lights.Count), $"{lit} loot lights are on, the budget is {RenderAssets.LootLights}");
                // The lit ones are the nearest.
                lights.Sort((a, b) => (a.transform.position - HeroController.Instance.transform.position).sqrMagnitude.CompareTo((b.transform.position - HeroController.Instance.transform.position).sqrMagnitude));
                Check.That(lights[0].enabled, "The loot nearest the hero is not lit");
            }
            foreach (var go in made) Object.Destroy(go);
        }

        // Frame time with a big fight on screen. The numbers are for the editor on a PC: they catch a change that makes
        // the game several times slower, not what a phone gets.
        [TwfCheck("World/Performance: a big fight keeps its frame time")]
        static IEnumerator FightFrameTime()
        {
            yield return Play.Scene("World", 3f);
            var spot = Play.SpotAtLevel(12, new Vector2(-0.7f, -0.7f));
            Play.MoveHero(spot);
            yield return Play.Wait(5f);
            var into = new List<MonsterBrain>();
            var rng = new System.Random(5);
            for (int i = 0; i < 24; i++)
            {
                var p = Play.HeroAt + new Vector2(Mathf.Cos(i * 0.52f), Mathf.Sin(i * 0.52f)) * (6f + i % 3 * 3f);
                if (!WorldGen.Walkable(WorldGen.CellAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)))) continue;
                MonsterSpawner.SpawnPack(WorldGen.RegionAt(p), p, 0.5f, into, rng, 12, 0f, 0f, null, 0f);
            }
            Check.AtLeast(into.Count, 30, "monsters spawned for the fight");
            var frames = new List<float>();
            yield return Play.Frames(6f, frames);
            Debug.Log($"[TwfCheck] fight: {into.Count} monsters, median {Play.Percentile(frames, 0.5f):0.0} ms, p95 {Play.Percentile(frames, 0.95f):0.0} ms, worst {frames[frames.Count - 1]:0} ms");
            Check.AtMost(Play.Percentile(frames, 0.5f), 25f, $"median frame time (ms) with {into.Count} monsters");
            Check.AtMost(Play.Percentile(frames, 0.95f), 50f, $"95th percentile frame time (ms) with {into.Count} monsters");
            foreach (var m in into) if (m != null) Object.Destroy(m.gameObject);
        }

        // Running into new country generates chunks. Counts the frames that stall.
        [TwfCheck("World/Performance: running into new country does not freeze")]
        static IEnumerator StreamingHitches()
        {
            yield return Play.Scene("World", 3f);
            var spot = Play.SpotAtLevel(30, new Vector2(0.8f, 0.6f));
            Play.MoveHero(spot);
            yield return Play.Wait(6f);
            var frames = new List<float>();
            var dir = new Vector2(-0.8f, -0.6f);
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until)
            {
                Play.MoveHero(Play.HeroAt + dir * 12f * Time.unscaledDeltaTime);
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
            }
            frames.Sort();
            int over100 = frames.FindAll(f => f > 100f).Count;
            Debug.Log($"[TwfCheck] streaming: {frames.Count} frames, median {Play.Percentile(frames, 0.5f):0.0} ms, p99 {Play.Percentile(frames, 0.99f):0.0} ms, worst {frames[frames.Count - 1]:0} ms, over 100 ms: {over100}");
            Check.AtMost(frames[frames.Count - 1], 600f, "worst frame (ms) while running 12 tiles a second into new country");
            Check.AtMost(over100, 20, "frames over 100 ms in 20 seconds of running into new country");
        }
    }
}
