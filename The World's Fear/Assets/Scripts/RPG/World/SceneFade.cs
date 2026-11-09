using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TWF
{
    // A black screen that survives scene loads: fade out before changing scene, and it fades back in by itself.
    public class SceneFade : MonoBehaviour
    {
        const float FadeInSeconds = 0.7f;

        static SceneFade instance;

        Image image;
        float alpha, target, speed = 1f;

        public static void ToBlack(float seconds)
        {
            if (instance == null) Create();
            instance.target = 1f;
            instance.speed = 1f / Mathf.Max(0.01f, seconds);
        }

        static void Create()
        {
            var go = new GameObject("Scene Fade");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var imageGo = new GameObject("Black");
            imageGo.transform.SetParent(go.transform, false);
            var image = imageGo.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            instance = go.AddComponent<SceneFade>();
            instance.image = image;
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            alpha = 1f;
            target = 0f;
            speed = 1f / FadeInSeconds;
        }

        void Update()
        {
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * speed);
            image.color = new Color(0f, 0f, 0f, alpha);
        }
    }
}
