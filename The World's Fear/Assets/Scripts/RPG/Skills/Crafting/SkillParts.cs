using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Skills are crafted: Element -> Delivery -> Modifiers.
    public enum PartKind { Element, Delivery, Modifier }
    // New parts are appended so saved recipes keep their meaning.
    public enum ElementPart { Fire, Frost, Lightning, Poison, Arcane, Physical, Holy, Shadow }
    // The last ten are weapon arts: they need that weapon in your hands (unlocked by Vexa, the Brood Mother).
    public enum DeliveryPart
    {
        Forward, Single, Pierce, Explosion, Nova, Wave, Blink, Dash, Self, Aura, Hex,
        Whirlwind, AxeThrow, Quake, KnifeFan, SpearThrow, Barrage, BoltVolley, Meteor, Missiles, Flurry,
    }
    public enum ModPart { Speed, Split, Chain, Echo, Wide, Lingering, Swift, Homing, Heavy, Empower, Haste, Barrier, Mend, Weaken, Expose }

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
        // Extra elements (dual / triple-element spells, unlocked by bosses).
        public List<ElementPart> extraElements = new List<ElementPart>();
        public List<int> extraElementTiers = new List<int>();

        public List<ElementPart> AllElements
        {
            get
            {
                var l = new List<ElementPart> { element };
                if (extraElements != null) l.AddRange(extraElements);
                return l;
            }
        }

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

        public static int Count(PartKind k) => k == PartKind.Element ? 8 : k == PartKind.Delivery ? 21 : 15;

        public const int FirstWeaponArt = (int)DeliveryPart.Whirlwind;

        // The weapon a weapon-art delivery needs, or null for ordinary deliveries.
        public static WeaponType? WeaponOf(DeliveryPart d) => (int)d >= FirstWeaponArt ? (WeaponType)((int)d - FirstWeaponArt) : (WeaponType?)null;

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
                        case ElementPart.Holy: return "Heals you every time it hits. Cast on yourself for a big heal.";
                        case ElementPart.Shadow: return "Weakens enemies: they deal less damage.";
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
                        case DeliveryPart.Self: return "Cast on yourself: a blessing that depends on the element.";
                        case DeliveryPart.Aura: return "A field around you that pulses for several seconds.";
                        case DeliveryPart.Hex: return "Curses an enemy: it takes much more damage for a while.";
                        case DeliveryPart.Whirlwind: return "Sword art: spin, hitting everything around you again and again.";
                        case DeliveryPart.AxeThrow: return "Axe art: hurl a spinning axe through every enemy.";
                        case DeliveryPart.Quake: return "Hammer art: pound the ground three times around you.";
                        case DeliveryPart.KnifeFan: return "Dagger art: knives in every direction.";
                        case DeliveryPart.SpearThrow: return "Spear art: throw your spear in a long, piercing line.";
                        case DeliveryPart.Barrage: return "Bow art: five rapid volleys of arrows.";
                        case DeliveryPart.BoltVolley: return "Crossbow art: three piercing bolts in a row.";
                        case DeliveryPart.Meteor: return "Staff art: call a meteor down on the target spot.";
                        case DeliveryPart.Missiles: return "Wand art: six homing missiles.";
                        case DeliveryPart.Flurry: return "Fist art: a flurry of eight punches.";
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
                        case ModPart.Empower: return "Buff: after casting you deal +25% damage for 5 s.";
                        case ModPart.Haste: return "Buff: after casting you move and attack faster for 4 s.";
                        case ModPart.Barrier: return "Buff: casting shields you for 15% of your life.";
                        case ModPart.Mend: return "Buff: casting heals you for 8% of your life.";
                        case ModPart.Weaken: return "Debuff: enemies hit deal 30% less damage for 4 s.";
                        case ModPart.Expose: return "Debuff: enemies hit take 25% more damage for 4 s.";
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
                        case ElementPart.Holy: return new[] { R(Attribute.Vitality, 3), R(Attribute.Magic, 2) };
                        case ElementPart.Shadow: return new[] { R(Attribute.Magic, 3), R(Attribute.Dexterity, 2) };
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
                        case DeliveryPart.Self: return new[] { R(Attribute.Vitality, 2) };
                        case DeliveryPart.Aura: return new[] { R(Attribute.Magic, 5), R(Attribute.Vitality, 3) };
                        case DeliveryPart.Hex: return new[] { R(Attribute.Magic, 6) };
                        case DeliveryPart.Whirlwind: case DeliveryPart.AxeThrow: case DeliveryPart.Quake: case DeliveryPart.Flurry:
                            return new[] { R(Attribute.Strength, 6) };
                        case DeliveryPart.KnifeFan: case DeliveryPart.SpearThrow: case DeliveryPart.Barrage: case DeliveryPart.BoltVolley:
                            return new[] { R(Attribute.Dexterity, 6) };
                        case DeliveryPart.Meteor: case DeliveryPart.Missiles:
                            return new[] { R(Attribute.Magic, 6) };
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
                        case ModPart.Empower: return new[] { R(Attribute.Strength, 5) };
                        case ModPart.Haste: return new[] { R(Attribute.Swiftness, 4) };
                        case ModPart.Barrier: return new[] { R(Attribute.Vitality, 5) };
                        case ModPart.Mend: return new[] { R(Attribute.Vitality, 4), R(Attribute.Magic, 2) };
                        case ModPart.Weaken: return new[] { R(Attribute.Magic, 4), R(Attribute.Strength, 2) };
                        case ModPart.Expose: return new[] { R(Attribute.Dexterity, 6) };
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
            if (c.extraElements != null)
                for (int i = 0; i < c.extraElements.Count; i++)
                    if (!RequirementsMet(PartKind.Element, (int)c.extraElements[i], i < c.extraElementTiers.Count ? c.extraElementTiers[i] : 1)) return false;
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
                case ElementPart.Holy: return new Color(1f, 0.92f, 0.55f);
                case ElementPart.Shadow: return new Color(0.55f, 0.35f, 0.75f);
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
                        case ElementPart.Holy: return "sun";
                        case ElementPart.Shadow: return "moon";
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
                        case DeliveryPart.Self: return "self";
                        case DeliveryPart.Aura: return "aura";
                        case DeliveryPart.Hex: return "hex";
                        case DeliveryPart.Whirlwind: return "spiral";
                        case DeliveryPart.AxeThrow: return "slash";
                        case DeliveryPart.Quake: return "nova";
                        case DeliveryPart.KnifeFan: return "poison";
                        case DeliveryPart.SpearThrow: return "sword";
                        case DeliveryPart.Barrage: return "bow";
                        case DeliveryPart.BoltVolley: return "bow";
                        case DeliveryPart.Meteor: return "meteor";
                        case DeliveryPart.Missiles: return "staff";
                        case DeliveryPart.Flurry: return "fist";
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
                        case ModPart.Empower: return "fist";
                        case ModPart.Haste: return "wing";
                        case ModPart.Barrier: return "shield";
                        case ModPart.Mend: return "heal";
                        case ModPart.Weaken: return "down";
                        case ModPart.Expose: return "crack";
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
            { ElementPart.Holy, "Holy" }, { ElementPart.Shadow, "Shadow" },
        };

        static readonly Dictionary<DeliveryPart, string> nouns = new Dictionary<DeliveryPart, string>
        {
            { DeliveryPart.Forward, "Bolt" }, { DeliveryPart.Single, "Strike" }, { DeliveryPart.Pierce, "Lance" },
            { DeliveryPart.Explosion, "Blast" }, { DeliveryPart.Nova, "Nova" }, { DeliveryPart.Wave, "Wave" },
            { DeliveryPart.Blink, "Blink" }, { DeliveryPart.Dash, "Rush" },
            { DeliveryPart.Self, "Blessing" }, { DeliveryPart.Aura, "Aura" }, { DeliveryPart.Hex, "Hex" },
            { DeliveryPart.Whirlwind, "Whirlwind" }, { DeliveryPart.AxeThrow, "Axe" }, { DeliveryPart.Quake, "Quake" },
            { DeliveryPart.KnifeFan, "Knives" }, { DeliveryPart.SpearThrow, "Spear" }, { DeliveryPart.Barrage, "Barrage" },
            { DeliveryPart.BoltVolley, "Volley" }, { DeliveryPart.Meteor, "Meteor" }, { DeliveryPart.Missiles, "Missiles" },
            { DeliveryPart.Flurry, "Flurry" },
        };

        static readonly Dictionary<ModPart, string> modNouns = new Dictionary<ModPart, string>
        {
            { ModPart.Speed, "Haste" }, { ModPart.Split, "Many" }, { ModPart.Chain, "Chains" }, { ModPart.Echo, "Echoes" },
            { ModPart.Wide, "Expanse" }, { ModPart.Lingering, "Lingering" }, { ModPart.Swift, "Swiftness" },
            { ModPart.Homing, "Seeking" }, { ModPart.Heavy, "Ruin" },
            { ModPart.Empower, "Might" }, { ModPart.Haste, "Wind" }, { ModPart.Barrier, "Warding" }, { ModPart.Mend, "Mending" },
            { ModPart.Weaken, "Frailty" }, { ModPart.Expose, "Ruin's Mark" },
        };

        public static string NameFor(ElementPart e, DeliveryPart d, List<ModPart> mods) => NameFor(new List<ElementPart> { e }, d, mods);

        public static string NameFor(List<ElementPart> els, DeliveryPart d, List<ModPart> mods)
        {
            var adj = new List<string>();
            foreach (var e in els) adj.Add(adjectives[e]);
            string n = $"{string.Join(" ", adj)} {nouns[d]}";
            if (mods.Count == 1) n += $" of {modNouns[mods[0]]}";
            else if (mods.Count >= 2) n += $" of {modNouns[mods[0]]} and {modNouns[mods[1]]}";
            return n;
        }

        public static int CraftCost(int level, int mods) => 40 + level * 15 + mods * 30;
        public const int MaxCrafted = 6;
        // Bosses unlock more modifiers (see BossUnlocks).
        public static int MaxMods(int level) => BossUnlocks.MaxMods;

        // A random part drop; further from the village means higher tiers.
        public static PartRef Roll(System.Random rng, int monsterLevel, MonsterRank rank)
        {
            double r = rng.NextDouble();
            var kind = r < 0.3 ? PartKind.Element : r < 0.6 ? PartKind.Delivery : PartKind.Modifier;
            int tier = 1 + (monsterLevel >= 15 ? 1 : 0) + (monsterLevel >= 35 ? 1 : 0);
            if (rank >= MonsterRank.Rare && rng.NextDouble() < 0.4) tier++;
            // Weapon arts only drop once Vexa, the Brood Mother has fallen.
            int count = kind == PartKind.Delivery && !BossUnlocks.WeaponArts ? FirstWeaponArt : Count(kind);
            return new PartRef { kind = kind, id = rng.Next(count), tier = Mathf.Clamp(tier, 1, 3) };
        }
    }
}
