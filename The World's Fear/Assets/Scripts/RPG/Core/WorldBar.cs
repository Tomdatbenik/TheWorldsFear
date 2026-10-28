using TMPro;
using UnityEngine;

namespace TWF
{
    // Health bar (and optional nameplate) floating above a combatant.
    public class WorldBar : MonoBehaviour
    {
        Combatant target;
        Transform fill, shield, delayFill;
        float width;
        float delayed = 1f;
        float hideTimer;
        bool alwaysShow;
        GameObject visual;
        float yOffset;

        public static WorldBar Attach(Combatant c, float yOffset, float width, string label, Color labelColor, Color barColor, bool alwaysShow)
        {
            var go = new GameObject("WorldBar");
            go.transform.SetParent(c.transform, false);
            go.AddComponent<Billboard>();
            var bar = go.AddComponent<WorldBar>();
            bar.yOffset = yOffset;
            bar.target = c;
            bar.width = width;
            bar.alwaysShow = alwaysShow;
            bar.Build(label, labelColor, barColor);
            return bar;
        }

        void Build(string label, Color labelColor, Color barColor)
        {
            visual = new GameObject("visual");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = new Vector3(0f, yOffset, 0f);
            float h = 0.16f;

            var bg = GameAssets.CreateWorldSprite("bg", GameAssets.Pixel, visual.transform, new Color(0, 0, 0, 0.75f), SortOrder.Bars);
            bg.transform.localScale = new Vector3(width + 0.08f, h + 0.08f, 1f) / bg.sprite.bounds.size.x;

            delayFill = MakeFill("delay", new Color(1f, 0.9f, 0.6f, 0.9f), SortOrder.Bars + 1, h);
            fill = MakeFill("fill", barColor, SortOrder.Bars + 2, h);
            shield = MakeFill("shield", new Color(Palette.Shield.r, Palette.Shield.g, Palette.Shield.b, 0.85f), SortOrder.Bars + 3, h * 0.4f);

            if (!string.IsNullOrEmpty(label))
            {
                var tgo = new GameObject("label");
                tgo.transform.SetParent(visual.transform, false);
                tgo.transform.localPosition = new Vector3(0f, 0.32f, 0f);
                var tmp = tgo.AddComponent<TextMeshPro>();
                tmp.font = GameAssets.Font;
                tmp.text = label;
                tmp.fontSize = 2.4f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = labelColor;
                tmp.outlineWidth = 0.3f;
                tmp.outlineColor = new Color32(0, 0, 0, 255);
                tmp.sortingOrder = SortOrder.Bars + 4;
                tmp.rectTransform.sizeDelta = new Vector2(8f, 1f);
            }

            visual.SetActive(alwaysShow);
            target.Damaged += (_, __) => hideTimer = 4f;
        }

        Transform MakeFill(string name, Color c, int order, float h)
        {
            var pivot = new GameObject(name + "Pivot").transform;
            pivot.SetParent(visual.transform, false);
            pivot.localPosition = new Vector3(-width / 2f, 0f, 0f);
            var sr = GameAssets.CreateWorldSprite(name, GameAssets.Pixel, pivot, c, order);
            float unit = sr.sprite.bounds.size.x;
            sr.transform.localScale = new Vector3(1f / unit, h / unit, 1f);
            sr.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            pivot.localScale = new Vector3(width, 1f, 1f);
            return pivot;
        }

        void LateUpdate()
        {
            if (target == null) return;
            // Keep the size constant even when the parent is scaled (the Billboard handles rotation).
            var ls = target.transform.lossyScale;
            transform.localScale = new Vector3(1f / Mathf.Max(0.01f, Mathf.Abs(ls.x)), 1f / Mathf.Max(0.01f, Mathf.Abs(ls.y)), 1f);

            float f = target.HPFraction;
            delayed = Mathf.MoveTowards(delayed, f, Time.deltaTime * (delayed > f ? 0.6f : 5f));
            if (delayed < f) delayed = f;
            fill.localScale = new Vector3(width * f, 1f, 1f);
            delayFill.localScale = new Vector3(width * delayed, 1f, 1f);
            float s = target.MaxHP > 0 ? Mathf.Clamp01(target.ShieldHP / target.MaxHP) : 0f;
            shield.localScale = new Vector3(width * s, 1f, 1f);

            if (!alwaysShow)
            {
                if (hideTimer > 0f) hideTimer -= Time.deltaTime;
                bool show = !target.IsDead && (hideTimer > 0f || f < 1f || target.rank != MonsterRank.Normal);
                if (visual.activeSelf != show) visual.SetActive(show);
            }
            else if (target.IsDead && visual.activeSelf) visual.SetActive(false);
        }
    }
}
