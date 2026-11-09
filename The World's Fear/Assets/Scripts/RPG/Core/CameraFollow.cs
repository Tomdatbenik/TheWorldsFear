using UnityEngine;

namespace TWF
{
    // 2.5D orbit camera: tilted perspective view that follows the hero and can rotate around them.
    // Rotate with two fingers, the on-screen arrows, or Q / E on a keyboard. Pinch or scroll to zoom.
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
        [System.NonSerialized] public float focusHeight;   // lift the focus off the ground (cutscene close-ups)

        float yaw, targetYaw;
        Vector3 velocity;
        Vector3 focus;
        bool hasFocus;
        float shake;
        Camera cam;
        float lastTwistAngle;
        float lastPinch;

        public static float RotateInput; // -1..1 from UI buttons

        // What the old perspective view showed from top to bottom per unit of distance (fov 38), kept so zoom feels the same.
        const float ZoomScale = 0.344f;
        const float ViewStep = 22.5f;
        const float ArtPPU = 72f;
        const float MinViewSize = 7.5f;   // never closer than 15 units top to bottom

        // Keeps the camera on whole art pixels so the world never shimmers as it scrolls.
        Vector3 SnapToPixels(Vector3 pos, Quaternion rot)
        {
            int k = Mathf.Max(1, Mathf.RoundToInt(Screen.height / (2f * cam.orthographicSize * ArtPPU)));
            float step = 1f / (ArtPPU * k);
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
            float r = Vector3.Dot(pos, right), u = Vector3.Dot(pos, up);
            return pos + right * (Mathf.Round(r / step) * step - r) + up * (Mathf.Round(u / step) * step - u);
        }

        void Awake()
        {
            instance = this;
            cam = GetComponent<Camera>();
            if (cam)
            {
                // Orthographic: the world art is rendered flat, so a perspective ground would slide against it.
                cam.orthographic = true;
                cam.orthographicSize = distance * ZoomScale;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 200f;
                cam.transparencySortMode = TransparencySortMode.Orthographic;
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
                if (KeyBinds.Pressed(GameAction.CameraRight)) targetYaw = Snap(targetYaw) + 45f;
                if (KeyBinds.Pressed(GameAction.CameraLeft)) targetYaw = Snap(targetYaw) - 45f;
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
            Vector3 goal = new Vector3(target.position.x + ahead.x, target.position.y + ahead.y, -focusHeight);
            focus = Vector3.SmoothDamp(focus, goal, ref velocity, smoothTime);
            shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.5f);
            Apply(shake);
        }

        // The ground is the XY plane and "up" points toward -Z.
        void Apply(float shakeAmount)
        {
            // One grid for the whole picture: the view size is always a whole number of screen pixels per art pixel.
            if (cam)
            {
                // Whole screen pixels per art pixel, leaning zoomed-out; a small window still shows at least MinViewSize.
                int k = Mathf.Max(1, Mathf.FloorToInt(Screen.height / (2f * distance * ZoomScale * ArtPPU)));
                cam.orthographicSize = Mathf.Max(Screen.height / (2f * ArtPPU * k), MinViewSize);
            }
            // The world art has a baked view every 22.5 degrees; the camera stops on those so ground and buildings always match.
            float shownYaw = Mathf.Round(yaw / ViewStep) * ViewStep;
            var orbit = Quaternion.Euler(0f, 0f, shownYaw);
            Vector3 back = orbit * new Vector3(0f, -Mathf.Cos(pitch * Mathf.Deg2Rad), -Mathf.Sin(pitch * Mathf.Deg2Rad));
            Vector3 pos = focus + back * distance;
            Vector3 forward = (focus - pos).normalized;
            var rot = Quaternion.LookRotation(forward, Vector3.back);
            pos += (Vector3)(Random.insideUnitCircle * shakeAmount);
            if (cam && cam.orthographic) pos = SnapToPixels(pos, rot);
            transform.SetPositionAndRotation(pos, rot);
            View.Set(rot, shownYaw);
        }
    }

    public static class Sound
    {
        static AudioSource source;
        static readonly System.Collections.Generic.Dictionary<AudioClip, float> lastPlayed = new System.Collections.Generic.Dictionary<AudioClip, float>();
        const float MinGap = 0.08f;   // the same sound can't restart faster than this

        public static void Play(AudioClip clip, Vector3 at, float volume = 1f)
        {
            if (clip == null) return;
            // Many hits in the same moment (area attacks, packs) play the sound once, not dozens of times.
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(clip, out float last) && now - last < MinGap) return;
            lastPlayed[clip] = now;
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
