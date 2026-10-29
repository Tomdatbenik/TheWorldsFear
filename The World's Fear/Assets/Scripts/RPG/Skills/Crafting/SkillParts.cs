using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Skills are crafted: Element -> Delivery -> Modifiers.
    public enum PartKind { Element, Delivery, Modifier }
    public enum ElementPart { Fire, Frost, Lightning, Poison, Arcane, Physical }
    public enum DeliveryPart { Forward, Single, Pierce, Explosion, Nova, Wave, Blink, Dash }
    public enum ModPart { Speed, Split, Chain, Echo, Wide, Lingering, Swift, Homing, Heavy }

    // A collected part ("glyph"). Tiers I-III make it stronger but need more attributes.
    [Serializable]
    public class PartRef
    {
        public PartKind kind;
        public int id;
        public int tier = 1;

        public string Name => SkillParts.PartName(kind, id);
        public string FullName => $"{Name} {SkillParts.Roman(tier)}";
    }

    [Serializable]
    public class CraftedSkill
    {
        public string uid;
        public ElementPart element;
        public DeliveryPart delivery;
        public List<ModPart> mods = new List<ModPart>();
        public int elementTier = 1, deliveryTier = 1;
        public List<int> modTiers = new List<int>();
        public string name;

        public int ModTier(ModPart m)
        {
            int i = mods.IndexOf(m);
            return i >= 0 && i < modTiers.Count ? modTiers[i] : 0;
        }

        public bool Has(ModPart m) => mods.Contains(m);
    }

    public struct Requirement
    {
        public Attribute attribute;
        public int amount;

        public bool Met => Profile.Attr(attribute) >= amount;
        public override string ToString() => $"{amount} {attribute}";
    }

    public static class SkillParts
    {
        public static string Roman(int t) => t >= 3 ? "III" : t == 2 ? "II" : "I";

        public static int Count(PartKind k) => k == PartKind.Element ? 6 : k == PartKind.Delivery ? 8 : 9;

        public static string PartName(PartKind k, int id)
        {
            switch (k)
            {
                case PartKind.Element: return ((ElementPart)id).ToString();
                case PartKind.Delivery: return ((DeliveryPart)id).ToString();
                default: return ((ModPart)id).ToString();
            }
        }

        public static string Describe(PartKind k, int id)
        {
            switch (k)
            {
                case PartKind.Element:
                    switch ((ElementPart)id)
                    {
                        case ElementPart.Fire: return "Sets enemies on fire.";
                        case ElementPart.Frost: return "Slows enemies down.";
                        case ElementPart.Lightning: return "Arcs to a nearby enemy and may stun.";
                        case ElementPart.Poison: return "Heavy damage over time.";
                        case ElementPart.Arcane: return "Pure extra damage.";
                        default: return "Knocks enemies back.";
                    }
                case PartKind.Delivery:
                    switch ((DeliveryPart)id)
                    {
                        case DeliveryPart.Forward: return "Shoots a projectile forward.";
                        case DeliveryPart.Single: return "Instantly strikes one enemy.";
                        case DeliveryPart.Pierce: return "A fast line through every enemy.";
                        case DeliveryPart.Explosion: return "A blast on the spot you aim at.";
                        case DeliveryPart.Nova: return "A burst all around you.";
                        case DeliveryPart.Wave: return "A wide cone in front of you.";
                        case DeliveryPart.Blink: return "Teleport, releasing the element where you land.";
                        default: return "Charge forward, hitting everything in the way.";
                    }
                default:
                    switch ((ModPart)id)
                    {
                        case ModPart.Speed: return "Faster projectiles and shorter cast time.";
                        case ModPart.Split: return "Extra projectiles or extra blasts.";
                        case ModPart.Chain: return "Hits jump to more enemies.";
                        case ModPart.Echo: return "Casts itself again a moment later.";
                        case ModPart.Wide: return "Bigger area and projectiles.";
                        case ModPart.Lingering: return "Leaves a damaging zone behind.";
                        case ModPart.Swift: return "Much shorter cooldown, a bit less damage.";
                        case ModPart.Homing: return "Projectiles seek enemies.";
                        default: return "Much more damage, longer cooldown.";
                    }
            }
        }

        // Attribute requirements (tier II/III need more).
        public static Requirement[] Requirements(PartKind k, int id, int tier)
        {
            int scale = tier <= 1 ? 1 : tier == 2 ? 2 : 3;
            Requirement R(Attribute a, int n) => new Requirement { attribute = a, amount = n * scale };
            switch (k)
            {
                case PartKind.Element:
                    switch ((ElementPart)id)
                    {
                        case ElementPart.Physical: return tier > 1 ? new[] { R(Attribute.Strength, 3) } : new Requirement[0];
                        case ElementPart.Arcane: return new[] { R(Attribute.Magic, 4) };
                        case ElementPart.Lightning: return new[] { R(Attribute.Magic, 3), R(Attribute.Dexterity, 2) };
                        case ElementPart.Poison: return new[] { R(Attribute.Dexterity, 3) };
                        default: return new[] { R(Attribute.Magic, 2) };
                    }
                case PartKind.Delivery:
                    switch ((DeliveryPart)id)
                    {
                        case DeliveryPart.Forward:
                        case DeliveryPart.Single: return tier > 1 ? new[] { R(Attribute.Dexterity, 2) } : new Requirement[0];
                        case DeliveryPart.Pierce: return new[] { R(Attribute.Dexterity, 6) };
                        case DeliveryPart.Explosion: return new[] { R(Attribute.Magic, 6) };
                        case DeliveryPart.Nova: return new[] { R(Attribute.Magic, 4), R(Attribute.Vitality, 2) };
                        case DeliveryPart.Wave: return new[] { R(Attribute.Strength, 4) };
                        case DeliveryPart.Blink: return new[] { R(Attribute.Magic, 10) };
                        default: return new[] { R(Attribute.Swiftness, 8) };
                    }
                default:
                    switch ((ModPart)id)
                    {
                        case ModPart.Speed: return new[] { R(Attribute.Swiftness, 5) };
                        case ModPart.Split: return new[] { R(Attribute.Dexterity, 10) };
                        case ModPart.Chain: return new[] { R(Attribute.Magic, 12) };
                        case ModPart.Echo: return new[] { R(Attribute.Magic, 15) };
                        case ModPart.Wide: return new[] { R(Attribute.Magic, 5) };
                        case ModPart.Lingering: return new[] { R(Attribute.Vitality, 4), R(Attribute.Magic, 4) };
                        case ModPart.Swift: return new[] { R(Attribute.Swiftness, 6), R(Attribute.Dexterity, 4) };
                        case ModPart.Homing: return new[] { R(Attribute.Dexterity, 5) };
                        default: return new[] { R(Attribute.Strength, 8) };
                    }
            }
        }

        public static bool RequirementsMet(PartKind k, int id, int tier)
        {
            foreach (var r in Requirements(k, id, tier))
                if (!r.Met) return false;
            return true;
        }

        public static bool RequirementsMet(CraftedSkill c)
        {
            if (!RequirementsMet(PartKind.Element, (int)c.element, c.elementTier)) return false;
            if (!RequirementsMet(PartKind.Delivery, (int)c.delivery, c.deliveryTier)) return false;
            for (int i = 0; i < c.mods.Count; i++)
                if (!RequirementsMet(PartKind.Modifier, (int)c.mods[i], c.ModTier(c.mods[i]))) return false;
            return true;
        }

        public static string RequirementText(PartKind k, int id, int tier)
        {
            var reqs = Requirements(k, id, tier);
            if (reqs.Length == 0) return "<color=#8f8>No requirement</color>";
            var parts = new List<string>();
            foreach (var r in reqs) parts.Add(r.Met ? $"<color=#8f8>{r}</color>" : $"<color=#f66>{r}</color>");
            return "Needs " + string.Join(", ", parts);
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
                default: return new Color(0.88f, 0.86f, 0.82f);
            }
        }

        public static string Icon(PartKind k, int id)
        {
            switch (k)
            {
                case PartKind.Element:
                    switch ((ElementPart)id)
                    {
                        case ElementPart.Fire: return "fire";
                        case ElementPart.Frost: return "frost";
                        case ElementPart.Lightning: return "bolt";
                        case ElementPart.Poison: return "poison";
                        case ElementPart.Arcane: return "arcane";
                        default: return "sword";
                    }
                case PartKind.Delivery:
                    switch ((DeliveryPart)id)
                    {
                        case DeliveryPart.Forward: return "dash";
                        case DeliveryPart.Single: return "eye";
                        case DeliveryPart.Pierce: return "bow";
                        case DeliveryPart.Explosion: return "meteor";
                        case DeliveryPart.Nova: return "nova";
                        case DeliveryPart.Wave: return "slash";
                        case DeliveryPart.Blink: return "portal";
                        default: return "leap";
                    }
                default:
                    switch ((ModPart)id)
                    {
                        case ModPart.Speed: return "up";
                        case ModPart.Split: return "rain";
                        case ModPart.Chain: return "bolt";
                        case ModPart.Echo: return "spiral";
                        case ModPart.Wide: return "nova";
                        case ModPart.Lingering: return "trap";
                        case ModPart.Swift: return "dash";
                        case ModPart.Homing: return "eye";
                        default: return "skull";
                    }
            }
        }

        public static Color KindColor(PartKind k) =>
            k == PartKind.Element ? new Color(1f, 0.7f, 0.35f) : k == PartKind.Delivery ? new Color(0.5f, 0.8f, 1f) : new Color(0.8f, 0.6f, 1f);

        static readonly Dictionary<ElementPart, string> adjectives = new Dictionary<ElementPart, string>
        {
            { ElementPart.Fire, "Searing" }, { ElementPart.Frost, "Frozen" }, { ElementPart.Lightning, "Crackling" },
            { ElementPart.Poison, "Venomous" }, { ElementPart.Arcane, "Arcane" }, { ElementPart.Physical, "Brutal" },
        };

        static readonly Dictionary<DeliveryPart, string> nouns = new Dictionary<DeliveryPart, string>
        {
            { DeliveryPart.Forward, "Bolt" }, { DeliveryPart.Single, "Strike" }, { DeliveryPart.Pierce, "Lance" },
            { DeliveryPart.Explosion, "Blast" }, { DeliveryPart.Nova, "Nova" }, { DeliveryPart.Wave, "Wave" },
            { DeliveryPart.Blink, "Blink" }, { DeliveryPart.Dash, "Rush" },
        };

        static readonly Dictionary<ModPart, string> modNouns = new Dictionary<ModPart, string>
        {
            { ModPart.Speed, "Haste" }, { ModPart.Split, "Many" }, { ModPart.Chain, "Chains" }, { ModPart.Echo, "Echoes" },
            { ModPart.Wide, "Expanse" }, { ModPart.Lingering, "Lingering" }, { ModPart.Swift, "Swiftness" },
            { ModPart.Homing, "Seeking" }, { ModPart.Heavy, "Ruin" },
        };

        public static string NameFor(ElementPart e, DeliveryPart d, List<ModPart> mods)
        {
            string n = $"{adjectives[e]} {nouns[d]}";
            if (mods.Count == 1) n += $" of {modNouns[mods[0]]}";
            else if (mods.Count >= 2) n += $" of {modNouns[mods[0]]} and {modNouns[mods[1]]}";
            return n;
        }

        public static int CraftCost(int level, int mods) => 40 + level * 15 + mods * 30;
        public const int MaxCrafted = 6;
        public static int MaxMods(int level) => level >= 15 ? 3 : 2;

        // A random part drop; further from the village means higher tiers.
        public static PartRef Roll(System.Random rng, int monsterLevel, MonsterRank rank)
        {
            double r = rng.NextDouble();
            var kind = r < 0.3 ? PartKind.Element : r < 0.6 ? PartKind.Delivery : PartKind.Modifier;
            int tier = 1 + (monsterLevel >= 15 ? 1 : 0) + (monsterLevel >= 35 ? 1 : 0);
            if (rank >= MonsterRank.Rare && rng.NextDouble() < 0.4) tier++;
            return new PartRef { kind = kind, id = rng.Next(Count(kind)), tier = Mathf.Clamp(tier, 1, 3) };
        }
    }
}
