using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum WeaponType { Sword, Axe, Hammer, Daggers, Spear, Bow, Crossbow, Staff, Wand, Fists }

    // How each weapon's basic attack behaves.
    public class WeaponProfile
    {
        public WeaponType type;
        public bool ranged;
        public float range;
        public float arc;            // melee arc in degrees (360 = circle at the target)
        public float attacksPerSecond;
        public float damageMult;
        public int hits = 1;         // daggers strike twice
        public float critBonus;
        public float knockback;
        public StatusSpec status;
        public float projectileSpeed;
        public int pierce;
        public float explodeRadius;
        public bool arrow;
        public bool homing;
        public float projectileSize = 0.4f;
        public Color color = Color.white;
        public float implicitDamage = 1f; // weapon item damage scale
        public string[] tierNames;
    }

    public static class Weapons
    {
        public static readonly Dictionary<WeaponType, WeaponProfile> Profiles = new Dictionary<WeaponType, WeaponProfile>
        {
            { WeaponType.Sword, new WeaponProfile { type = WeaponType.Sword, range = 1.9f, arc = 120f, attacksPerSecond = 1.3f, damageMult = 1f, knockback = 0.4f,
                tierNames = new[] { "Rusty Blade", "Iron Sword", "Steel Broadsword", "Runed Greatsword" } } },
            { WeaponType.Axe, new WeaponProfile { type = WeaponType.Axe, range = 2.1f, arc = 200f, attacksPerSecond = 0.95f, damageMult = 1.15f, knockback = 0.7f, implicitDamage = 1.1f,
                tierNames = new[] { "Hatchet", "Iron Axe", "Battle Axe", "Bloodreaver" } } },
            { WeaponType.Hammer, new WeaponProfile { type = WeaponType.Hammer, range = 1.8f, arc = 360f, attacksPerSecond = 0.7f, damageMult = 1.6f, knockback = 1.2f, implicitDamage = 1.3f,
                status = StatusSpec.Stun(0.7f, 0.25f), tierNames = new[] { "Mallet", "War Hammer", "Maul", "Earthbreaker" } } },
            { WeaponType.Daggers, new WeaponProfile { type = WeaponType.Daggers, range = 1.5f, arc = 90f, attacksPerSecond = 2f, damageMult = 0.55f, hits = 2, critBonus = 0.1f, implicitDamage = 0.8f,
                tierNames = new[] { "Shivs", "Twin Daggers", "Fang Blades", "Shadow Kris" } } },
            { WeaponType.Spear, new WeaponProfile { type = WeaponType.Spear, range = 3f, arc = 35f, attacksPerSecond = 1.15f, damageMult = 1.05f, knockback = 0.5f,
                tierNames = new[] { "Wooden Spear", "Iron Spear", "Halberd", "Dragon Lance" } } },
            { WeaponType.Bow, new WeaponProfile { type = WeaponType.Bow, ranged = true, range = 7f, attacksPerSecond = 1.4f, damageMult = 0.95f, projectileSpeed = 20f,
                arrow = true, homing = true, projectileSize = 0.9f, color = new Color(0.95f, 0.75f, 0.35f),
                tierNames = new[] { "Short Bow", "Hunting Bow", "Composite Bow", "Elderwood Longbow" } } },
            { WeaponType.Crossbow, new WeaponProfile { type = WeaponType.Crossbow, ranged = true, range = 8f, attacksPerSecond = 0.75f, damageMult = 1.7f, projectileSpeed = 28f,
                pierce = 2, arrow = true, projectileSize = 1f, color = new Color(0.8f, 0.8f, 0.85f), implicitDamage = 1.25f,
                tierNames = new[] { "Light Crossbow", "Crossbow", "Arbalest", "Siege Repeater" } } },
            { WeaponType.Staff, new WeaponProfile { type = WeaponType.Staff, ranged = true, range = 6.5f, attacksPerSecond = 0.85f, damageMult = 1.35f, projectileSpeed = 11f,
                explodeRadius = 1.5f, projectileSize = 0.55f, color = new Color(0.75f, 0.5f, 1f), implicitDamage = 1.1f,
                tierNames = new[] { "Gnarled Staff", "Oak Staff", "Crystal Staff", "Starforged Staff" } } },
            { WeaponType.Wand, new WeaponProfile { type = WeaponType.Wand, ranged = true, range = 6f, attacksPerSecond = 2.2f, damageMult = 0.5f, projectileSpeed = 18f,
                homing = true, projectileSize = 0.3f, color = new Color(0.5f, 0.85f, 1f), implicitDamage = 0.75f,
                tierNames = new[] { "Twig Wand", "Bone Wand", "Crystal Wand", "Void Scepter" } } },
            { WeaponType.Fists, new WeaponProfile { type = WeaponType.Fists, range = 1.3f, arc = 90f, attacksPerSecond = 1.4f, damageMult = 0.5f,
                tierNames = new[] { "Fists", "Fists", "Fists", "Fists" } } },
        };

        public static WeaponProfile Get(WeaponType t) => Profiles[t];

        public static WeaponType Equipped
        {
            get
            {
                var w = Profile.Equipped(ItemSlot.Weapon);
                return w != null ? (WeaponType)w.weaponType : WeaponType.Fists;
            }
        }

        // The element of the weapon in your hands (physical when it has none).
        public static ElementPart EquippedElement
        {
            get
            {
                var w = Profile.Equipped(ItemSlot.Weapon);
                return w != null ? (ElementPart)w.element : ElementPart.Physical;
            }
        }

        public static string Name(WeaponType t) => t == WeaponType.Daggers ? "Daggers" : t.ToString();

        // ---------- Generated weapon art (16x16, used in hand, as loot and as icons) ----------

        static readonly Dictionary<WeaponType, Sprite> sprites = new Dictionary<WeaponType, Sprite>();

        public static Sprite Sprite(WeaponType t)
        {
            if (sprites.TryGetValue(t, out var s) && s) return s;
            const int n = 16;
            var px = new Color[n * n];
            Color steel = new Color(0.82f, 0.85f, 0.9f), steelD = new Color(0.55f, 0.58f, 0.66f), wood = new Color(0.55f, 0.36f, 0.2f),
                woodD = new Color(0.38f, 0.24f, 0.13f), gold = new Color(0.95f, 0.78f, 0.3f), gem = new Color(0.6f, 0.4f, 1f), str = new Color(0.92f, 0.9f, 0.82f);
            void P(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < n && y < n) px[y * n + x] = c; }
            void Diag(int x0, int y0, int len, Color c, Color shade) { for (int i = 0; i < len; i++) { P(x0 + i, y0 + i, c); P(x0 + i + 1, y0 + i, shade); } }
            switch (t)
            {
                case WeaponType.Sword:
                    Diag(5, 5, 10, steel, steelD); for (int i = 2; i < 7; i++) P(i, 8 - i + 2, gold); Diag(1, 1, 4, wood, woodD); break;
                case WeaponType.Axe:
                    Diag(2, 2, 12, wood, woodD);
                    for (int y = 9; y < 15; y++) for (int x = 9; x < 14; x++) if ((x - 11) * (x - 11) + (y - 12) * (y - 12) < 9 && x + 1 > y - 3) P(x, y, (x + y) % 5 == 0 ? steelD : steel);
                    break;
                case WeaponType.Hammer:
                    Diag(2, 2, 10, wood, woodD);
                    for (int y = 9; y < 15; y++) for (int x = 8; x < 15; x++) if (Mathf.Abs((x - y)) < 4) P(x, y, (x + y) % 4 == 0 ? steelD : steel);
                    break;
                case WeaponType.Daggers:
                    Diag(3, 3, 6, steel, steelD); P(2, 4, gold); P(4, 2, gold); Diag(1, 1, 2, wood, woodD);
                    Diag(8, 3, 6, steel, steelD); P(7, 4, gold); P(9, 2, gold);
                    break;
                case WeaponType.Spear:
                    Diag(0, 0, 13, wood, woodD); P(13, 13, steel); P(14, 14, steel); P(15, 15, str); P(12, 14, steelD); P(14, 12, steelD); P(13, 14, steel); P(14, 13, steel); break;
                case WeaponType.Bow:
                    for (int y = 1; y < 15; y++) { int x = 4 + Mathf.RoundToInt(Mathf.Sin((y - 1) / 13f * Mathf.PI) * 5f); P(x, y, wood); P(x + 1, y, woodD); P(4, y, str); }
                    break;
                case WeaponType.Crossbow:
                    Diag(2, 2, 11, wood, woodD);
                    for (int i = 0; i < 9; i++) { P(4 + i, 12 - i, steel); }
                    P(8, 8, steelD); P(12, 12, str); break;
                case WeaponType.Staff:
                    Diag(1, 1, 11, wood, woodD);
                    for (int y = 11; y < 16; y++) for (int x = 11; x < 16; x++) if ((x - 13) * (x - 13) + (y - 13) * (y - 13) <= 4) P(x, y, (x == 12 && y == 14) ? Color.white : gem);
                    break;
                case WeaponType.Wand:
                    Diag(3, 3, 8, woodD, wood); P(11, 11, gold); P(12, 12, new Color(0.5f, 0.85f, 1f)); P(13, 13, Color.white); P(12, 11, new Color(0.5f, 0.85f, 1f)); P(11, 12, new Color(0.5f, 0.85f, 1f)); break;
                default:
                    for (int y = 4; y < 11; y++) for (int x = 4; x < 11; x++) P(x, y, new Color(0.9f, 0.7f, 0.55f)); break;
            }
            // Outline.
            var o = (Color[])px.Clone();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                if (px[y * n + x].a > 0f) continue;
                bool near = (x > 0 && px[y * n + x - 1].a > 0f) || (x < n - 1 && px[y * n + x + 1].a > 0f) || (y > 0 && px[(y - 1) * n + x].a > 0f) || (y < n - 1 && px[(y + 1) * n + x].a > 0f);
                if (near) o[y * n + x] = new Color(0.06f, 0.05f, 0.06f);
            }
            var tex = GameAssets.NewTexture(n, n);
            tex.SetPixels(o);
            tex.Apply();
            s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.15f, 0.15f), GameAssets.WorldPPU);
            sprites[t] = s;
            return s;
        }
    }

    public enum Attribute { Strength, Vitality, Dexterity, Swiftness, Magic }

    public static class Attributes
    {
        public static readonly int Count = 5;

        public static string Name(Attribute a) => a.ToString();

        public static string Describe(Attribute a)
        {
            switch (a)
            {
                case Attribute.Strength: return "+1.5 damage, +1% damage";
                case Attribute.Vitality: return "+12 life, +0.15 life regen";
                case Attribute.Dexterity: return "+1.5% attack speed, +0.4% crit";
                case Attribute.Swiftness: return "+1% move speed, +1% projectile speed";
                default: return "+0.4% cooldown reduction, +0.5% area, +1% skill power";
            }
        }

        public static Color Color(Attribute a)
        {
            switch (a)
            {
                case Attribute.Strength: return new Color(1f, 0.45f, 0.35f);
                case Attribute.Vitality: return new Color(0.45f, 0.95f, 0.45f);
                case Attribute.Dexterity: return new Color(1f, 0.85f, 0.35f);
                case Attribute.Swiftness: return new Color(0.5f, 0.85f, 1f);
                default: return new Color(0.75f, 0.55f, 1f);
            }
        }

        public static IEnumerable<StatModifier> Modifiers(Attribute a, int points, object source)
        {
            if (points <= 0) yield break;
            switch (a)
            {
                case Attribute.Strength:
                    yield return new StatModifier(StatType.Damage, ModKind.Flat, 1.5f * points, source);
                    yield return new StatModifier(StatType.Damage, ModKind.Percent, 0.01f * points, source);
                    break;
                case Attribute.Vitality:
                    yield return new StatModifier(StatType.MaxHP, ModKind.Flat, 12f * points, source);
                    yield return new StatModifier(StatType.HPRegen, ModKind.Flat, 0.15f * points, source);
                    break;
                case Attribute.Dexterity:
                    yield return new StatModifier(StatType.AttackSpeed, ModKind.Percent, 0.015f * points, source);
                    yield return new StatModifier(StatType.CritChance, ModKind.Flat, 0.004f * points, source);
                    break;
                case Attribute.Swiftness:
                    yield return new StatModifier(StatType.MoveSpeed, ModKind.Percent, 0.01f * points, source);
                    yield return new StatModifier(StatType.ProjectileSpeed, ModKind.Flat, 0.01f * points, source);
                    break;
                case Attribute.Magic:
                    yield return new StatModifier(StatType.CooldownReduction, ModKind.Flat, 0.004f * points, source);
                    yield return new StatModifier(StatType.AreaSize, ModKind.Flat, 0.005f * points, source);
                    yield return new StatModifier(StatType.SkillPower, ModKind.Flat, 0.01f * points, source);
                    break;
            }
        }
    }
}
