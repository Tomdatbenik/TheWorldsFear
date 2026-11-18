using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TWF.Tests
{
    // The quest line: a new hero is led from the farms to the World's Fear, one quest at a time.
    public static class QuestChecks
    {
        [TwfCheck("Quests/Every quest says what to do, why, and what it pays")]
        static void QuestsAreWritten()
        {
            var ids = new HashSet<string>();
            foreach (var q in Quests.All)
            {
                Check.That(!string.IsNullOrEmpty(q.id) && ids.Add(q.id), $"Quest id '{q.id}' is empty or used twice");
                Check.That(q.id != Quests.Finished, "A quest uses the id that means 'all finished'");
                Check.That(!string.IsNullOrEmpty(q.title) && !string.IsNullOrEmpty(q.goalText), $"Quest '{q.id}' has no title or goal");
                Check.AtLeast(q.story?.Length ?? 0, 80, $"length of the explanation of quest '{q.id}'");
                Check.AtLeast(q.goal, 1, $"goal of quest '{q.id}'");
                Check.That(q.count != null, $"Quest '{q.id}' cannot be measured");
                Check.That(q.gold > 0 || q.potions > 0, $"Quest '{q.id}' pays nothing");
            }
            Check.That(Quests.All[0].id == "farms" && Quests.All[0].goal == 2, "The first quest is not 'reach level 2'");
            Check.That(Quests.All[1].id == "craft", "The second quest is not 'craft your first skill'");
        }

        [TwfCheck("Quests/A new hero is sent to the farms, then to the forge, and is paid for each")]
        static IEnumerator QuestLine()
        {
            yield return Play.Scene("TestRange", 2f);
            var d = Profile.Data;
            var keep = (d.quest, d.questBase, d.level, d.gold, d.kills, d.potions);
            var skills = new List<CraftedSkill>(d.craftedSkills);
            try
            {
                d.quest = "";
                d.level = 1;
                Check.That(Quests.Current.id == "farms", $"A new hero starts on quest '{Quests.Current.id}'");
                Check.That(!Quests.Check(), "The first quest completed at level 1");
                // Level 2: paid, and on to the forge.
                d.level = 2;
                int gold = d.gold;
                Check.That(Quests.Check(), "Reaching level 2 did not complete the first quest");
                Check.That(d.gold == gold + Quests.All[0].gold, $"The first quest paid {d.gold - gold} gold, not {Quests.All[0].gold}");
                Check.That(Quests.Current.id == "craft" && Quests.Unread, "The forge quest did not follow as a new quest");
                // The skill a hero starts with does not count: one has to be crafted.
                while (d.craftedSkills.Count > 1) d.craftedSkills.RemoveAt(d.craftedSkills.Count - 1);
                Check.That(!Quests.Check(), "The forge quest completed without crafting");
                d.craftedSkills.Add(new CraftedSkill { uid = "questtest", element = ElementPart.Physical, delivery = DeliveryPart.Single, name = "Test" });
                Check.That(Quests.Check(), "Crafting a skill did not complete the forge quest");
                // A quest that counts kills counts from when it began.
                d.kills = 500;
                d.quest = "gear";
                while (Quests.Current.id == "gear") { if (!Quests.Check()) break; }
                if (Quests.Current.id == "hunt")
                {
                    Check.That(Quests.Progress(Quests.Current) == 0, $"The hunt began at {Quests.Progress(Quests.Current)} kills");
                    d.kills += Quests.Current.goal;
                    Check.That(Quests.Check(), "Slaying the monsters asked for did not complete the hunt");
                }
                // The whole line can be walked and read to its end without a fault.
                foreach (var q in Quests.All) { q.count(); Quests.GoalLine(q); Quests.RewardLine(q); }
                d.quest = Quests.Finished;
                Check.That(Quests.Current == null && !Quests.Check(), "There is a quest after the last one");

                // The book opens on the quest in hand and marks it read; the HUD shows it.
                d.quest = "";
                d.level = 1;
                Panels.Toggle(PanelKind.Quests);
                yield return null;
                var book = Object.FindAnyObjectByType<QuestPanel>();
                Check.That(book != null && book.IsOpen, "The quest book did not open");
                string page = "";
                foreach (var t in book.GetComponentsInChildren<TMPro.TextMeshProUGUI>()) page += t.text;
                Check.That(page.Contains(Quests.All[0].title) && page.Contains("farms"), "The quest book does not explain the first quest");
                Check.That(!Quests.Unread, "Opening the book did not mark the quest as read");
                Panels.CloseAll();
                yield return null;
                var tracker = GameUI.Instance.Root.Find("Status/QuestTracker");
                Check.That(tracker != null && tracker.gameObject.activeInHierarchy, "The HUD shows no quest");
                Check.That(tracker.GetComponentInChildren<TMPro.TextMeshProUGUI>().text.Contains(Quests.All[0].title), "The HUD does not name the quest in hand");
                Check.That(GameUI.Instance.Root.Find("Menu/QuestBtn") != null, "There is no quest button in the menu");
            }
            finally
            {
                Panels.CloseAll();
                (d.quest, d.questBase, d.level, d.gold, d.kills, d.potions) = keep;
                d.craftedSkills.Clear();
                d.craftedSkills.AddRange(skills);
            }
        }
    }
}
