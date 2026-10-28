using UnityEngine;

namespace TWF
{
    // A rectangle of the world map belonging to one zone.
    public class ZoneArea : MonoBehaviour
    {
        public ZoneDefinition zone;
        public Rect bounds;

        public bool Contains(Vector2 p) => bounds.Contains(p);

        void OnDrawGizmos()
        {
            Gizmos.color = zone != null ? zone.nameColor : Color.white;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
