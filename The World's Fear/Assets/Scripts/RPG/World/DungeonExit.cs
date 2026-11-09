using UnityEngine;
using UnityEngine.SceneManagement;

namespace TWF
{
    // A flight of steps climbing out of the dungeon, back to the gate on the surface.
    public class DungeonExit : MonoBehaviour
    {
        public const int HalfWidth = 2;   // tiles either side of the centre column
        public const int Length = 4;      // tiles from the foot of the steps to the wall

        public string wallTexture = "stone_wall_tile";

        const float Rise = 0.4f;
        const float Reach = 2.2f;

        bool inRange, leaving;

        float W => HalfWidth + 0.5f;
        float Height => Length * Rise;

        void Start()
        {
            var stone = StoneQuads.Stone(wallTexture);
            var q = new StoneQuads();
            var up = Vector3.back;
            for (int i = 0; i < Length; i++)
            {
                float z = -(i + 1) * Rise;
                q.Add(new Vector3(-W, i, -i * Rise), Vector3.right, up, Vector3.down, 2f * W, Rise);
                q.Add(new Vector3(-W, i, z), Vector3.right, Vector3.up, up, 2f * W, 1f);
                q.Add(new Vector3(-W, i, 0f), Vector3.up, up, Vector3.left, 1f, -z);
                q.Add(new Vector3(W, i, 0f), Vector3.up, up, Vector3.right, 1f, -z);
            }
            q.Build(transform, "Steps Up", stone);

            var solid = new GameObject("Steps Collider");
            solid.transform.SetParent(transform, false);
            solid.transform.localPosition = new Vector3(0f, Length * 0.5f, 0f);
            solid.layer = LayerMask.NameToLayer("Walls");
            solid.AddComponent<BoxCollider2D>().size = new Vector2(2f * W, Length);
            solid.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;

            PointLights.Attach(transform, new Color(0.7f, 0.82f, 1f), 7f, 1.6f, new Vector3(0f, Length - 1f, -3f), true);
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null || GameUI.Instance == null || leaving) return;
            bool near = Vector2.Distance(hero.transform.position, transform.position) < Reach;
            if (near && !inRange) GameUI.Instance.ShowInteract("Climb out", () => Leave(hero));
            else if (!near && inRange) GameUI.Instance.HideInteract();
            inRange = near;
        }

        void Leave(HeroController hero)
        {
            if (leaving) return;
            leaving = true;
            GameUI.Instance.HideInteract();
            Profile.Save();
            Vector2 foot = transform.position;
            hero.StartCoroutine(DungeonTravel.WalkStairs(hero, foot + Vector2.down * 0.4f, foot + Vector2.up * (Length - 0.5f), 0f, -Height,
                () => SceneManager.LoadScene("World")));
        }
    }
}
