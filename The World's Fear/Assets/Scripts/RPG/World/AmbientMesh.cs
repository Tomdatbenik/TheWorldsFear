using UnityEngine;

namespace TWF
{
    // Frees a chunk's ambient-life mesh when the chunk unloads.
    public class AmbientMesh : MonoBehaviour
    {
        public Mesh mesh;

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
