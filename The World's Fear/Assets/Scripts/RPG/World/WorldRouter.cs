using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Paths round walls, cliffs, water and lava in the open world, worked out on the fly: nothing is baked. The world
    // is a tile grid the generator can answer for any position (WorldGen.CellAt), so a breadth-first flow field
    // spreads out from a target over a window of tiles round it, and a monster steps to whichever neighbouring tile
    // is closer to the target. Only used when a wall really stands between a monster and where it wants to go.
    public static class WorldRouter
    {
        const int Window = 72;          // tiles across the window a field covers (the target in its middle)
        const int Limit = 2600;         // tiles a field may spread over before it stops
        const int MaxFields = 12;       // fields kept (the hero's, and the homes monsters walk back to)

        class Field
        {
            public Vector2Int target, origin;
            public readonly int[] dist = new int[Window * Window];
            public float lastUsed;
        }

        static readonly List<Field> fields = new List<Field>();
        static readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();
        static int seed = int.MinValue;

        // The direction to move from `from` toward `target` (same length as desired), or desired itself when the way is
        // open, the target is out of reach of a field, or the monster is already in the target's tile.
        public static Vector2 Route(Vector2 from, Vector2 target, Vector2 desired)
        {
            if (desired == Vector2.zero || !Walls.Blocked(from, target)) return desired;
            var f = FieldFor(Cell(target));
            if (f == null) return desired;
            var c = Cell(from);
            int here = Dist(f, c.x, c.y);
            if (here <= 0) return desired;      // outside the field or on a blocked tile: carry on as before
            int best = here;
            var step = c;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int d = Dist(f, c.x + dx, c.y + dy);
                if (d <= 0 || d >= best) continue;
                if (dx != 0 && dy != 0)
                {
                    // no wall corners, and no cutting across a plateau's edge
                    if (Dist(f, c.x + dx, c.y) <= 0 || Dist(f, c.x, c.y + dy) <= 0) continue;
                    if (!WorldGen.CanStep(c.x, c.y, c.x + dx, c.y) || !WorldGen.CanStep(c.x + dx, c.y, c.x + dx, c.y + dy)
                        || !WorldGen.CanStep(c.x, c.y, c.x, c.y + dy) || !WorldGen.CanStep(c.x, c.y + dy, c.x + dx, c.y + dy)) continue;
                }
                else if (!WorldGen.CanStep(c.x, c.y, c.x + dx, c.y + dy)) continue;
                best = d;
                step = new Vector2Int(c.x + dx, c.y + dy);
            }
            if (step == c) return desired;
            return (new Vector2(step.x + 0.5f, step.y + 0.5f) - from).normalized * desired.magnitude;
        }

        static Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

        static int Dist(Field f, int x, int y)
        {
            int lx = x - f.origin.x, ly = y - f.origin.y;
            if (lx < 0 || ly < 0 || lx >= Window || ly >= Window) return 0;
            return f.dist[ly * Window + lx];
        }

        static bool Open(int x, int y)
        {
            var c = WorldGen.CellAt(x, y);
            return WorldGen.Walkable(c) || c == TWF.Cell.Plateau || c == TWF.Cell.Stairs;
        }

        static Field FieldFor(Vector2Int target)
        {
            if (seed != WorldGen.Seed) { fields.Clear(); seed = WorldGen.Seed; }
            foreach (var f in fields)
                if (f.target == target) { f.lastUsed = Time.time; return f; }
            if (!Open(target.x, target.y)) return null;
            Field field;
            if (fields.Count < MaxFields) fields.Add(field = new Field());
            else
            {
                field = fields[0];
                foreach (var f in fields) if (f.lastUsed < field.lastUsed) field = f;
            }
            Build(field, target);
            return field;
        }

        static void Build(Field f, Vector2Int target)
        {
            f.target = target;
            f.origin = target - new Vector2Int(Window / 2, Window / 2);
            f.lastUsed = Time.time;
            System.Array.Clear(f.dist, 0, f.dist.Length);
            Set(f, target.x, target.y, 1);
            queue.Clear();
            queue.Enqueue(target);
            int seen = 0;
            while (queue.Count > 0 && seen++ < Limit)
            {
                var c = queue.Dequeue();
                int d = Dist(f, c.x, c.y);
                Visit(f, c, c.x + 1, c.y, d);
                Visit(f, c, c.x - 1, c.y, d);
                Visit(f, c, c.x, c.y + 1, d);
                Visit(f, c, c.x, c.y - 1, d);
            }
        }

        static void Visit(Field f, Vector2Int from, int x, int y, int d)
        {
            int lx = x - f.origin.x, ly = y - f.origin.y;
            if (lx < 0 || ly < 0 || lx >= Window || ly >= Window) return;
            int i = ly * Window + lx;
            if (f.dist[i] != 0) return;
            if (!Open(x, y)) { f.dist[i] = -1; return; }
            if (!WorldGen.CanStep(from.x, from.y, x, y)) return;      // reachable another way, perhaps
            f.dist[i] = d + 1;
            queue.Enqueue(new Vector2Int(x, y));
        }

        static void Set(Field f, int x, int y, int v) => f.dist[(y - f.origin.y) * Window + (x - f.origin.x)] = v;
    }
}
