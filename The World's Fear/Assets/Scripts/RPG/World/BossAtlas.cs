using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Boss lair locations sold by the cartographer; bought lairs are drawn on the world map.
    public static class BossAtlas
    {
        const int SearchChunks = 70;   // how far out (in chunks) lairs are looked for

        public static int Price(BossKind k)
        {
            int i = System.Array.IndexOf(WorldManager.BossOrder, k);
            int[] basePrice = { 150, 350, 600, 900, 1500 };
            return (i >= 0 ? basePrice[i] : 500) + Profile.Data.level * 5;
        }

        // The nearest lair of a boss that hasn't been beaten yet.
        public static bool NearestLair(BossKind kind, out Vector2 position)
        {
            position = default;
            float best = float.MaxValue;
            for (int cx = -SearchChunks; cx <= SearchChunks; cx++)
            for (int cy = -SearchChunks; cy <= SearchChunks; cy++)
            {
                if (!WorldGen.HasBossGate(cx, cy, out var cell)) continue;
                Vector2 p = cell;
                if (WorldManager.KindForGate(p) != kind) continue;
                if (Profile.Data.defeatedGates.Contains(WorldManager.GateKey(p))) continue;
                float d = p.magnitude;
                if (d < best) { best = d; position = p; }
            }
            return best < float.MaxValue;
        }

        public static bool Known(BossKind kind, out Vector2 position)
        {
            foreach (var p in Revealed())
                if (WorldManager.KindForGate(p) == kind && !Profile.Data.defeatedGates.Contains(WorldManager.GateKey(p)))
                {
                    position = p;
                    return true;
                }
            position = default;
            return false;
        }

        public static IEnumerable<Vector2> Revealed()
        {
            foreach (var key in Profile.Data.revealedGates)
            {
                var parts = key.Split('_');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                    yield return new Vector2(x, y);
            }
        }

        public static bool Buy(BossKind kind)
        {
            if (Known(kind, out _) || Profile.Data.gold < Price(kind)) return false;
            if (!NearestLair(kind, out var p)) return false;
            Profile.AddGold(-Price(kind));
            Profile.Data.revealedGates.Add(WorldManager.GateKey(p));
            Profile.Save();
            return true;
        }

        // "430 m north-east" from a point.
        public static string Directions(Vector2 from, Vector2 to)
        {
            var d = to - from;
            string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            float a = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 22.5f, 360f);
            return $"{Mathf.RoundToInt(d.magnitude)} m {names[(int)(a / 45f) % 8]}";
        }
    }
}
