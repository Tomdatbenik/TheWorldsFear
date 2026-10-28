using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum Biome { Village, Forest, Marsh, Ruins, Ember }

    public enum MonsterArchetype { Chaser, Charger, Spitter, Swarmling, Brute, Caster, Exploder }

    [CreateAssetMenu(menuName = "TWF/Zone")]
    public class ZoneDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string flavor;
        public Biome biome;
        public int minLevel = 1;
        public int maxLevel = 5;
        [Tooltip("Seed for this zone's generated monster species. Same seed = same monsters.")]
        public int seed = 1;
        public int speciesCount = 5;
        public List<MonsterArchetype> archetypes = new List<MonsterArchetype>();
        [Range(0, 1)] public float eliteChance = 0.12f;
        [Range(0, 1)] public float rareChance = 0.06f;
        public Vector2Int packSize = new Vector2Int(3, 5);
        public Color nameColor = Color.white;
        public bool safeZone;
    }
}
