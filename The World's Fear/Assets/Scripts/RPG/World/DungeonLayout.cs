using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // The floor plan of a dungeon: which cells are open and where the rooms are. Holds no scene objects.
    // The way out sits against the top wall of the first room and, after the warden, of the last room.
    public class DungeonLayout
    {
        public const int Size = 100;
        const int Attempts = 20;
        const int FinalExitOffset = 7;     // the second way out, this far left of the last room's centre

        public class Room
        {
            public RectInt rect;
            public bool isStart, isFinal, spawned;
            public readonly List<MonsterBrain> monsters = new List<MonsterBrain>();
            public Vector2 Centre => rect.center;

            public bool Clear
            {
                get
                {
                    if (isStart) return true;
                    if (!spawned) return false;
                    monsters.RemoveAll(m => m == null || m.Combatant.IsDead);
                    return monsters.Count == 0;
                }
            }
        }

        public readonly bool[,] floor = new bool[Size, Size];
        public readonly List<Room> rooms = new List<Room>();
        public Room start, last;

        readonly DungeonVariant variant;
        readonly System.Random rng;
        readonly int level;

        DungeonLayout(DungeonVariant variant, int level, int seed)
        {
            this.variant = variant;
            this.level = level;
            rng = new System.Random(seed);
        }

        // The same seed always gives the same plan. A plan that fails the checks is thrown away for the next one.
        public static DungeonLayout Generate(DungeonVariant variant, int level, int seed)
        {
            DungeonLayout plan = null;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                plan = new DungeonLayout(variant, level, seed + attempt * 7919);
                plan.Build();
                if (plan.Valid()) return plan;
            }
            Debug.LogError($"No valid {variant.kind} layout for seed {seed}");
            return plan;
        }

        public bool Open(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && floor[x, y];

        public int ExitColumn(Room room) => Mathf.RoundToInt(room.Centre.x) - (room.isFinal ? FinalExitOffset : 0);

        void Build()
        {
            if (variant.style == DungeonStyle.Keep) PlaceKeep();
            else
            {
                PlaceRooms();
                ConnectRooms();
            }
            if (variant.style == DungeonStyle.Caves) Roughen();
            SealExit(start);
            SealExit(last);
        }

        // ---------- Rooms ----------

        void PlaceRooms()
        {
            int count = variant.RoomCount(level);
            start = AddRoom(new RectInt(Size / 2 - 8, 6, 16, 12), false);
            start.isStart = true;
            last = AddRoom(new RectInt(Size / 2 - 11 + rng.Next(-24, 25), Size - 28, 22, 20), false);
            last.isFinal = true;

            bool caves = variant.style == DungeonStyle.Caves;
            for (int tries = 0; tries < 1200 && rooms.Count < count; tries++)
            {
                int w = rng.Next(variant.roomWidth.x, variant.roomWidth.y), h = rng.Next(variant.roomHeight.x, variant.roomHeight.y);
                var r = new RectInt(rng.Next(6, Size - w - 6), rng.Next(8, Size - h - 8), w, h);
                if (!Overlaps(r)) AddRoom(r, caves);
            }
        }

        Room AddRoom(RectInt r, bool rounded)
        {
            var room = new Room { rect = r };
            rooms.Add(room);
            float halfW = r.width * 0.5f, halfH = r.height * 0.5f;
            for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
            {
                float dx = (x + 0.5f - r.center.x) / halfW, dy = (y + 0.5f - r.center.y) / halfH;
                if (!rounded || dx * dx + dy * dy < 0.82f + (float)rng.NextDouble() * 0.3f) floor[x, y] = true;
            }
            return room;
        }

        bool Overlaps(RectInt r)
        {
            int gap = variant.roomGap;
            var padded = new RectInt(r.x - gap, r.y - gap, r.width + 2 * gap, r.height + 2 * gap);
            foreach (var other in rooms)
                if (padded.Overlaps(other.rect)) return true;
            return false;
        }

        // A planned fortress: three columns of rooms, mirrored left and right, with straight corridors between them.
        void PlaceKeep()
        {
            int[] columns = { 18, 50, 82 };
            int[] rows = { 12, 36, 58, 82 };
            int count = variant.RoomCount(level);
            int flankRows = count >= 12 ? 4 : count >= 9 ? 3 : 2;

            start = AddRoom(new RectInt(Size / 2 - 8, 6, 16, 12), false);
            start.isStart = true;
            last = AddRoom(new RectInt(Size / 2 - 11, Size - 28, 22, 20), false);
            last.isFinal = true;

            var left = new Room[rows.Length];
            var right = new Room[rows.Length];
            var middle = new Room[rows.Length];
            middle[0] = start;
            middle[3] = last;
            middle[1] = AddRoom(Centred(columns[1], rows[1], 24, 16), false);
            middle[2] = AddRoom(Centred(columns[1], rows[2], 18, 14), false);

            for (int row = 0; row < flankRows; row++)
            {
                int w = 12 + 2 * rng.Next(4), h = 11 + 2 * rng.Next(3);
                if ((h & 1) == 1) h++;
                left[row] = AddRoom(Centred(columns[0], rows[row], w, h), false);
                right[row] = AddRoom(Centred(columns[2], rows[row], w, h), false);
            }

            for (int row = 0; row < rows.Length; row++)
            {
                if (left[row] != null) Connect(middle[row], left[row]);
                if (right[row] != null) Connect(middle[row], right[row]);
                if (row == rows.Length - 1) break;
                if (left[row] != null && left[row + 1] != null) Connect(left[row], left[row + 1]);
                if (right[row] != null && right[row + 1] != null) Connect(right[row], right[row + 1]);
                if (row > 0) Connect(middle[row], middle[row + 1]);
            }
        }

        static RectInt Centred(int x, int y, int w, int h) => new RectInt(x - w / 2, y - h / 2, w, h);

        // ---------- Corridors ----------

        // Every room joins the nearest room already connected, then a few extra links make loops.
        void ConnectRooms()
        {
            var joined = new List<Room> { start };
            var waiting = new List<Room>(rooms);
            waiting.Remove(start);
            while (waiting.Count > 0)
            {
                Room bestA = null, bestB = null;
                float best = float.MaxValue;
                foreach (var a in joined)
                foreach (var b in waiting)
                {
                    float d = Vector2.Distance(a.Centre, b.Centre);
                    if (d < best) { best = d; bestA = a; bestB = b; }
                }
                Connect(bestA, bestB);
                joined.Add(bestB);
                waiting.Remove(bestB);
            }

            int loops = variant.extraLinks + rng.Next(2);
            for (int tries = 0; tries < 40 && loops > 0; tries++)
            {
                var a = rooms[rng.Next(rooms.Count)];
                var b = rooms[rng.Next(rooms.Count)];
                if (a == b || a.isStart || b.isStart || Vector2.Distance(a.Centre, b.Centre) > 45f) continue;
                Connect(a, b);
                loops--;
            }
        }

        void Connect(Room a, Room b)
        {
            var from = Vector2Int.RoundToInt(a.Centre);
            var to = Vector2Int.RoundToInt(b.Centre);
            if (a.isStart || b.isStart)
            {
                // Leave the start room through its side, keeping the wall behind the stairs whole.
                var startRoom = a.isStart ? a : b;
                var other = a.isStart ? b : a;
                int side = other.Centre.x >= startRoom.Centre.x ? startRoom.rect.xMax + 3 : startRoom.rect.xMin - 5;
                var door = Vector2Int.RoundToInt(startRoom.Centre);
                var outside = new Vector2Int(side, door.y);
                var target = Vector2Int.RoundToInt(other.Centre);
                Carve(door, outside);
                Path(outside, target);
                return;
            }
            Path(from, to);
        }

        void Path(Vector2Int from, Vector2Int to)
        {
            if (variant.style == DungeonStyle.Caves) { Wander(from, to); return; }
            var corner = variant.style == DungeonStyle.Keep || rng.Next(2) == 0 ? new Vector2Int(to.x, from.y) : new Vector2Int(from.x, to.y);
            Carve(from, corner);
            Carve(corner, to);
        }

        // A straight run along one axis.
        void Carve(Vector2Int from, Vector2Int to)
        {
            var p = from;
            var step = new Vector2Int(System.Math.Sign(to.x - from.x), System.Math.Sign(to.y - from.y));
            while (true)
            {
                Brush(p);
                if (p == to) break;
                p += step;
            }
        }

        void Brush(Vector2Int p)
        {
            for (int x = 0; x < variant.corridorWidth; x++)
            for (int y = 0; y < variant.corridorWidth; y++)
                if (x + p.x >= 2 && y + p.y >= 2 && x + p.x < Size - 2 && y + p.y < Size - 2) floor[x + p.x, y + p.y] = true;
        }

        // A passage that drifts from side to side on its way.
        void Wander(Vector2Int from, Vector2Int to)
        {
            var p = from;
            for (int guard = 0; p != to && guard < 500; guard++)
            {
                Brush(p);
                int dx = to.x - p.x, dy = to.y - p.y, far = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (rng.NextDouble() < 0.25)
                {
                    if (Mathf.Abs(dx) > Mathf.Abs(dy)) p.y = Mathf.Clamp(p.y + (rng.Next(2) * 2 - 1), 4, Size - 8);
                    else p.x = Mathf.Clamp(p.x + (rng.Next(2) * 2 - 1), 4, Size - 8);
                }
                else if (rng.Next(far) < Mathf.Abs(dx)) p.x += System.Math.Sign(dx);
                else p.y += System.Math.Sign(dy);
            }
            var corner = new Vector2Int(to.x, p.y);
            Carve(p, corner);
            Carve(corner, to);
        }

        // ---------- Finishing ----------

        // Eats away at the edges so caves don't keep the straight lines of their first shapes.
        void Roughen()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var open = new List<Vector2Int>();
                for (int x = 3; x < Size - 3; x++)
                for (int y = 3; y < Size - 3; y++)
                {
                    if (floor[x, y]) continue;
                    int near = 0;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (Open(x + dx, y + dy)) near++;
                    if (near >= 7 || (near >= 2 && rng.NextDouble() < 0.3)) open.Add(new Vector2Int(x, y));
                }
                foreach (var c in open) floor[c.x, c.y] = true;
            }
        }

        // The wall behind a flight of steps stays solid, whatever was dug near it.
        void SealExit(Room room)
        {
            int cx = ExitColumn(room);
            for (int x = cx - 3; x <= cx + 4; x++) floor[x, room.rect.yMax] = false;
        }

        bool Valid()
        {
            var seen = new bool[Size, Size];
            var queue = new Queue<Vector2Int>();
            var first = Vector2Int.FloorToInt(start.Centre);
            if (!Open(first.x, first.y)) return false;
            seen[first.x, first.y] = true;
            queue.Enqueue(first);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in new[] { c + Vector2Int.up, c + Vector2Int.down, c + Vector2Int.left, c + Vector2Int.right })
                {
                    if (!Open(n.x, n.y) || seen[n.x, n.y]) continue;
                    seen[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            foreach (var room in rooms)
            {
                var c = Vector2Int.FloorToInt(room.Centre);
                if (!Open(c.x, c.y) || !seen[c.x, c.y]) return false;
            }
            return rooms.Count >= variant.minRooms - 1;
        }
    }
}
