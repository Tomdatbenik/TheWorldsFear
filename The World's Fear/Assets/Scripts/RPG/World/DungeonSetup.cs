using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TWF
{
    // Builds the dungeon behind the gate that was entered, from its kind and floor plan: the way out at the start,
    // and the warden with the vault in the room furthest away. The same gate always gives the same layout.
    // Monsters appear as the hero comes near a room, so a large dungeon stays cheap on phones.
    [DefaultExecutionOrder(-80)]
    public class DungeonSetup : MonoBehaviour
    {
        public static DungeonSetup Instance { get; private set; }

        const int Size = DungeonLayout.Size;
        const int MeshBlock = 16;           // floor and walls are split up so nearby torches light each piece
        const int WardenLevelBonus = 2;
        const float WardenHealth = 10f;     // times a normal monster of its level; true bosses have 22
        const float WardenScale = 1.35f;
        const int FlowLimit = 90;           // monsters further than this many steps stop routing to the hero

        List<DungeonLayout.Room> rooms;
        DungeonLayout layout;
        DungeonVariant variant;
        readonly List<DungeonChest> chests = new List<DungeonChest>();
        bool[,] floor;
        int[,] flow;
        System.Random rng;
        int level;
        Biome biome;
        ZoneDefinition zone;
        string gateKey;
        ArtSet art;
        DungeonLayout.Room start, last;
        MonsterBrain warden;
        bool wardenDown;
        float spawnCheck;
        Vector2Int flowFrom = new Vector2Int(-1, -1);
        readonly Queue<Vector2Int> flowQueue = new Queue<Vector2Int>();

        public Vector2 HeroSpawn { get; private set; }

        void Awake()
        {
            Instance = this;
            level = Mathf.Max(1, WorldManager.PendingDungeonLevel);
            biome = WorldManager.PendingDungeonBiome == Biome.Village ? Biome.Ruins : WorldManager.PendingDungeonBiome;
            zone = WorldManager.PendingDungeonZone;
            gateKey = WorldManager.PendingDungeonKey;
            rng = new System.Random(WorldManager.PendingDungeonSeed);
            variant = DungeonVariant.For(biome, WorldManager.PendingDungeonSeed);
            art = ArtSet.Instance;

            layout = DungeonLayout.Generate(variant, level, WorldManager.PendingDungeonSeed);
            floor = layout.floor;
            rooms = layout.rooms;
            start = layout.start;
            last = layout.last;
            flow = new int[Size, Size];
            BuildGeometry();
            BuildBlockers();
            PlaceExit();
            PlaceTorches();
            PlacePillars();
            PlaceChests();

            var boot = FindAnyObjectByType<GameBootstrap>();
            if (boot != null && boot.defaultSpawn != null) boot.defaultSpawn.position = HeroSpawn;
        }

        void Start()
        {
            ZoneLighting.Instance?.SetDungeon(variant.ambient);
            var hero = HeroController.Instance;
            if (hero != null) PointLights.Attach(hero.transform, new Color(1f, 0.85f, 0.68f), 12f, 2.6f, new Vector3(0f, 0f, -2.5f), true);
            GameUI.Instance?.ShowZoneBanner(WorldManager.PendingDungeonName, $"{variant.kind}  ·  Monster level {level}  ·  {chests.Count} chests to find", new Color(0.75f, 0.85f, 1f));
        }

        bool Open(int x, int y) => layout.Open(x, y);

        // ---------- Geometry ----------

        void BuildGeometry()
        {
            var floorMat = StoneQuads.Stone(variant.floor);
            var wallMat = StoneQuads.Stone(variant.wall);
            float wallHeight = variant.wallHeight;
            var blocks = new Dictionary<Vector2Int, (StoneQuads floors, StoneQuads walls)>();

            (StoneQuads floors, StoneQuads walls) Block(int x, int y)
            {
                var key = new Vector2Int(x / MeshBlock, y / MeshBlock);
                if (!blocks.TryGetValue(key, out var b)) blocks[key] = b = (new StoneQuads(), new StoneQuads());
                return b;
            }

            for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                if (!floor[x, y]) continue;
                var b = Block(x, y);
                b.floors.Add(new Vector3(x, y, 0f), Vector3.right, Vector3.up, Vector3.back, 1f, 1f);
                if (!Open(x, y - 1)) b.walls.Add(new Vector3(x, y, 0f), Vector3.right, Vector3.back, Vector3.up, 1f, wallHeight);
                if (!Open(x, y + 1)) b.walls.Add(new Vector3(x, y + 1, 0f), Vector3.right, Vector3.back, Vector3.down, 1f, wallHeight);
                if (!Open(x - 1, y)) b.walls.Add(new Vector3(x, y, 0f), Vector3.up, Vector3.back, Vector3.right, 1f, wallHeight);
                if (!Open(x + 1, y)) b.walls.Add(new Vector3(x + 1, y, 0f), Vector3.up, Vector3.back, Vector3.left, 1f, wallHeight);
            }

            foreach (var kv in blocks)
            {
                kv.Value.floors.Build(transform, $"Floor {kv.Key}", floorMat);
                kv.Value.walls.Build(transform, $"Walls {kv.Key}", wallMat);
            }
        }

        void BuildBlockers()
        {
            var grid = new GameObject("Grid").AddComponent<Grid>();
            grid.transform.SetParent(transform, false);
            var go = new GameObject("Blockers");
            go.transform.SetParent(grid.transform, false);
            go.layer = LayerMask.NameToLayer("Walls");
            var map = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapCollider2D>();
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;

            for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                if (floor[x, y]) continue;
                bool nearFloor = false;
                for (int dx = -2; dx <= 2 && !nearFloor; dx++)
                for (int dy = -2; dy <= 2 && !nearFloor; dy++)
                    nearFloor = Open(x + dx, y + dy);
                if (nearFloor) map.SetTile(new Vector3Int(x, y, 0), WorldTiles.Instance.blocker);
            }
        }

        // ---------- Contents ----------

        void PlaceExit()
        {
            int top = start.rect.yMax;
            int foot = top - DungeonExit.Length;
            int cx = layout.ExitColumn(start);
            AddExit(new Vector2(cx + 0.5f, foot));
            HeroSpawn = new Vector2(cx + 0.5f, foot - 2.5f);
        }

        void AddExit(Vector2 at)
        {
            var go = new GameObject("Dungeon Exit");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.AddComponent<DungeonExit>().wallTexture = variant.wall;
        }

        void PlaceTorches()
        {
            foreach (var room in rooms)
            {
                var r = room.rect;
                int spacing = variant.torchSpacing;
                if (variant.style == DungeonStyle.Caves)
                {
                    for (int x = r.xMin + 2; x < r.xMax - 1; x += spacing) CaveLight(x, r);
                    continue;
                }
                for (int x = r.xMin + 3; x < r.xMax - 2; x += spacing)
                    if (!Open(x, r.yMax)) Torch(new Vector3(x + 0.5f, r.yMax - 0.35f, -2.2f));
                if (!Open(r.xMin - 1, (int)r.center.y)) Torch(new Vector3(r.xMin + 0.35f, r.center.y, -2.2f));
                if (!Open(r.xMax, (int)r.center.y)) Torch(new Vector3(r.xMax - 0.35f, r.center.y, -2.2f));
            }
        }

        // On the highest bit of open floor in this column of the room, against whatever wall stops it.
        void CaveLight(int x, RectInt r)
        {
            for (int y = r.yMax - 1; y >= r.yMin; y--)
                if (Open(x, y) && !Open(x, y + 1))
                {
                    Torch(new Vector3(x + 0.5f, y + 0.65f, -1.6f));
                    return;
                }
        }

        void Torch(Vector3 at)
        {
            var go = new GameObject("Torch");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            GameAssets.CreateWorldSprite("Flame", GameAssets.PixelGlow, Billboard.Create(go.transform), variant.flame, SortOrder.Units + 1, 0.8f);
            var light = PointLights.Attach(go.transform, variant.torch, variant.torchRange, variant.torchIntensity, Vector3.zero, true);
            go.AddComponent<DungeonTorch>().target = light;
        }

        void PlacePillars()
        {
            if (!variant.pillars || art == null || art.pillar == null || art.pillar.Length == 0) return;
            foreach (var room in rooms)
            {
                var r = room.rect;
                if (room.isStart || r.width < 15 || r.height < 13) continue;
                float dx = r.width * 0.28f, dy = r.height * 0.28f;
                foreach (var offset in new[] { new Vector2(-dx, -dy), new Vector2(dx, -dy), new Vector2(-dx, dy), new Vector2(dx, dy) })
                {
                    var set = rng.NextDouble() < 0.3 && art.brokenPillar != null && art.brokenPillar.Length > 0 ? art.brokenPillar : art.pillar;
                    Pillar(set[rng.Next(set.Length)], room.Centre + offset);
                }
            }
        }

        void Pillar(Sprite sprite, Vector2 at)
        {
            var go = new GameObject("Pillar");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.layer = LayerMask.NameToLayer("Walls");
            var bb = Billboard.Create(go.transform, "Visual");
            bb.localScale = Vector3.one * 1.4f;
            var sr = bb.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            RenderAssets.MakeLit(sr);
            go.AddComponent<CircleCollider2D>().radius = 0.6f;
        }

        void PlaceChests()
        {
            var side = new List<DungeonLayout.Room>(rooms);
            side.RemoveAll(r => r.isStart || r.isFinal);
            int wanted = Mathf.Min(side.Count, 3 + rooms.Count / 4 + variant.extraChests);
            for (int i = 0; i < wanted; i++)
            {
                int pick = rng.Next(side.Count);
                var room = side[pick];
                side.RemoveAt(pick);
                AddChest(room, NearestOpen(room.Centre + new Vector2((float)(rng.NextDouble() * 2 - 1) * 2f, room.rect.height * 0.5f - 3f)), false, i);
            }
            AddChest(last, last.Centre + new Vector2(0f, last.rect.height * 0.5f - 3.5f), true, wanted);
        }

        // This spot if it is clear of the walls, otherwise the closest one that is.
        Vector2 NearestOpen(Vector2 at)
        {
            int cx = Mathf.FloorToInt(at.x), cy = Mathf.FloorToInt(at.y);
            bool Clear(int x, int y) => Open(x, y) && Open(x - 1, y) && Open(x + 1, y) && Open(x, y - 1) && Open(x, y + 1);
            if (Clear(cx, cy)) return at;
            for (int radius = 1; radius < 12; radius++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == radius && Clear(cx + dx, cy + dy))
                    return new Vector2(cx + dx + 0.5f, cy + dy + 0.5f);
            return at;
        }

        void AddChest(DungeonLayout.Room room, Vector2 at, bool vault, int index)
        {
            var go = new GameObject(vault ? "Dungeon Vault" : "Dungeon Chest");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var chest = go.AddComponent<DungeonChest>();
            chest.key = $"dgn_{gateKey}_{index}";
            chest.level = level;
            chest.vault = vault;
            chest.roomClear = () => room.Clear;
            chest.opened = OnChestOpened;
            chests.Add(chest);
        }

        void OnChestOpened()
        {
            foreach (var c in chests)
                if (!Profile.Data.openedChests.Contains(c.key)) return;
            Profile.Data.openedChests.Add(DungeonGate.DoneKey(gateKey));
            Profile.Save();
            GameUI.Instance?.Toast("Every chest found. This dungeon is plundered.", Palette.Gold, 4f);
        }

        // ---------- Monsters ----------

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            WatchWarden();
            spawnCheck -= Time.deltaTime;
            if (spawnCheck > 0f) return;
            spawnCheck = 0.3f;
            Vector2 p = hero.transform.position;
            foreach (var room in rooms)
            {
                if (room.spawned || room.isStart) continue;
                var r = room.rect;
                float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
                if (dx * dx + dy * dy < variant.spawnDistance * variant.spawnDistance) Populate(room);
            }
        }

        void Populate(DungeonLayout.Room room)
        {
            room.spawned = true;
            var packRng = new System.Random(rng.Next());
            int packs = Mathf.Clamp(room.rect.width * room.rect.height / variant.areaPerPack, variant.minPacks, variant.maxPacks);
            var roster = variant.Roster(MonsterGenerator.SpeciesFor(zone));
            for (int i = 0; i < packs; i++)
            {
                var at = room.Centre + new Vector2((float)(packRng.NextDouble() * 2 - 1) * Mathf.Max(0f, room.rect.width * 0.5f - 5f), (float)(packRng.NextDouble() * 2 - 1) * Mathf.Max(0f, room.rect.height * 0.5f - 5f));
                MonsterSpawner.SpawnPack(zone, NearestOpen(at), 1f, room.monsters, packRng, level, variant.rareChance, variant.eliteChance, roster, variant.warbandChance);
            }
            if (room.isFinal) SpawnWarden(room, packRng);
            foreach (var m in room.monsters)
            {
                if (m == null) continue;
                m.leashRange = 200f;
                // Small or ragged rooms can leave part of a pack inside the rock.
                Vector2 p = m.transform.position;
                if (Open(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y))) continue;
                m.transform.position = NearestOpen(p);
                m.Home = m.transform.position;
            }
        }

        void SpawnWarden(DungeonLayout.Room room, System.Random r)
        {
            int wardenLevel = level + WardenLevelBonus;
            int pick = r.Next();
            var species = MonsterGenerator.DesignedFor(biome, variant.wardenRole, pick) ?? MonsterGenerator.DesignedFor(biome, MonsterArchetype.Brute, pick);
            if (species == null)
            {
                int seed = r.Next();
                species = MonsterGenerator.Create(seed, biome, MonsterArchetype.Brute);
                species.name = MonsterGenerator.MakeName(new System.Random(seed), biome, MonsterArchetype.Brute, species.isSpider);
            }
            var affixes = MonsterSpawner.RollAffixes(r, 3);
            warden = MonsterBrain.Spawn(species, wardenLevel, MonsterRank.Rare, room.Centre, affixes, zone);
            var c = warden.Combatant;
            c.displayName = variant.wardenTitle + ": " + species.name;
            c.Stats.SetBase(StatType.MaxHP, MonsterGenerator.BaseHP(wardenLevel) * WardenHealth);
            c.ResetLife();
            warden.transform.localScale = Vector3.one * WardenScale;
            warden.Home = room.Centre;
            room.monsters.Add(warden);
            GameUI.Instance?.ShowBossBar(c);
            GameUI.Instance?.Toast($"The {variant.wardenTitle} of this place wakes", new Color(1f, 0.45f, 0.35f), 3f);
        }

        void WatchWarden()
        {
            if (warden == null && !wardenDown && !last.spawned) return;
            if (wardenDown || (warden != null && !warden.Combatant.IsDead)) return;
            wardenDown = true;
            // A way out right here, so nobody has to walk the whole way back.
            int cx = layout.ExitColumn(last);
            AddExit(new Vector2(cx + 0.5f, last.rect.yMax - DungeonExit.Length));
            // Cleared: saved now, so it stays cleared even if the game is closed down here. The gate caves in once the hero is back up.
            var done = DungeonGate.DoneKey(gateKey);
            if (!string.IsNullOrEmpty(gateKey) && !Profile.Data.openedChests.Contains(done))
            {
                Profile.Data.openedChests.Add(done);
                Profile.Save();
            }
            GameUI.Instance?.Toast($"The {variant.wardenTitle} is slain. A way out opens; this place will cave in once you leave, so loot what you can.", Palette.Gold, 5f);
        }

        // ---------- Finding the way around walls ----------

        // Monsters walk straight at the hero; when a wall is in the way, this turns them along the shortest way round.
        public Vector2 Route(Vector2 from, Vector2 desired)
        {
            var hero = HeroController.Instance;
            if (hero == null || desired == Vector2.zero) return desired;
            Vector2 target = hero.transform.position;
            if (!Walls.Blocked(from, target)) return desired;
            RefreshFlow(target);

            int x = Mathf.FloorToInt(from.x), y = Mathf.FloorToInt(from.y);
            if (!Open(x, y) || flow[x, y] <= 0) return desired;
            int best = flow[x, y];
            var bestCell = new Vector2Int(x, y);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (!Open(nx, ny) || flow[nx, ny] <= 0 || flow[nx, ny] >= best) continue;
                if (dx != 0 && dy != 0 && (!Open(x + dx, y) || !Open(x, y + dy))) continue;   // don't cut wall corners
                best = flow[nx, ny];
                bestCell = new Vector2Int(nx, ny);
            }
            if (bestCell.x == x && bestCell.y == y) return desired;
            return (new Vector2(bestCell.x + 0.5f, bestCell.y + 0.5f) - from).normalized * desired.magnitude;
        }

        void RefreshFlow(Vector2 target)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(target.x), Mathf.FloorToInt(target.y));
            if (cell == flowFrom || !Open(cell.x, cell.y)) return;
            flowFrom = cell;
            System.Array.Clear(flow, 0, flow.Length);
            flow[cell.x, cell.y] = 1;
            flowQueue.Clear();
            flowQueue.Enqueue(cell);
            while (flowQueue.Count > 0)
            {
                var c = flowQueue.Dequeue();
                int d = flow[c.x, c.y];
                if (d >= FlowLimit) continue;
                Visit(c.x + 1, c.y, d);
                Visit(c.x - 1, c.y, d);
                Visit(c.x, c.y + 1, d);
                Visit(c.x, c.y - 1, d);
            }
        }

        void Visit(int x, int y, int d)
        {
            if (!Open(x, y) || flow[x, y] != 0) return;
            flow[x, y] = d + 1;
            flowQueue.Enqueue(new Vector2Int(x, y));
        }
    }
}
