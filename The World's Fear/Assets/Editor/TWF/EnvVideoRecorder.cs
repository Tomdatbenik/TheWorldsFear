using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace TWF.EditorTools
{
    // Records the running game to an MP4 (walking past the test spot, turning the camera) and measures how far each
    // building's baked footprint sits from where the ground really shows it, in pixels at 720p. Called from `unity command
    // script eval` in Play mode; the step-by-step run script waits on Done.
    public static class EnvVideoRecorder
    {
        const int Fps = 30, W = 1280, H = 720;
        static int Frames = 450;
        static MediaEncoder enc;
        static RenderTexture rt;
        static Texture2D tex;
        static string logPath;
        static int frame, lastGameFrame;
        static StringBuilder log;
        static VirtualJoystick fakeStick, realStick;
        public static bool Done { get; private set; }

        // ---------- A scripted tour: run to waypoints, spin the camera, at a chosen time of day ----------

        struct Step { public Vector2 to; public float wait; public int turns; }
        static List<Step> tour;
        static int stepIndex, turnsDone;
        static float stepClock;

        static Step Run(float x, float y) => new Step { to = new Vector2(x, y) };
        static Step Wait(float s) => new Step { wait = s };
        static Step Turns(int n) => new Step { turns = n };

        static List<Step> TourPlan(string kind)
        {
            switch (kind)
            {
                case "behind":
                {
                    // Behind the slate house, then behind the nearest tree.
                    Vector2 tree = new Vector2(14f, 24f);
                    float best = float.MaxValue;
                    foreach (var c in Object.FindObjectsByType<CircleCollider2D>())
                    {
                        if (!c.name.StartsWith("oak")) continue;
                        float d = Vector2.Distance(c.transform.position, new Vector2(24f, 21f));
                        if (d < best) { best = d; tree = c.transform.position; }
                    }
                    return new List<Step>
                    {
                        Wait(1f), Run(5.2f, 11f), Run(5.2f, 19f), Run(9f, 19f), Wait(1.2f), Turns(2), Wait(0.8f), Run(15f, 19.5f), Run(tree.x, 19.5f),
                        Run(tree.x, tree.y + 1.3f), Wait(1.2f), Turns(2), Wait(0.8f), Run(tree.x + 2.4f, tree.y + 1.3f), Run(tree.x + 2.4f, tree.y - 2f), Wait(1f),
                    };
                }
                case "dusk":
                case "night":
                    return new List<Step>
                    {
                        Wait(0.8f), Run(2.5f, 12f), Wait(0.3f), Turns(4), Run(-9f, 4.5f), Wait(0.3f), Turns(2), Run(2.5f, 4.5f), Wait(0.5f),
                    };
                default:
                    return new List<Step>
                    {
                        Wait(1f), Run(2.5f, 9f), Run(2.5f, 19f), Wait(0.4f), Turns(8),
                        Run(2.5f, 28f), Wait(0.3f), Run(2.5f, 5f), Run(-14f, 4.5f), Wait(0.4f), Turns(8),
                        Run(-3f, 4.5f), Run(14f, 4.5f), Wait(0.3f), Turns(2), Run(2.5f, 4.5f), Run(2.5f, -8f), Run(2.5f, -14f), Wait(0.4f), Turns(4), Wait(1f),
                    };
            }
        }

        public static string BeginTour(string kind, string mp4Path, string logFile, float time01, float startX, float startY)
        {
            string r = Begin(mp4Path, logFile, startX, startY, 17f, 2600);
            if (r != "recording") return r;
            tour = TourPlan(kind);
            stepIndex = 0;
            turnsDone = 0;
            stepClock = 0f;
            Frames = 60 * Fps;   // a limit; the tour ends itself when its steps run out
            DayNight.SetTime(time01);
            return r + " " + kind;
        }

        // True while the tour has steps left.
        static bool TourTick(float dt)
        {
            if (stepIndex >= tour.Count) { Stick(Vector2.zero); return false; }
            var s = tour[stepIndex];
            stepClock += dt;
            var hero = HeroController.Instance;
            Vector2 stick = Vector2.zero;
            if (s.wait > 0f)
            {
                if (stepClock >= s.wait) NextStep();
            }
            else if (s.turns > 0)
            {
                // One 45 degree camera step every 0.7 seconds, the speed of tapping Q or E.
                if (turnsDone >= s.turns && stepClock >= 0.7f) NextStep();
                else if (turnsDone < s.turns && stepClock >= 0.7f * turnsDone)
                {
                    CameraFollow.RotateBy(45f);
                    turnsDone++;
                }
            }
            else
            {
                var to = s.to - (Vector2)hero.transform.position;
                if (to.magnitude < 0.35f || stepClock > 6f) NextStep();
                else stick = View.ToScreen(to.normalized);
            }
            Stick(stick);
            return true;
        }

        static void NextStep()
        {
            stepIndex++;
            turnsDone = 0;
            stepClock = 0f;
        }

        public static string Begin(string mp4Path, string logFile, float startX, float startY, float dist, int kbps = 0)
        {
            if (!Application.isPlaying) return "not playing";
            Done = false;
            frame = 0;
            lastGameFrame = -1;
            logPath = logFile;
            log = new StringBuilder("frame,seconds,maxDriftPx\n");
            Directory.CreateDirectory(Path.GetDirectoryName(mp4Path));
            tour = null;
            Frames = 450;
            if (kbps > 0)
                enc = new MediaEncoder(mp4Path, new VideoTrackEncoderAttributes(new H264EncoderAttributes { gopSize = 30, numConsecutiveBFrames = 2 })
                    { frameRate = new MediaRational(Fps), width = W, height = H, targetBitRate = (uint)(kbps * 1000), includeAlpha = false });
            else
                enc = new MediaEncoder(mp4Path, new VideoTrackAttributes { frameRate = new MediaRational(Fps), width = W, height = H, includeAlpha = false });
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var hero = HeroController.Instance;
            var p = new Vector2(startX, startY);
            hero.GetComponent<Rigidbody2D>().position = p;
            hero.transform.position = p;
            var follow = Camera.main.GetComponent<CameraFollow>();
            follow.distance = dist;
            CameraFollow.RotateBy(-9999f);
            SetYaw(follow, 0f);
            follow.SnapToTarget();
            Time.captureFramerate = Fps;
            realStick = MobaInput.Joystick;
            var go = new GameObject("VideoStick");
            go.SetActive(false);
            fakeStick = go.AddComponent<VirtualJoystick>();
            MobaInput.Joystick = fakeStick;
            EditorApplication.update += Tick;
            return "recording";
        }

        static void SetYaw(CameraFollow f, float yaw)
        {
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(CameraFollow).GetField("yaw", bf).SetValue(f, yaw);
            typeof(CameraFollow).GetField("targetYaw", bf).SetValue(f, yaw);
        }

        static void Stick(Vector2 v)
        {
            var prop = typeof(VirtualJoystick).GetProperty("Value");
            prop.SetValue(fakeStick, v, null);
        }

        static void Plan(float t)
        {
            Vector2 stick = Vector2.zero;
            if (t >= 0.5f && t < 2.8f) stick = Vector2.up;
            if (t >= 12f && t < 14f) stick = Vector2.right;
            Stick(stick);
            foreach (float at in new[] { 3.5f, 4.5f, 5.5f, 6.5f, 7.5f, 8.5f, 9.5f, 10.5f })
                if (Mathf.Abs(t - at) < 0.5f / Fps) CameraFollow.RotateBy(45f);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Finish(); return; }
            if (Time.frameCount == lastGameFrame) return;
            lastGameFrame = Time.frameCount;

            float t = frame / (float)Fps;
            if (tour != null)
            {
                if (!TourTick(1f / Fps)) { Finish(); return; }
            }
            else Plan(t);
            var cam = Camera.main;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            enc.AddFrame(tex);
            log.AppendLine($"{frame},{t:F3},{Drift(cam):F1}");
            frame++;
            if (frame >= Frames) Finish();
        }

        // For each visible building: how far does the sprite put its footprint corners from the ground that is really there?
        static float Drift(Camera cam)
        {
            float worst = 0f;
            var R = cam.transform.right;
            var U = cam.transform.up;
            foreach (var e in Object.FindObjectsByType<EnvSprite>())
            {
                var c = e.GetComponent<BoxCollider2D>();
                if (c == null) continue;
                var pivot = e.transform.position;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    var rel = new Vector3(sx * c.size.x / 2f, sy * c.size.y / 2f, 0f);
                    var truePos = pivot + rel;
                    var a = cam.WorldToScreenPoint(truePos);
                    if (a.z <= 0f || a.x < 0 || a.x > cam.pixelWidth || a.y < 0 || a.y > cam.pixelHeight) continue;
                    var card = pivot + R * Vector3.Dot(rel, R) + U * Vector3.Dot(rel, U);
                    var b = cam.WorldToScreenPoint(card);
                    float scale = H / (float)cam.pixelHeight;
                    worst = Mathf.Max(worst, Vector2.Distance(a, b) * scale);
                }
            }
            return worst;
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            enc?.Dispose();
            enc = null;
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            Time.captureFramerate = 0;
            if (fakeStick != null) Object.DestroyImmediate(fakeStick.gameObject);
            MobaInput.Joystick = realStick;
            File.WriteAllText(logPath, log.ToString());
            Done = true;
        }
    }
}
