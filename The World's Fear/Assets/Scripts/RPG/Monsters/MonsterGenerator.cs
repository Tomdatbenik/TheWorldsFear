using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum MonsterAffix { Fast, Vampiric, Molten, Shielded, Teleporter, FrostAura, Multishot, ExtraHealth, ExtraStrong }

    // A generated kind of monster: look, name, archetype and stat profile.
    public class MonsterSpecies
    {
        public string name;
        public MonsterArchetype archetype;
        public Biome biome;
        public MonsterLook look;
        public float hpMult = 1f;
        public float damageMult = 1f;
        public float armorMult = 1f;
        public float moveSpeed = 3f;
        public float attackRange = 1f;
        public float attackCooldown = 1.2f;
        public float scale = 1f;
        public float preferredDistance; // ranged monsters keep their distance
        public float projectileSpeed = 7f;
        public float packMultiplier = 1f;
        public bool isSpider;
        public MobDef mob;            // a designed mob rendered from 3D; null for the older generated creatures
        public Color projectileColor = Color.white;
        public StatusSpec onHitStatus;
        public ElementPart element = ElementPart.Physical;
        public int seed;              // to redraw it in the codex
    }

    public static class MonsterGenerator
    {
        static readonly Dictionary<string, List<MonsterSpecies>> cache = new Dictionary<string, List<MonsterSpecies>>();

        static readonly Dictionary<Biome, string[]> prefixes = new Dictionary<Biome, string[]>
        {
            { Biome.Forest, new[] { "Moss", "Bark", "Thorn", "Fern", "Root", "Sap", "Bramble", "Leaf", "Briar", "Twig" } },
            { Biome.Marsh, new[] { "Bog", "Mire", "Rot", "Sludge", "Murk", "Blight", "Fen", "Gloom", "Slime", "Reek" } },
            { Biome.Ruins, new[] { "Stone", "Dust", "Grave", "Crypt", "Rune", "Bone", "Ash", "Iron", "Tomb", "Hollow" } },
            { Biome.Ember, new[] { "Cinder", "Ember", "Magma", "Scorch", "Blaze", "Soot", "Pyre", "Flame", "Char", "Smolder" } },
            { Biome.Village, new[] { "Field", "Barn", "Mud" } },
        };

        static readonly Dictionary<MonsterArchetype, string[]> suffixes = new Dictionary<MonsterArchetype, string[]>
        {
            { MonsterArchetype.Chaser, new[] { "fang", "lurker", "stalker", "maw", "prowler", "biter" } },
            { MonsterArchetype.Swarmling, new[] { "ling", "mite", "skitter", "grub", "tick", "swarm" } },
            { MonsterArchetype.Spitter, new[] { "spitter", "belcher", "toad", "retcher", "spewer" } },
            { MonsterArchetype.Charger, new[] { "horn", "rammer", "tusk", "charger", "boar" } },
            { MonsterArchetype.Brute, new[] { "golem", "brute", "hulk", "ogre", "smasher", "colossus" } },
            { MonsterArchetype.Caster, new[] { "witch", "shaman", "seer", "hexer", "wraith", "mystic" } },
            { MonsterArchetype.Exploder, new[] { "bloat", "bomber", "popper", "burster", "volatile" } },
            { MonsterArchetype.Guardian, new[] { "bulwark", "warden", "shell", "aegis" } },
            { MonsterArchetype.Skirmisher, new[] { "dart", "slash", "flicker", "striker" } },
        };

        // The designed mobs of a biome. While the roster is still being built, generated creatures fill the rest.
        static List<MonsterSpecies> RosterFor(Biome biome)
        {
            var list = new List<MonsterSpecies>();
            foreach (var d in MobCatalog.ForBiome(biome))
                if (!d.warbandOnly) list.Add(CreateMob(d));
            return list;
        }

        // Factions that can field a warband here: a commander and at least one fighter.
        public static List<string> WarbandFactions(Biome biome)
        {
            var found = new HashSet<string>();
            foreach (var d in MobCatalog.ForBiome(biome))
                if (!string.IsNullOrEmpty(d.faction) && d.leader) found.Add(d.faction);
            return new List<string>(found);
        }

        public static List<MonsterSpecies> WarbandRoster(Biome biome, string faction)
        {
            var list = new List<MonsterSpecies>();
            foreach (var d in MobCatalog.ForBiome(biome))
                if (d.faction == faction) list.Add(CreateMob(d));
            return list;
        }

        // A designed mob: its look comes from the catalogue, its stats from its role unless the mob sets its own.
        public static MonsterSpecies CreateMob(MobDef d)
        {
            var seed = MobCatalog.StableSeed(d.id);
            var arch = d.RoleValue;
            var biome = d.BiomeValue;
            d.sprites = d.sprites ?? new MobSprites(d);
            var idle = d.sprites.Get("idle", 6);          // 6 = facing down
            var back = d.sprites.Get("idle", 2);
            Color tint = Color.white;
            if (!string.IsNullOrEmpty(d.tint)) ColorUtility.TryParseHtmlString(d.tint, out tint);
            else tint = BiomeTint(biome);
            var look = new MonsterLook
            {
                frames = idle, backFrames = back, primary = tint, accent = tint,
                pixelSize = Mathf.Max(8, Mathf.RoundToInt(d.heightUnits * 16f / 0.75f)),
            };
            var sp = new MonsterSpecies { name = d.name, archetype = arch, biome = biome, look = look, mob = d, seed = seed, projectileColor = tint };
            sp.element = Elements.ForBiome(biome, new System.Random(seed * 31 + 7));
            ApplyRole(sp, arch, biome, new System.Random(seed ^ 0x5f3759));
            sp.scale = 1f;                                 // the sprites are already at their true size
            if (d.body == "spider" && biome == Biome.Forest) sp.onHitStatus = StatusSpec.Poison(0.3f, 3f);
            if (d.status == "poison") sp.onHitStatus = StatusSpec.Poison(0.35f, 3f);
            else if (d.status == "burn") sp.onHitStatus = StatusSpec.Burn(0.35f, 3f);
            else if (d.status == "slow") sp.onHitStatus = StatusSpec.Slow(0.35f, 2f);
            if (d.hp > 0f) sp.hpMult = d.hp;
            if (d.damage > 0f) sp.damageMult = d.damage;
            if (d.armor > 0f) sp.armorMult = d.armor;
            if (d.speed > 0f) sp.moveSpeed = d.speed;
            if (d.range > 0f) sp.attackRange = d.range;
            if (d.cooldown > 0f) sp.attackCooldown = d.cooldown;
            if (d.keepAway > 0f) sp.preferredDistance = d.keepAway;
            if (d.projectileSpeed > 0f) sp.projectileSpeed = d.projectileSpeed;
            if (d.pack > 0f) sp.packMultiplier = d.pack;
            return sp;
        }

        // A designed mob of this fighting style in this biome, or null. Brutes count guardians too, since guardians took their place.
        public static MonsterSpecies DesignedFor(Biome biome, MonsterArchetype arch, int seed)
        {
            var match = MobCatalog.ForBiome(biome).FindAll(d => !d.warbandOnly && (d.RoleValue == arch || (arch == MonsterArchetype.Brute && d.RoleValue == MonsterArchetype.Guardian)));
            return match.Count > 0 ? CreateMob(match[Mathf.Abs(seed) % match.Count]) : null;
        }

        static Color BiomeTint(Biome b)
        {
            switch (b)
            {
                case Biome.Marsh: return new Color(0.7f, 0.5f, 1f);
                case Biome.Ruins: return new Color(0.75f, 0.85f, 1f);
                case Biome.Ember: return new Color(1f, 0.55f, 0.2f);
                default: return new Color(0.55f, 0.85f, 0.35f);
            }
        }

        public static List<MonsterSpecies> SpeciesFor(ZoneDefinition zone)
        {
            if (zone == null) return new List<MonsterSpecies>();
            if (cache.TryGetValue(zone.id, out var list)) return list;

            var rng = new System.Random(zone.seed);
            list = new List<MonsterSpecies>();
            var archetypes = new List<MonsterArchetype>(zone.archetypes);
            if (archetypes.Count == 0) archetypes.Add(MonsterArchetype.Chaser);
            var usedNames = new HashSet<string>();
            var roster = zone.safeZone ? new List<MonsterSpecies>() : RosterFor(zone.biome);
            list.AddRange(roster);
            int generated = Mathf.Max(0, zone.speciesCount - roster.Count);

            for (int i = 0; i < generated; i++)
            {
                var arch = archetypes[i % archetypes.Count];
                int seed = zone.seed * 977 + i * 131;
                var sp = Create(seed, zone.biome, arch);
                string name;
                int guard = 0;
                do
                {
                    name = MakeName(new System.Random(seed + guard * 17), zone.biome, arch, sp.isSpider);
                    guard++;
                } while (usedNames.Contains(name) && guard < 20);
                usedNames.Add(name);
                sp.name = name;
                list.Add(sp);
            }

            // Every forest has its signature spiders.
            if (zone.biome == Biome.Forest && !list.Exists(s => s.mob != null && s.mob.body == "spider"))
            {
                var spider = Create(zone.seed * 31 + 7, Biome.Forest, MonsterArchetype.Chaser, true);
                spider.name = "Forest Spider";
                list.Add(spider);
            }

            // Hand-drawn creatures from the original game join the generated ones.
            foreach (var hm in HandmadeMonsters.For(zone.biome))
                if (!list.Exists(s => s.name == hm.name)) list.Add(hm);

            cache[zone.id] = list;
            return list;
        }

        static readonly string[] spiderSuffixes = { "weaver", "crawler", "spinner", "widow", "fang", "stalker" };

        public static string MakeName(System.Random rng, Biome biome, MonsterArchetype arch, bool spider = false)
        {
            var pre = prefixes[biome];
            var suf = spider ? spiderSuffixes : suffixes[arch];
            return pre[rng.Next(pre.Length)] + suf[rng.Next(suf.Length)];
        }

        public static MonsterSpecies Create(int seed, Biome biome, MonsterArchetype arch, bool forceSpider = false)
        {
            var rng = new System.Random(seed ^ 0x5f3759);
            // Some chasers and swarmlings are spiders.
            bool spider = forceSpider || ((arch == MonsterArchetype.Chaser || arch == MonsterArchetype.Swarmling) && ((seed >> 3) & 3) == 0);
            var look = spider ? SpiderSpriteGenerator.Generate(seed, biome) : MonsterSpriteGenerator.Generate(seed, biome, arch);
            var sp = new MonsterSpecies { archetype = arch, biome = biome, look = look, isSpider = spider, seed = seed };
            sp.element = Elements.ForBiome(biome, new System.Random(seed * 31 + 7));
            sp.projectileColor = sp.look.accent;

            if (spider) sp.onHitStatus = StatusSpec.Poison(0.3f, 3f);
            ApplyRole(sp, arch, biome, rng);
            return sp;
        }

        // What a role means in numbers. Designed mobs can override any of this in their own config.
        static void ApplyRole(MonsterSpecies sp, MonsterArchetype arch, Biome biome, System.Random rng)
        {
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            switch (arch)
            {
                case MonsterArchetype.Guardian:
                    // the same threat as a Brute; standing in the line of fire is how it plays, not extra difficulty
                    sp.hpMult = R(2.4f, 2.9f); sp.damageMult = R(1.7f, 2f); sp.armorMult = 2f; sp.moveSpeed = R(1.9f, 2.3f);
                    sp.attackRange = 1.6f; sp.attackCooldown = R(2.6f, 3.2f); sp.packMultiplier = 0.5f;
                    break;
                case MonsterArchetype.Skirmisher:
                    // the same threat as a Chaser; darting in and out replaces walking up, at the same speed
                    sp.hpMult = R(0.9f, 1.15f); sp.damageMult = R(0.9f, 1.1f); sp.moveSpeed = R(3f, 3.6f);
                    sp.attackRange = 1.1f; sp.attackCooldown = R(1.0f, 1.4f); sp.preferredDistance = 0f;
                    break;
                case MonsterArchetype.Chaser:
                    sp.hpMult = R(0.9f, 1.15f); sp.damageMult = R(0.9f, 1.1f); sp.moveSpeed = R(3f, 3.6f);
                    sp.attackRange = 1.1f; sp.attackCooldown = R(1.0f, 1.4f); sp.scale = R(1f, 1.15f);
                    break;
                case MonsterArchetype.Swarmling:
                    sp.hpMult = R(0.35f, 0.45f); sp.damageMult = R(0.45f, 0.55f); sp.moveSpeed = R(4.2f, 4.8f);
                    sp.attackRange = 0.9f; sp.attackCooldown = R(0.8f, 1.0f); sp.scale = R(0.9f, 1f); sp.packMultiplier = 1.4f;
                    break;
                case MonsterArchetype.Spitter:
                    sp.hpMult = R(0.7f, 0.85f); sp.damageMult = R(0.85f, 1f); sp.moveSpeed = R(2.4f, 2.9f);
                    sp.attackRange = 6.5f; sp.attackCooldown = R(1.8f, 2.4f); sp.preferredDistance = R(4.5f, 5.5f);
                    sp.projectileSpeed = R(6.5f, 8.5f); sp.scale = R(1f, 1.1f);
                    if (biome == Biome.Marsh) sp.onHitStatus = StatusSpec.Poison(0.4f, 3f);
                    if (biome == Biome.Ember) sp.onHitStatus = StatusSpec.Burn(0.4f, 2.5f);
                    break;
                case MonsterArchetype.Charger:
                    sp.hpMult = R(1.15f, 1.35f); sp.damageMult = R(1.3f, 1.5f); sp.moveSpeed = R(2.6f, 3f);
                    sp.attackRange = 1.2f; sp.attackCooldown = R(1.3f, 1.6f); sp.scale = R(1.1f, 1.25f);
                    break;
                case MonsterArchetype.Brute:
                    sp.hpMult = R(2.4f, 2.9f); sp.damageMult = R(1.7f, 2f); sp.armorMult = 2f; sp.moveSpeed = R(1.9f, 2.3f);
                    sp.attackRange = 1.6f; sp.attackCooldown = R(2.6f, 3.2f); sp.scale = R(1.05f, 1.2f); sp.packMultiplier = 0.5f;
                    break;
                case MonsterArchetype.Caster:
                    sp.hpMult = R(0.75f, 0.9f); sp.damageMult = R(1.2f, 1.4f); sp.moveSpeed = R(2.2f, 2.6f);
                    sp.attackRange = 7f; sp.attackCooldown = R(3.2f, 4f); sp.preferredDistance = R(5f, 6f); sp.scale = R(1f, 1.1f);
                    if (biome == Biome.Ember) sp.onHitStatus = StatusSpec.Burn(0.4f, 3f);
                    else if (biome == Biome.Ruins) sp.onHitStatus = StatusSpec.Slow(0.4f, 2f);
                    break;
                case MonsterArchetype.Exploder:
                    sp.hpMult = R(0.55f, 0.7f); sp.damageMult = R(2.6f, 3.2f); sp.moveSpeed = R(3.6f, 4.1f);
                    sp.attackRange = 1.3f; sp.attackCooldown = 99f; sp.scale = R(0.95f, 1.1f);
                    break;
            }
        }

        // Base stats for a monster of a given level, before species and rank multipliers.
        // Monsters get much tougher with level so deep areas stay a threat to geared heroes.
        public static float BaseHP(int level) => 50f + 40f * level * (1f + level * 0.12f);
        public static float BaseDamage(int level) => 6f + 3f * level * (1f + level * 0.03f);
        // Armor grows faster than linearly, so deep areas shrug off under-geared heroes.
        public static float BaseArmor(int level) => 4f + level * 3f + level * level * 0.05f;

        public static string AffixName(MonsterAffix a)
        {
            switch (a)
            {
                case MonsterAffix.FrostAura: return "Frost Aura";
                case MonsterAffix.ExtraHealth: return "Extra Health";
                case MonsterAffix.ExtraStrong: return "Extra Strong";
                default: return a.ToString();
            }
        }
    }

    // Wraps the original spider and fire slime art as species.
    public static class HandmadeMonsters
    {
        public static IEnumerable<MonsterSpecies> For(Biome biome)
        {
            var db = GameDatabase.Instance;
            if (db == null) yield break;
            foreach (var h in db.handmadeMonsters)
            {
                if (h == null || h.biome != biome || h.frames == null || h.frames.Length == 0) continue;
                var sp = MonsterGenerator.Create(h.name.GetHashCode(), biome, h.archetype);
                sp.name = h.name;
                // Re-cut the hand-drawn frames with the pivot at the feet so they stand on the ground.
                var frames = new Sprite[h.frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    var f = h.frames[i];
                    frames[i] = Sprite.Create(f.texture, f.rect, new Vector2(0.5f, 0.15f), f.pixelsPerUnit);
                }
                sp.look = new MonsterLook { frames = frames, primary = h.tint, accent = h.tint, pixelSize = Mathf.RoundToInt(h.frames[0].bounds.size.x * 16f) };
                sp.scale = h.scale;
                sp.projectileColor = h.tint;
                yield return sp;
            }
        }
    }
}
