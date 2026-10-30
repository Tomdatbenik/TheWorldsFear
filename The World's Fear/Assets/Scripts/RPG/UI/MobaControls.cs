using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TWF
{
    // Unified input: touch controls on mobile, WASD / Space / Q E F R / mouse in the editor.
    public static class MobaInput
    {
        public static VirtualJoystick Joystick;
        public static bool AttackButtonHeld;
        public static bool Blocked; // menus open

        static readonly KeyCode[] skillKeys = { KeyCode.Q, KeyCode.E, KeyCode.F, KeyCode.R, KeyCode.G, KeyCode.T, KeyCode.Y };

        public static Vector2 Move
        {
            get
            {
                if (Blocked) return Vector2.zero;
                // Input is relative to the camera: "up" walks away from the viewer.
                if (Joystick != null && Joystick.Value.sqrMagnitude > 0.0025f) return View.ToWorld(Joystick.Value);
                Vector2 k = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                return View.ToWorld(k.sqrMagnitude > 1f ? k.normalized : k);
            }
        }

        public static bool AttackHeld
        {
            get
            {
                if (Blocked) return false;
                if (AttackButtonHeld || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.J)) return true;
                return !Application.isMobilePlatform && Input.GetMouseButton(0) &&
                       (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
            }
        }

        public static bool KeyboardSkillPressed(int slot) => !Blocked && Input.GetKeyDown(skillKeys[slot]);

        public static string KeyName(int slot) => skillKeys[slot].ToString();

        // Mouse aim as an offset in "range units" (magnitude 0..1), desktop only.
        public static Vector2? MouseAim(Vector2 heroPos, float range)
        {
            if (Application.isMobilePlatform) return null;
            var ground = View.ScreenToGround(Input.mousePosition);
            if (!ground.HasValue) return null;
            return Vector2.ClampMagnitude((ground.Value - heroPos) / Mathf.Max(0.1f, range), 1f);
        }
    }

    // Floating joystick: appears where you touch inside its zone.
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform baseRect;
        public RectTransform handle;
        public float radius = 110f;

        public Vector2 Value { get; private set; }

        Vector2 homePos;
        Canvas canvas;
        RectTransform zone;

        void Start()
        {
            zone = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            homePos = baseRect.anchoredPosition;
            MobaInput.Joystick = this;
        }

        void OnDisable() => Release();

        Vector2 ToLocal(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)baseRect.parent, e.position, e.pressEventCamera, out var local);
            return local;
        }

        public void OnPointerDown(PointerEventData e)
        {
            baseRect.localPosition = ToLocal(e);
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            Vector2 delta = ToLocal(e) - (Vector2)baseRect.localPosition;
            Vector2 clamped = Vector2.ClampMagnitude(delta, radius);
            handle.anchoredPosition = clamped;
            Value = clamped / radius;
            if (Value.magnitude < 0.12f) Value = Vector2.zero;
        }

        public void OnPointerUp(PointerEventData e) => Release();

        void Release()
        {
            Value = Vector2.zero;
            if (handle) handle.anchoredPosition = Vector2.zero;
            if (baseRect) baseRect.anchoredPosition = homePos;
        }
    }

    // Turns the camera by 45 degrees per press.
    public class RotateButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public float direction = 1f;
        float downTime;

        // Each press turns the camera exactly one 45 degree step.
        public void OnPointerDown(PointerEventData e)
        {
            downTime = Time.unscaledTime;
            CameraFollow.RotateBy(45f * direction);
            transform.localScale = Vector3.one * 0.92f;
        }

        public void OnPointerUp(PointerEventData e)
        {
            transform.localScale = Vector3.one;
        }

        void OnDisable() => CameraFollow.RotateInput = 0f;
    }

    public class AttackButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public void OnPointerDown(PointerEventData e)
        {
            MobaInput.AttackButtonHeld = true;
            transform.localScale = Vector3.one * 0.93f;
        }

        public void OnPointerUp(PointerEventData e)
        {
            MobaInput.AttackButtonHeld = false;
            transform.localScale = Vector3.one;
        }

        void OnDisable() => MobaInput.AttackButtonHeld = false;
    }

    // Tap = quick cast at the nearest enemy. Drag = aim with an indicator; release over Cancel to abort.
    public class SkillButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public int slot;
        public float dragRadius = 160f;
        public Image cooldownOverlay;
        public TextMeshProUGUI cooldownText;
        public TextMeshProUGUI glyph;
        public Image icon;
        public Image ring;
        public Image background;
        public TextMeshProUGUI rankText;
        public RectTransform cancelZone;

        bool pressed, aiming;
        Vector2 aim;
        SkillDefinition shownSkill;
        float readyFlash;

        public static bool AnyAiming { get; private set; }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            var skill = hero.SkillIn(slot);
            // Socket buttons only show while a unique armor piece fills them.
            if (slot >= HeroController.FirstSocketSlot)
            {
                var group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                group.alpha = skill != null ? 1f : 0f;
                group.blocksRaycasts = skill != null;
            }
            if (skill != shownSkill)
            {
                shownSkill = skill;
                string key = skill == null ? null : !string.IsNullOrEmpty(skill.iconKey) ? skill.iconKey : IconFactory.KeyFor(skill.id, skill.displayName);
                var sprite = skill != null ? IconFactory.Get(key, Color.Lerp(skill.color, Color.white, 0.15f)) : null;
                if (icon != null)
                {
                    icon.sprite = sprite;
                    icon.enabled = sprite != null;
                }
                glyph.text = skill != null && sprite == null ? skill.glyph : "";
                ring.color = skill != null ? skill.color : new Color(1, 1, 1, 0.15f);
                background.color = skill != null ? new Color(skill.color.r * 0.25f, skill.color.g * 0.25f, skill.color.b * 0.25f, 0.9f) : new Color(0, 0, 0, 0.35f);
            }
            rankText.text = skill != null ? ToRoman(hero.RankIn(slot)) : "";

            float left = hero.CooldownLeft(slot);
            bool cooling = skill != null && left > 0f;
            cooldownOverlay.fillAmount = cooling ? hero.CooldownFraction(slot) : 0f;
            cooldownText.text = cooling ? (left >= 1f ? Mathf.CeilToInt(left).ToString() : left.ToString("0.0")) : "";
            if (cooling) readyFlash = 1f;
            else if (readyFlash > 0f)
            {
                readyFlash -= Time.deltaTime * 3f;
                transform.localScale = Vector3.one * (1f + Mathf.Max(0f, readyFlash) * 0.12f);
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (HeroController.Instance == null || HeroController.Instance.SkillIn(slot) == null) return;
            pressed = true;
            aiming = false;
            aim = Vector2.zero;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!pressed) return;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(e.pressEventCamera, transform.position);
            float scale = GetComponentInParent<Canvas>().scaleFactor;
            Vector2 delta = (e.position - center) / scale;
            if (!aiming && delta.magnitude > 25f)
            {
                aiming = true;
                AnyAiming = true;
                if (cancelZone) cancelZone.gameObject.SetActive(true);
            }
            if (aiming)
            {
                aim = View.ToWorld(Vector2.ClampMagnitude(delta / dragRadius, 1f));
                bool overCancel = cancelZone && RectTransformUtility.RectangleContainsScreenPoint(cancelZone, e.position, e.pressEventCamera);
                AimIndicator.Show(HeroController.Instance, HeroController.Instance.SkillIn(slot), aim, overCancel);
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!pressed) return;
            pressed = false;
            bool cancelled = aiming && cancelZone && RectTransformUtility.RectangleContainsScreenPoint(cancelZone, e.position, e.pressEventCamera);
            if (aiming)
            {
                AimIndicator.Hide();
                AnyAiming = false;
                if (cancelZone) cancelZone.gameObject.SetActive(false);
            }
            if (!cancelled && HeroController.Instance != null)
                HeroController.Instance.TryCast(slot, aim, aiming);
            aiming = false;
        }

        static string ToRoman(int n)
        {
            switch (n)
            {
                case 1: return "I";
                case 2: return "II";
                case 3: return "III";
                case 4: return "IV";
                case 5: return "V";
                default: return n > 0 ? n.ToString() : "";
            }
        }
    }

    // World-space indicator shown while dragging a skill button.
    public class AimIndicator : MonoBehaviour
    {
        static AimIndicator instance;
        SpriteRenderer rangeRing, line, circle;

        static AimIndicator Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("AimIndicator");
            instance = go.AddComponent<AimIndicator>();
            instance.rangeRing = GameAssets.CreateWorldSprite("range", GameAssets.Ring, go.transform, new Color(1, 1, 1, 0.35f), SortOrder.Telegraph + 2);
            instance.line = GameAssets.CreateWorldSprite("line", GameAssets.Pixel, go.transform, new Color(1, 1, 1, 0.3f), SortOrder.Telegraph + 2);
            instance.circle = GameAssets.CreateWorldSprite("circle", GameAssets.SoftCircle, go.transform, new Color(1, 1, 1, 0.35f), SortOrder.Telegraph + 3);
            return instance;
        }

        public static void Show(HeroController hero, SkillDefinition skill, Vector2 aim, bool cancelling)
        {
            if (skill == null) return;
            var ind = Get();
            ind.gameObject.SetActive(true);
            Vector2 pos = hero.transform.position;
            ind.transform.position = pos;
            Color c = cancelling ? new Color(1f, 0.3f, 0.3f) : skill.color;

            float skillRange = hero.SkillRange(skill);
            float rangeSize = skillRange * 2f;
            ind.rangeRing.transform.localScale = Vector3.one * rangeSize / GameAssets.Ring.bounds.size.x;
            ind.rangeRing.color = new Color(c.r, c.g, c.b, 0.35f);
            ind.rangeRing.gameObject.SetActive(skill.targeting != Targeting.Self);

            bool showLine = skill.targeting == Targeting.Skillshot || skill.targeting == Targeting.Dash;
            ind.line.gameObject.SetActive(showLine);
            Vector2 dir = aim.sqrMagnitude > 0.0001f ? aim.normalized : hero.Facing;
            if (showLine)
            {
                float unit = GameAssets.Pixel.bounds.size.x;
                ind.line.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                ind.line.transform.localScale = new Vector3(skillRange / unit, 0.7f / unit, 1f);
                ind.line.transform.localPosition = dir * skillRange / 2f;
                ind.line.color = new Color(c.r, c.g, c.b, 0.35f);
            }

            bool showCircle = skill.targeting == Targeting.GroundAoE || skill.targeting == Targeting.Self;
            ind.circle.gameObject.SetActive(showCircle);
            if (showCircle)
            {
                Vector2 at = skill.targeting == Targeting.Self ? Vector2.zero : dir * skillRange * Mathf.Clamp01(aim.magnitude);
                ind.circle.transform.localPosition = at;
                float area = 1f + hero.Combatant.Stats.Get(StatType.AreaSize);
                ind.circle.transform.localScale = Vector3.one * skill.indicatorRadius * area * 2f / GameAssets.SoftCircle.bounds.size.x * 1.25f;
                ind.circle.color = new Color(c.r, c.g, c.b, 0.45f);
            }
        }

        public static void Hide()
        {
            if (instance != null) instance.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (HeroController.Instance != null) transform.position = HeroController.Instance.transform.position;
        }
    }
}
