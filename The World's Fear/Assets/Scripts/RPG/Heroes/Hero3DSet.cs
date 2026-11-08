using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // The hero rendered from the 3D model: 8 directions of sprite strips (96x144 per frame) in Resources/TWF/Hero3D,
    // named <animation>_<direction>.png. Frames are cut once, the first time an animation is used.
    public class Hero3DSet
    {
        const int W = 96, H = 144;
        const float PPU = 72f;             // 144 px = 2 world units, the same height as the drawn characters
        const float FootPivot = 0.01f;     // her soles sit about one pixel above the bottom of the frame

        // How far the character travels per frame of each cycle, in world units. Playing the frames at speed / this keeps
        // a planted foot still on the ground.
        public const float WalkPerFrame = 0.106f;
        public const float RunPerFrame = 0.233f;

        // Counter-clockwise from screen right, 45 degrees apart.
        static readonly string[] Directions = { "right", "up_right", "up", "up_left", "left", "down_left", "down", "down_right" };

        static Hero3DSet female, male;
        readonly string folder;
        readonly Dictionary<string, Sprite[]> strips = new Dictionary<string, Sprite[]>();

        Hero3DSet(string folder) { this.folder = folder; }

        public static Hero3DSet Instance => For(true);

        public static Hero3DSet For(bool isFemale)
        {
            if (isFemale)
            {
                if (female == null && Resources.Load<Texture2D>("TWF/Hero3D/idle_down") != null) female = new Hero3DSet("TWF/Hero3D/");
                return female;
            }
            if (male == null && Resources.Load<Texture2D>("TWF/Hero3D_Male/idle_down") != null) male = new Hero3DSet("TWF/Hero3D_Male/");
            return male;
        }

        public static int DirectionOf(Vector2 screen)
        {
            float a = Mathf.Atan2(screen.y, screen.x) * Mathf.Rad2Deg;
            return Mathf.RoundToInt(Mathf.Repeat(a, 360f) / 45f) % 8;
        }

        public static string AttackFor(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.Hammer: return "smash";
                case WeaponType.Daggers:
                case WeaponType.Spear: return "thrust";
                case WeaponType.Bow: return "shoot";
                case WeaponType.Crossbow: return "aim";
                case WeaponType.Staff:
                case WeaponType.Wand: return "cast";
                case WeaponType.Fists: return "punch";
                default: return "slash";
            }
        }

        // The worn and held pieces, rendered on these same animations. By base item, simplest first (ItemGenerator.BaseTier).
        static readonly string[] Helms = { "leather_cap", "iron_helm", "great_helm", "crown" };
        static readonly string[] Chests = { "padded_vest", "chainmail", "plate", "aegis" };
        static readonly string[] Gloves = { "wraps", "leather_gloves", "gauntlets", "talons" };
        static readonly string[] Boots = { "sandals", "leather_boots", "greaves", "striders" };

        public static string PieceFor(ItemInstance item)
        {
            string[] names;
            switch (item.slot)
            {
                case ItemSlot.Helm: names = Helms; break;
                case ItemSlot.Chest: names = Chests; break;
                case ItemSlot.Gloves: names = Gloves; break;
                case ItemSlot.Boots: names = Boots; break;
                default: return null;
            }
            return names[Mathf.Clamp(ItemGenerator.BaseTier(item), 0, names.Length - 1)];
        }

        public static string WeaponPiece(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.Sword: return "sword";
                case WeaponType.Axe: return "axe";
                case WeaponType.Hammer: return "hammer";
                case WeaponType.Daggers: return "dagger";
                case WeaponType.Spear: return "spear";
                case WeaponType.Bow: return "bow";
                case WeaponType.Crossbow: return "crossbow";
                case WeaponType.Staff: return "staff";
                case WeaponType.Wand: return "wand";
                default: return null;
            }
        }

        // One layer of a piece; empty where the piece has no such strip.
        public Sprite[] GetLayer(string piece, bool front, string animation, int direction)
        {
            string gearFolder = folder.Contains("Male") ? "TWF/Hero3D_Gear/M/" : "TWF/Hero3D_Gear/F/";
            return Strip(gearFolder + piece + (front ? "/front/" : "/back/") + animation + "_" + Directions[direction]);
        }

        public Sprite[] Get(string animation, int direction) => Strip(folder + animation + "_" + Directions[direction]);

        Sprite[] Strip(string key)
        {
            if (strips.TryGetValue(key, out var frames)) return frames;
            var tex = Resources.Load<Texture2D>(key);
            frames = new Sprite[tex != null ? tex.width / W : 0];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Sprite.Create(tex, new Rect(i * W, 0, W, H), new Vector2(0.5f, FootPivot), PPU, 0, SpriteMeshType.FullRect);
            strips[key] = frames;
            return frames;
        }
    }
}
