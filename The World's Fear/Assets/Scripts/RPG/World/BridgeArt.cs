using UnityEngine;

namespace TWF
{
    // River bridges from the environment kit's pieces (pixelart-gen: env/bridge/*.json, env_kit.py --mesh):
    // bridge_deck (a 1 x 1 plank deck on two beams, its top at local height 0), bridge_rail (a railing for one tile
    // edge, origin at the middle of the deck's north edge) and bridge_pier (a pile from under the deck down into the
    // water). Art only: the pieces carry no colliders, the world adds its own edge walls.
    public static class BridgeArt
    {
        public const string Deck = "bridge_deck", Rail = "bridge_rail", Pier = "bridge_pier";

        public static bool Installed => EnvMesh.Has(Deck) && EnvMesh.Has(Rail);

        // One bridge tile. cell is the tile's south-west corner on the ground (the tile covers cell .. cell + 1),
        // deckHeight the height of the walking surface, and rail* say which sides get a railing (north is +Z).
        // Returns false, placing nothing, if the meshes aren't installed.
        public static bool PlaceTile(Transform parent, Vector2 cell, float deckHeight, bool railN, bool railE, bool railS, bool railW)
        {
            if (!Installed) return false;
            var mid = cell + new Vector2(0.5f, 0.5f);
            // The deck's planks lie across the bridge: it runs east-west unless only its east/west sides are railed.
            bool northSouth = (railE || railW) && !(railN || railS);
            EnvMesh.Place(Deck, parent, mid, deckHeight, northSouth ? 90f : 0f);
            // A pile under every other tile's railed sides, so a long bridge stands on a row of pairs.
            bool piers = EnvMesh.Has(Pier) && ((Mathf.FloorToInt(cell.x) + Mathf.FloorToInt(cell.y)) & 1) == 0;
            Side(parent, mid, deckHeight, railN, Vector2.up, 0f, piers);
            Side(parent, mid, deckHeight, railW, Vector2.left, 90f, piers);
            Side(parent, mid, deckHeight, railS, Vector2.down, 180f, piers);
            Side(parent, mid, deckHeight, railE, Vector2.right, 270f, piers);
            return true;
        }

        static void Side(Transform parent, Vector2 mid, float deckHeight, bool rail, Vector2 dir, float yaw, bool pier)
        {
            if (!rail) return;
            EnvMesh.Place(Rail, parent, mid + dir * 0.5f, deckHeight, yaw);
            if (pier) EnvMesh.Place(Pier, parent, mid + dir * 0.33f, deckHeight, yaw);
        }
    }
}
