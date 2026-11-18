using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public class Quest
    {
        public string id, title;
        public string goalText;         // one line, for the tracker on the HUD
        public string story;            // the quest book's explanation: what to do, where, and why
        public int goal = 1;
        public Func<int> count;         // how far along, read from the hero's save
        public bool fromStart;          // counted from the moment the quest began (monsters slain), not over the whole run
        public int gold, potions;
    }

    // The quest line: one quest at a time, in order, leading a new hero from the town gates to the World's Fear.
    // Each quest is measured from what the save already records (level, crafted skills, waypoints, slain guardians),
    // so nothing in the game has to report to it, and a hero from before quests existed walks through the ones
    // already done.
    public static class Quests
    {
        public const string Finished = "done";

        static List<Quest> all;

        public static event Action Changed;
        // A quest began that the player has not read in the book yet.
        public static bool Unread { get; private set; }

        static SaveData D => Profile.Data;

        static int Spent(int[] attributes)
        {
            int n = 0;
            foreach (int a in attributes) n += a;
            return n;
        }

        static int LairsDestroyed()
        {
            int n = 0;
            if (D.lairs != null)
                foreach (var l in D.lairs) if (l != null && l.destroyed) n++;
            return n;
        }

        // openedChests also holds the marks of cleared and caved-in dungeons.
        static int ChestsOpened()
        {
            int n = 0;
            foreach (var k in D.openedChests) if (!k.EndsWith("_done") && !k.EndsWith("_fallen")) n++;
            return n;
        }

        static int DungeonsCleared()
        {
            int n = 0;
            foreach (var k in D.openedChests) if (k.EndsWith("_done")) n++;
            return n;
        }

        static Quest Boss(BossKind k, string id, string title, string story, int gold) => new Quest
        {
            id = id, title = title, goalText = $"Defeat {BossTitan.BossName(k)}", gold = gold, potions = 1,
            count = () => WorldManager.Slain(k) ? 1 : 0,
            story = story + $"\n\nIts fall unlocks: <color=#ffd040>{BossUnlocks.Describe(k)}</color>.",
        };

        public static List<Quest> All
        {
            get
            {
                if (all != null) return all;
                all = new List<Quest>
                {
                    new Quest
                    {
                        id = "farms", title = "Out to the farms", goalText = "Reach level 2", goal = 2, count = () => D.level, gold = 100,
                        story = "The town is safe. The country round it is not.\n\n" +
                                "Leave through any of the four gates. The farms that ring the walls are overrun, but the monsters there are the weakest you will meet: " +
                                "they grow a level stronger for every stretch you walk away from town. Fight among the fields until you reach level 2.\n\n" +
                                "If a fight goes badly, drink a potion or walk back inside the walls. Be careful: a hero who falls is gone for good.",
                    },
                    new Quest
                    {
                        id = "craft", title = "Your first skill", goalText = "Craft a skill at the Arcane Forge", count = () => D.craftedSkills.Count - 1, gold = 60, potions = 1,
                        story = "Skills are made, not found.\n\n" +
                                "Walk back to the market square in town and talk to the Arcane Forge. A skill is one element and one delivery: " +
                                "you own Physical, and the Single delivery (it strikes one enemy at once) that you have not used yet. Pick them and press Craft.\n\n" +
                                "Crafting costs gold: the reward of your last quest pays for this one. The new skill goes into a free skill slot by itself. " +
                                "Monsters drop more parts, and the Forge sells some.",
                    },
                    new Quest
                    {
                        id = "points", title = "Grow stronger", goalText = "Spend 3 attribute points and a passive point", goal = 4, gold = 50,
                        count = () => Mathf.Min(3, Spent(D.attributes)) + Mathf.Min(1, SkillTreeDefinition.PointsSpent(Profile.Ranks)),
                        story = "Every level gives you 3 attribute points and 1 passive point. A red number on the Skills button (the book, top right) says you have some left.\n\n" +
                                "Open Skills. On the Attributes page, put points into Strength, Vitality, Dexterity, Swiftness or Magic: each one tells you what it gives, " +
                                "and better skill parts ask for them. On the Passives page, pick a passive for a lasting bonus.",
                    },
                    new Quest
                    {
                        id = "gear", title = "Dress for battle", goalText = "Wear 4 pieces of gear", goal = 4, count = () => D.equipment.Count, gold = 75,
                        story = "Monsters drop weapons and armour, and the Merchant on the market square sells some.\n\n" +
                                "Open your Bag (top right), tap an item to see what it gives, and equip it. Fill four slots: your weapon counts as one. " +
                                "Sell what you do not need: the bag only holds so much.",
                    },
                    new Quest
                    {
                        id = "hunt", title = "Thin them out", goalText = "Slay 40 monsters", goal = 40, fromStart = true, count = () => D.kills, gold = 100, potions = 1,
                        story = "Time to put the new skill and gear to work.\n\n" +
                                "Every kind of monster has an element. Hit its weakness and you deal half again as much damage; hit it with its own element and you deal half. " +
                                "The Codex (top right) keeps a page on every monster you have slain, with its weakness.",
                    },
                    new Quest
                    {
                        id = "level5", title = "Beyond the farms", goalText = "Reach level 5", goal = 5, count = () => D.level, gold = 150,
                        story = "The fields are getting too easy: monsters more than three levels below you give less and less experience.\n\n" +
                                "Walk further out. The name and level of each land shows as you enter it. Stay near lands of your own level, or a little above if you feel brave.",
                    },
                    new Quest
                    {
                        id = "waypoint", title = "A way back", goalText = "Discover a waypoint", count = () => D.waypoints.Count - 1, gold = 100,
                        story = "The country is large and the walk home is long.\n\n" +
                                "Out in the wild stand waypoints: stone portals like the one beside the market square. Step into one to discover it. " +
                                "From then on you can travel between any waypoints you know, so a trip to the shops costs you nothing. They show on your Map.",
                    },
                    new Quest
                    {
                        id = "lair", title = "Burn it out", goalText = "Destroy a monster lair", count = LairsDestroyed, gold = 150, potions = 1,
                        story = "Goblin camps, spider nests and dens are scattered over the country. Monsters keep coming out of them for as long as their huts or nests stand.\n\n" +
                                "Attack the huts themselves and break every one to destroy the lair for good: it pays gold and a good deal of experience. " +
                                "Linger and only kill what comes out, and the lair sends champions, each stronger than the last. A lair left alone grows.",
                    },
                    new Quest
                    {
                        id = "chest", title = "Fortune favours the bold", goalText = "Open a chest", count = ChestsOpened, gold = 100,
                        story = "Chests stand out in the country, and most are guarded.\n\n" +
                                "A challenge chest opens only after you survive its waves of monsters: the number on it says how many. Stay close to it: walk away and the challenge fails. " +
                                "The more waves, the better the loot. Start with a small one.",
                    },
                    new Quest
                    {
                        id = "weaponskill", title = "Tricks of the trade", goalText = "Learn a weapon skill from the Weaponmaster", count = () => D.weaponSkillsOwned.Count, gold = 150,
                        story = "Your weapon can do more than its basic attack.\n\n" +
                                "The Weaponmaster on the market square teaches weapon skills for gold. Learn one for the kind of weapon you carry and attach it: " +
                                "it gets a button of its own beside your crafted skills.",
                    },
                    new Quest
                    {
                        id = "dungeon", title = "Into the dark", goalText = "Clear a dungeon", count = DungeonsCleared, gold = 250, potions = 1,
                        story = "Here and there a stairwell leads down under the ground.\n\n" +
                                "The monsters below are two levels above the land round the stairs, the rooms hold chests, and at the end waits a warden. " +
                                "Slay the warden to clear the dungeon. It caves in once you climb out, so loot what you can first.",
                    },
                    new Quest
                    {
                        id = "level10", title = "A name worth knowing", goalText = "Reach level 10", goal = 10, count = () => D.level, gold = 300,
                        story = "The guardians that hold this country are far beyond you still.\n\n" +
                                "Keep hunting, keep crafting better skills as you find parts, and visit the Mystic on the market square: " +
                                "there one property of an item can be reforged into another, for gold.",
                    },
                    Boss(BossKind.Slime, "slime", "The first guardian",
                        $"Four guardians keep the World's Fear sealed away, and the first is {BossTitan.BossName(BossKind.Slime)}.\n\n" +
                        $"You cannot grow past level {Profile.LevelCaps[0]} until it falls. Its lair lies far out: the Cartographer on the market square sells a map that marks it. " +
                        "The fortress is held by generals. Defeat them to open the way to the guardian.", 500),
                    Boss(BossKind.Spider, "spider", "The brood mother",
                        $"The second guardian is {BossTitan.BossName(BossKind.Spider)}, further out still. Her lair was sealed until the first guardian fell.\n\n" +
                        $"Your level now stops at {Profile.LevelCaps[1]} until she is dead. With two elements in one skill you can hit more weaknesses: craft for the land you are walking into.", 750),
                    Boss(BossKind.Zombie, "zombie", "The rotting colossus",
                        $"The third guardian is {BossTitan.BossName(BossKind.Zombie)}.\n\n" +
                        $"Your level stops at {Profile.LevelCaps[2]} until it falls. Weapon arts are open at the Arcane Forge now: deliveries that use your weapon itself.", 1000),
                    Boss(BossKind.Skeleton, "skeleton", "The bone king",
                        $"The last guardian is {BossTitan.BossName(BossKind.Skeleton)}.\n\n" +
                        $"Your level stops at {Profile.LevelCaps[3]} until he falls. When he does, the seal on the World's Fear breaks.", 1500),
                    Boss(BossKind.WorldsFear, "worldsfear", "The World's Fear",
                        "All four guardians are dead and the seal is broken.\n\n" +
                        "Find the World's Fear and end it. Everything you have crafted, found and learned was for this.", 3000),
                };
                return all;
            }
        }

        // ---------- The quest in hand ----------

        public static int Index
        {
            get
            {
                string id = D.quest;
                if (id == Finished) return All.Count;
                int i = string.IsNullOrEmpty(id) ? 0 : All.FindIndex(q => q.id == id);
                return i < 0 ? 0 : i;
            }
        }

        public static Quest Current => Index < All.Count ? All[Index] : null;

        public static int Progress(Quest q)
        {
            if (q == null) return 0;
            int n = q.count() - (q.fromStart ? D.questBase : 0);
            return Mathf.Clamp(n, 0, q.goal);
        }

        public static string GoalLine(Quest q) => q.goal > 1 ? $"{q.goalText}  ({Progress(q)}/{q.goal})" : q.goalText;

        public static string RewardLine(Quest q)
        {
            string s = q.gold > 0 ? $"{q.gold} gold" : "";
            if (q.potions > 0) s += (s.Length > 0 ? " and " : "") + (q.potions == 1 ? "a potion" : $"{q.potions} potions");
            return s;
        }

        public static void MarkRead()
        {
            if (!Unread) return;
            Unread = false;
            Changed?.Invoke();
        }

        // Completes the quest in hand when its goal is met, pays it out and hands over the next one.
        // One quest per call, so a hero who is already far along sees them pass one by one.
        public static bool Check()
        {
            if (Profile.RunOver) return false;
            var q = Current;
            if (q == null || Progress(q) < q.goal) return false;
            if (q.gold > 0) Profile.AddGold(q.gold);
            if (q.potions > 0) D.potions = Mathf.Min(Profile.MaxPotions, D.potions + q.potions);
            int next = Index + 1;
            D.quest = next < All.Count ? All[next].id : Finished;
            D.questBase = next < All.Count && All[next].fromStart ? All[next].count() : 0;
            Unread = next < All.Count;
            string reward = RewardLine(q);
            GameUI.Instance?.Toast($"Quest complete: {q.title}" + (reward.Length > 0 ? $"\n<size=75%>+ {reward}</size>" : ""), Palette.Gold, 3.5f);
            Sound.Play(Sfx.LevelUp, HeroController.Instance != null ? HeroController.Instance.transform.position : Vector3.zero, 0.6f);
            Profile.Save();
            Changed?.Invoke();
            return true;
        }

        // ---------- Called every frame by the HUD ----------

        static float nextCheck;

        public static void Tick()
        {
            // Not behind an open menu (the message would be hidden), and not in the debug range's throwaway hero.
            if (Profile.Sandbox || Panels.AnyOpen || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + (Check() ? 4f : 0.5f);
        }
    }
}
