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
                cam.clearFlags = CameraClearFlags.SolidColor;
                var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                data.renderPostProcessing = true;
                data.renderShadows = true;
                data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing;
            }
        }

        public static void Shake(float amount)
        {
            if (instance != null) instance.shake = Mathf.Max(instance.shake, amount);
        }

        public static void RotateBy(float degrees)
        {
            if (instance != null) instance.targetYaw = Snap(instance.targetYaw) + degrees;
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            focus = target.position;
            hasFocus = true;
            velocity = Vector3.zero;
            yaw = targetYaw;
            gestureActive = false;
            Apply(0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (MobaInput.Blocked) EndGesture();
            if (!MobaInput.Blocked)
            {
                // Rotation always lands on 45 degree steps so the world stays aligned.
                if (Input.GetKeyDown(KeyCode.C)) targetYaw = Snap(targetYaw) + 45f;
                if (Input.GetKeyDown(KeyCode.Z)) targetYaw = Snap(targetYaw) - 45f;
                distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y, minDistance, maxDistance);
                HandleTouchGestures();
            }
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-12f * dt));
        }

        // Two fingers that both started on the world (not on buttons): twist to rotate, pinch to zoom.
        // Tracking resets whenever the gesture breaks, so stale finger positions can't make the camera jump.
        bool gestureActive;
        int fingerA = -1, fingerB = -1;

        void HandleTouchGestures()
        {
            if (Input.touchCount != 2)
            {
                EndGesture();
                return;
            }
            var a = Input.GetTouch(0);
            var b = Input.GetTouch(1);
            var es = UnityEngine.EventSystems.EventSystem.current;
            bool overUI = es != null && (es.IsPointerOverGameObject(a.fingerId) || es.IsPointerOverGameObject(b.fingerId));
            if (overUI || a.phase == TouchPhase.Ended || b.phase == TouchPhase.Ended || a.phase == TouchPhase.Canceled || b.phase == TouchPhase.Canceled)
            {
                EndGesture();
                return;
            }

            Vector2 d = b.position - a.position;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float pinch = Mathf.Max(1f, d.magnitude);
            if (!gestureActive || a.fingerId != fingerA || b.fingerId != fingerB)
            {
                // Start fresh: remember where the fingers are, don't move yet.
                gestureActive = true;
                fingerA = a.fingerId;
                fingerB = b.fingerId;
                lastTwistAngle = angle;
                lastPinch = pinch;
                return;
            }
            float twist = Mathf.Clamp(Mathf.DeltaAngle(lastTwistAngle, angle), -20f, 20f);
            targetYaw -= twist;
            distance = Mathf.Clamp(distance * Mathf.Clamp(lastPinch / pinch, 0.9f, 1.1f), minDistance, maxDistance);
            lastTwistAngle = angle;
            lastPinch = pinch;
        }

        static float Snap(float y) => Mathf.Round(y / 45f) * 45f;

        // When the fingers lift, settle on the nearest 45 degree step.
        void EndGesture()
        {
            if (gestureActive) targetYaw = Snap(targetYaw);
            gestureActive = false;
        }

        // Snap the view to a 45 degree step (used by the reset button and after teleports).
        public static void ResetRotation()
        {
            if (instance == null) return;
            instance.targetYaw = Mathf.Round(instance.targetYaw / 45f) * 45f;
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
