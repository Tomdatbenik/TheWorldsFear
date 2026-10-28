using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    [Serializable]
    public class SkillTreeNode
    {
        public string id;
        public SkillDefinition active;    // set exactly one of active / passive
        public PassiveDefinition passive;
        public int tier;
        [Tooltip("Column 0-2 inside the tier, for layout.")]
        public int column;
        public int requiredLevel = 1;
        [Tooltip("Node that must have at least one point first (optional).")]
        public string requires;

        public bool IsActive => active != null;
        public int MaxRank => active != null ? active.maxRank : passive != null ? passive.maxRank : 1;
        public string DisplayName => active != null ? active.displayName : passive != null ? passive.displayName : id;
        public string Glyph => active != null ? active.glyph : passive != null ? passive.glyph : "?";
        public Color Color => active != null ? active.color : passive != null ? passive.color : Color.gray;
        public string Describe(int rank) => active != null ? active.Describe(rank) : passive != null ? passive.Describe(rank) : "";
    }

    [CreateAssetMenu(menuName = "TWF/Skill Tree")]
    public class SkillTreeDefinition : ScriptableObject
    {
        [Tooltip("Points that must be spent in the tree before each tier opens.")]
        public int[] pointsToUnlockTier = { 0, 2, 5, 9, 14 };
        public List<SkillTreeNode> nodes = new List<SkillTreeNode>();

        public SkillTreeNode Find(string id) => nodes.Find(n => n.id == id);

        public int TierCount => pointsToUnlockTier.Length;

        public static int PointsSpent(Dictionary<string, int> ranks)
        {
            int total = 0;
            foreach (var kv in ranks) total += kv.Value;
            return total;
        }

        public enum CanRankUp { Yes, Maxed, NoPoints, LevelTooLow, TierLocked, MissingRequirement }

        public CanRankUp Check(SkillTreeNode node, Dictionary<string, int> ranks, int level, int freePoints)
        {
            ranks.TryGetValue(node.id, out int rank);
            if (rank >= node.MaxRank) return CanRankUp.Maxed;
            if (level < node.requiredLevel) return CanRankUp.LevelTooLow;
            if (PointsSpent(ranks) < pointsToUnlockTier[Mathf.Clamp(node.tier, 0, TierCount - 1)]) return CanRankUp.TierLocked;
            if (!string.IsNullOrEmpty(node.requires) && (!ranks.TryGetValue(node.requires, out int req) || req <= 0))
                return CanRankUp.MissingRequirement;
            if (freePoints <= 0) return CanRankUp.NoPoints;
            return CanRankUp.Yes;
        }
    }

}
