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
        public bool warbandOnly;      // never in the ordinary small packs
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
    // The eight directions of an animation are copied into one shared sheet: monsters that share a texture are drawn
    // together, so a crowd costs a few textures per kind of monster instead of one per animation and direction.
    public class MobSprites
    {
        const float PPU = 72f;
        static readonly string[] Directions = { "right", "up_right", "up", "up_left", "left", "down_left", "down", "down_right" };

        readonly MobDef def;
        readonly Dictionary<string, Sprite[]> strips = new Dictionary<string, Sprite[]>();
        readonly Dictionary<string, Sprite[]> looks = new Dictionary<string, Sprite[]>();
        readonly Dictionary<string, Texture2D> sheets = new Dictionary<string, Texture2D>();

        public MobSprites(MobDef def)
        {
            this.def = def;
        }

        // How many shared sheets this mob has made (for the tests).
        public int SheetCount => sheets.Count;

        static Texture2D Load(MobDef def, string key) => Resources.Load<Texture2D>("TWF/Mobs/" + def.id + "/" + key);

        // A strip on its own texture: for a portrait or a species' resting look, which should not bring a whole sheet with it.
        public Sprite[] Look(string animation, int direction)
        {
            string key = animation + "_" + Directions[direction];
            if (looks.TryGetValue(key, out var frames)) return frames;
            frames = Cut(Load(def, key), 0);
            looks[key] = frames;
            return frames;
        }

        public Sprite[] Get(string animation, int direction)
        {
            string key = animation + "_" + Directions[direction];
            if (strips.TryGetValue(key, out var frames)) return frames;
            var tex = Load(def, key);
            var sheet = tex != null ? SheetFor(animation, tex) : null;
            if (sheet != null)
            {
                Graphics.CopyTexture(tex, 0, 0, 0, 0, tex.width, tex.height, sheet, 0, 0, 0, direction * def.h);
                int count = tex.width / def.w;
                frames = new Sprite[count];
                for (int i = 0; i < count; i++)
                    frames[i] = Sprite.Create(sheet, new Rect(i * def.w, direction * def.h, def.w, def.h), new Vector2(0.5f, def.pivotY), PPU, 0, SpriteMeshType.FullRect);
                // The strip lives on in the sheet (unless a portrait still shows it).
                if (!looks.ContainsKey(key)) Resources.UnloadAsset(tex);
            }
            else frames = Cut(tex, 0);
            strips[key] = frames;
            return frames;
        }

        Sprite[] Cut(Texture2D tex, int y)
        {
            var frames = new Sprite[tex != null ? tex.width / def.w : 0];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Sprite.Create(tex, new Rect(i * def.w, y, def.w, def.h), new Vector2(0.5f, def.pivotY), PPU, 0, SpriteMeshType.FullRect);
            return frames;
        }

        // The sheet for an animation: eight rows, one per direction. None where the device cannot copy textures, the
        // strips are compressed, or the sheet would be too large: the strips are then used as they are.
        Texture2D SheetFor(string animation, Texture2D strip)
        {
            if (sheets.TryGetValue(animation, out var sheet)) return sheet != null && strip.width <= sheet.width && strip.format == sheet.format ? sheet : null;
            sheet = null;
            int width = strip.width;
            if (def.anims != null)
                foreach (var a in def.anims)
                    if (a.name == animation) width = Mathf.Max(width, a.frames * def.w);
            int height = Directions.Length * def.h;
            bool can = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0
                && !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(strip.format)
                && strip.height == def.h && width <= SystemInfo.maxTextureSize && height <= SystemInfo.maxTextureSize && strip.mipmapCount == 1;
            if (can)
            {
                sheet = new Texture2D(width, height, strip.format, false) { name = def.id + " " + animation, filterMode = strip.filterMode, wrapMode = TextureWrapMode.Clamp };
                var raw = sheet.GetRawTextureData<byte>();
                for (int i = 0; i < raw.Length; i++) raw[i] = 0;
                sheet.Apply(false, true);
            }
            sheets[animation] = sheet;
            return sheet;
        }
    }
}
