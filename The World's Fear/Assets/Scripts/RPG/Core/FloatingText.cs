using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // Pooled world-space popup text: damage numbers, heals, "Level Up!".
    public class FloatingText : MonoBehaviour
    {
        static readonly Stack<FloatingText> pool = new Stack<FloatingText>();
        static Transform root;

        TextMeshPro tmp;
        float life, maxLife;
        Vector3 velocity;
        Color color;
        float baseScale;

        public static void Damage(Vector3 pos, float amount, bool crit, bool onPlayer)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(amount));
            Color c = onPlayer ? new Color(1f, 0.35f, 0.3f) : crit ? new Color(1f, 0.85f, 0.2f) : Color.white;
            Spawn(pos, crit ? n + "!" : n.ToString(), c, crit ? 1.35f : 0.9f);
        }

        public static FloatingText Spawn(Vector3 pos, string text, Color color, float scale = 1f, float duration = 0.8f)
        {
            if (root == null) root = new GameObject("FloatingText").transform;
            FloatingText ft = null;
            while (pool.Count > 0 && ft == null) ft = pool.Pop();
            if (ft == null)
            {
                var go = new GameObject("ft");
                go.transform.SetParent(root, false);
                ft = go.AddComponent<FloatingText>();
                ft.tmp = go.AddComponent<TextMeshPro>();
                ft.tmp.font = GameAssets.Font;
                ft.tmp.alignment = TextAlignmentOptions.Center;
                ft.tmp.fontStyle = FontStyles.Bold;
                ft.tmp.outlineWidth = 0.25f;
                ft.tmp.outlineColor = new Color32(0, 0, 0, 255);
                ft.tmp.sortingOrder = SortOrder.Text;
                ft.tmp.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
                go.AddComponent<Billboard>();
                // Always readable, even behind trees.
                ft.tmp.fontMaterial.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            }
            ft.gameObject.SetActive(true);
            ft.transform.position = pos + View.Right * Random.Range(-0.25f, 0.25f);
            ft.tmp.text = text;
            ft.tmp.fontSize = 5f;
            ft.color = color;
            ft.tmp.color = color;
            ft.baseScale = scale;
            ft.transform.localScale = Vector3.one * scale;
            ft.life = ft.maxLife = duration;
            ft.velocity = View.Right * Random.Range(-0.4f, 0.4f) + View.Up * 2.2f;
            return ft;
        }

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f)
            {
                gameObject.SetActive(false);
                pool.Push(this);
                return;
            }
            float t = 1f - life / maxLife;
            transform.position += velocity * Time.deltaTime;
            velocity -= View.Up * (4f * Time.deltaTime);
            float pop = t < 0.15f ? Mathf.Lerp(1.4f, 1f, t / 0.15f) : 1f;
            transform.localScale = Vector3.one * baseScale * pop;
            tmp.color = new Color(color.r, color.g, color.b, t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f);
        }
    }
}
