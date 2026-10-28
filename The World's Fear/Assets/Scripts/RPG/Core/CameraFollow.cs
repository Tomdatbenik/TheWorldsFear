using UnityEngine;

namespace TWF
{
    // 2.5D orbit camera: tilted perspective view that follows the hero and can rotate around them.
    // Rotate with two fingers, the on-screen arrows, or Z / C on a keyboard. Pinch or scroll to zoom.
    public class CameraFollow : MonoBehaviour
    {
        static CameraFollow instance;

        public Transform target;
        public float smoothTime = 0.12f;
        [Tooltip("Kept for old scenes; the 2.5D camera uses distance instead.")]
        public float orthographicSize = 7f;
        public float pitch = 52f;          // degrees above the ground
        public float distance = 17f;
        public float minDistance = 10f;
        public float maxDistance = 26f;
        public float fieldOfView = 38f;
        public float rotateSpeed = 120f;   // degrees per second for keys / buttons
        public Vector2 lookAhead = new Vector2(0.8f, 0.6f);

        float yaw, targetYaw;
        Vector3 velocity;
        Vector3 focus;
        bool hasFocus;
        float shake;
        Camera cam;
        float lastTwistAngle;
        float lastPinch;

        public static float RotateInput; // -1..1 from UI buttons

        void Awake()
        {
            instance = this;
            cam = GetComponent<Camera>();
            if (cam)
            {
                cam.orthographic = false;
                cam.fieldOfView = fieldOfView;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 200f;
                cam.transparencySortMode = TransparencySortMode.Perspective;
            }
        }

        public static void Shake(float amount)
        {
            if (instance != null) instance.shake = Mathf.Max(instance.shake, amount);
        }

        public static void RotateBy(float degrees)
        {
            if (instance != null) instance.targetYaw += degrees;
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            focus = target.position;
            hasFocus = true;
            velocity = Vector3.zero;
            Apply(0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!MobaInput.Blocked)
            {
                float keys = (Input.GetKey(KeyCode.C) ? 1f : 0f) - (Input.GetKey(KeyCode.Z) ? 1f : 0f);
                targetYaw += (keys + RotateInput) * rotateSpeed * dt;
                distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y, minDistance, maxDistance);
                HandleTouchGestures();
            }
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-12f * dt));
        }

        // Two fingers that are not on buttons: twist to rotate, pinch to zoom.
        void HandleTouchGestures()
        {
            if (Input.touchCount != 2)
            {
                lastPinch = 0f;
                return;
            }
            var a = Input.GetTouch(0);
            var b = Input.GetTouch(1);
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && (es.IsPointerOverGameObject(a.fingerId) || es.IsPointerOverGameObject(b.fingerId))) return;
            Vector2 d = b.position - a.position;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float pinch = d.magnitude;
            if (lastPinch > 0f)
            {
                targetYaw -= Mathf.DeltaAngle(lastTwistAngle, angle);
                distance = Mathf.Clamp(distance * lastPinch / Mathf.Max(1f, pinch), minDistance, maxDistance);
            }
            lastTwistAngle = angle;
            lastPinch = pinch;
        }

        void LateUpdate()
        {
            if (target == null)
            {
                if (HeroController.Instance != null) target = HeroController.Instance.transform;
                else return;
            }
            if (!hasFocus)
            {
                focus = target.position;
                hasFocus = true;
            }

            Vector2 ahead = Vector2.zero;
            if (HeroController.Instance != null && target == HeroController.Instance.transform)
                ahead = Vector2.Scale(MobaInput.Move, lookAhead);
            Vector3 goal = new Vector3(target.position.x + ahead.x, target.position.y + ahead.y, 0f);
            focus = Vector3.SmoothDamp(focus, goal, ref velocity, smoothTime);
            shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.5f);
            Apply(shake);
        }

        // The ground is the XY plane and "up" points toward -Z.
        void Apply(float shakeAmount)
        {
            var orbit = Quaternion.Euler(0f, 0f, yaw);
            Vector3 back = orbit * new Vector3(0f, -Mathf.Cos(pitch * Mathf.Deg2Rad), -Mathf.Sin(pitch * Mathf.Deg2Rad));
            Vector3 pos = focus + back * distance;
            Vector3 forward = (focus - pos).normalized;
            var rot = Quaternion.LookRotation(forward, Vector3.back);
            transform.SetPositionAndRotation(pos + (Vector3)(Random.insideUnitCircle * shakeAmount), rot);
            View.Set(rot, yaw);
        }
    }

    public static class Sound
    {
        static AudioSource source;

        public static void Play(AudioClip clip, Vector3 at, float volume = 1f)
        {
            if (clip == null) return;
            if (source == null)
            {
                var go = new GameObject("SFX");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
            }
            source.pitch = Random.Range(0.93f, 1.07f);
            source.PlayOneShot(clip, volume);
        }
    }
}
