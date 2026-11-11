using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Flames, gas or frost on a creature while it burns, is poisoned or chilled.
    public class StatusFx : MonoBehaviour
    {
        static readonly (StatusType type, string sheet)[] kinds =
            { (StatusType.Burn, "flame_fire"), (StatusType.Poison, "flame_poison"), (StatusType.Chill, "flame_frost") };

        Combatant body;
        readonly List<FxPlayer>[] shown = { new List<FxPlayer>(), new List<FxPlayer>(), new List<FxPlayer>() };

        public static void Ensure(Combatant c)
        {
            if (c != null && c.GetComponent<StatusFx>() == null) c.gameObject.AddComponent<StatusFx>().body = c;
        }

        void Update()
        {
            for (int k = 0; k < kinds.Length; k++)
            {
                bool on = body != null && !body.IsDead && body.HasStatus(kinds[k].type);
                var list = shown[k];
                list.RemoveAll(p => p == null);
                if (on && list.Count == 0) Show(k);
                else if (!on && list.Count > 0)
                {
                    foreach (var p in list) p.Stop();
                    list.Clear();
                }
            }
        }

        void Show(int k)
        {
            float h = body.height;
            int count = h > 2.4f ? 3 : 2;
            for (int i = 0; i < count; i++)
            {
                // Spread across the body from the hips up, in front of it, then carried along.
                float side = (i - (count - 1) / 2f) * 0.26f * Mathf.Max(1f, h / 1.6f);
                var p = Fx.Play(kinds[k].sheet, (Vector2)transform.position, (0.5f + 0.18f * h) * (i == 1 ? 1.15f : 1f), null, 0f, 9999f);
                if (p == null) return;
                p.transform.position += View.Right * side + View.Up * (h * (i % 2 == 0 ? 0.18f : 0.32f));
                Fx.Lift(p, 1f);
                p.StartAt(i * 3);
                p.Follow(transform);
                shown[k].Add(p);
            }
        }

        void OnDestroy()
        {
            foreach (var list in shown)
                foreach (var p in list) if (p != null) Destroy(p.gameObject);
        }
    }
}
