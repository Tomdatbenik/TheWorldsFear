using System;
using System.Collections;
using UnityEngine;

namespace TWF
{
    // Walks the hero along a flight of stairs, then fades out and hands over to the next scene.
    public static class DungeonTravel
    {
        const float MaxSeconds = 8f;     // never trap the player if something stops the walk
        const float FadeFrom = 0.45f;    // how far along the stairs the screen starts going dark
        const float FadeSeconds = 0.9f;

        public static IEnumerator WalkStairs(HeroController hero, Vector2 from, Vector2 to, float startZ, float endZ, Action arrive)
        {
            var collider = hero.GetComponent<Collider2D>();
            collider.enabled = false;
            hero.Combatant.Invulnerable = true;
            float time = 0f;

            // First to the foot of the steps.
            while (time < MaxSeconds && Vector2.Distance(hero.transform.position, from) > 0.2f)
            {
                MobaInput.Blocked = true;
                hero.AutoWalk = (from - (Vector2)hero.transform.position).normalized;
                time += Time.deltaTime;
                yield return null;
            }

            bool fading = false;
            float length = Vector2.Distance(from, to);
            while (time < MaxSeconds)
            {
                MobaInput.Blocked = true;
                Vector2 pos = hero.transform.position;
                float along = Mathf.Clamp01(Vector2.Dot(pos - from, (to - from).normalized) / length);
                hero.AutoWalk = (to - pos).normalized;
                var p = hero.transform.position;
                hero.transform.position = new Vector3(p.x, p.y, Mathf.Lerp(startZ, endZ, along));
                if (!fading && along >= FadeFrom)
                {
                    fading = true;
                    SceneFade.ToBlack(FadeSeconds);
                }
                if (along >= 0.98f) break;
                time += Time.deltaTime;
                yield return null;
            }

            hero.AutoWalk = Vector2.zero;
            if (!fading) SceneFade.ToBlack(FadeSeconds);
            yield return new WaitForSeconds(0.25f);
            arrive();
        }
    }
}
