using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // A boss's fortress on the overworld (WorldGen.Fortress): a raised arena reached by one stairway, past a terrace
    // held by the boss's generals. No portal and no loading screen: the boss lives up there and cannot leave.
    //  - sealed at the foot of the stairs until the boss before it has been slain;
    //  - the way from the terrace into the arena opens once the generals are dead;
    //  - the boss wakes when the hero steps into the arena.
    public class BossFortress : MonoBehaviour
    {
        // The fortress whose boss is up and fighting: BossTitan asks it where the arena is.
        public static BossFortress Active { get; private set; }

        WorldGen.Fortress f;
        BossKind kind;
        int level;
        string key;
        GameObject stairSeal, neckSeal;
        TextMeshPro label;
        readonly List<MonsterBrain> generals = new List<MonsterBrain>();
        readonly List<Vector2> edgePoints = new List<Vector2>();
        bool generalsSpawned, generalsDead, told;
        BossTitan boss;
        string labelText;

        public Vector2 Centre => f.ArenaCentre;

        public void Init(WorldGen.Fortress fortress)
        {
            f = fortress;
            kind = f.kind;
            level = WorldGen.LevelAt(f.gate) + 3;
            key = WorldManager.GateKey(f.gate);
        }

        bool Defeated => Profile.Data.defeatedGates.Contains(key);
        bool Sealed => !WorldManager.IsUnlocked(kind);

        void Start()
        {
            float h = WorldGen.PlateauHeight;
            var foot = new Vector2(f.StairA.x + 1f, f.StairA.y);                 // the middle of the stairs' bottom edge
            stairSeal = Barrier("Seal", foot + Vector2.down * 0.15f, 2.4f, 0f, new Color(1f, 0.25f, 0.15f, 0.8f));
            neckSeal = Barrier("Gate", new Vector2(f.gate.x, f.gate.y - 1f), f.Neck.width + 0.4f, h, new Color(1f, 0.6f, 0.2f, 0.8f));

            label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(Billboard.Create(transform), false);
            label.transform.position = new Vector3(foot.x, foot.y - 1.2f, -2.6f);
            label.font = GameAssets.Font;
            label.fontSize = 3f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.55f, 0.4f);
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(12, 2);

            for (int x = f.Arena.xMin; x < f.Arena.xMax; x++)
            for (int y = f.Arena.yMin; y < f.Arena.yMax; y++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (Inside(p, 2f) && !Inside(p, 3.2f)) edgePoints.Add(p);
            }
        }

        // A glowing wall across a passage: blocks while it is active.
        GameObject Barrier(string name, Vector2 at, float width, float height, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(at.x, at.y, 0f);
            go.layer = LayerMask.NameToLayer("Walls");
            go.AddComponent<BoxCollider2D>().size = new Vector2(width, 0.5f);
            int n = Mathf.CeilToInt(width / 0.8f);
            for (int i = 0; i < n; i++)
            {
                var bb = Billboard.Create(go.transform, "Flame" + i);
                bb.localPosition = new Vector3(-width / 2f + (i + 0.5f) * width / n, 0f, -height);
                var sr = GameAssets.CreateWorldSprite("Glow", GameAssets.PixelGlow, bb, color, SortOrder.Effects, 1.5f);
                sr.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                sr.transform.localScale = new Vector3(sr.transform.localScale.x * 0.7f, sr.transform.localScale.y * 1.6f, 1f);
            }
            PointLights.Attach(go.transform, color, 5f, 1.6f, new Vector3(0, 0, -height - 1f), true);
            return go;
        }

        void SetLabel(string text)
        {
            if (text == labelText) return;
            labelText = text;
            label.text = text;
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            Vector2 hp = hero.transform.position;
            string lair = BossTitan.LairName(kind), bossName = BossTitan.BossName(kind);

            if (Defeated)
            {
                stairSeal.SetActive(false);
                neckSeal.SetActive(false);
                SetLabel($"{bossName}, vanquished");
                return;
            }
            if (Sealed)
            {
                stairSeal.SetActive(true);
                neckSeal.SetActive(true);
                SetLabel($"{lair}\n<size=70%><color=#bbb>Sealed: first defeat {BossTitan.BossName(WorldManager.Previous(kind))}</color></size>");
                return;
            }
            stairSeal.SetActive(false);
            SetLabel($"{lair}\n<size=70%>{bossName} · Recommended level {level}</size>");

            // The generals take the terrace when someone comes near.
            if (!generalsSpawned && Vector2.Distance(hp, f.TerraceCentre) < 26f)
            {
                generalsSpawned = true;
                var zone = WorldGen.RegionAt(f.gate);
                var rng = new System.Random(f.gate.x * 73 + f.gate.y * 31);
                MonsterSpawner.SpawnPack(zone, f.TerraceCentre + Vector2.left * 3.5f, 0f, generals, rng, level, 1f, 0f, null, 0f);
                MonsterSpawner.SpawnPack(zone, f.TerraceCentre + Vector2.right * 3.5f, 0f, generals, rng, level, 0f, 1f, null, 0f);
                foreach (var g in generals) { g.leashRange = 40f; }
            }
            if (generalsSpawned && !generalsDead)
            {
                generals.RemoveAll(g => g == null || g.Combatant.IsDead);
                if (generals.Count == 0)
                {
                    generalsDead = true;
                    Sound.Play(Sfx.LevelUp, neckSeal.transform.position, 0.7f);
                    GameUI.Instance?.Toast($"The generals have fallen. {bossName} awaits.", new Color(1f, 0.6f, 0.3f), 4f);
                }
                else if (!told && f.Terrace.Contains(new Vector2Int(Mathf.FloorToInt(hp.x), Mathf.FloorToInt(hp.y))))
                {
                    told = true;
                    GameUI.Instance?.Toast($"Defeat {bossName}'s generals to open the way.", new Color(1f, 0.6f, 0.3f), 4f);
                }
            }
            neckSeal.SetActive(!generalsDead);

            // The boss wakes when the hero is inside the arena.
            if (boss == null && generalsDead && Inside(hp, 0.5f))
            {
                WorldManager.PendingGateKey = key;
                WorldManager.PendingBossKind = kind;
                WorldManager.PendingBossLevel = level;
                Active = this;
                boss = BossTitan.Spawn(f.ArenaCentre + Vector2.up * 4f, level, WorldGen.BossBiome(kind), kind);
                boss.transform.SetParent(transform, true);
            }
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            foreach (var g in generals) if (g != null) Destroy(g.gameObject);
        }

        // ---------- The arena, for the boss ----------

        public bool Inside(Vector2 p, float margin = 0f)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            if (!f.InArena(x, y)) return false;
            if (margin <= 0f) return true;
            int m = Mathf.CeilToInt(margin);
            for (int dx = -m; dx <= m; dx++)
            for (int dy = -m; dy <= m; dy++)
                if (dx * dx + dy * dy <= margin * margin && !f.InArena(x + dx, y + dy)) return false;
            return true;
        }

        public Vector2 ClampInside(Vector2 p, float margin = 1f)
        {
            if (Inside(p, margin)) return p;
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                var q = Vector2.Lerp(p, Centre, t);
                if (Inside(q, margin)) return q;
            }
            return Centre;
        }

        public Vector2 EdgePoint(Vector2 awayFrom)
        {
            if (edgePoints.Count == 0) return Centre;
            Vector2 best = edgePoints[0];
            float bestScore = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = edgePoints[Random.Range(0, edgePoints.Count)];
                float score = Vector2.Distance(p, awayFrom) + Random.value * 4f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }
    }
}
