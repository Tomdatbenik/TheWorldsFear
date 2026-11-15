using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Spray drifting up where a waterfall lands, while the hero is near enough to see it.
    public class FallSpray : MonoBehaviour
    {
        readonly List<Vector3> feet = new List<Vector3>();
        float timer;

        public void Init(Cell[,] cells, int[,] elev, int x0, int y0)
        {
            int n = cells.GetLength(0) - 2;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                if (cells[x + 1, y + 1] != Cell.Water) continue;
                int lv = elev[x + 1, y + 1];
                if (lv == 0) continue;
                // The lowest neighbour below a raised pool or stream is where its water lands.
                int[] dx = { 0, 0, -1, 1 }, dy = { -1, 1, 0, 0 };
                for (int k = 0; k < 4; k++)
                {
                    int nl = elev[x + dx[k] + 1, y + dy[k] + 1];
                    if (nl >= lv) continue;
                    feet.Add(new Vector3(x0 + x + 0.5f + dx[k] * 0.9f, y0 + y + 0.5f + dy[k] * 0.9f, -nl * WorldGen.LevelHeight));
                }
            }
        }

        void Update()
        {
            if (feet.Count == 0) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.18f;
            var hero = HeroController.Instance;
            if (hero == null) return;
            var p = feet[Random.Range(0, feet.Count)];
            if (((Vector2)p - (Vector2)hero.transform.position).sqrMagnitude > 30f * 30f) return;
            VFX.Burst(p, new Color(0.88f, 0.96f, 1f, 0.8f), 3, 0.5f);
        }
    }
}
