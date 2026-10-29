using UnityEngine;

namespace TWF
{
    // A warm flickering light (furnaces, campfires).
    public class Flicker : MonoBehaviour
    {
        public Light target;
        float baseI, seed;

        void Start()
        {
            if (target != null) baseI = target.intensity;
            seed = Random.value * 10f;
        }

        void Update()
        {
            if (target == null) return;
            float t = Time.time + seed;
            target.intensity = baseI * (0.85f + Mathf.Sin(t * 9f) * 0.08f + Mathf.Sin(t * 23f) * 0.06f);
        }
    }
}
