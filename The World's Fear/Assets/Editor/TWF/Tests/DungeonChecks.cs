using System.Collections;
using UnityEngine;

namespace TWF.Tests
{
    // Inside a dungeon, running.
    public static class DungeonChecks
    {
        // The town's wall tops and stairs were kept in TerrainHeight's static list after leaving the open world, and a
        // dungeon is laid out on the same coordinates: they rose out of its floor as slopes and stairs that carried the
        // hero up over the dungeon's walls and out of it.
        [TwfCheck("Dungeon/The floor is flat and the walls hold the hero in, after coming from the open world")]
        static IEnumerator FlatAndWalledIn()
        {
            yield return Play.Scene("World", 3f);
            WorldManager.PendingDungeonSeed = 12345;
            WorldManager.PendingDungeonLevel = 10;
            WorldManager.PendingDungeonBiome = Biome.Ruins;
            yield return Play.Scene("Dungeon", 3f);
            Check.That(DungeonSetup.Instance != null, "No DungeonSetup in the Dungeon scene");
            Check.That(TerrainHeight.World == null, "The open world's height function is still set inside the dungeon");

            var biome = WorldManager.PendingDungeonBiome == Biome.Village ? Biome.Ruins : WorldManager.PendingDungeonBiome;
            var layout = DungeonLayout.Generate(DungeonVariant.For(biome, WorldManager.PendingDungeonSeed),
                Mathf.Max(1, WorldManager.PendingDungeonLevel), WorldManager.PendingDungeonSeed);
            int raised = 0; string first = "";
            for (int x = 0; x < DungeonLayout.Size; x++)
            for (int y = 0; y < DungeonLayout.Size; y++)
            {
                if (!layout.Open(x, y)) continue;
                float h = TerrainHeight.At(new Vector2(x + 0.5f, y + 0.5f));
                if (Mathf.Abs(h) > 0.01f) { if (raised++ == 0) first = $"({x},{y}) at {h:0.##}"; }
            }
            Check.That(raised == 0, $"{raised} floor cells of the dungeon are not flat, first {first}");

            // Run at the walls in every direction: the hero must stay on the floor, at floor height.
            var hero = HeroController.Instance;
            var dirs = new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down, new Vector2(1, 1).normalized, new Vector2(-1, 1).normalized, new Vector2(1, -1).normalized, new Vector2(-1, -1).normalized };
            var spawn = DungeonSetup.Instance.HeroSpawn;
            try
            {
                foreach (var d in dirs)
                {
                    Play.MoveHero(spawn);
                    yield return Play.Wait(0.3f);
                    hero.AutoWalk = d;
                    yield return Play.Wait(2.5f);
                    hero.AutoWalk = Vector2.zero;
                    var p = Play.HeroAt;
                    Check.That(layout.Open(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)), $"Walking {d} from {spawn}, the hero got out of the dungeon to {p}");
                    Check.Near(hero.transform.position.y, 0f, 0.05f, $"height of the hero after walking {d} to {p}");
                }
            }
            finally { hero.AutoWalk = Vector2.zero; }
        }
    }
}
