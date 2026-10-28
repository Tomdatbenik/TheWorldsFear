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

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public string classId = "warrior";
        public int level = 1;
        public int xp;
        public int gold;
        public int skillPoints;
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

        public static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");
        public static bool HasSave => File.Exists(SavePath);

        public static SaveData Data
        {
            get
            {
                if (data == null && !Load()) NewGame("warrior");
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

        public static void NewGame(string classId)
        {
            RunOver = false;
            data = new SaveData { classId = classId };
            ranks = null;
            exploredSet = null;
            var cls = GameDatabase.Instance != null ? GameDatabase.Instance.FindClass(classId) : null;
            if (cls != null && cls.skillTree != null && !string.IsNullOrEmpty(cls.starterNode))
            {
                data.nodeRanks.Add(new NodeRank { id = cls.starterNode, rank = 1 });
                data.equippedSkills[0] = cls.starterNode;
            }
            data.skillPoints = 1;
            data.waypoints.Add("village");
        }

        public static bool Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return false;
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                if (data == null) return false;
                exploredSet = null;
                if (data.equippedSkills == null || data.equippedSkills.Length != 4) data.equippedSkills = new string[4];
                ranks = null;
                // Repair saves that are missing the class's free starter skill.
                var cls = GameDatabase.Instance != null ? GameDatabase.Instance.FindClass(data.classId) : null;
                if (cls != null && !string.IsNullOrEmpty(cls.starterNode) && RankOf(cls.starterNode) == 0)
                {
                    Ranks[cls.starterNode] = 1;
                    if (Array.IndexOf(data.equippedSkills, cls.starterNode) < 0) data.equippedSkills[0] = cls.starterNode;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not load save: " + e.Message);
                return false;
            }
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
                LeveledUp?.Invoke(Data.level);
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

        public static bool RankUp(SkillTreeNode node)
        {
            var tree = Class.skillTree;
            if (tree.Check(node, Ranks, Data.level, Data.skillPoints) != SkillTreeDefinition.CanRankUp.Yes) return false;
            Ranks[node.id] = RankOf(node.id) + 1;
            Data.skillPoints--;

            // Auto-equip a newly learned active into a free matching slot.
            if (node.IsActive && RankOf(node.id) == 1 && Array.IndexOf(Data.equippedSkills, node.id) < 0)
            {
                if (node.active.isUltimate)
                {
                    if (string.IsNullOrEmpty(Data.equippedSkills[3])) Data.equippedSkills[3] = node.id;
                }
                else
                {
                    for (int i = 0; i < 3; i++)
                        if (string.IsNullOrEmpty(Data.equippedSkills[i]))
                        {
                            Data.equippedSkills[i] = node.id;
                            break;
                        }
                }
            }
            NotifySkills();
            return true;
        }

        public static void Respec()
        {
            int spent = SkillTreeDefinition.PointsSpent(Ranks);
            var cls = Class;
            Ranks.Clear();
            Data.equippedSkills = new string[4];
            if (!string.IsNullOrEmpty(cls.starterNode))
            {
                Ranks[cls.starterNode] = 1;
                Data.equippedSkills[0] = cls.starterNode;
                spent -= 1;
            }
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
            ItemsChanged?.Invoke();
            return true;
        }

        public static ItemInstance Equipped(ItemSlot slot) => Data.equipment.Find(i => i.slot == slot);

        public static void EquipItem(ItemInstance item)
        {
            if (!Data.inventory.Remove(item)) return;
            var old = Equipped(item.slot);
            if (old != null)
            {
                Data.equipment.Remove(old);
                Data.inventory.Add(old);
            }
            Data.equipment.Add(item);
            ItemsChanged?.Invoke();
            Changed?.Invoke();
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
        public static int BuyPrice(ItemInstance item) => item.SellValue * 4;

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
