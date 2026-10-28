using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TWF
{
    [DefaultExecutionOrder(-100)]
    // Tracks which zone the hero is in, owns waypoints and camps, and handles loot drops.
    public class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance { get; private set; }

        public string bossSceneName = "BossArena";

        readonly List<ZoneArea> areas = new List<ZoneArea>();
        readonly List<Waypoint> waypoints = new List<Waypoint>();
        ZoneArea currentArea;
        float checkTimer;

        public IReadOnlyList<Waypoint> Waypoints => waypoints;
        public ZoneDefinition CurrentZone => currentArea != null ? currentArea.zone : null;

        void Awake()
        {
            Instance = this;
            areas.AddRange(FindObjectsByType<ZoneArea>(FindObjectsSortMode.None));
            waypoints.AddRange(FindObjectsByType<Waypoint>(FindObjectsSortMode.None));
            waypoints.Sort((a, b) => a.order.CompareTo(b.order));
        }

        public Waypoint FindWaypoint(string id) => waypoints.Find(w => w.id == id);

        public Vector2 SpawnPoint()
        {
            var wp = FindWaypoint(Profile.Data.lastWaypoint) ?? FindWaypoint("village");
            if (wp != null) return (Vector2)wp.transform.position + Vector2.down * 1.5f;
            return Vector2.zero;
        }

        public ZoneArea AreaAt(Vector2 p)
        {
            foreach (var a in areas)
                if (a.Contains(p)) return a;
            return null;
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 0.4f;

            var area = AreaAt(hero.transform.position);
            if (area != null && area != currentArea)
            {
                currentArea = area;
                var z = area.zone;
                string levels = z.safeZone ? "Safe zone" : $"Level {z.minLevel} - {z.maxLevel}";
                if (GameUI.Instance != null) GameUI.Instance.ShowZoneBanner(z.displayName, levels, z.nameColor);
                if (!z.safeZone && Profile.Data.level + 3 < z.minLevel && GameUI.Instance != null)
                    GameUI.Instance.Toast("Danger! The monsters here are far stronger than you.", new Color(1f, 0.4f, 0.3f), 3f);
                Profile.Save();
            }
        }

        public void Teleport(Waypoint wp)
        {
            var hero = HeroController.Instance;
            if (hero == null || wp == null) return;
            Panels.CloseAll();
            Vector2 to = (Vector2)wp.transform.position + Vector2.down * 1.5f;
            VFX.Burst(hero.transform.position, new Color(0.5f, 0.8f, 1f), 20, 1f);
            hero.GetComponent<Rigidbody2D>().position = to;
            hero.transform.position = to;
            var follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (follow) follow.SnapToTarget();
            VFX.Burst(to, new Color(0.5f, 0.8f, 1f), 20, 1f);
            Profile.Data.lastWaypoint = wp.id;
            Profile.Save();
        }

        public void EnterBoss()
        {
            Profile.Save();
            SceneManager.LoadScene(bossSceneName);
        }
    }
}
