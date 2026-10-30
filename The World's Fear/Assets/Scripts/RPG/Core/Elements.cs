using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Elemental matchups. Every monster has an element: it is weak to one element (x1.5 damage) and
    // resists its own (x0.5). Physical is neutral both ways: no weakness, no strength.
    // Damage carries its elements as a bit mask; with several, the best matchup counts.
    public static class Elements
    {
        public const float WeakMult = 1.5f, ResistMult = 0.5f;

        public static ElementPart? WeaknessOf(ElementPart e)
        {
            switch (e)
            {
                case ElementPart.Fire: return ElementPart.Frost;
                case ElementPart.Frost: return ElementPart.Fire;
                case ElementPart.Lightning: return ElementPart.Poison;
                case ElementPart.Poison: return ElementPart.Holy;
                case ElementPart.Arcane: return ElementPart.Shadow;
                case ElementPart.Shadow: return ElementPart.Holy;
                case ElementPart.Holy: return ElementPart.Shadow;
                default: return null;
            }
        }

        public static int Mask(ElementPart e) => e == ElementPart.Physical ? 0 : 1 << (int)e;

        public static int Mask(IEnumerable<ElementPart> list)
        {
            int m = 0;
            foreach (var e in list) m |= Mask(e);
            return m;
        }

        public static IEnumerable<ElementPart> In(int mask)
        {
            for (int i = 0; i < 8; i++)
                if ((mask & (1 << i)) != 0) yield return (ElementPart)i;
        }

        public static float Factor(ElementPart attack, ElementPart defender)
        {
            if (attack == ElementPart.Physical || defender == ElementPart.Physical) return 1f;
            if (attack == defender) return ResistMult;
            return WeaknessOf(defender) == attack ? WeakMult : 1f;
        }

        // Several elements: the best one decides.
        public static float Factor(int mask, ElementPart defender)
        {
            if (mask == 0 || defender == ElementPart.Physical) return 1f;
            float best = 0f;
            foreach (var e in In(mask)) best = Mathf.Max(best, Factor(e, defender));
            return best > 0f ? best : 1f;
        }

        public static string Name(ElementPart e) => e.ToString();
        public static Color Color(ElementPart e) => SkillParts.ElementColor(e);
        public static string Tag(ElementPart e) => $"<color={Palette.Hex(Color(e))}>{e}</color>";

        // A monster's element: mostly set by where it lives.
        public static ElementPart ForBiome(Biome b, System.Random rng)
        {
            double r = rng.NextDouble();
            switch (b)
            {
                case Biome.Ember: return r < 0.6 ? ElementPart.Fire : r < 0.8 ? ElementPart.Arcane : ElementPart.Physical;
                case Biome.Marsh: return r < 0.45 ? ElementPart.Poison : r < 0.75 ? ElementPart.Frost : ElementPart.Physical;
                case Biome.Ruins: return r < 0.4 ? ElementPart.Shadow : r < 0.7 ? ElementPart.Arcane : r < 0.85 ? ElementPart.Holy : ElementPart.Physical;
                case Biome.Forest: return r < 0.35 ? ElementPart.Physical : r < 0.65 ? ElementPart.Poison : ElementPart.Lightning;
                default: return ElementPart.Physical;
            }
        }
    }
}
