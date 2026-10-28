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
            var p = WaypointPosition(Profile.Data.lastWaypoint) + Vector2.down * 1.5f;
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
                GameUI.Instance.ShowZoneBanner(zone.displayName, sub, zone.nameColor);
                if (!zone.safeZone && Profile.Data.level + 4 < lvl)
                    GameUI.Instance.Toast("Danger! The monsters here are far stronger than you.", new Color(1f, 0.4f, 0.3f), 3f);
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

        public void EnterBoss(int level)
        {
            PendingBossLevel = level;
            Profile.Save();
            SceneManager.LoadScene(bossSceneName);
        }
    }
}
