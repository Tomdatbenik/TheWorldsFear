using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Stands a sprite up and turns it to face the camera (2.5D). The ground stays flat.
    public class Billboard : MonoBehaviour
    {
        static readonly List<Billboard> all = new List<Billboard>();

        void OnEnable()
        {
            all.Add(this);
            transform.rotation = View.Rotation;
        }

        void OnDisable() => all.Remove(this);

        // Called by the camera after it moves; only does work when the view rotated.
        public static void Sync(bool force)
        {
            if (!force) return;
            var r = View.Rotation;
            for (int i = 0; i < all.Count; i++) all[i].transform.rotation = r;
        }

        // A child transform that billboards; place content inside it at local (0, height).
        public static Transform Create(Transform parent, string name = "Billboard")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Billboard>();
            return go.transform;
        }
    }

    // Shared view state for the 2.5D camera.
    public static class View
    {
        static Quaternion rotation = Quaternion.identity;

        public static float Yaw { get; private set; }
        public static Quaternion Rotation => rotation;
        // "Up" on screen, used for text and bars floating above characters.
        public static Vector3 Up => rotation * Vector3.up;
        public static Vector3 Right => rotation * Vector3.right;

        public static void Set(Quaternion camRotation, float yaw)
        {
            bool changed = Quaternion.Angle(camRotation, rotation) > 0.01f;
            rotation = camRotation;
            Yaw = yaw;
            Billboard.Sync(changed);
        }

        // Rotates a screen-space direction (joystick, drag) into the world by the camera yaw.
        public static Vector2 ToWorld(Vector2 screenDir)
        {
            if (screenDir == Vector2.zero) return screenDir;
            return (Vector2)(Quaternion.Euler(0f, 0f, Yaw) * screenDir);
        }

        // Inverse of ToWorld: world direction as seen on screen.
        public static Vector2 ToScreen(Vector2 worldDir) => (Vector2)(Quaternion.Euler(0f, 0f, -Yaw) * worldDir);

        // Projects a screen point onto the ground plane (z = 0).
        public static Vector2? ScreenToGround(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return null;
            float t = -ray.origin.z / ray.direction.z;
            if (t < 0f) return null;
            return (Vector2)(ray.origin + ray.direction * t);
        }
    }
}
