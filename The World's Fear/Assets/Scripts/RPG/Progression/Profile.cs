using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TWF
{
    [Serializable]
    public class NodeRank
    {
        public string id;
        public int rank;
    }

    // A monster lair the hero has come across: when it was founded (in play time), how big it began, and whether
    // it has been destroyed. The world is endless, so only lairs actually found are written down.
    [Serializable]
    public class LairRecord
    {
        public string key;
        public float born;
        public int startSize;
        public bool destroyed;
        public int destroyedSize;
    }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public int worldSeed;           // 0 on saves from before each hero had their own world
        public string classId = "adventurer";
        public int level = 1;
        public int xp;
        public int gold;
        public int skillPoints;          // passive points
        public int attributePoints;
        public int[] attributes = new int[5];
        public List<PartRef> parts = new List<PartRef>();
        public List<CraftedSkill> craftedSkills = new List<CraftedSkill>();
        public List<NodeRank> nodeRanks = new List<NodeRank>();
        public string[] equippedSkills = new string[4]; // node ids; slot 3 = ultimate
        public List<ItemInstance> inventory = new List<ItemInstance>();
        public List<ItemInstance> equipment = new List<ItemInstance>();
        public List<string> waypoints = new List<string>();
        public string lastWaypoint = "village";
        public bool bossDefeated;
        public int potions = 3;
        public List<ItemInstance> shopStock = new List<ItemInstance>();
        public int shopStockLevel;
        public int deaths;
        public int kills;
        public List<long> explored = new List<long>();
        public float farthest;          // furthest distance from the village this run
        public string bestItem = "";
        public int bossesKilled;
        public List<string> guardiansSlain = new List<string>();
        public List<int> weaponSkillsOwned = new List<int>();          // WeaponSkillId values
        public int[] weaponSkillAttached = new int[10];                 // per WeaponType: skill id + 1, 0 = none
        public List<int> masteriesOwned = new List<int>();              // WeaponType values with their mastery learned
        public List<string> revealedGates = new List<string>();         // boss lairs bought from the cartographer
        // Codex
        public List<CodexMonster> codexMonsters = new List<CodexMonster>();
        public List<string> bossesSeen = new List<string>();
        public List<string> codexPowers = new List<string>();
        public List<int> codexWeapons = new List<int>();
        public int[] lootByRarity = new int[5];
        public List<string> openedChests = new List<string>();
        public Appearance appearance = new Appearance();   // the four guardians unseal the World's Fear
        public List<string> defeatedGates = new List<string>();
        public bool hasReturn;          // come back to this spot after a boss fight
        public float returnX, returnY;
        public bool runCounted;
        public float playTime;          // seconds played in this run, the clock lairs grow by
        public List<LairRecord> lairs = new List<LairRecord>();         // this run has been added to the legacy's finished runs
    }

    // The player's persistent progress: level, XP, gold, skills, items.
    public static class Profile
    {
        public const int MaxLevel = 100;
        public const int InventorySize = 24;
        public const int MaxPotions = 5;

        static SaveData data;
        static Dictionary<string, int> ranks;

        public static event Action Changed;          // anything that the HUD shows
        public static event Action<int> LeveledUp;
        public static event Action SkillsChanged;
        public static event Action ItemsChanged;
        public static void NotifyItems() { ItemsChanged?.Invoke(); Changed?.Invoke(); }

        // ---------- Hero slots ----------
        // Up to three heroes, each in a file of their own with their own world. Unlocks and achievements (Legacy)
        // are shared by all of them.
        public const int Slots = 3;

        public static int Slot
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("twf_slot", 0), 0, Slots - 1);
            set { PlayerPrefs.SetInt("twf_slot", Mathf.Clamp(value, 0, Slots - 1)); PlayerPrefs.Save(); }
        }

        public static string SlotPath(int slot)
        {
            MoveOldSave();
            return Path.Combine(Application.persistentDataPath, $"hero_{slot + 1}.json");
        }

        public static string SavePath => SlotPath(Slot);
        public static bool HasSave => File.Exists(SavePath);
        public static bool SlotUsed(int slot) => File.Exists(SlotPath(slot));

        // Saves from before the slots lived in save.json: that hero becomes the first slot.
        static bool moved;
        static void MoveOldSave()
        {
            if (moved) return;
            moved = true;
            try
            {
                string old = Path.Combine(Application.persistentDataPath, "save.json");
                string first = Path.Combine(Application.persistentDataPath, "hero_1.json");
                if (File.Exists(old) && !File.Exists(first)) File.Move(old, first);
            }
            catch (Exception e) { Debug.LogWarning("Could not move the old save: " + e.Message); }
        }

        // A slot's hero read for the title screen, without making it the current one. Null when empty or unreadable.
        public static SaveData Peek(int slot)
        {
            try
            {
                var path = SlotPath(slot);
                if (!File.Exists(path)) return null;
                var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                return d != null && d.classId == "adventurer" ? d : null;
            }
            catch { return null; }
        }

        // Switches to another slot: whatever hero was loaded is let go, so nothing of it carries over.
        public static void UseSlot(int slot)
        {
            if (slot == Slot && data != null && !RunOver) return;
            Slot = slot;
            data = null;
            ranks = null;
            exploredSet = null;
            lairIndex = null;
            RunOver = false;
        }

        public static void DeleteSlot(int slot)
        {
            try { if (File.Exists(SlotPath(slot))) File.Delete(SlotPath(slot)); }
            catch (Exception e) { Debug.LogWarning("Could not delete hero: " + e.Message); }
            if (slot == Slot) { data = null; ranks = null; exploredSet = null; lairIndex = null; }
        }

        public static SaveData Data
        {
            get
            {
                if (data == null && !Load()) NewGame(WeaponType.Sword);
                return data;
            }
        }

        public static Dictionary<string, int> Ranks
        {
            get
            {
                if (ranks == null)
                {
                    ranks = new Dictionary<string, int>();
                    foreach (var n in Data.nodeRanks) ranks[n.id] = n.rank;
                }
                return ranks;
            }
        }

        public static HeroClassDefinition Class => GameDatabase.Instance.FindClass(Data.classId) ?? GameDatabase.Instance.classes[0];

        // A new hero: no classes, the starting weapon decides the basic attack.
        public static void NewGame(WeaponType starterWeapon, Appearance look = null)
        {
            RunOver = false;
            lairIndex = null;
            data = new SaveData { classId = "adventurer", appearance = look ?? new Appearance(), worldSeed = new System.Random().Next(1, int.MaxValue) };
            WorldGen.SetSeed(data.worldSeed);
            ranks = null;
            exploredSet = null;
            data.skillPoints = 1;
            data.attributePoints = 3;
            data.waypoints.Add("village");
            var weapon = ItemGenerator.Generate(1, Rarity.Common, ItemSlot.Weapon, new System.Random(), starterWeapon);
            data.equipment.Add(weapon);
            Crafting.GiveStarterKit(data);
            Legacy.ApplyStartBonuses(data);
        }

        static Dictionary<string, LairRecord> lairIndex;

        // The saved record of a lair, or null when it has never been found.
        public static LairRecord Lair(string key)
        {
            if (lairIndex == null)
            {
                lairIndex = new Dictionary<string, LairRecord>();
                if (Data.lairs == null) Data.lairs = new List<LairRecord>();
                foreach (var l in Data.lairs) if (l != null && l.key != null) lairIndex[l.key] = l;
            }
            return lairIndex.TryGetValue(key, out var r) ? r : null;
        }

        public static void AddLair(LairRecord r)
        {
            Lair(r.key);
            Data.lairs.Add(r);
            lairIndex[r.key] = r;
        }

        public static bool Load()
        {
            lairIndex = null;
            RunOver = false;
            try
            {
                if (!File.Exists(SavePath)) return false;
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                if (data == null) return false;
                // Saves from the class-based version can't carry over.
                if (data.classId != "adventurer")
                {
                    data = null;
                    return false;
                }
                exploredSet = null;
                if (data.worldSeed == 0) data.worldSeed = WorldGen.LegacySeed;
                WorldGen.SetSeed(data.worldSeed);
                if (data.equippedSkills == null || data.equippedSkills.Length != 4) data.equippedSkills = new string[4];
                if (data.attributes == null || data.attributes.Length != Attributes.Count) data.attributes = new int[Attributes.Count];
                if (data.weaponSkillAttached == null || data.weaponSkillAttached.Length != 10) data.weaponSkillAttached = new int[10];
                data.weaponSkillsOwned ??= new List<int>();
                data.masteriesOwned ??= new List<int>();
                data.revealedGates ??= new List<string>();
                data.codexMonsters ??= new List<CodexMonster>();
                data.bossesSeen ??= new List<string>();
                data.codexPowers ??= new List<string>();
                data.codexWeapons ??= new List<int>();
                data.openedChests ??= new List<string>();
                data.appearance ??= new Appearance();
                if (data.lootByRarity == null || data.lootByRarity.Length < 5) data.lootByRarity = new int[5];
                // Older legendary / mythic items could have crit chance and other fraction stats rounded to 0.
                foreach (var list in new[] { data.inventory, data.equipment, data.shopStock })
                    if (list != null) foreach (var it in list) { ItemGenerator.RepairZeroStats(it); MythicPowers.Upgrade(it); }
                ranks = null;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not load save: " + e.Message);
                return false;
            }
        }

        // ---------- Attributes ----------

        public static int Attr(Attribute a) => Data.attributes[(int)a];

        public static bool AddAttribute(Attribute a)
        {
            if (Data.attributePoints <= 0) return false;
            Data.attributePoints--;
            Data.attributes[(int)a]++;
            ItemsChanged?.Invoke(); // rebuilds hero stats
            Changed?.Invoke();
            return true;
        }

        public static int AttributeResetCost => 40 + Data.level * 20;

        public static bool ResetAttributes()
        {
            if (Data.gold < AttributeResetCost) return false;
            int spent = 0;
            for (int i = 0; i < Attributes.Count; i++) { spent += Data.attributes[i]; Data.attributes[i] = 0; }
            if (spent == 0) return false;
            Data.gold -= AttributeResetCost;
            Data.attributePoints += spent;
            ItemsChanged?.Invoke();
            NotifySkills();
            return true;
        }

        public static bool RunOver { get; private set; }

        public static void Save()
        {
            if (data == null || RunOver) return;
            var current = Ranks; // build the lookup before clearing the list it may be built from
            data.nodeRanks = new List<NodeRank>();
            foreach (var kv in current) data.nodeRanks.Add(new NodeRank { id = kv.Key, rank = kv.Value });
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not save: " + e.Message);
            }
        }

        public static void DeleteSave()
        {
            RunOver = HeroController.Instance != null && HeroController.Instance.Combatant.IsDead;
            if (File.Exists(SavePath)) File.Delete(SavePath);
            // After a death the data stays in memory (for the summary) but can never be saved again.
            if (RunOver) return;
            data = null;
            ranks = null;
            exploredSet = null;
        }

        // ---------- Experience ----------

        public static int XPToNext(int level) => Mathf.RoundToInt(100f * Mathf.Pow(level, 1.5f));

        public static float XPFraction => Data.level >= MaxLevel ? 1f : (float)Data.xp / XPToNext(Data.level);

        // Monsters well below your level give less XP.
        public static int MonsterXP(int monsterLevel, MonsterRank rank)
        {
            float baseXP = 12f + monsterLevel * 6f;
            float mult = rank == MonsterRank.Elite ? 3f : rank == MonsterRank.Rare ? 6f : rank == MonsterRank.Boss ? 50f : 1f;
            int diff = Data.level - monsterLevel;
            float penalty = diff <= 3 ? 1f : Mathf.Max(0.1f, 1f - (diff - 3) * 0.2f);
            return Mathf.Max(1, Mathf.RoundToInt(baseXP * mult * penalty));
        }

        public static void AddXP(int amount)
        {
            if (Data.level >= MaxLevel) return;
            Data.xp += amount;
            while (Data.level < MaxLevel && Data.xp >= XPToNext(Data.level))
            {
                Data.xp -= XPToNext(Data.level);
                Data.level++;
                Data.skillPoints++;
                Data.attributePoints += 3;
                LeveledUp?.Invoke(Data.level);
                Legacy.OnLevel(Data.level);
            }
            if (Data.level >= MaxLevel) Data.xp = 0;
            Changed?.Invoke();
        }

        public static void AddGold(int amount)
        {
            Data.gold = Mathf.Max(0, Data.gold + amount);
            Changed?.Invoke();
        }

        // ---------- Skills ----------

        public static int RankOf(string nodeId) => nodeId != null && Ranks.TryGetValue(nodeId, out int r) ? r : 0;

        // Passive board: spend passive points on nodes.
        public static bool RankUp(SkillTreeNode node)
        {
            var tree = Class.skillTree;
            if (tree.Check(node, Ranks, Data.level, Data.skillPoints) != SkillTreeDefinition.CanRankUp.Yes) return false;
            Ranks[node.id] = RankOf(node.id) + 1;
            Data.skillPoints--;
            NotifySkills();
            return true;
        }

        public static void Respec()
        {
            int spent = SkillTreeDefinition.PointsSpent(Ranks);
            Ranks.Clear();
            Data.skillPoints += Mathf.Max(0, spent);
            NotifySkills();
        }

        public static void Equip(int slot, string nodeId)
        {
            for (int i = 0; i < 4; i++)
                if (Data.equippedSkills[i] == nodeId) Data.equippedSkills[i] = null;
            Data.equippedSkills[slot] = nodeId;
            NotifySkills();
        }

        public static void NotifySkills()
        {
            SkillsChanged?.Invoke();
            Changed?.Invoke();
        }

        // ---------- Items ----------

        public static bool AddItem(ItemInstance item)
        {
            if (Data.inventory.Count >= InventorySize) return false;
            Data.inventory.Add(item);
            Codex.RecordItem(item);
            ItemsChanged?.Invoke();
            return true;
        }

        public static ItemInstance Equipped(ItemSlot slot) => Data.equipment.Find(i => i.slot == slot);

        // The weapon the hero is fighting with right now; false when bare-handed.
        public static bool RunWeapon(out WeaponType weapon)
        {
            weapon = Weapons.Equipped;
            return weapon != WeaponType.Fists;
        }

        public static bool IsTwoHanded(ItemInstance item) =>
            item != null && item.slot == ItemSlot.Weapon && Weapons.Get((WeaponType)item.weaponType).twoHanded;

        // False when the bag can't take what this pushes out: a two-handed weapon also frees the
        // off hand, and an off-hand item frees the hands from a two-handed weapon.
        public static bool EquipItem(ItemInstance item)
        {
            if (!Data.inventory.Contains(item) || WeaponMasteries.NeedsDualWield(item)) return false;
            var old = Equipped(item.slot);
            ItemInstance bumped = null;
            if (IsTwoHanded(item)) bumped = Equipped(ItemSlot.OffHand);
            else if (item.slot == ItemSlot.OffHand && IsTwoHanded(Equipped(ItemSlot.Weapon))) bumped = Equipped(ItemSlot.Weapon);
            int free = InventorySize - (Data.inventory.Count - 1);
            if ((old != null ? 1 : 0) + (bumped != null ? 1 : 0) > free) return false;
            Data.inventory.Remove(item);
            foreach (var o in new[] { old, bumped })
            {
                if (o == null) continue;
                Data.equipment.Remove(o);
                Data.inventory.Add(o);
            }
            Data.equipment.Add(item);
            ItemsChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }

        // With Dual Wield learned, a sword can go in either hand: a main-hand sword becomes an off-hand blade (half its
        // damage, as off-hand swords have) and an off-hand sword becomes a main-hand one. Only from the bag.
        public static bool CanSwitchHand(ItemInstance item) =>
            item != null && Data.inventory.Contains(item) && WeaponMasteries.Has(WeaponType.Sword) &&
            ((item.slot == ItemSlot.Weapon && item.weaponType == (int)WeaponType.Sword) || WeaponMasteries.IsOffHandSword(item));

        public static bool EquipInOtherHand(ItemInstance item)
        {
            if (!CanSwitchHand(item)) return false;
            float main = Weapons.Get(WeaponType.Sword).implicitDamage, off = 0.5f;
            var before = (item.slot, item.weaponType, item.offHandKind, item.implicitValue);
            if (item.slot == ItemSlot.Weapon)
            {
                item.slot = ItemSlot.OffHand;
                item.offHandKind = (int)OffHandKind.Sword;
                item.implicitValue = Mathf.Round(item.implicitValue * off / main);
            }
            else
            {
                item.slot = ItemSlot.Weapon;
                item.weaponType = (int)WeaponType.Sword;
                item.implicitValue = Mathf.Round(item.implicitValue * main / off);
            }
            if (EquipItem(item)) return true;
            (item.slot, item.weaponType, item.offHandKind, item.implicitValue) = before;   // bag full: leave it as it was
            return false;
        }

        public static void UnequipItem(ItemInstance item)
        {
            if (Data.inventory.Count >= InventorySize) return;
            if (!Data.equipment.Remove(item)) return;
            Data.inventory.Add(item);
            ItemsChanged?.Invoke();
            Changed?.Invoke();
        }

        public static void SellItem(ItemInstance item)
        {
            if (!Data.inventory.Remove(item)) return;
            AddGold(item.SellValue);
            ItemsChanged?.Invoke();
        }

        // ---------- Shop ----------

        public static int PotionPrice => 20 + Data.level * 6;
        public static int RefreshPrice => 25 + Data.level * 10;
        public static int BuyPrice(ItemInstance item) => item.SellValue * 5;

        // The merchant restocks whenever you've gained a level since your last visit.
        public static List<ItemInstance> ShopStock()
        {
            if (Data.shopStock.Count == 0 || Data.shopStockLevel != Data.level) RollShopStock();
            return Data.shopStock;
        }

        public static void RollShopStock()
        {
            var rng = new System.Random();
            Data.shopStock.Clear();
            for (int i = 0; i < 8; i++)
            {
                var rarity = rng.NextDouble() < 0.08 ? Rarity.Rare : rng.NextDouble() < 0.6 ? Rarity.Magic : Rarity.Common;
                // Always offer a weapon and some armor.
                var slot = i == 0 ? ItemSlot.Weapon : ItemGenerator.RandomSlot(rng);
                Data.shopStock.Add(ItemGenerator.Generate(Data.level, rarity, slot, rng));
            }
            // Now and then the merchant has a mythic item for sale (at a mythic price).
            if (rng.NextDouble() < 0.06)
                Data.shopStock[Data.shopStock.Count - 1] = ItemGenerator.Generate(Data.level, Rarity.Mythic, ItemGenerator.RandomSlot(rng), rng);
            Data.shopStockLevel = Data.level;
        }

        public static bool BuyItem(ItemInstance item)
        {
            int price = BuyPrice(item);
            if (Data.gold < price || Data.inventory.Count >= InventorySize || !Data.shopStock.Remove(item)) return false;
            Data.gold -= price;
            Data.inventory.Add(item);
            ItemsChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }

        public static bool BuyPotion()
        {
            if (Data.gold < PotionPrice || Data.potions >= MaxPotions) return false;
            Data.gold -= PotionPrice;
            Data.potions++;
            Changed?.Invoke();
            return true;
        }

        // ---------- Exploration ----------

        static HashSet<long> exploredSet;

        public static long ChunkKey(Vector2Int c) => ((long)c.x << 32) | (uint)c.y;

        public static bool IsExplored(Vector2Int c)
        {
            if (exploredSet == null) exploredSet = new HashSet<long>(Data.explored);
            return exploredSet.Contains(ChunkKey(c));
        }

        public static void MarkExplored(Vector2Int c)
        {
            if (IsExplored(c)) return;
            exploredSet.Add(ChunkKey(c));
            Data.explored.Add(ChunkKey(c));
        }

        public static void UnlockWaypoint(string id)
        {
            if (!Data.waypoints.Contains(id)) Data.waypoints.Add(id);
        }
    }
}
