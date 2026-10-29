using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TWF.EditorTools
{
    // Creates or updates all game content assets (classes, skills, passives, trees, zones, affixes,
    // the hero prefab and the GameDatabase). Re-running overwrites the values set here.
    public static class ContentBuilder
    {
        const string Root = "Assets/Content";
        const string DbPath = "Assets/Resources/TWF/GameDatabase.asset";

        static bool createdAny;

        [MenuItem("TWF/Build Content")]
        public static void BuildAll()
        {
            createdAny = false;
            Dir(Root);
            Dir(Root + "/Skills");
            Dir(Root + "/Passives");
            Dir(Root + "/Classes");
            Dir(Root + "/Zones");
            Dir(Root + "/Affixes");
            Dir("Assets/Resources/TWF");
            Dir("Assets/Prefabs/RPG");

            var db = Asset<GameDatabase>(DbPath);
            db.classes = new List<HeroClassDefinition> { BuildAdventurer() };
            db.zones = BuildZones();
            db.affixes = BuildAffixes();
            db.heroPrefab = BuildHeroPrefab();
            db.handmadeMonsters = new List<HandmadeMonster>
            {
                new HandmadeMonster
                {
                    name = "Fire Slime", biome = Biome.Ember, archetype = MonsterArchetype.Exploder, tint = C("#ff7a3d"), scale = 0.8f,
                    frames = LoadSprites("Assets/Sprites/Slime/FireSlime/fireslime.png"),
                },
            };
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // Fields set on assets created in this pass don't always persist; a second pass fixes them.
            if (createdAny)
            {
                BuildAll();
                return;
            }
            Debug.Log("TWF content built.");
        }

        // ---------- Helpers ----------

        static void Dir(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parent = Path.GetDirectoryName(path).Replace('\\', '/');
                Dir(parent);
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        static Sprite[] LoadSprites(string path)
        {
            var list = new List<Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Sprite sp) list.Add(sp);
            list.Sort((a, b) => a.rect.x.CompareTo(b.rect.x));
            return list.ToArray();
        }

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
                createdAny = true;
            }
            EditorUtility.SetDirty(a);
            return a;
        }

        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static SkillDefinition Skill(string id, string name, string glyph, string color, Targeting targeting, float range, float cooldown,
            string description, params SkillEffect[] effects)
        {
            var s = Asset<SkillDefinition>($"{Root}/Skills/{id}.asset");
            s.id = id;
            s.displayName = name;
            s.glyph = glyph;
            s.color = C(color);
            s.targeting = targeting;
            s.range = range;
            s.baseCooldown = cooldown;
            s.description = description;
            s.effects = new List<SkillEffect>(effects);
            s.isUltimate = false;
            s.castLock = targeting == Targeting.Dash ? 0.2f : 0.12f;
            s.indicatorRadius = 2f;
            s.maxRank = 5;
            return s;
        }

        static SkillDefinition Ult(SkillDefinition s)
        {
            s.isUltimate = true;
            s.maxRank = 3;
            s.powerPerRank = 0.3f;
            s.cooldownPerRank = 0.1f;
            return s;
        }

        static SkillDefinition Radius(SkillDefinition s, float r)
        {
            s.indicatorRadius = r;
            return s;
        }

        static PassiveDefinition Passive(string id, string name, string glyph, string color, string description, int maxRank,
            StatModifier[] mods, params PassiveProc[] procs)
        {
            var p = Asset<PassiveDefinition>($"{Root}/Passives/{id}.asset");
            p.id = id;
            p.displayName = name;
            p.glyph = glyph;
            p.color = C(color);
            p.description = description;
            p.maxRank = maxRank;
            p.modifiersPerRank = new List<StatModifier>(mods ?? new StatModifier[0]);
            p.procs = new List<PassiveProc>(procs);
            return p;
        }

        static StatModifier Flat(StatType s, float v) => new StatModifier(s, ModKind.Flat, v);
        static StatModifier Pct(StatType s, float v) => new StatModifier(s, ModKind.Percent, v);

        static SkillTreeNode Node(SkillDefinition s, int tier, int col, int level, string requires = null) =>
            new SkillTreeNode { id = s.id, active = s, tier = tier, column = col, requiredLevel = level, requires = requires };

        static SkillTreeNode Node(PassiveDefinition p, int tier, int col, int level, string requires = null) =>
            new SkillTreeNode { id = p.id, passive = p, tier = tier, column = col, requiredLevel = level, requires = requires };

        static List<StatEntry> Stats(float hp, float dmg, float armor, float move, float atkSpeed, float crit, float cdr = 0f, float regen = 1f) =>
            new List<StatEntry>
            {
                new StatEntry(StatType.MaxHP, hp), new StatEntry(StatType.Damage, dmg), new StatEntry(StatType.Armor, armor),
                new StatEntry(StatType.MoveSpeed, move), new StatEntry(StatType.AttackSpeed, atkSpeed),
                new StatEntry(StatType.CritChance, crit), new StatEntry(StatType.CritDamage, 0f),
                new StatEntry(StatType.CooldownReduction, cdr), new StatEntry(StatType.LifeSteal, 0f),
                new StatEntry(StatType.HPRegen, regen),
            };

        static List<StatEntry> Growth(float hp, float dmg, float armor, float regen) =>
            new List<StatEntry>
            {
                new StatEntry(StatType.MaxHP, hp), new StatEntry(StatType.Damage, dmg),
                new StatEntry(StatType.Armor, armor), new StatEntry(StatType.HPRegen, regen),
            };

        // ---------- The hero (no classes) and the shared passive board ----------

        static HeroClassDefinition BuildAdventurer()
        {
            string red = "#e8573c", gold = "#f2c14e", green = "#7cd65a", ice = "#8fd8ff", venom = "#9be04a", arcane = "#b884ff", fire = "#ff6a2b", storm = "#ffe066";

            var thickSkin = Passive("p_thickskin", "Thick Skin", "HP", "#8fbf6a", "Toughened by battle.", 5, new[] { Pct(StatType.MaxHP, 0.05f), Flat(StatType.Armor, 4f) });
            var mastery = Passive("p_mastery", "Weapon Mastery", "DMG", red, "Every hit lands harder.", 5, new[] { Pct(StatType.Damage, 0.05f), Flat(StatType.CritChance, 0.015f) });
            var swift = Passive("p_swift", "Swift Strikes", "SPD", green, "Attack faster.", 5, new[] { Pct(StatType.AttackSpeed, 0.06f) });
            var nimble = Passive("p_nimble", "Nimble", "NMB", "#d7d7d7", "Light on your feet.", 3, new[] { Pct(StatType.MoveSpeed, 0.05f), Flat(StatType.Armor, 2f) });
            var mind = Passive("p_mind", "Arcane Mind", "CDR", arcane, "Skills recover faster.", 5, new[] { Flat(StatType.CooldownReduction, 0.03f) });
            var power = Passive("p_power", "Skill Power", "PWR", fire, "Crafted skills hit harder.", 5, new[] { Flat(StatType.SkillPower, 0.06f) });

            var bloodlust = Passive("p_bloodlust", "Bloodlust", "BLD", "#b0302a", "Kills restore life.", 3, null, new HealOnKillProc { percentMaxHPPerRank = 0.025f });
            var eagle = Passive("p_eagle", "Eagle Eye", "EYE", gold, "Find the weak spots.", 5, new[] { Flat(StatType.CritChance, 0.03f), Flat(StatType.CritDamage, 0.1f) });
            var reach = Passive("p_reach", "Far Reach", "RNG", ice, "Skills travel further.", 3, new[] { Flat(StatType.SkillRange, 0.08f), Flat(StatType.ProjectileSpeed, 0.08f) });
            var expanse = Passive("p_expanse", "Expanse", "AREA", arcane, "Bigger blasts.", 3, new[] { Flat(StatType.AreaSize, 0.08f) });

            var hardened = Passive("p_hardened", "Battle Hardened", "SHLD", "#9fc3ff", "Getting hit raises a barrier.", 3, null, new ShieldOnHitTakenProc { percentPerRank = 0.06f, cooldown = 8f });
            var ignite = Passive("p_ignite", "Ignite", "IGN", fire, "Hits may set enemies on fire.", 3, null, new StatusOnHitProc { chancePerRank = 0.1f, status = StatusSpec.Burn(0.4f, 3f) });
            var venomTips = Passive("p_venom", "Venom Tips", "VEN", venom, "Hits may poison.", 3, null, new StatusOnHitProc { chancePerRank = 0.1f, status = StatusSpec.Poison(0.4f, 3f) });
            var frostbite = Passive("p_frostbite", "Frostbite", "CHL", ice, "Hits may chill.", 3, null, new StatusOnHitProc { chancePerRank = 0.12f, status = StatusSpec.Slow(0.35f, 2f) });

            var execute = Passive("p_execute", "Executioner", "EXE", "#ff4040", "Finish off the weak.", 3, null, new ExecuteProc { bonusPerRank = 0.15f, threshold = 0.35f });
            var berserk = Passive("p_berserk", "Berserker", "RAGE", "#ff7040", "Deadlier when wounded.", 3, new[] { Flat(StatType.LifeSteal, 0.01f) }, new LowLifeProc { stat = StatType.Damage, valuePerRank = 0.12f, threshold = 0.45f });
            var explosive = Passive("p_explosive", "Volatile", "BOOM", "#ff8a3d", "Enemies may explode on death.", 3, null, new ExplodeOnKillProc { chancePerRank = 0.15f, radius = 2.2f, damageMult = 0.9f, color = C("#ff8a3d") });
            var staticCharge = Passive("p_static", "Static Charge", "STC", storm, "Hits may stun.", 3, null, new StatusOnHitProc { chancePerRank = 0.04f, status = StatusSpec.Stun(0.6f) });

            var survivor = Passive("p_survivor", "Survivalist", "SRV", "#8fbf6a", "Live off the land.", 3, new[] { Pct(StatType.MaxHP, 0.06f), Flat(StatType.HPRegen, 1f) }, new HealOnKillProc { percentMaxHPPerRank = 0.015f });
            var lingering = Passive("p_lingering", "Lingering Power", "DUR", arcane, "Effects last longer.", 3, new[] { Flat(StatType.EffectDuration, 0.15f) });
            var hydra = Passive("p_hydra", "Hydra", "MULTI", green, "One more projectile.", 1, new[] { Flat(StatType.ExtraProjectiles, 1f) });

            var tree = Asset<SkillTreeDefinition>($"{Root}/Classes/PassiveBoard.asset");
            tree.pointsToUnlockTier = new[] { 0, 3, 7, 12, 18 };
            tree.nodes = new List<SkillTreeNode>
            {
                Node(thickSkin, 0, 0, 1), Node(mastery, 0, 1, 1), Node(swift, 0, 2, 1), Node(nimble, 0, 3, 1),
                Node(mind, 1, 0, 3), Node(power, 1, 1, 3), Node(bloodlust, 1, 2, 3), Node(eagle, 1, 3, 3),
                Node(reach, 2, 0, 6), Node(expanse, 2, 1, 6), Node(hardened, 2, 2, 6), Node(ignite, 2, 3, 6),
                Node(venomTips, 3, 0, 10), Node(frostbite, 3, 1, 10), Node(execute, 3, 2, 10), Node(berserk, 3, 3, 10),
                Node(explosive, 4, 0, 15), Node(staticCharge, 4, 1, 15), Node(survivor, 4, 2, 15), Node(lingering, 4, 3, 15),
            };
            tree.nodes.Add(Node(hydra, 4, 4, 20));

            var cls = Asset<HeroClassDefinition>($"{Root}/Classes/Adventurer.asset");
            cls.id = "adventurer";
            cls.displayName = "Adventurer";
            cls.description = "A wanderer who fights with whatever weapon they find and crafts their own skills.";
            cls.tint = Color.white;
            cls.accentColor = C(gold);
            cls.baseStats = Stats(170, 12, 10, 5.4f, 1f, 0.05f, 0f, 1.2f);
            cls.growthPerLevel = Growth(18, 2.2f, 1.2f, 0.2f);
            cls.skillTree = tree;
            cls.starterNode = "";
            return cls;
        }

        // ---------- Zones ----------

        static List<ZoneDefinition> BuildZones()
        {
            ZoneDefinition Z(string id, string name, string flavor, Biome biome, int min, int max, int seed, string color,
                float elite, float rare, Vector2Int pack, params MonsterArchetype[] arch)
            {
                var z = Asset<ZoneDefinition>($"{Root}/Zones/{id}.asset");
                z.id = id;
                z.displayName = name;
                z.flavor = flavor;
                z.biome = biome;
                z.minLevel = min;
                z.maxLevel = max;
                z.seed = seed;
                z.nameColor = C(color);
                z.eliteChance = elite;
                z.rareChance = rare;
                z.packSize = pack;
                z.archetypes = new List<MonsterArchetype>(arch);
                z.speciesCount = 5;
                z.safeZone = biome == Biome.Village;
                return z;
            }

            return new List<ZoneDefinition>
            {
                Z("village", "Village of Ashvale", "The last safe hearth.", Biome.Village, 1, 1, 1, "#f2e6c9", 0, 0, new Vector2Int(0, 0)),
                Z("forest", "Whispering Forest", "Something moves between the trees.", Biome.Forest, 1, 6, 1101, "#9be07a", 0.1f, 0.04f, new Vector2Int(3, 5),
                    MonsterArchetype.Chaser, MonsterArchetype.Swarmling, MonsterArchetype.Spitter, MonsterArchetype.Charger, MonsterArchetype.Brute),
                Z("marsh", "Blightmarsh", "The water is thick with rot.", Biome.Marsh, 6, 12, 2207, "#b58cff", 0.12f, 0.05f, new Vector2Int(3, 6),
                    MonsterArchetype.Chaser, MonsterArchetype.Spitter, MonsterArchetype.Exploder, MonsterArchetype.Caster, MonsterArchetype.Swarmling),
                Z("ruins", "Sunken Ruins", "Stone halls of a forgotten kingdom.", Biome.Ruins, 12, 18, 3313, "#c9c3b5", 0.14f, 0.06f, new Vector2Int(3, 6),
                    MonsterArchetype.Brute, MonsterArchetype.Charger, MonsterArchetype.Caster, MonsterArchetype.Spitter, MonsterArchetype.Chaser),
                Z("ember", "Ember Fields", "The ground itself is burning.", Biome.Ember, 18, 25, 4421, "#ff7a3d", 0.16f, 0.08f, new Vector2Int(4, 7),
                    MonsterArchetype.Charger, MonsterArchetype.Exploder, MonsterArchetype.Caster, MonsterArchetype.Brute, MonsterArchetype.Spitter, MonsterArchetype.Swarmling),
            };
        }

        // ---------- Affixes ----------

        static List<AffixDefinition> BuildAffixes()
        {
            AffixDefinition A(string id, string text, bool prefix, StatType stat, ModKind kind, Vector2 l1, Vector2 l30, SlotMask slots, int weight = 10)
            {
                var a = Asset<AffixDefinition>($"{Root}/Affixes/{id}.asset");
                a.id = id;
                a.nameText = text;
                a.isPrefix = prefix;
                a.stat = stat;
                a.kind = kind;
                a.rangeAtLevel1 = l1;
                a.rangeAtLevel30 = l30;
                a.slots = slots;
                a.weight = weight;
                return a;
            }

            return new List<AffixDefinition>
            {
                A("brutal", "Brutal", true, StatType.Damage, ModKind.Flat, new Vector2(1, 3), new Vector2(18, 30), SlotMask.Weapon | SlotMask.Gloves | SlotMask.Ring | SlotMask.Amulet, 14),
                A("savage", "Savage", true, StatType.Damage, ModKind.Percent, new Vector2(0.04f, 0.08f), new Vector2(0.14f, 0.22f), SlotMask.Weapon | SlotMask.Amulet, 8),
                A("sturdy", "Sturdy", true, StatType.Armor, ModKind.Flat, new Vector2(2, 5), new Vector2(25, 45), SlotMask.Armor, 14),
                A("fortified", "Fortified", true, StatType.Armor, ModKind.Percent, new Vector2(0.05f, 0.08f), new Vector2(0.15f, 0.22f), SlotMask.Chest | SlotMask.Helm, 6),
                A("vital", "Vital", true, StatType.MaxHP, ModKind.Percent, new Vector2(0.04f, 0.06f), new Vector2(0.1f, 0.15f), SlotMask.Chest | SlotMask.Amulet, 7),
                A("deadly", "Deadly", true, StatType.CritDamage, ModKind.Flat, new Vector2(0.08f, 0.15f), new Vector2(0.3f, 0.5f), SlotMask.Weapon | SlotMask.Amulet | SlotMask.Gloves, 8),
                A("vampiric", "Vampiric", true, StatType.LifeSteal, ModKind.Flat, new Vector2(0.01f, 0.02f), new Vector2(0.03f, 0.06f), SlotMask.Weapon | SlotMask.Ring | SlotMask.Amulet, 5),
                A("bear", "of the Bear", false, StatType.MaxHP, ModKind.Flat, new Vector2(8, 15), new Vector2(120, 200), SlotMask.Armor | SlotMask.Jewelry, 14),
                A("haste", "of Haste", false, StatType.AttackSpeed, ModKind.Percent, new Vector2(0.04f, 0.07f), new Vector2(0.12f, 0.18f), SlotMask.Weapon | SlotMask.Gloves | SlotMask.Ring, 8),
                A("precision", "of Precision", false, StatType.CritChance, ModKind.Flat, new Vector2(0.02f, 0.04f), new Vector2(0.06f, 0.1f), SlotMask.Weapon | SlotMask.Gloves | SlotMask.Jewelry | SlotMask.Helm, 9),
                A("wind", "of the Wind", false, StatType.MoveSpeed, ModKind.Percent, new Vector2(0.03f, 0.05f), new Vector2(0.08f, 0.12f), SlotMask.Boots, 12),
                A("focus", "of Focus", false, StatType.CooldownReduction, ModKind.Flat, new Vector2(0.02f, 0.04f), new Vector2(0.06f, 0.1f), SlotMask.Helm | SlotMask.Jewelry, 8),
                A("mending", "of Mending", false, StatType.HPRegen, ModKind.Flat, new Vector2(0.5f, 1f), new Vector2(6f, 10f), SlotMask.Armor | SlotMask.Jewelry, 9),
                // Skill modifiers.
                A("farreach", "Far-Reaching", true, StatType.SkillRange, ModKind.Flat, new Vector2(0.06f, 0.1f), new Vector2(0.18f, 0.3f), SlotMask.Weapon | SlotMask.Amulet | SlotMask.Gloves, 7),
                A("expanse", "of Expanse", false, StatType.AreaSize, ModKind.Flat, new Vector2(0.06f, 0.1f), new Vector2(0.18f, 0.3f), SlotMask.Chest | SlotMask.Helm | SlotMask.Amulet | SlotMask.Ring, 7),
                A("hydra", "of the Hydra", false, StatType.ExtraProjectiles, ModKind.Flat, new Vector2(1f, 1f), new Vector2(1f, 2f), SlotMask.Weapon, 3),
                A("swiftflight", "Swift-Flight", true, StatType.ProjectileSpeed, ModKind.Flat, new Vector2(0.08f, 0.14f), new Vector2(0.2f, 0.35f), SlotMask.Weapon | SlotMask.Gloves, 6),
                A("lingering", "of Lingering", false, StatType.EffectDuration, ModKind.Flat, new Vector2(0.08f, 0.14f), new Vector2(0.25f, 0.4f), SlotMask.Helm | SlotMask.Boots | SlotMask.Ring, 6),
                A("scattering", "Scattering", true, StatType.ProjectileSpread, ModKind.Flat, new Vector2(0.15f, 0.25f), new Vector2(0.4f, 0.7f), SlotMask.Weapon | SlotMask.Gloves, 5),
            };
        }

        // ---------- Hero prefab ----------

        // An original hero: generated pixel-art character, no third-party sprites.
        static GameObject BuildHeroPrefab()
        {
            const string path = "Assets/Prefabs/RPG/RPGHero.prefab";
            var go = new GameObject("RPGHero");
            go.tag = "Player";
            go.layer = LayerMask.NameToLayer("Hero");

            // The sprite lives on a billboarded child so 2D physics on the root is unaffected.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sortingOrder = SortOrder.Units;
            visual.AddComponent<Billboard>();
            visual.AddComponent<CharacterAnimator>();
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            var col = go.AddComponent<CapsuleCollider2D>();
            col.direction = CapsuleDirection2D.Horizontal;
            col.size = new Vector2(0.7f, 0.45f);
            col.offset = new Vector2(0f, 0.15f);
            go.AddComponent<AudioListener>();

            var shadow = new GameObject("Shadow");
            shadow.transform.SetParent(go.transform, false);
            shadow.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            shadow.transform.localScale = new Vector3(1f, 0.4f, 1f);
            var shadowSr = shadow.AddComponent<SpriteRenderer>();
            shadowSr.sortingOrder = SortOrder.Units - 1;
            shadowSr.color = new Color(0f, 0f, 0f, 0.35f);

            var weapon = new GameObject("Weapon");
            weapon.transform.SetParent(go.transform, false);
            var wsr = weapon.AddComponent<SpriteRenderer>();
            wsr.sortingOrder = SortOrder.Units + 1;

            go.AddComponent<Combatant>();
            var hero = go.AddComponent<HeroController>();
            hero.weapon = weapon.transform;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
