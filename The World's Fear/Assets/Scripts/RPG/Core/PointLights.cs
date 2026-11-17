using UnityEngine;

namespace TWF
{
    // Small point lights on spells, waypoints and loot, with a budget so phones stay fast.
    public class PointLights : MonoBehaviour
    {
        const int MaxTransient = 8;
        static int transientCount;

        bool transient;

        public static Light Attach(Transform parent, Color color, float range, float intensity, Vector3 localOffset, bool persistent = false)
        {
            if (!persistent && transientCount >= MaxTransient) return null;
            if (!RenderAssets.HighQuality && !persistent) return null;
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localOffset;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            var pl = go.AddComponent<PointLights>();
            pl.transient = !persistent;
            if (pl.transient) transientCount++;
            return light;
        }

        // ---------- Loot lights ----------
        // Every dropped rare used to carry its own light: a floor full of loot meant dozens of lights on every
        // sprite near them. Now only the few nearest the hero are lit (RenderAssets.LootLights); the rest keep
        // their glowing sprite and beam.
        static readonly System.Collections.Generic.List<Light> loot = new System.Collections.Generic.List<Light>();
        static int lootFrame = -1;
        bool isLoot;
        public static int LootLit { get; private set; }

        public static Light AttachLoot(Transform parent, Color color, float range, float intensity, Vector3 localOffset)
        {
            var light = Attach(parent, color, range, intensity, localOffset, true);
            if (light == null) return null;
            light.GetComponent<PointLights>().isLoot = true;
            light.enabled = false;
            loot.Add(light);
            lootFrame = -1;
            return light;
        }

        void Update()
        {
            if (!isLoot || Time.frameCount == lootFrame || (lootFrame >= 0 && Time.frameCount < lootFrame + 12)) return;
            lootFrame = Time.frameCount;
            Budget();
        }

        static void Budget()
        {
            loot.RemoveAll(l => l == null);
            var hero = HeroController.Instance;
            Vector3 at = hero != null ? hero.transform.position : Vector3.zero;
            loot.Sort((a, b) => (a.transform.position - at).sqrMagnitude.CompareTo((b.transform.position - at).sqrMagnitude));
            int max = RenderAssets.LootLights;
            for (int i = 0; i < loot.Count; i++) loot[i].enabled = i < max;
            LootLit = Mathf.Min(max, loot.Count);
        }

        void OnDestroy()
        {
            if (transient) transientCount = Mathf.Max(0, transientCount - 1);
            if (isLoot) lootFrame = -1;
        }
    }
}
