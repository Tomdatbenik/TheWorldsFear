using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Central content registry, loaded from Resources/TWF/GameDatabase.
    [CreateAssetMenu(menuName = "TWF/Game Database")]
    public class GameDatabase : ScriptableObject
    {
        static GameDatabase instance;

        public static GameDatabase Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<GameDatabase>("TWF/GameDatabase");
                return instance;
            }
        }

        public List<HeroClassDefinition> classes = new List<HeroClassDefinition>();
        public List<ZoneDefinition> zones = new List<ZoneDefinition>();
        public List<AffixDefinition> affixes = new List<AffixDefinition>();

        [Tooltip("Hand-drawn monsters added to a biome's generated species.")]
        public List<HandmadeMonster> handmadeMonsters = new List<HandmadeMonster>();

        public GameObject heroPrefab;

        public HeroClassDefinition FindClass(string id) => classes.Find(c => c != null && c.id == id);
        public ZoneDefinition FindZone(string id) => zones.Find(z => z != null && z.id == id);
    }

    [System.Serializable]
    public class HandmadeMonster
    {
        public string name;
        public Biome biome;
        public MonsterArchetype archetype;
        public Sprite[] frames;
        public Color tint = Color.white;
        public float scale = 1f;
    }

    public static class HeroVisuals
    {
        public static Sprite ArrowSprite => CharacterSpriteGenerator.Arrow();
    }
}
