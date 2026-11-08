using System;
using UnityEngine;

namespace TWF
{
    // Order matches KeyBinds.defaults. Skill1..Skill7 are the hero's skill slots, in order.
    public enum GameAction { MoveUp, MoveDown, MoveLeft, MoveRight, Attack, Potion, Interact, CameraLeft, CameraRight, Skill1, Skill2, Skill3, Skill4, Skill5, Skill6, Skill7 }

    // The player's keyboard layout, kept per install rather than per hero.
    public static class KeyBinds
    {
        public static readonly GameAction[] Actions = (GameAction[])Enum.GetValues(typeof(GameAction));

        static readonly KeyCode[] defaults =
        {
            KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.H, KeyCode.F, KeyCode.Q, KeyCode.E,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7,
        };

        static KeyCode[] keys;
        static bool capturing;
        static int captureEndFrame = -1;

        public static event Action Changed;

        static KeyCode[] Keys
        {
            get
            {
                if (keys == null)
                {
                    keys = new KeyCode[Actions.Length];
                    foreach (var a in Actions) keys[(int)a] = (KeyCode)PlayerPrefs.GetInt(PrefKey(a), (int)defaults[(int)a]);
                    MoveOldCameraKeys();
                }
                return keys;
            }
        }

        // Once per install: the camera moved from Z / C to Q / E and Interact from E to F. A key the player chose themselves stays.
        static void MoveOldCameraKeys()
        {
            const string done = "twf_keys_camera_qe";
            if (PlayerPrefs.GetInt(done, 0) == 1) return;
            void Move(GameAction a, KeyCode from, KeyCode to)
            {
                if (keys[(int)a] != from) return;
                keys[(int)a] = to;
                PlayerPrefs.SetInt(PrefKey(a), (int)to);
            }
            Move(GameAction.Interact, KeyCode.E, KeyCode.F);
            Move(GameAction.CameraLeft, KeyCode.Z, KeyCode.Q);
            Move(GameAction.CameraRight, KeyCode.C, KeyCode.E);
            PlayerPrefs.SetInt(done, 1);
            PlayerPrefs.Save();
        }

        static string PrefKey(GameAction a) => "twf_key_" + a;

        // True while the keybinds menu waits for a key, and for the rest of the frame it got one.
        public static bool Capturing
        {
            get => capturing || captureEndFrame == Time.frameCount;
            set
            {
                if (capturing && !value) captureEndFrame = Time.frameCount;
                capturing = value;
            }
        }

        public static KeyCode Get(GameAction a) => Keys[(int)a];
        public static bool Held(GameAction a) => Input.GetKey(Get(a));
        public static bool Pressed(GameAction a) => Input.GetKeyDown(Get(a));

        // A key can only do one thing: taking one that's in use swaps the two actions.
        public static void Set(GameAction action, KeyCode key)
        {
            var old = Get(action);
            foreach (var a in Actions)
                if (a != action && Get(a) == key) Store(a, old);
            Store(action, key);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void ResetToDefaults()
        {
            foreach (var a in Actions) Store(a, defaults[(int)a]);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static void Store(GameAction a, KeyCode key)
        {
            Keys[(int)a] = key;
            PlayerPrefs.SetInt(PrefKey(a), (int)key);
        }

        public static string Label(GameAction a) => KeyName(Get(a));

        public static string KeyName(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "Num" + (int)(k - KeyCode.Keypad0);
            if (k == KeyCode.Mouse1) return "RMB";
            if (k == KeyCode.Mouse2) return "MMB";
            if (k >= KeyCode.Mouse3 && k <= KeyCode.Mouse6) return "M" + (int)(k - KeyCode.Mouse0 + 1);
            return k.ToString().Replace("Left", "L").Replace("Right", "R").Replace("Arrow", "");
        }

        public static string ActionName(GameAction a)
        {
            switch (a)
            {
                case GameAction.MoveUp: return "Move up";
                case GameAction.MoveDown: return "Move down";
                case GameAction.MoveLeft: return "Move left";
                case GameAction.MoveRight: return "Move right";
                case GameAction.Attack: return "Attack";
                case GameAction.Potion: return "Potion";
                case GameAction.Interact: return "Interact";
                case GameAction.CameraLeft: return "Rotate camera left";
                case GameAction.CameraRight: return "Rotate camera right";
                case GameAction.Skill4: return "Skill 4 (ultimate)";
                case GameAction.Skill5: return "Skill 5 (weapon)";
                case GameAction.Skill6: return "Skill 6 (armor)";
                case GameAction.Skill7: return "Skill 7 (armor)";
                default: return "Skill " + (a - GameAction.Skill1 + 1);
            }
        }
    }
}
