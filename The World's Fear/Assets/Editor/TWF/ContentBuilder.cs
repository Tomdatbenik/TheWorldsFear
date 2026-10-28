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
            db.classes = new List<HeroClassDefinition> { BuildWarrior(), BuildRanger(), BuildMage() };
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

        // ---------- Warrior ----------

        static HeroClassDefinition BuildWarrior()
        {
            string red = "#e8573c", steel = "#c9d4e0", gold = "#f2c14e";

            var cleave = Skill("w_cleave", "Cleave", "CLV", red, Targeting.Skillshot, 2.6f, 5f,
                "A wide sweeping strike that knocks enemies back.",
                new MeleeArcEffect { radius = 2.7f, angle = 170f, damageMult = 1.9f, knockback = 1.3f, color = C(red) });
            var leap = Radius(Skill("w_leap", "Leap Slam", "LEAP", gold, Targeting.Dash, 5.5f, 9f,
                "Leap forward and crash down, stunning enemies where you land.",
                new DashEffect { distance = 5.5f, duration = 0.32f, damageMult = 2.2f, hitRadius = 2.2f, damageAtEnd = true, status = StatusSpec.Stun(0.9f), color = C(gold) }), 2.2f);
            var warcry = Radius(Skill("w_warcry", "War Cry", "CRY", gold, Targeting.Self, 0f, 15f,
                "A battle shout that empowers you and hardens your skin.",
                new BuffEffect { stat = StatType.Damage, kind = ModKind.Percent, value = 0.3f, duration = 6f, color = C(gold) },
                new ShieldEffect { percentMaxHP = 0.15f, duration = 6f }), 1.5f);
            var pound = Radius(Skill("w_pound", "Ground Pound", "PND", steel, Targeting.Self, 0f, 11f,
                "Smash the ground, stunning everything around you.",
                new GroundAreaEffect { atCaster = true, radius = 3f, delay = 0.05f, damageMult = 1.6f, status = StatusSpec.Stun(1.1f), color = C(steel), knockback = 1.5f }), 3f);
            var charge = Skill("w_charge", "Shield Charge", "CHG", steel, Targeting.Dash, 6f, 8f,
                "Charge through enemies, slowing them.",
                new DashEffect { distance = 6f, duration = 0.28f, damageMult = 1.4f, hitRadius = 1.3f, status = StatusSpec.Slow(0.5f, 2f), color = C(steel) });
            var whirl = Ult(Radius(Skill("w_whirlwind", "Whirlwind", "WHRL", red, Targeting.Self, 0f, 38f,
                "Become a storm of steel, hitting everything around you again and again.",
                new BuffEffect { stat = StatType.MoveSpeed, kind = ModKind.Percent, value = 0.25f, duration = 2.6f, scaleWithRank = false, color = C(red) },
                new RepeatEffect
                {
                    times = 10, interval = 0.25f,
                    inner = new List<SkillEffect> { new MeleeArcEffect { radius = 2.6f, angle = 360f, damageMult = 0.75f, color = C(red) } },
                }), 2.6f));
            var shatter = Ult(Radius(Skill("w_earthshatter", "Earthshatter", "QUAKE", gold, Targeting.GroundAoE, 6f, 50f,
                "Split the earth: a huge delayed eruption that stuns for a long time.",
                new GroundAreaEffect { radius = 4.2f, delay = 0.7f, damageMult = 6f, status = StatusSpec.Stun(1.8f), color = C(gold), knockback = 2f }), 4.2f));

            var thickSkin = Passive("w_thickskin", "Thick Skin", "HP", "#8fbf6a", "Toughened by battle.", 5,
                new[] { Pct(StatType.MaxHP, 0.06f), Flat(StatType.Armor, 4f) });
            var mastery = Passive("w_mastery", "Weapon Mastery", "DMG", red, "Every swing hits harder.", 5,
                new[] { Pct(StatType.Damage, 0.05f), Flat(StatType.CritChance, 0.02f) });
            var swift = Passive("w_swift", "Swift Strikes", "SPD", steel, "Attack faster.", 3,
                new[] { Pct(StatType.AttackSpeed, 0.08f) });
            var bloodlust = Passive("w_bloodlust", "Bloodlust", "BLD", "#b0302a", "Kills restore life.", 3, null,
                new HealOnKillProc { percentMaxHPPerRank = 0.025f });
            var hardened = Passive("w_hardened", "Battle Hardened", "SHLD", "#9fc3ff", "Getting hit raises a barrier.", 3, null,
                new ShieldOnHitTakenProc { percentPerRank = 0.06f, cooldown = 8f });
            var execute = Passive("w_execute", "Executioner", "EXE", "#ff4040", "Finish off the weak.", 3, null,
                new ExecuteProc { bonusPerRank = 0.15f, threshold = 0.35f });
            var berserk = Passive("w_berserk", "Berserker", "RAGE", "#ff7040", "Deadlier when wounded.", 3,
                new[] { Flat(StatType.LifeSteal, 0.01f) },
                new LowLifeProc { stat = StatType.Damage, valuePerRank = 0.12f, threshold = 0.45f });
            var unstoppable = Passive("w_unstoppable", "Unstoppable", "UNST", gold, "Nothing slows you down for long.", 3,
                new[] { Pct(StatType.MoveSpeed, 0.04f), Flat(StatType.CooldownReduction, 0.04f) });

            var tree = Asset<SkillTreeDefinition>($"{Root}/Classes/WarriorTree.asset");
            tree.nodes = new List<SkillTreeNode>
            {
                Node(cleave, 0, 0, 1), Node(thickSkin, 0, 1, 1), Node(mastery, 0, 2, 1),
                Node(leap, 1, 0, 2), Node(swift, 1, 1, 3), Node(bloodlust, 1, 2, 3),
                Node(warcry, 2, 0, 5), Node(pound, 2, 1, 6), Node(hardened, 2, 2, 5),
                Node(whirl, 3, 0, 8), Node(execute, 3, 1, 9), Node(berserk, 3, 2, 10),
                Node(shatter, 4, 0, 15), Node(charge, 4, 1, 12), Node(unstoppable, 4, 2, 14),
            };

            var cls = Asset<HeroClassDefinition>($"{Root}/Classes/Warrior.asset");
            cls.id = "warrior";
            cls.displayName = "Warrior";
            cls.description = "A steel-clad brawler. High life and armor, crushing melee skills, and crowd control.";
            cls.tint = Color.white;
            cls.accentColor = C(red);
            cls.baseStats = Stats(190, 13, 14, 5.2f, 1.25f, 0.05f, 0f, 1.5f);
            cls.growthPerLevel = Growth(24, 2.7f, 1.6f, 0.25f);
            cls.basicAttack = HeroClassDefinition.BasicAttackKind.Melee;
            cls.attackRange = 1.9f;
            cls.attackArc = 120f;
            cls.attackDamageMult = 1f;
            cls.showSword = true;
            cls.useArrowSprite = false;
            cls.skillTree = tree;
            cls.starterNode = cleave.id;
            return cls;
        }

        // ---------- Ranger ----------

        static HeroClassDefinition BuildRanger()
        {
            string green = "#7cd65a", ice = "#8fd8ff", venom = "#9be04a", amber = "#f5b041";

            var pierce = Skill("r_pierce", "Piercing Arrow", "PRC", amber, Targeting.Skillshot, 10f, 5f,
                "A powerful arrow that passes through every enemy in a line.",
                new ProjectileEffect { speed = 22f, range = 10f, damageMult = 2.1f, pierce = -1, size = 1.1f, useArrowSprite = true, color = C(amber), hitRadius = 0.45f });
            var multishot = Skill("r_multishot", "Multishot", "MLT", green, Targeting.Skillshot, 7f, 6f,
                "Loose a fan of arrows.",
                new ProjectileEffect { count = 5, spread = 55f, speed = 18f, range = 7f, damageMult = 0.95f, size = 0.9f, useArrowSprite = true, color = C(green) });
            var roll = Skill("r_roll", "Evasive Roll", "ROLL", "#d7d7d7", Targeting.Dash, 4f, 6f,
                "Roll away, briefly untouchable, and shoot faster after.",
                new DashEffect { distance = 4f, duration = 0.2f, invulnerable = true, color = Color.white },
                new BuffEffect { stat = StatType.AttackSpeed, kind = ModKind.Percent, value = 0.3f, duration = 3f, color = C(green) });
            var trap = Radius(Skill("r_trap", "Frost Trap", "TRAP", ice, Targeting.GroundAoE, 7f, 10f,
                "Throw a trap that bursts into freezing shards.",
                new GroundAreaEffect { radius = 2.4f, delay = 0.5f, damageMult = 1.3f, status = StatusSpec.Slow(0.55f, 2.5f), color = C(ice) }), 2.4f);
            var poison = Skill("r_poison", "Venom Volley", "VNM", venom, Targeting.Skillshot, 8f, 7f,
                "Three poisoned arrows.",
                new ProjectileEffect { count = 3, spread = 18f, speed = 18f, range = 8f, damageMult = 0.9f, size = 0.9f, useArrowSprite = true, color = C(venom), status = StatusSpec.Poison(0.5f, 4f) });
            var rain = Ult(Radius(Skill("r_rain", "Arrow Rain", "RAIN", amber, Targeting.GroundAoE, 8f, 35f,
                "Darken the sky with arrows over a wide area.",
                new GroundAreaEffect { radius = 3.4f, delay = 0.4f, ticks = 7, interval = 0.35f, damageMult = 0.85f, status = StatusSpec.Slow(0.3f, 1f), color = C(amber) }), 3.4f));
            var barrage = Ult(Skill("r_barrage", "Barrage", "BRG", green, Targeting.Skillshot, 9f, 45f,
                "Unleash a rapid stream of arrows.",
                new RepeatEffect
                {
                    times = 8, interval = 0.12f,
                    inner = new List<SkillEffect> { new ProjectileEffect { count = 3, spread = 22f, speed = 22f, range = 9f, damageMult = 0.75f, size = 0.9f, useArrowSprite = true, color = C(green) } },
                }));

            var eagle = Passive("r_eagle", "Eagle Eye", "EYE", amber, "Find the weak spots.", 5,
                new[] { Flat(StatType.CritChance, 0.035f), Flat(StatType.CritDamage, 0.1f) });
            var quick = Passive("r_quickdraw", "Quick Draw", "SPD", green, "Faster shooting.", 5,
                new[] { Pct(StatType.AttackSpeed, 0.07f) });
            var venomTips = Passive("r_venomtips", "Venom Tips", "VEN", venom, "Arrows may poison.", 3, null,
                new StatusOnHitProc { chancePerRank = 0.12f, status = StatusSpec.Poison(0.4f, 3f) });
            var nimble = Passive("r_nimble", "Nimble", "NMB", "#d7d7d7", "Light on your feet.", 3,
                new[] { Pct(StatType.MoveSpeed, 0.05f), Flat(StatType.Armor, 3f) });
            var sharp = Passive("r_sharpshooter", "Sharpshooter", "DMG", amber, "More damage on every arrow.", 5,
                new[] { Pct(StatType.Damage, 0.06f) });
            var explosive = Passive("r_explosive", "Explosive Tips", "BOOM", "#ff8a3d", "Enemies may explode on death.", 3, null,
                new ExplodeOnKillProc { chancePerRank = 0.15f, radius = 2.2f, damageMult = 0.9f, color = C("#ff8a3d") });
            var survivor = Passive("r_survivor", "Survivalist", "SRV", "#8fbf6a", "Live off the land.", 3,
                new[] { Pct(StatType.MaxHP, 0.06f), Flat(StatType.HPRegen, 1f) },
                new HealOnKillProc { percentMaxHPPerRank = 0.015f });
            var hunter = Passive("r_hunter", "Hunter's Focus", "FCS", ice, "Skills come back sooner.", 3,
                new[] { Flat(StatType.CooldownReduction, 0.04f) });

            var tree = Asset<SkillTreeDefinition>($"{Root}/Classes/RangerTree.asset");
            tree.nodes = new List<SkillTreeNode>
            {
                Node(pierce, 0, 0, 1), Node(eagle, 0, 1, 1), Node(sharp, 0, 2, 1),
                Node(multishot, 1, 0, 2), Node(quick, 1, 1, 3), Node(nimble, 1, 2, 3),
                Node(roll, 2, 0, 5), Node(trap, 2, 1, 6), Node(venomTips, 2, 2, 5),
                Node(rain, 3, 0, 8), Node(explosive, 3, 1, 9), Node(survivor, 3, 2, 10),
                Node(barrage, 4, 0, 15), Node(poison, 4, 1, 12), Node(hunter, 4, 2, 14),
            };

            var cls = Asset<HeroClassDefinition>($"{Root}/Classes/Ranger.asset");
            cls.id = "ranger";
            cls.displayName = "Ranger";
            cls.description = "A swift archer. Long range, fast attacks and critical hits; fragile up close.";
            cls.tint = C("#c8f0b8");
            cls.accentColor = C(green);
            cls.baseStats = Stats(145, 11, 7, 5.6f, 1.45f, 0.08f, 0f, 1f);
            cls.growthPerLevel = Growth(17, 2.4f, 1f, 0.2f);
            cls.basicAttack = HeroClassDefinition.BasicAttackKind.Projectile;
            cls.attackRange = 6.5f;
            cls.attackDamageMult = 1f;
            cls.projectileSpeed = 20f;
            cls.projectileColor = C(amber);
            cls.useArrowSprite = true;
            cls.showSword = false;
            cls.skillTree = tree;
            cls.starterNode = pierce.id;
            return cls;
        }

        // ---------- Mage ----------

        static HeroClassDefinition BuildMage()
        {
            string fire = "#ff6a2b", frost = "#7fd3ff", arcane = "#b884ff", storm = "#ffe066";

            var fireball = Radius(Skill("m_fireball", "Fireball", "FIRE", fire, Targeting.Skillshot, 9f, 4.5f,
                "Hurl a ball of fire that explodes and sets enemies alight.",
                new ProjectileEffect { speed = 14f, range = 9f, damageMult = 2.2f, explodeRadius = 1.7f, size = 0.75f, color = C(fire), status = StatusSpec.Burn(0.35f, 3f) }), 1.7f);
            var nova = Radius(Skill("m_nova", "Frost Nova", "NOVA", frost, Targeting.Self, 0f, 9f,
                "A burst of cold that freezes enemies in place.",
                new GroundAreaEffect { atCaster = true, radius = 3.2f, delay = 0.05f, damageMult = 1.3f, status = StatusSpec.Slow(0.65f, 2.5f), color = C(frost) }), 3.2f);
            var blink = Skill("m_blink", "Blink", "BLNK", arcane, Targeting.Dash, 5.5f, 8f,
                "Teleport a short distance.",
                new DashEffect { distance = 5.5f, blink = true, invulnerable = true, color = C(arcane) });
            var orb = Skill("m_orb", "Arcane Orb", "ORB", arcane, Targeting.Skillshot, 11f, 7f,
                "A slow orb of raw magic that tears through everything.",
                new ProjectileEffect { speed = 7f, range = 11f, damageMult = 1.7f, pierce = -1, size = 1.2f, hitRadius = 0.8f, color = C(arcane) });
            var barrier = Radius(Skill("m_barrier", "Barrier", "BARR", frost, Targeting.Self, 0f, 15f,
                "Wrap yourself in a protective ward.",
                new ShieldEffect { percentMaxHP = 0.35f, duration = 6f }), 1.2f);
            var chain = Radius(Skill("m_storm", "Thunderclap", "ZAP", storm, Targeting.GroundAoE, 7f, 10f,
                "Call lightning on a spot, stunning enemies.",
                new GroundAreaEffect { radius = 2.2f, delay = 0.35f, damageMult = 2f, status = StatusSpec.Stun(0.8f), color = C(storm) }), 2.2f);
            var meteor = Ult(Radius(Skill("m_meteor", "Meteor", "MTR", fire, Targeting.GroundAoE, 8f, 40f,
                "Call down a meteor that devastates a large area.",
                new GroundAreaEffect { radius = 3.6f, delay = 0.9f, damageMult = 6.5f, status = StatusSpec.Burn(0.3f, 4f), color = C(fire), knockback = 2f }), 3.6f));
            var blizzard = Ult(Radius(Skill("m_blizzard", "Blizzard", "BLZ", frost, Targeting.GroundAoE, 8f, 45f,
                "A raging storm of ice over a wide area.",
                new GroundAreaEffect { radius = 3.8f, delay = 0.3f, ticks = 10, interval = 0.35f, damageMult = 0.75f, status = StatusSpec.Slow(0.45f, 1f), color = C(frost) }), 3.8f));

            var mind = Passive("m_arcanemind", "Arcane Mind", "CDR", arcane, "Spells recover faster.", 5,
                new[] { Flat(StatType.CooldownReduction, 0.03f) });
            var power = Passive("m_power", "Spell Power", "DMG", fire, "Raw magical might.", 5,
                new[] { Pct(StatType.Damage, 0.06f) });
            var ignite = Passive("m_ignite", "Ignite", "IGN", fire, "Attacks may set enemies on fire.", 3, null,
                new StatusOnHitProc { chancePerRank = 0.12f, status = StatusSpec.Burn(0.4f, 3f) });
            var manaShield = Passive("m_manashield", "Mana Shield", "SHLD", frost, "Getting hit raises a magical barrier.", 3, null,
                new ShieldOnHitTakenProc { percentPerRank = 0.08f, cooldown = 9f });
            var static_ = Passive("m_static", "Static Charge", "STC", storm, "Hits may stun.", 3, null,
                new StatusOnHitProc { chancePerRank = 0.04f, status = StatusSpec.Stun(0.6f) });
            var focus = Passive("m_focus", "Focus", "CRIT", storm, "Precise spellcasting.", 3,
                new[] { Flat(StatType.CritChance, 0.04f), Flat(StatType.CritDamage, 0.12f) });
            var vitality = Passive("m_vitality", "Arcane Vitality", "HP", "#8fbf6a", "Magic sustains you.", 3,
                new[] { Pct(StatType.MaxHP, 0.07f), Flat(StatType.HPRegen, 1f) });
            var chill = Passive("m_frostbite", "Frostbite", "CHL", frost, "Hits may chill enemies.", 3, null,
                new StatusOnHitProc { chancePerRank = 0.15f, status = StatusSpec.Slow(0.35f, 2f) });

            var tree = Asset<SkillTreeDefinition>($"{Root}/Classes/MageTree.asset");
            tree.nodes = new List<SkillTreeNode>
            {
                Node(fireball, 0, 0, 1), Node(power, 0, 1, 1), Node(vitality, 0, 2, 1),
                Node(nova, 1, 0, 2), Node(mind, 1, 1, 3), Node(chill, 1, 2, 3),
                Node(blink, 2, 0, 5), Node(chain, 2, 1, 6), Node(ignite, 2, 2, 5),
                Node(meteor, 3, 0, 8), Node(manaShield, 3, 1, 9), Node(focus, 3, 2, 10),
                Node(blizzard, 4, 0, 15), Node(orb, 4, 1, 12), Node(static_, 4, 2, 14),
            };
            // Barrier sits beside Frost Nova.
            tree.nodes.Insert(5, Node(barrier, 1, 3, 4));

            var cls = Asset<HeroClassDefinition>($"{Root}/Classes/Mage.asset");
            cls.id = "mage";
            cls.displayName = "Mage";
            cls.description = "A master of fire, frost and lightning. Huge area damage and control; the frailest hero.";
            cls.tint = C("#d8c8ff");
            cls.accentColor = C(arcane);
            cls.baseStats = Stats(135, 15, 6, 5.2f, 1.1f, 0.05f, 0.05f, 1f);
            cls.growthPerLevel = Growth(16, 3.1f, 0.9f, 0.2f);
            cls.basicAttack = HeroClassDefinition.BasicAttackKind.Projectile;
            cls.attackRange = 6f;
            cls.attackDamageMult = 1f;
            cls.projectileSpeed = 13f;
            cls.projectileColor = C(arcane);
            cls.useArrowSprite = false;
            cls.showSword = false;
            cls.skillTree = tree;
            cls.starterNode = fireball.id;
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
