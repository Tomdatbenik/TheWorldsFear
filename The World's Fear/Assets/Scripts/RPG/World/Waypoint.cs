using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // Diablo-style waypoint: touch it once to unlock, then teleport between unlocked ones from the map.
    public class Waypoint : MonoBehaviour
    {
        public string id;
        public string displayName;
        public int order;

        SpriteRenderer glow;
        bool heroInside;

        void Start()
        {
            var baseSr = GameAssets.CreateWorldSprite("Base", GameAssets.Diamond, transform, new Color(0.35f, 0.55f, 0.9f, 0.9f), SortOrder.Ground + 5, 1.8f);
            baseSr.transform.localScale = new Vector3(baseSr.transform.localScale.x, baseSr.transform.localScale.y * 0.5f, 1f);
            glow = GameAssets.CreateWorldSprite("Glow", GameAssets.PixelGlow, transform, new Color(0.5f, 0.8f, 1f, 0.5f), SortOrder.Ground + 6, 2.4f);
            baseSr.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            glow.transform.localPosition = new Vector3(0, 0, Ground.DecalZ * 2f);
            PointLights.Attach(transform, new Color(0.5f, 0.8f, 1f), 6f, 2.5f, new Vector3(0, 0, -1.5f), true);
            var label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(Billboard.Create(transform), false);
            label.transform.localPosition = new Vector3(0, 1.4f, 0);
            label.font = GameAssets.Font;
            label.text = displayName;
            label.fontSize = 2.6f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.7f, 0.9f, 1f);
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(8, 1);
        }

        void Update()
        {
            bool unlocked = Profile.Data.waypoints.Contains(id);
            float pulse = 0.35f + Mathf.Sin(Time.time * 3f) * 0.15f;
            glow.color = unlocked ? new Color(0.5f, 0.8f, 1f, pulse + 0.2f) : new Color(0.6f, 0.6f, 0.6f, 0.25f);

            var hero = HeroController.Instance;
            if (hero == null) return;
            bool inside = Vector2.Distance(hero.transform.position, transform.position) < 1.4f;
            if (inside && !heroInside)
            {
                if (!unlocked)
                {
                    Profile.UnlockWaypoint(id);
                    GameUI.Instance?.Toast($"Waypoint discovered: {displayName}", new Color(0.6f, 0.85f, 1f));
                    VFX.Ring(transform.position, new Color(0.5f, 0.8f, 1f), 2.5f, 0.6f);
                }
                Profile.Data.lastWaypoint = id;
                hero.Combatant.Heal(hero.Combatant.MaxHP, true);
                Profile.Save();
                // Offer travel with a button instead of popping a menu open mid-movement.
                if (Profile.Data.waypoints.Count > 1) GameUI.Instance?.ShowInteract("Travel", () => Panels.Toggle(PanelKind.Map));
            }
            else if (!inside && heroInside)
            {
                GameUI.Instance?.HideInteract();
            }
            heroInside = inside;
        }
    }
}
