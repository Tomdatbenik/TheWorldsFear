using UnityEngine;

namespace TWF
{
    // Tells the shaders where heroes stand, so whatever is between the camera and a hero dithers away (TWF_Reveal.hlsl).
    public class EnvReveal : MonoBehaviour
    {
        const int Max = 4;
        static readonly Vector4[] points = new Vector4[Max];
        static readonly int PointsId = Shader.PropertyToID("_TWF_Reveal");
        static readonly int CountId = Shader.PropertyToID("_TWF_RevealCount");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("Env Reveal");
            DontDestroyOnLoad(go);
            go.AddComponent<EnvReveal>();
        }

        void LateUpdate()
        {
            int n = 0;
            // Every hero in the scene; there is one today, and other players join this list when multiplayer arrives.
            foreach (var hero in FindObjectsByType<HeroController>())
            {
                if (n >= Max) break;
                var visual = hero.transform.Find("Visual");
                var p = (visual != null ? visual : hero.transform).position;
                points[n++] = new Vector4(p.x, p.y, p.z, 1f);
            }
            Shader.SetGlobalVectorArray(PointsId, points);
            Shader.SetGlobalFloat(CountId, n);
        }
    }
}
