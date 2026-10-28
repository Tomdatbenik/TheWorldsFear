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

        void OnDestroy()
        {
            if (transient) transientCount = Mathf.Max(0, transientCount - 1);
        }
    }
}
