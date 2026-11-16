using UnityEngine;

namespace TWF
{
    // The hero carries a lantern: a warm light round them that comes up as the daylight goes (and always
    // underground), with a slight flicker, so the night is dark but never blind.
    public class HeroLantern : MonoBehaviour
    {
        const float Range = 14f, Brightness = 3f;
        Light lamp;
        float seed;

        void Start()
        {
            lamp = PointLights.Attach(transform, new Color(1f, 0.76f, 0.45f), Range, 0f, new Vector3(0.25f, 1.4f, 0f), true);
            seed = Random.value * 10f;
        }

        void LateUpdate()
        {
            if (lamp == null) return;
            float dark = ZoneLighting.Underground ? 1f : 1f - DayNight.Daylight;
            float flicker = 1f + (Mathf.PerlinNoise(seed, Time.time * 3f) - 0.5f) * 0.12f;
            lamp.intensity = Brightness * Mathf.SmoothStep(0f, 1f, dark) * flicker;
            lamp.enabled = lamp.intensity > 0.02f;
        }
    }
}
