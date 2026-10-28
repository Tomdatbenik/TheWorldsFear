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

        void ApplyMood(Mood m)
        {
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
                return new Mood { sun = new Color(1f, 0.45f, 0.35f), sunIntensity = 0.68f, sunAngles = new Vector2(40, 250), ambient = new Color(0.21f, 0.12f, 0.15f), fog = new Color(0.12f, 0.04f, 0.05f), fogStart = 25, fogEnd = 60 };
            switch ((Biome)index)
            {
                case Biome.Village:
                    return new Mood { sun = new Color(1f, 0.86f, 0.66f), sunIntensity = 0.78f, sunAngles = new Vector2(48, 225), ambient = new Color(0.34f, 0.32f, 0.38f), fog = new Color(0.62f, 0.55f, 0.45f), fogStart = 30, fogEnd = 75 };
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
}
