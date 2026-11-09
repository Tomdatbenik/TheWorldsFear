using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // Sun, ambient light, fog and post-processing per biome, blended when you move between biomes.
    public class ZoneLighting : MonoBehaviour
    {
        public static ZoneLighting Instance { get; private set; }

        struct Mood
        {
            public Color sun;
            public float sunIntensity;
            public Vector2 sunAngles; // elevation, azimuth (degrees)
            public Color ambient;
            public Color fog;
            public float fogStart, fogEnd;
        }

        Light sun;
        Volume[] volumes;
        int target = (int)Biome.Village;
        Mood current;
        bool boss;

        public static ZoneLighting Create()
        {
            var go = new GameObject("ZoneLighting");
            var z = go.AddComponent<ZoneLighting>();
            return z;
        }

        void Awake()
        {
            Instance = this;
            RenderAssets.Apply();

            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) l.enabled = false;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.shadowBias = 0.02f;
            sun.shadowNormalBias = 0.2f;

            var ra = RenderAssets.Instance;
            int count = ra != null && ra.volumes != null ? ra.volumes.Length : 0;
            volumes = new Volume[count];
            for (int i = 0; i < count; i++)
            {
                var v = new GameObject("Post " + i).AddComponent<Volume>();
                v.transform.SetParent(transform, false);
                v.isGlobal = true;
                v.sharedProfile = ra.volumes[i];
                v.weight = i == target ? 1f : 0f;
                volumes[i] = v;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            current = MoodFor(target, false);
            ApplyMood(current);
        }

        public void SetBiome(Biome b)
        {
            boss = false;
            target = (int)b;
        }

        public void SetBoss()
        {
            boss = true;
            target = volumes.Length - 1;
        }

        void Update()
        {
            DayNight.Tick(Time.deltaTime);
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 1.5f);
            var goal = MoodFor(target, boss);
            current.sun = Color.Lerp(current.sun, goal.sun, k);
            current.sunIntensity = Mathf.Lerp(current.sunIntensity, goal.sunIntensity, k);
            current.sunAngles = Vector2.Lerp(current.sunAngles, goal.sunAngles, k);
            current.ambient = Color.Lerp(current.ambient, goal.ambient, k);
            current.fog = Color.Lerp(current.fog, goal.fog, k);
            current.fogStart = Mathf.Lerp(current.fogStart, goal.fogStart, k);
            current.fogEnd = Mathf.Lerp(current.fogEnd, goal.fogEnd, k);
            ApplyMood(current);

            for (int i = 0; i < volumes.Length; i++)
                volumes[i].weight = Mathf.MoveTowards(volumes[i].weight, i == target ? 1f : 0f, Time.unscaledDeltaTime * 0.6f);
        }

        // The biome's mood at this time of day. The boss arenas keep their own fixed light, and the Ember's lava lights its nights.
        Mood AtTimeOfDay(Mood m)
        {
            float amount = boss ? 0f : target == (int)Biome.Ember ? 0.55f : 1f;
            float height = DayNight.SunHeight;
            float day = Mathf.Lerp(1f, DayNight.Daylight, amount);
            float warm = amount * Mathf.Clamp01(DayNight.Daylight * 1.5f) * (1f - Mathf.SmoothStep(0f, 0.35f, Mathf.Abs(height)));

            var moon = new Color(0.55f, 0.66f, 1f);
            m.sun = Color.Lerp(moon, Color.Lerp(m.sun, new Color(1f, 0.55f, 0.3f), warm * 0.7f), day);
            m.sunIntensity = Mathf.Lerp(m.sunIntensity * 0.35f, m.sunIntensity, day);
            m.ambient = Color.Lerp(new Color(m.ambient.r * 0.55f, m.ambient.g * 0.6f, m.ambient.b * 0.85f, m.ambient.a), m.ambient, day);
            m.ambient = Color.Lerp(m.ambient, new Color(m.ambient.r * 1.15f, m.ambient.g * 0.95f, m.ambient.b * 0.8f, m.ambient.a), warm);
            var duskFog = Color.Lerp(m.fog, new Color(0.95f, 0.55f, 0.4f), warm * 0.25f);
            m.fog = Color.Lerp(new Color(m.fog.r * 0.35f, m.fog.g * 0.4f, m.fog.b * 0.6f, m.fog.a), duskFog, day);

            // The sun climbs and crosses the sky; the moon takes over while the light is dimmest, with no shadow to give it away.
            float elevation = height > 0f ? Mathf.Lerp(30f, m.sunAngles.x, Mathf.SmoothStep(0f, 0.7f, height)) : m.sunAngles.x * 0.9f;
            // Flat ground gets less light from a low sun; make up for it so dawn and dusk stay warm rather than dark.
            float elevated = Mathf.Lerp(m.sunAngles.x, elevation, amount);
            m.sunIntensity *= Mathf.Clamp(Mathf.Sin(m.sunAngles.x * Mathf.Deg2Rad) / Mathf.Sin(elevated * Mathf.Deg2Rad), 1f, 1.6f);
            float azimuth = m.sunAngles.y + DayNight.ArcPosition * 140f;
            m.sunAngles = Vector2.Lerp(m.sunAngles, new Vector2(elevation, azimuth), amount);
            sun.shadowStrength = 0.75f * Mathf.Lerp(1f, Mathf.SmoothStep(0f, 0.18f, Mathf.Abs(height)), amount);
            return m;
        }

        void ApplyMood(Mood m)
        {
            m = AtTimeOfDay(m);
            sun.color = m.sun;
            sun.intensity = m.sunIntensity;
            // The ground is XY with up = -Z: tilt the light down from that "sky".
            float elev = m.sunAngles.x * Mathf.Deg2Rad, az = m.sunAngles.y * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(elev) * Mathf.Cos(az), Mathf.Cos(elev) * Mathf.Sin(az), Mathf.Sin(elev));
            sun.transform.rotation = Quaternion.LookRotation(dir, Vector3.back);
            Shader.SetGlobalColor("_TWF_Ambient", m.ambient);
            RenderSettings.ambientLight = m.ambient;
            RenderSettings.fogColor = m.fog;
            RenderSettings.fogStartDistance = m.fogStart;
            RenderSettings.fogEndDistance = m.fogEnd;
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = m.fog;
        }

        static Mood MoodFor(int index, bool boss)
        {
            if (boss)
                return new Mood { sun = new Color(1f, 0.72f, 0.6f), sunIntensity = 1.0f, sunAngles = new Vector2(55, 240), ambient = new Color(0.42f, 0.32f, 0.36f), fog = new Color(0.18f, 0.08f, 0.09f), fogStart = 32, fogEnd = 80 };
            switch ((Biome)index)
            {
                case Biome.Village:
                    return new Mood { sun = new Color(1f, 0.93f, 0.8f), sunIntensity = 0.92f, sunAngles = new Vector2(48, 45), ambient = new Color(0.36f, 0.36f, 0.38f), fog = new Color(0.58f, 0.65f, 0.7f), fogStart = 40, fogEnd = 95 };
                case Biome.Forest:
                    return new Mood { sun = new Color(1f, 0.88f, 0.62f), sunIntensity = 0.74f, sunAngles = new Vector2(55, 210), ambient = new Color(0.29f, 0.32f, 0.27f), fog = new Color(0.5f, 0.52f, 0.38f), fogStart = 28, fogEnd = 70 };
                case Biome.Marsh:
                    return new Mood { sun = new Color(0.75f, 0.9f, 0.85f), sunIntensity = 0.53f, sunAngles = new Vector2(40, 160), ambient = new Color(0.21f, 0.27f, 0.26f), fog = new Color(0.28f, 0.36f, 0.32f), fogStart = 14, fogEnd = 48 };
                case Biome.Ruins:
                    return new Mood { sun = new Color(0.82f, 0.88f, 1f), sunIntensity = 0.65f, sunAngles = new Vector2(62, 300), ambient = new Color(0.26f, 0.28f, 0.35f), fog = new Color(0.42f, 0.46f, 0.55f), fogStart = 24, fogEnd = 66 };
                case Biome.Ember:
                    return new Mood { sun = new Color(1f, 0.55f, 0.35f), sunIntensity = 0.62f, sunAngles = new Vector2(30, 180), ambient = new Color(0.27f, 0.15f, 0.14f), fog = new Color(0.25f, 0.1f, 0.07f), fogStart = 18, fogEnd = 55 };
                default:
                    return new Mood { sun = Color.white, sunIntensity = 0.62f, sunAngles = new Vector2(50, 220), ambient = new Color(0.30f, 0.30f, 0.34f), fog = Color.gray, fogStart = 30, fogEnd = 80 };
            }
        }
    }

    // The time of day: a sunny stretch and a shorter night. Other systems ask it how bright the world is.
    public static class DayNight
    {
        public const float CycleSeconds = 600f;
        const float DayShare = 0.65f;     // of the whole cycle

        static readonly List<(Material material, float day, float night)> glowing = new List<(Material, float, float)>();

        // 0..1 around the cycle: 0 is sunrise.
        public static float Time01 { get; private set; } = 0.12f;

        // Around the circle: 0..PI is the day, PI..2PI the night.
        static float Phase => Time01 < DayShare ? Time01 / DayShare * Mathf.PI : Mathf.PI + (Time01 - DayShare) / (1f - DayShare) * Mathf.PI;

        // +1 at noon, -1 at midnight.
        public static float SunHeight => Mathf.Sin(Phase);

        // -0.5..0.5 across the arc of whichever of the sun or moon is up.
        public static float ArcPosition => (Phase % Mathf.PI) / Mathf.PI - 0.5f;

        // 0 at night, 1 in full day.
        public static float Daylight => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.18f, 0.12f, SunHeight));

        // How strongly a lamp shines now: `day` times its strength in full daylight, `night` in the dark.
        public static float Scale(float day, float night) => Mathf.Lerp(night, day, Daylight);

        // Windows and the like: a material whose _Emission follows the time of day.
        public static void GlowAtNight(Material material, float day, float night)
        {
            if (material != null) glowing.Add((material, day, night));
        }

        public static void SetTime(float t01)
        {
            Time01 = Mathf.Repeat(t01, 1f);
            Apply();
        }

        public static void Tick(float dt)
        {
            Time01 = Mathf.Repeat(Time01 + dt / CycleSeconds, 1f);
            Apply();
        }

        static void Apply()
        {
            Shader.SetGlobalFloat("_TWF_Day", Daylight);
            for (int i = glowing.Count - 1; i >= 0; i--)
            {
                if (glowing[i].material == null) { glowing.RemoveAt(i); continue; }
                glowing[i].material.SetFloat("_Emission", Scale(glowing[i].day, glowing[i].night));
            }
        }
    }

    // A lamp that is dim by day and bright at night.
    public class NightLight : MonoBehaviour
    {
        public Light target;
        public float day = 0.2f, night = 1.5f;
        float baseIntensity;

        void Start()
        {
            if (target != null) baseIntensity = target.intensity;
        }

        void Update()
        {
            if (target != null) target.intensity = baseIntensity * DayNight.Scale(day, night);
        }
    }
}
