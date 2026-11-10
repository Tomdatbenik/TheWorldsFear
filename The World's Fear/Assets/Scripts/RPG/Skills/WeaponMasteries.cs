using UnityEngine;

namespace TWF
{
    // One passive mastery per weapon type, learned from the Weaponmaster. It is always on while that weapon
    // is in your hands and changes how the weapon's basic attack works. Saved by WeaponType value.
    public static class WeaponMasteries
    {
        // Tuning.
        public const float RunAndGunSpeed = 0.8f;      // Bow: attack speed while shooting on the move
        public const int JavelinEvery = 4;              // Spear: every Nth attack is a throw
        public const float JavelinDamage = 1.5f;
        public const float JavelinRange = 9f;
        public const float KnifeRange = 5.5f;           // Daggers: how far thrown knives fly
        public const int RepeaterBolts = 3;             // Crossbow: bolts per burst
        public const float RepeaterDamage = 0.8f;
        public const float RepeaterReload = 2.5f;       // times the normal time between shots
        public const float GravityRadius = 3.5f;        // Staff
        public const float GravityPull = 1.5f;
        public const int ChainJumps = 2;                // Wand
        public const float ChainRange = 5f;
        public const float HeavyDamage = 0.35f;         // Hammer: each aftershock, times the slam
        public const int HeavyTicks = 2;

        public static bool Has(WeaponType w) => w != WeaponType.Fists && Profile.Data.masteriesOwned.Contains((int)w);
        public static bool Active(WeaponType w) => Has(w) && Weapons.Equipped == w;

        public static int Price(WeaponType w) => 600 + Profile.Data.level * 30;

        public static bool Buy(WeaponType w)
        {
            if (w == WeaponType.Fists || Has(w) || Profile.Data.gold < Price(w)) return false;
            Profile.AddGold(-Price(w));
            Profile.Data.masteriesOwned.Add((int)w);
            Profile.Save();
            return true;
        }

        public static string Name(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Sword: return "Dual Wield";
                case WeaponType.Axe: return "Spin";
                case WeaponType.Hammer: return "Heavy";
                case WeaponType.Daggers: return "Throwing Knives";
                case WeaponType.Spear: return "Javelin";
                case WeaponType.Bow: return "Run and Gun";
                case WeaponType.Crossbow: return "Repeater";
                case WeaponType.Staff: return "Gravity Orb";
                case WeaponType.Wand: return "Chain Bolts";
                default: return "";
            }
        }

        public static string Describe(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Sword: return "Lets you hold a sword in your off hand. With one equipped, every attack also strikes with the off-hand blade, and its damage adds to yours.";
                case WeaponType.Axe: return "Your attacks become a full spin, hitting everything around you.";
                case WeaponType.Hammer: return $"Every slam leaves quaking ground that hits {HeavyTicks} more times and slows enemies in it.";
                case WeaponType.Daggers: return "Your attacks throw knives instead of stabbing: one per strike, plus one for every extra slash and extra projectile you have.";
                case WeaponType.Spear: return $"Every {JavelinEvery}th attack hurls your spear instead, piercing every enemy in a long line.";
                case WeaponType.Bow: return $"Shoot while you move. Attacks are {Mathf.RoundToInt((1f - RunAndGunSpeed) * 100)}% slower.";
                case WeaponType.Crossbow: return $"Fire bursts of {RepeaterBolts} bolts at {Mathf.RoundToInt(RepeaterDamage * 100)}% damage each, then reload.";
                case WeaponType.Staff: return "Your orbs pull nearby enemies toward the spot they hit.";
                case WeaponType.Wand: return $"Bolts jump on to {ChainJumps} more enemies after they hit.";
                default: return "";
            }
        }

        public static string Icon(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Sword: return "sword";
                case WeaponType.Axe: return "spiral";
                case WeaponType.Hammer: return "nova";
                case WeaponType.Daggers: return "slash";
                case WeaponType.Spear: return "leap";
                case WeaponType.Bow: return "dash";
                case WeaponType.Crossbow: return "bow";
                case WeaponType.Staff: return "arcane";
                case WeaponType.Wand: return "bolt";
                default: return "fist";
            }
        }

        // Dual Wield: an off-hand sword only goes on once the sword mastery is learned.
        public static bool IsOffHandSword(ItemInstance item) =>
            item != null && item.slot == ItemSlot.OffHand && item.offHandKind == (int)OffHandKind.Sword;

        public static bool NeedsDualWield(ItemInstance item) => IsOffHandSword(item) && !Has(WeaponType.Sword);

        public static bool DualWielding => Active(WeaponType.Sword) && IsOffHandSword(Profile.Equipped(ItemSlot.OffHand));
    }
}
