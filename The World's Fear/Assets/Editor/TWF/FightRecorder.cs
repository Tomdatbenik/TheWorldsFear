using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace TWF.EditorTools
{
    // Records short clips of the hero fighting monsters from the running game, driven by `unity command script eval` in Play mode:
    // FightRecorder.Begin("melee", "out.mp4"), then poll FightRecorder.Done. The hero is healed every frame so a clip can never kill it
    // (a dead hero deletes the save). Monsters and the weapon are put back between clips only as far as the save file: back it up first.
    public static class FightRecorder
    {
        const int Fps = 30, W = 1280, H = 720, Kbps = 3200;
        static readonly Vector2 Center = new Vector2(2.5f, 5f);

        static MediaEncoder enc;
        static RenderTexture rt;
        static Texture2D tex;
        static VirtualJoystick fakeStick, realStick;
        static int frame, lastGameFrame, frames;
        static Action<float> script;
        static readonly List<MonsterBrain> spawned = new List<MonsterBrain>();
        static float respawnAt;
        static readonly System.Random rng = new System.Random(5);
        public static bool Done { get; private set; } = true;

        static HeroController Hero => HeroController.Instance;

        public static string Begin(string clip, string mp4Path, float seconds = 0f)
        {
            if (!Application.isPlaying) return "not playing";
            if (!Done) return "busy";
            Done = false;
            frame = 0;
            lastGameFrame = -1;
            spawned.Clear();
            respawnAt = 0f;
            ClearMonsters();
            var hero = Hero;
            Vector2 p = Center;
            hero.GetComponent<Rigidbody2D>().position = p;
            hero.transform.position = p;
            var follow = Camera.main.GetComponent<CameraFollow>();
            follow.distance = 7.5f;                 // close: these are watched on a phone
            follow.SnapToTarget();
            DayNight.SetTime(0.5f);
            Directory.CreateDirectory(Path.GetDirectoryName(mp4Path));
            enc = new MediaEncoder(mp4Path, new VideoTrackEncoderAttributes(new H264EncoderAttributes { gopSize = 30, numConsecutiveBFrames = 0 })
                { frameRate = new MediaRational(Fps), width = W, height = H, targetBitRate = (uint)(Kbps * 1000), includeAlpha = false });
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            Time.captureFramerate = Fps;
            realStick = MobaInput.Joystick;
            var go = new GameObject("FightStick");
            go.SetActive(false);
            fakeStick = go.AddComponent<VirtualJoystick>();
            MobaInput.Joystick = fakeStick;
            SetUp(clip);
            frames = Mathf.RoundToInt((seconds > 0f ? seconds : Length) * Fps);
            EditorApplication.update += Tick;
            return "recording " + clip + " " + frames + " frames";
        }

        static float Length = 12f;

        // ---------- Clips ----------

        static void SetUp(string clip)
        {
            Length = 12f;
            switch (clip)
            {
                case "melee":
                    Weapon(WeaponType.Sword);
                    S("moss_slime", new Vector2(4f, 2f)); S("shambling_dead", new Vector2(-3f, 3f)); S("shambling_dead", new Vector2(3f, -3f)); S("moss_slime", new Vector2(-4f, -2f));
                    script = t => { Circle(t, 2.4f); Attack(Nearest()); KeepUp(t, 4, "moss_slime", "shambling_dead"); };
                    break;
                case "ranged":
                    Weapon(WeaponType.Bow);
                    S("moss_slime", new Vector2(6f, 2f)); S("shambling_dead", new Vector2(5f, -3f)); S("bristle_boar", new Vector2(7f, 0f));
                    script = t =>
                    {
                        Kite(t);
                        Attack(Nearest());
                        KeepUp(t, 3, "moss_slime", "shambling_dead");
                    };
                    break;
                case "tank":
                    Weapon(WeaponType.Bow);
                    {
                        var a = S("ember_imp", new Vector2(8f, 1f));
                        var b = S("ember_imp", new Vector2(8f, -2f));
                        var g = S("barkshield_treant", new Vector2(4f, 3f));
                        var pack = new List<MonsterBrain> { a, b, g };
                        foreach (var m in pack) m.Pack = pack;
                        script = t =>
                        {
                            Stand();
                            var aim = spawned.Find(m => m != null && !m.Combatant.IsDead && m.Species.preferredDistance > 0f);
                            Attack(aim != null ? aim.Combatant : Nearest());
                        };
                    }
                    break;
                case "quick":
                    Weapon(WeaponType.Sword);
                    S("forest_spider", new Vector2(5f, 2f)); S("mire_stalker", new Vector2(-4f, 3f)); S("crypt_blade", new Vector2(3f, -4f));
                    script = t => { Stand(); Attack(Nearest()); };
                    break;
                case "shooters":
                    Weapon(WeaponType.Sword);
                    S("bone_archer", new Vector2(6f, 3f)); S("bog_toad", new Vector2(6f, -2f)); S("sporecap_spitter", new Vector2(-6f, 2f));
                    S("ash_cultist", new Vector2(7f, 1f)); S("blight_wisp", new Vector2(-7f, -2f)); S("crypt_wraith", new Vector2(2f, 7f));
                    script = t => { Circle(t, 1.2f); };
                    break;
                case "charger":
                    Weapon(WeaponType.Sword);
                    S("bristle_boar", new Vector2(8f, 2f)); S("cinder_ram", new Vector2(-8f, -1f)); S("grave_hound", new Vector2(2f, 8f));
                    script = t => { Circle(t * 0.7f, 4f); Attack(Nearest()); };      // keeps moving so the chargers have room for a run-up
                    Length = 15f;
                    break;
                case "exploder":
                    Weapon(WeaponType.Sword);
                    S("fire_slime", new Vector2(6f, 1f)); S("bloat_slime", new Vector2(-6f, 2f)); S("fire_slime", new Vector2(3f, -6f));
                    script = t => { Stand(); Attack(Nearest()); KeepUp(t, 3, "fire_slime", "bloat_slime"); };
                    break;
                case "goblins":
                    Weapon(WeaponType.Sword);
                    Band("forest", 11);
                    script = t => { Circle(t, 2f); Attack(Nearest()); };
                    Length = 15f;
                    break;
                case "undead":
                    Weapon(WeaponType.Bow);
                    Band("ruins", 23);
                    script = t => { Kite(t); Attack(Nearest()); };
                    Length = 15f;
                    break;
                default:
                    throw new ArgumentException("unknown clip " + clip);
            }
        }

        static MonsterBrain S(string id, Vector2 offset)
        {
            var sp = MonsterGenerator.CreateMob(MobCatalog.Get(id));
            var m = MonsterBrain.Spawn(sp, 1, MonsterRank.Normal, Center + offset);
            Wake(m);
            spawned.Add(m);
            return m;
        }

        static void Wake(MonsterBrain m)
        {
            m.leashRange = 200f;
            m.aggroRange = 200f;
            m.Aggro();
        }

        static void Band(string zoneId, int seed)
        {
            var zone = GameDatabase.Instance.FindZone(zoneId);
            var list = new List<MonsterBrain>();
            Warband.Spawn(zone, zone.biome, Center + new Vector2(0f, 8f), 1, list, new System.Random(seed));
            foreach (var m in list) { Wake(m); spawned.Add(m); }
        }

        // Keep at least n monsters on screen for a fight that would otherwise end early.
        static void KeepUp(float t, int n, params string[] ids)
        {
            if (t < respawnAt) return;
            spawned.RemoveAll(m => m == null || m.Combatant.IsDead);
            if (spawned.Count >= n) return;
            respawnAt = t + 0.8f;
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            S(ids[rng.Next(ids.Length)], new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 7f);
        }

        // ---------- The hero's hands ----------

        // Nothing else may turn the camera mid-clip.
        static void HoldCamera(CameraFollow f)
        {
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(CameraFollow).GetField("yaw", bf).SetValue(f, 0f);
            typeof(CameraFollow).GetField("targetYaw", bf).SetValue(f, 0f);
        }

        static void Weapon(WeaponType t)
        {
            var item = Profile.Equipped(ItemSlot.Weapon);
            if (item != null) item.weaponType = (int)t;
            Hero.RebuildStats();
        }

        static void Stick(Vector2 v) => typeof(VirtualJoystick).GetProperty("Value").SetValue(fakeStick, v, null);
        static void Stand() => Stick(Vector2.zero);

        static void Circle(float t, float r)
        {
            float a = t * 1.1f;
            Vector2 goal = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            Vector2 to = goal - (Vector2)Hero.transform.position;
            Stick(to.magnitude > 0.2f ? (Vector2)View.ToScreen(to.normalized) : Vector2.zero);
        }

        // Stand still and shoot, then step away from the nearest monster for a moment.
        static void Kite(float t)
        {
            var near = Nearest();
            if (near != null && Mathf.Repeat(t, 2.5f) > 1.7f)
            {
                Vector2 away = ((Vector2)Hero.transform.position - (Vector2)near.transform.position).normalized;
                Stick(View.ToScreen(away));
            }
            else Stand();
        }

        static Combatant Nearest() => Combat.NearestHostile(Team.Player, Hero.transform.position, 30f);

        static void Attack(Combatant target)
        {
            if (target == null || target.IsDead) return;
            Vector2 dir = ((Vector2)target.transform.position - (Vector2)Hero.transform.position).normalized;
            var m = typeof(HeroController).GetMethod("TryBasicAttack", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(Hero, new object[] { target, (Vector2?)dir });
        }

        // ---------- Capture ----------

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Finish(); return; }
            if (Time.frameCount == lastGameFrame) return;
            lastGameFrame = Time.frameCount;

            var hc = Hero.Combatant;
            hc.Stats.SetBase(StatType.MaxHP, 5000000f);
            hc.Heal(5000000f, false);
            float t = frame / (float)Fps;
            script?.Invoke(t);

            var cam = Camera.main;
            cam.orthographicSize = 6f;              // the follow camera sizes itself from the Game view; a clip wants a fixed close framing
            HoldCamera(cam.GetComponent<CameraFollow>());
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            enc.AddFrame(tex);
            frame++;
            if (frame >= frames) Finish();
        }

        static void ClearMonsters()
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBrain>()) UnityEngine.Object.Destroy(m.gameObject);
            foreach (var p in UnityEngine.Object.FindObjectsByType<Projectile>()) UnityEngine.Object.Destroy(p.gameObject);
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            enc?.Dispose();
            enc = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            Time.captureFramerate = 0;
            if (fakeStick != null) UnityEngine.Object.DestroyImmediate(fakeStick.gameObject);
            MobaInput.Joystick = realStick;
            script = null;
            ClearMonsters();
            Done = true;
        }
    }
}
