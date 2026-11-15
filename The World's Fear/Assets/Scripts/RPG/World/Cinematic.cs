using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // A flight over the world: the walled town from the south road, round the citadel, out over the farms, then away
    // to the nearest high mountain range with its waterfalls. F9 in the open world plays it (any key or tap skips).
    // With Record set, every frame is also written as a JPG (at 20 frames a second of game time) to RecordFolder.
    public class Cinematic : MonoBehaviour
    {
        public static bool Playing { get; private set; }
        public static bool Record;
        public static string RecordFolder;
        public static int RecordWidth = 1600, RecordHeight = 900;

        struct Key { public Vector3 focus; public float yaw, size, pitch, hold; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("Cinematic Director");
            DontDestroyOnLoad(go);
            go.AddComponent<Cinematic>();
        }

        void Update()
        {
            if (!Playing && Input.GetKeyDown(KeyCode.F9)) Play();
        }

        public static void Play()
        {
            var me = FindAnyObjectByType<Cinematic>();
            if (me == null || Playing || ChunkWorld.Instance == null || HeroController.Instance == null) return;
            me.StartCoroutine(me.Flight());
        }

        IEnumerator Flight()
        {
            Playing = true;
            var hidden = new List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>())
                if (c.enabled && c.isRootCanvas) { c.enabled = false; hidden.Add(c); }
            var heroCombat = HeroController.Instance.Combatant;
            heroCombat.Stats.AddModifier(new StatModifier(StatType.Armor, ModKind.Percent, 50f, "cinematic"));

            Vector2 farm = WorldGen.Farms.Count > 0 ? WorldGen.Farms[0].rect.center : new Vector2(0f, -80f);
            var keys = new List<Key>
            {
                new Key { focus = new Vector3(0f, 0f, -70f), yaw = 0f, size = 11f, pitch = 34f, hold = 0f },
                new Key { focus = new Vector3(0f, 2f, -46f), yaw = 0f, size = 13f, pitch = 38f, hold = 5f },        // up the south road to the gate
                new Key { focus = new Vector3(0f, 3f, -24f), yaw = 0f, size = 15f, pitch = 44f, hold = 5f },        // through it into the plaza
                new Key { focus = new Vector3(0f, 6f, 0f), yaw = 70f, size = 24f, pitch = 48f, hold = 7f },         // round the citadel
                new Key { focus = new Vector3(0f, 4f, 0f), yaw = 160f, size = 36f, pitch = 58f, hold = 7f },        // the whole town
                new Key { focus = new Vector3(farm.x, 1f, farm.y), yaw = 200f, size = 16f, pitch = 46f, hold = 7f }, // the farms
            };
            // The highest land near the town, and a waterfall on it if there is one.
            if (FindPeak(out var peak, out int level, out var fall))
            {
                float h = level * WorldGen.LevelHeight;
                var mid = Vector2.Lerp(farm, peak, 0.55f);
                keys.Add(new Key { focus = new Vector3(mid.x, -(-h * 0.4f), mid.y), yaw = 230f, size = 30f, pitch = 40f, hold = 7f });
                keys.Add(new Key { focus = new Vector3(peak.x, -(-h * 0.7f), peak.y), yaw = 300f, size = 28f, pitch = 34f, hold = 8f });
                if (fall != Vector2.zero)
                    keys.Add(new Key { focus = new Vector3(fall.x, -(-WorldGen.HeightAt(fall) - 2f), fall.y), yaw = 340f, size = 14f, pitch = 30f, hold = 7f });
                keys.Add(new Key { focus = new Vector3(peak.x, -(-h * 0.5f), peak.y), yaw = 380f, size = 40f, pitch = 55f, hold = 7f });
                // Make sure the land is there to be seen: load round each stop along the way.
                foreach (var k in keys) ChunkWorld.Instance.LoadAround(k.focus.G());
            }
            var hero = HeroController.Instance.transform.position;
            keys.Add(new Key { focus = hero, yaw = 360f * Mathf.Ceil(keys[keys.Count - 1].yaw / 360f), size = 9f, pitch = 52f, hold = 4f });
            foreach (var k in keys) ChunkWorld.Instance.LoadAround(k.focus.G());

            // Recording renders into its own 1600x900 target, whatever size the window is.
            RenderTexture target = null;
            Texture2D grab = null;
            var cam = Camera.main;
            if (Record)
            {
                System.IO.Directory.CreateDirectory(RecordFolder);
                Time.captureFramerate = 20;
                target = new RenderTexture(RecordWidth, RecordHeight, 24);
                grab = new Texture2D(RecordWidth, RecordHeight, TextureFormat.RGB24, false);
                cam.targetTexture = target;
            }
            CameraFollow.CinematicActive = true;
            int frame = 0;
            for (int i = 1; i < keys.Count; i++)
            {
                var a = keys[i - 1]; var b = keys[i];
                for (float t = 0f; t < b.hold; t += Time.deltaTime)
                {
                    float s = Smooth(t / b.hold);
                    // Ease the focus, size and angles together; loading chunks round the moving focus as it flies.
                    CameraFollow.CinematicFocus = Vector3.Lerp(a.focus, b.focus, s);
                    CameraFollow.CinematicYaw = Mathf.Lerp(a.yaw, b.yaw, s);
                    CameraFollow.CinematicSize = Mathf.Lerp(a.size, b.size, s);
                    CameraFollow.CinematicPitch = Mathf.Lerp(a.pitch, b.pitch, s);
                    if (Record)
                    {
                        yield return new WaitForEndOfFrame();
                        var prev = RenderTexture.active;
                        RenderTexture.active = target;
                        grab.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                        grab.Apply();
                        RenderTexture.active = prev;
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(RecordFolder, $"f{frame++:0000}.jpg"), grab.EncodeToJPG(90));
                    }
                    if (!Record && (Input.anyKeyDown || Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)) { i = keys.Count; break; }
                    yield return null;
                }
            }
            CameraFollow.CinematicActive = false;
            Time.captureFramerate = 0;
            if (target != null) { cam.targetTexture = null; target.Release(); Destroy(target); Destroy(grab); Record = false; }
            foreach (var c in hidden) if (c != null) c.enabled = true;
            if (heroCombat != null) heroCombat.Stats.RemoveModifiersFrom("cinematic");
            Playing = false;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // The highest ground within reach of the town (sampled on a coarse grid), and the nearest waterfall to it.
        static bool FindPeak(out Vector2 peak, out int level, out Vector2 fall)
        {
            peak = Vector2.zero; level = 0; fall = Vector2.zero;
            float bestScore = 0f;
            for (int r = 160; r <= 520; r += 24)
                for (int a = 0; a < 360; a += 6)
                {
                    var p = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * r;
                    int lv = WorldGen.ElevAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                    float score = lv - r * 0.012f;          // high, and not too far
                    if (lv >= 8 && score > bestScore) { bestScore = score; peak = p; level = lv; }
                }
            if (level == 0) return false;
            float bestFall = float.MaxValue;
            for (int x = -60; x <= 60; x++)
                for (int y = -60; y <= 60; y++)
                {
                    int wx = Mathf.FloorToInt(peak.x) + x, wy = Mathf.FloorToInt(peak.y) + y;
                    if (WorldGen.CellAt(wx, wy) != Cell.Water) continue;
                    int lv = WorldGen.ElevAt(wx, wy);
                    if (lv < 2 || WorldGen.ElevAt(wx, wy - 1) >= lv) continue;   // falls toward the camera (south)
                    float d = x * x + y * y;
                    if (d < bestFall) { bestFall = d; fall = new Vector2(wx + 0.5f, wy + 0.5f); }
                }
            return true;
        }
    }
}
