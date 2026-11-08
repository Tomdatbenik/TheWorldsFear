using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // One entry of Resources/TWF/Mobs/catalog.json, written by pixelart-gen/tools/mob_make.py. Zero in a stat means "use the role's default".
    [Serializable]
    public class MobAnimInfo
    {
        public string name;
        public int frames;
    }

    [Serializable]
    public class MobDef
    {
        public string id, name, biome, role, body, size;
        public int w, h;
        public float pivotY, heightUnits, walkPerFrame, runPerFrame;
        public float hp, damage, armor, speed, range, cooldown, keepAway, projectileSpeed, pack;
        public string status, tint;
        public string faction;        // goblin, undead, ...: fights as an organised warband
        public bool leader;           // the warband's commander
        public MobAnimInfo[] anims;

        [NonSerialized] public MobSprites sprites;

        public bool HasAnim(string a)
        {
            if (anims == null) return false;
            foreach (var i in anims) if (i.name == a) return true;
            return false;
        }

        public Biome BiomeValue => Enum.TryParse(biome, out Biome b) ? b : Biome.Forest;
        public MonsterArchetype RoleValue => Enum.TryParse(role, out MonsterArchetype r) ? r : MonsterArchetype.Chaser;
    }

    [Serializable]
    class MobCatalogData
    {
        public MobDef[] mobs;
    }

    // The monsters rendered from 3D models: designed mobs listed in a catalogue, so a new one needs no code.
    public static class MobCatalog
    {
        static Dictionary<string, MobDef> byId;

        static void Load()
        {
            if (byId != null) return;
            byId = new Dictionary<string, MobDef>();
            var text = Resources.Load<TextAsset>("TWF/Mobs/catalog");
            if (text == null) return;
            var data = JsonUtility.FromJson<MobCatalogData>(text.text);
            if (data?.mobs == null) return;
            foreach (var m in data.mobs)
                if (m != null && !string.IsNullOrEmpty(m.id)) byId[m.id] = m;
        }

        public static IEnumerable<MobDef> All
        {
            get { Load(); return byId.Values; }
        }

        public static MobDef Get(string id)
        {
            Load();
            return id != null && byId.TryGetValue(id, out var d) ? d : null;
        }

        public static List<MobDef> ForBiome(Biome biome)
        {
            Load();
            var list = new List<MobDef>();
            foreach (var d in byId.Values)
                if (d.BiomeValue == biome) list.Add(d);
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list;
        }

        // A stable number from an id (string.GetHashCode changes between runs).
        public static int StableSeed(string id)
        {
            int h = 23;
            foreach (char c in id) h = unchecked(h * 31 + c);
            return h;
        }
    }

    // Sprite strips of one mob, named <animation>_<direction>.png, cut the first time each is used.
    public class MobSprites
    {
        const float PPU = 72f;
        static readonly string[] Directions = { "right", "up_right", "up", "up_left", "left", "down_left", "down", "down_right" };

        readonly MobDef def;
        readonly Dictionary<string, Sprite[]> strips = new Dictionary<string, Sprite[]>();

        public MobSprites(MobDef def)
        {
            this.def = def;
        }

        public Sprite[] Get(string animation, int direction)
        {
            string key = animation + "_" + Directions[direction];
            if (strips.TryGetValue(key, out var frames)) return frames;
            var tex = Resources.Load<Texture2D>("TWF/Mobs/" + def.id + "/" + key);
            frames = new Sprite[tex != null ? tex.width / def.w : 0];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Sprite.Create(tex, new Rect(i * def.w, 0, def.w, def.h), new Vector2(0.5f, def.pivotY), PPU, 0, SpriteMeshType.FullRect);
            strips[key] = frames;
            return frames;
        }
    }
}
