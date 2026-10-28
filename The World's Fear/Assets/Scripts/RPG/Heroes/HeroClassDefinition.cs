using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    [CreateAssetMenu(menuName = "TWF/Hero Class")]
    public class HeroClassDefinition : ScriptableObject
    {
        public enum BasicAttackKind { Melee, Projectile }

        public string id;
        public string displayName;
        [TextArea] public string description;
        public Color tint = Color.white;
        public Color accentColor = Color.white;

        [Header("Stats")]
        public List<StatEntry> baseStats = new List<StatEntry>();
        public List<StatEntry> growthPerLevel = new List<StatEntry>();

        [Header("Basic attack")]
        public BasicAttackKind basicAttack = BasicAttackKind.Melee;
        public float attackRange = 1.8f;
        public float attackArc = 110f;
        public float attackDamageMult = 1f;
        public float projectileSpeed = 16f;
        public Color projectileColor = Color.white;
        public bool useArrowSprite;
        public bool showSword = true;

        [Header("Skills")]
        public SkillTreeDefinition skillTree;
        [Tooltip("Node granted at rank 1 for free at level 1.")]
        public string starterNode;
    }
}
