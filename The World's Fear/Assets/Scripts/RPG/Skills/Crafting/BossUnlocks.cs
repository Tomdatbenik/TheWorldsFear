namespace TWF
{
    // Each boss, the first time it falls, unlocks a crafting mechanic.
    public static class BossUnlocks
    {
        static bool Slain(BossKind k) => Profile.Data != null && Profile.Data.guardiansSlain.Contains(k.ToString());

        // Elements per spell: 1, 2 after the Slime Sovereign, 3 after the Bone King.
        public static int MaxElements => 1 + (Slain(BossKind.Slime) ? 1 : 0) + (Slain(BossKind.Skeleton) ? 1 : 0);

        // Weapon arts (weapon deliveries such as Spear Throw) after Vexa, the Brood Mother.
        public static bool WeaponArts => Slain(BossKind.Spider);

        // Modifiers per spell: 2, 3 after the Rotting Colossus, 4 after the World's Fear.
        public static int MaxMods => 2 + (Slain(BossKind.Zombie) ? 1 : 0) + (Slain(BossKind.WorldsFear) ? 1 : 0);

        public static string Describe(BossKind k)
        {
            switch (k)
            {
                case BossKind.Slime: return "Dual-element spells: combine two elements in one skill";
                case BossKind.Spider: return "Weapon arts: weapon deliveries like Spear Throw at the Arcane Forge";
                case BossKind.Zombie: return "A third modifier slot for every skill";
                case BossKind.Skeleton: return "Triple-element spells";
                case BossKind.WorldsFear: return "A fourth modifier slot";
                default: return null;
            }
        }
    }
}
