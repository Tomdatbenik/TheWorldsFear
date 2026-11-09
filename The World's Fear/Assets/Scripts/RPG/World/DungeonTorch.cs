using UnityEngine;

namespace TWF
{
    // A wall torch. Unlike lamps above ground it burns the same day and night.
    public class DungeonTorch : MonoBehaviour
    {
        public Light target;
        float baseIntensity, seed;

        void Start()
        {
            if (target != null) baseIntensity = target.intensity;
            seed = Random.value * 10f;
        }

        void Update()
        {
            if (target == null) return;
            float t = Time.time + seed;
            target.intensity = baseIntensity * (0.88f + Mathf.Sin(t * 9f) * 0.07f + Mathf.Sin(t * 23f) * 0.05f);
        }
    }
}
