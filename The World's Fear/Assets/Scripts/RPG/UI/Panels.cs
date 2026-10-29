using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum PanelKind { Skills, Inventory, Map, Shop, Craft }

    // Full-screen menus. Opening one pauses the game and blocks gameplay input.
    public abstract class GamePanel : MonoBehaviour
    {
        public bool IsOpen => gameObject.activeSelf;
        public virtual void Open() => gameObject.SetActive(true);
        public virtual void Close() => gameObject.SetActive(false);
        public virtual void Refresh() { }
    }

    public static class Panels
    {
        static readonly Dictionary<PanelKind, GamePanel> registry = new Dictionary<PanelKind, GamePanel>();

        public static bool AnyOpen
        {
            get
            {
                foreach (var p in registry.Values)
                    if (p != null && p.IsOpen) return true;
                return false;
            }
        }

        public static void Register(PanelKind kind, GamePanel panel)
        {
            registry[kind] = panel;
            panel.Close();
        }

        public static void Toggle(PanelKind kind)
        {
            if (!registry.TryGetValue(kind, out var panel) || panel == null) return;
            bool open = !panel.IsOpen;
            CloseAll();
            if (open)
            {
                panel.Open();
                panel.Refresh();
                SetPaused(true);
            }
        }

        public static void CloseAll()
        {
            foreach (var p in registry.Values)
                if (p != null && p.IsOpen) p.Close();
            SetPaused(false);
        }

        static void SetPaused(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
            MobaInput.Blocked = paused;
            if (GameUI.Instance != null) GameUI.Instance.SetControlsVisible(!paused);
            if (!paused) Profile.Save();
        }
    }
}
