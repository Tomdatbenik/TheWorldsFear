using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum PartKind { Shape, Element, Modifier }
    public enum ShapePart { Bolt, Nova, Beam, Burst, Wave, Orb }
    public enum ElementPart { Fire, Frost, Lightning, Poison, Arcane, Physical }
    public enum ModPart { Split, Chain, Pierce, Explode, Echo, Wide, Swift, Lingering }

    // A collected part (a "glyph"). Tiers I-III make it stronger.
    [Serializable]
    public class PartRef
    {
        public PartKind kind;
        public int id;
        public int tier = 1;

        public string Name => SkillParts.PartName(kind, id);
        public string FullName => $"{Name} {SkillParts.Roman(tier)}";
    }

    // A skill the player built from parts.
    [Serializable]
    public class CraftedSkill
    {
        public string uid;
        public ShapePart shape;
        public ElementPart element;
        public List<ModPart> mods = new List<ModPart>();
        public int shapeTier = 1, elementTier = 1;
        public List<int> modTiers = new List<int>();
        public string name;

        public int ModTier(ModPart m)
        {
            int i = mods.IndexOf(m);
            return i >= 0 && i < modTiers.Count ? modTiers[i] : 0;
        }
    }

    public static class SkillParts
    {
        public static string Roman(int t) => t >= 3 ? "III" : t == 2 ? "II" : "I";

        public static string PartName(PartKind k, int id)
        {
            switch (k)
            {
                case PartKind.Shape: return ((ShapePart)id).ToString() + " Glyph";
                case PartKind.Element: return ((ElementPart)id).ToString() + " Essence";
                default: return ((ModPart)id).ToString() + " Rune";
            }
        }

        public static string Describe(PartKind k, int id)
        {
            switch (k)
            {
                case PartKind.Shape:
                    switch ((ShapePart)id)
                    {
                        case ShapePart.Bolt: return "A fast projectile.";
                        case ShapePart.Nova: return "A burst all around you.";
                        case ShapePart.Beam: return "A lightning-fast line that pierces everything.";
                        case ShapePart.Burst: return "A blast on a spot you choose.";
                        case ShapePart.Wave: return "A wide sweep in front of you.";
                        case ShapePart.Orb: return "A slow orb that tears through crowds.";
                    }
                    break;
                case PartKind.Element:
                    switch ((ElementPart)id)
                    {
                        case ElementPart.Fire: return "Sets enemies ablaze.";
                        case ElementPart.Frost: return "Slows enemies.";
                        case ElementPart.Lightning: return "May stun; arcs to a nearby enemy.";
                        case ElementPart.Poison: return "Heavy damage over time.";
                        case ElementPart.Arcane: return "Pure extra damage.";
                        case ElementPart.Physical: return "Knocks enemies back.";
                    }
                    break;
                default:
                    switch ((ModPart)id)
                    {
                        case ModPart.Split: return "More projectiles, or extra blasts.";
                        case ModPart.Chain: return "Hits jump to more enemies.";
                        case ModPart.Pierce: return "Projectiles pass through; areas hit harder.";
                        case ModPart.Explode: return "Hits explode around the target.";
                        case ModPart.Echo: return "The skill casts again a moment later.";
                        case ModPart.Wide: return "Bigger areas and projectiles.";
                        case ModPart.Swift: return "Much shorter cooldown, a bit less damage.";
                        case ModPart.Lingering: return "Leaves a damaging zone behind.";
                    }
                    break;
            }
            return "";
        }

        public static Color ElementColor(ElementPart e)
        {
            switch (e)
            {
                case ElementPart.Fire: return new Color(1f, 0.45f, 0.15f);
                case ElementPart.Frost: return new Color(0.55f, 0.85f, 1f);
                case ElementPart.Lightning: return new Color(1f, 0.9f, 0.35f);
                case ElementPart.Poison: return new Color(0.55f, 0.95f, 0.3f);
                case ElementPart.Arcane: return new Color(0.75f, 0.5f, 1f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        public static string ElementIcon(ElementPart e)
        {
            switch (e)
            {
                case ElementPart.Fire: return "fire";
                case ElementPart.Frost: return "frost";
                case ElementPart.Lightning: return "bolt";
                case ElementPart.Poison: return "poison";
                case ElementPart.Arcane: return "arcane";
                default: return "sword";
            }
        }

        public static string ShapeIcon(ShapePart s)
        {
            switch (s)
            {
                case ShapePart.Bolt: return "dash";
                case ShapePart.Nova: return "nova";
                case ShapePart.Beam: return "bow";
                case ShapePart.Burst: return "meteor";
                case ShapePart.Wave: return "slash";
                default: return "arcane";
            }
        }

        public static string ModIcon(ModPart m)
        {
            switch (m)
            {
                case ModPart.Split: return "rain";
                case ModPart.Chain: return "bolt";
                case ModPart.Pierce: return "bow";
                case ModPart.Explode: return "skull";
                case ModPart.Echo: return "spiral";
                case ModPart.Wide: return "nova";
                case ModPart.Swift: return "up";
                default: return "trap";
            }
        }

        public static string IconFor(PartKind k, int id) =>
            k == PartKind.Shape ? ShapeIcon((ShapePart)id) : k == PartKind.Element ? ElementIcon((ElementPart)id) : ModIcon((ModPart)id);

        static readonly Dictionary<ElementPart, string> adjectives = new Dictionary<ElementPart, string>
        {
            { ElementPart.Fire, "Searing" }, { ElementPart.Frost, "Frozen" }, { ElementPart.Lightning, "Crackling" },
            { ElementPart.Poison, "Venomous" }, { ElementPart.Arcane, "Arcane" }, { ElementPart.Physical, "Brutal" },
        };

        static readonly Dictionary<ModPart, string> nouns = new Dictionary<ModPart, string>
        {
            { ModPart.Split, "Many" }, { ModPart.Chain, "Chains" }, { ModPart.Pierce, "Piercing" }, { ModPart.Explode, "Ruin" },
            { ModPart.Echo, "Echoes" }, { ModPart.Wide, "Expanse" }, { ModPart.Swift, "Haste" }, { ModPart.Lingering, "Lingering" },
        };

        public static string NameFor(ShapePart s, ElementPart e, List<ModPart> mods)
        {
            string shape = s == ShapePart.Burst ? "Blast" : s.ToString();
            string n = $"{adjectives[e]} {shape}";
            if (mods.Count == 1) n += $" of {nouns[mods[0]]}";
            else if (mods.Count >= 2) n += $" of {nouns[mods[0]]} and {nouns[mods[1]]}";
            return n;
        }

        public const int CraftCostBase = 60;
        public static int CraftCost(int level, int mods) => CraftCostBase + level * 20 + mods * 40;
        public const int MaxCrafted = 6;
        public static int MaxMods(int level) => level >= 15 ? 3 : 2;

        // A random part drop; deeper in the world means higher tiers.
        public static PartRef Roll(System.Random rng, int monsterLevel, MonsterRank rank)
        {
            double r = rng.NextDouble();
            var kind = r < 0.3 ? PartKind.Shape : r < 0.6 ? PartKind.Element : PartKind.Modifier;
            int count = kind == PartKind.Shape ? 6 : kind == PartKind.Element ? 6 : 8;
            int tier = 1 + (monsterLevel >= 15 ? 1 : 0) + (monsterLevel >= 35 ? 1 : 0);
            if (rank >= MonsterRank.Rare && rng.NextDouble() < 0.4) tier++;
            return new PartRef { kind = kind, id = rng.Next(count), tier = Mathf.Clamp(tier, 1, 3) };
        }
    }
}
