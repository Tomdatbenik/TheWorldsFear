using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TWF
{
    // Tracks the hero's region in the infinite world, waypoints, teleports and the boss arena.
    [DefaultExecutionOrder(-100)]
    public class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance { get; private set; }

        public string bossSceneName = "BossArena";
        public Vector2 villageWaypoint = new Vector2(2f, 3f);

        // Set when walking into a boss gate; read by the boss arena.
        public static int PendingBossLevel = 25;
        public static Biome PendingBossBiome = Biome.Ember;
        public static string PendingGateKey = "";
        public static BossKind PendingBossKind = BossKind.WorldsFear;

        // The bosses in the order you face them. Gates hold them by distance from the village (the
        // nearest gates hold the Slime, the furthest the World's Fear), and each lair stays sealed
        // until the boss before it has been slain.
        public static readonly BossKind[] BossOrder = { BossKind.Slime, BossKind.Spider, BossKind.Zombie, BossKind.Skeleton, BossKind.WorldsFear };
        static readonly float[] bandEnds = { 400f, 560f, 760f, 1000f };

        public static BossKind KindForGate(Vector2 p)
        {
            float d = p.magnitude;
            for (int i = 0; i < bandEnds.Length; i++)
                if (d < bandEnds[i]) return BossOrder[i];
            return BossKind.WorldsFear;
        }

        public static bool Slain(BossKind k) => Profile.Data.guardiansSlain.Contains(k.ToString());

        public static BossKind Previous(BossKind k)
        {
            int i = System.Array.IndexOf(BossOrder, k);
            return i > 0 ? BossOrder[i - 1] : k;
        }

        public static bool IsUnlocked(BossKind k)
        {
            if (k == BossKind.WorldsFear) return GuardiansLeft().Count == 0;
            int i = System.Array.IndexOf(BossOrder, k);
            return i <= 0 || Slain(BossOrder[i - 1]);
        }

        // The World's Fear is the end boss: its lairs stay sealed until all four guardians are slain.
        public static List<BossKind> GuardiansLeft()
        {
            var left = new List<BossKind>();
            foreach (var g in BossTitan.Guardians)
                if (!Profile.Data.guardiansSlain.Contains(g.ToString())) left.Add(g);
            return left;
        }

        public static string GateKey(Vector2 p) => $"{Mathf.RoundToInt(p.x)}_{Mathf.RoundToInt(p.y)}";

        readonly List<Waypoint> waypoints = new List<Waypoint>();
        string currentRegion;
        float checkTimer;

        public IReadOnlyList<Waypoint> Waypoints => waypoints;
        public ZoneDefinition CurrentZone { get; private set; }

        void Awake()
        {
            Instance = this;
            waypoints.AddRange(FindObjectsByType<Waypoint>());
        }

        public void RegisterWaypoint(Waypoint wp)
        {
            waypoints.RemoveAll(w => w == null);
            if (!waypoints.Contains(wp)) waypoints.Add(wp);
        }

        public Waypoint FindWaypoint(string id)
        {
            waypoints.RemoveAll(w => w == null);
            return waypoints.Find(w => w.id == id);
        }

        // Waypoints can be far away in unloaded chunks, so positions come from their ids.
        public Vector2 WaypointPosition(string id)
        {
            if (id == "village" || string.IsNullOrEmpty(id)) return villageWaypoint;
            var parts = id.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int cx) && int.TryParse(parts[2], out int cy)
                && WorldGen.HasWaypoint(cx, cy, out var cell))
                return (Vector2)cell + Vector2.one * 0.5f;
            return villageWaypoint;
        }

        public string WaypointName(string id)
        {
            if (id == "village") return "Ashvale Village";
            var p = WaypointPosition(id);
            return $"{WorldGen.RegionTitle(p)}  <size=70%><color=#ccc>Lv {WorldGen.LevelAt(p)}</color></size>";
        }

        public Vector2 SpawnPoint()
        {
            var d = Profile.Data;
            // Coming back from a boss: appear next to the gate you entered.
            Vector2 p = d.hasReturn ? new Vector2(d.returnX, d.returnY) : WaypointPosition(d.lastWaypoint) + Vector2.down * 1.5f;
            d.hasReturn = false;
            ChunkWorld.Instance?.LoadAround(p);
            return p;
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 0.4f;

            Vector2 pos = hero.transform.position;
            float dist = pos.magnitude;
            if (dist > Profile.Data.farthest) Profile.Data.farthest = dist;

            var zone = WorldGen.RegionAt(pos);
            if (zone == null || zone.id == currentRegion) return;
            currentRegion = zone.id;
            CurrentZone = zone;
            ZoneLighting.Instance?.SetBiome(zone.biome);
            if (GameUI.Instance != null)
            {
                int lvl = WorldGen.LevelAt(pos);
                string sub = zone.safeZone ? "Safe zone" : $"Monster level {lvl}  ·  {Mathf.RoundToInt(dist)}m from the village";
                // The warning is part of the banner so it never overlaps the area name.
                if (!zone.safeZone && Profile.Data.level + 4 < lvl)
                    sub += "\n<color=#ff6650>Danger: these monsters far outlevel you</color>";
                GameUI.Instance.ShowZoneBanner(zone.displayName, sub, zone.nameColor);
            }
            Profile.Save();
        }

        public void Teleport(string waypointId)
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            Panels.CloseAll();
            Vector2 to = WaypointPosition(waypointId) + Vector2.down * 1.5f;
            ChunkWorld.Instance?.LoadAround(to);
            VFX.Burst(hero.transform.position, new Color(0.5f, 0.8f, 1f), 20, 1f);
            hero.GetComponent<Rigidbody2D>().position = to;
            hero.transform.position = to;
            var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (follow) follow.SnapToTarget();
            VFX.Burst(to, new Color(0.5f, 0.8f, 1f), 20, 1f);
            Profile.Data.lastWaypoint = waypointId;
            Profile.Save();
        }

        public void Teleport(Waypoint wp)
        {
            if (wp != null) Teleport(wp.id);
        }

        public void EnterBoss(int level, Vector2 gatePosition)
        {
            PendingBossLevel = level;
            PendingBossBiome = WorldGen.BiomeAt(gatePosition);
            if (PendingBossBiome == Biome.Village) PendingBossBiome = Biome.Ember;
            PendingGateKey = GateKey(gatePosition);
            PendingBossKind = KindForGate(gatePosition);
            Profile.Data.hasReturn = true;
            Profile.Data.returnX = gatePosition.x;
            Profile.Data.returnY = gatePosition.y - 3f;
            Profile.Save();
            SceneManager.LoadScene(bossSceneName);
        }
    }
}
