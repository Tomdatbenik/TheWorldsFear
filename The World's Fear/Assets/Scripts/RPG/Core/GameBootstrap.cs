using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // Put one in every gameplay scene: spawns the hero, HUD, panels and camera, and handles death/respawn.
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        public Transform defaultSpawn;
        [Tooltip("Test mode: keeps spawning monsters from this zone around the hero.")]
        public string arenaZoneId;
        public int arenaMonsterCount = 8;

        public HeroController Hero { get; private set; }

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            MobaInput.Blocked = false;
            Application.targetFrameRate = 60;
            // 2.5D: sprites closer to the camera draw in front.
            GraphicsSettings.transparencySortMode = TransparencySortMode.Perspective;

            var db = GameDatabase.Instance;
            Vector2 spawn = SpawnPosition();
            var heroGo = Instantiate(db.heroPrefab, spawn, Quaternion.identity);
            heroGo.name = "Hero";
            Hero = heroGo.GetComponent<HeroController>();
            heroGo.GetComponent<Rigidbody2D>().mass = 3f;
            Hero.Died += OnHeroDied;

            var cam = Camera.main;
            if (cam != null)
            {
                foreach (var b in cam.GetComponents<Behaviour>())
                    if (b.GetType().Name.Contains("Cinemachine")) b.enabled = false;
                var follow = cam.GetComponent<CameraFollow>();
                if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
                follow.target = heroGo.transform;
                follow.SnapToTarget();
                if (cam.GetComponent<AudioListener>()) Destroy(cam.GetComponent<AudioListener>());
            }

            ZoneLighting.Create();
            var ui = new GameObject("UI").AddComponent<GameUI>();
            PanelsBuilder.Build(ui);
        }

        void OnEnable() => MonsterBrain.AnyDied += OnMonsterDied;
        void OnDisable() => MonsterBrain.AnyDied -= OnMonsterDied;

        void OnMonsterDied(MonsterBrain monster, Combatant killer) => Loot.Drop(monster.Combatant, monster.transform.position);

        void Start()
        {
            if (!string.IsNullOrEmpty(arenaZoneId)) StartCoroutine(ArenaLoop());
        }

        protected virtual Vector2 SpawnPosition()
        {
            if (WorldManager.Instance != null) return WorldManager.Instance.SpawnPoint();
            return defaultSpawn != null ? (Vector2)defaultSpawn.position : Vector2.zero;
        }

        void OnHeroDied() => StartCoroutine(DeathRoutine());

        // One life: the run ends, the save is wiped, and you start a new hero from the title screen.
        IEnumerator DeathRoutine()
        {
            var d = Profile.Data;
            string summary =
                $"<size=110%>YOU HAVE FALLEN</size>\n\n" +
                $"<size=45%><color=#ddd>{Profile.Class.displayName} · Level {d.level}\n" +
                $"Furthest journey: {Mathf.RoundToInt(d.farthest)}m from the village\n" +
                $"Monsters slain: {d.kills}   ·   Bosses slain: {d.bossesKilled}\n" +
                $"Gold: {d.gold}</color></size>";
            Profile.DeleteSave();
            yield return new WaitForSeconds(1.2f);
            GameUI.Instance.ShowDeath(summary);
            Time.timeScale = 0.3f;
            yield return new WaitForSecondsRealtime(1.5f);
            GameUI.Instance.ShowDeathButton("New hero", () =>
            {
                Time.timeScale = 1f;
                UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
            });
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Hero != null && !Hero.Combatant.IsDead) Profile.Save();
        }

        void OnApplicationQuit()
        {
            if (Hero != null && !Hero.Combatant.IsDead) Profile.Save();
        }

        // ---------- Test arena ----------

        IEnumerator ArenaLoop()
        {
            var zone = GameDatabase.Instance.FindZone(arenaZoneId);
            var species = MonsterGenerator.SpeciesFor(zone);
            int i = 0;
            while (true)
            {
                int alive = 0;
                foreach (var c in Combatant.All)
                    if (c.team == Team.Enemy && !c.IsDead) alive++;
                if (alive < arenaMonsterCount && Hero != null && !Hero.Combatant.IsDead)
                {
                    var sp = species[i++ % species.Count];
                    Vector2 at = (Vector2)Hero.transform.position + Random.insideUnitCircle.normalized * Random.Range(7f, 10f);
                    var rank = Random.value < 0.1f ? MonsterRank.Rare : Random.value < 0.2f ? MonsterRank.Elite : MonsterRank.Normal;
                    var affixes = rank == MonsterRank.Rare ? MonsterSpawner.RollAffixes(new System.Random(), 2) : null;
                    var m = MonsterBrain.Spawn(sp, Mathf.Clamp(Profile.Data.level, zone.minLevel, zone.maxLevel), rank, at, affixes, zone);
                    m.Aggro();
                }
                yield return new WaitForSeconds(1.2f);
            }
        }
    }
}
