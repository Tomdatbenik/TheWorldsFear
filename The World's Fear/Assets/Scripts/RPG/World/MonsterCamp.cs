using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // Spawns and respawns a pack of monsters from its zone.
    public class MonsterCamp : MonoBehaviour
    {
        public ZoneDefinition zone;
        [Range(0, 1)] public float depth;   // how deep in the zone: 0 = entrance level, 1 = max level
        public float activateRange = 22f;
        public float despawnRange = 34f;
        public float respawnTime = 90f;
        public int seed;
        [Tooltip("Infinite world: monster level set by distance (0 = use the zone's range).")]
        public int fixedLevel;

        readonly List<MonsterBrain> alive = new List<MonsterBrain>();
        float respawnAt;
        bool spawned;

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null || zone == null) return;
            float d = Vector2.Distance(hero.transform.position, transform.position);
            alive.RemoveAll(m => m == null || m.Combatant.IsDead);

            if (!spawned && d < activateRange && Time.time >= respawnAt)
            {
                MonsterSpawner.SpawnPack(zone, transform.position, depth, alive, new System.Random(seed + (int)(Time.time * 10)), fixedLevel);
                spawned = true;
            }
            else if (spawned && alive.Count == 0)
            {
                spawned = false;
                respawnAt = Time.time + respawnTime;
            }
            else if (spawned && d > despawnRange)
            {
                // Out of sight: remove untouched packs to keep things fast on phones.
                bool untouched = alive.TrueForAll(m => m.Combatant.HPFraction >= 1f);
                if (untouched)
                {
                    foreach (var m in alive) Destroy(m.gameObject);
                    alive.Clear();
                    spawned = false;
                }
            }
        }

        // Chunk unloaded: take the pack with it.
        void OnDestroy()
        {
            foreach (var m in alive)
                if (m != null) Destroy(m.gameObject);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, 2f);
        }
    }
}
