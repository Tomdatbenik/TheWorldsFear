using System.Collections;
using UnityEngine;

namespace TWF.Tests
{
    // The camera: it widens and leans toward a boss, and follows the hero's height at its own slower pace.
    public static class CameraChecks
    {
        [TwfCheck("Camera/A boss fight widens the view and leans toward the boss, then gives it back")]
        static IEnumerator BossView()
        {
            yield return Play.Scene("TestRange", 2f);
            var hero = HeroController.Instance;
            var follow = Camera.main.GetComponent<CameraFollow>();
            var cam = Camera.main;
            var keep = CameraFollow.Boss;
            var boss = new GameObject("CameraCheckBoss");
            try
            {
                CameraFollow.Boss = null;
                yield return Play.Wait(3f);
                float size = cam.orthographicSize;
                Vector2 rest = follow.Focus.G() - hero.transform.G();
                Check.Near(follow.Zoom, 1f, 0.001f, "zoom with no boss about");

                // A boss far off: the widest view, the focus part of the way to it.
                boss.transform.position = hero.transform.position + new Vector3(CameraFollow.BossZoomReach + 2f, 0f, 0f);
                CameraFollow.Boss = boss.transform;
                yield return null;
                yield return null;
                Check.That(follow.Zoom < 1.1f, $"The view jumped to zoom {follow.Zoom:0.##} in two frames instead of easing out");
                yield return Play.Wait(5f);
                Check.Near(follow.Zoom, CameraFollow.BossZoomFar, 0.01f, "zoom with the boss far off");
                float wide = Mathf.Max(size, Mathf.Min(size * CameraFollow.BossZoomFar, CameraFollow.BossViewMax));
                Check.Near(cam.orthographicSize, wide, 0.01f, "view size with the boss far off");
                Check.Near((follow.Focus.G() - hero.transform.G() - rest).x, CameraFollow.BossLeanMax, 0.15f, "how far the view leans toward the boss");

                // The boss up close: less wide.
                boss.transform.position = hero.transform.position;
                yield return Play.Wait(5f);
                Check.Near(follow.Zoom, CameraFollow.BossZoomNear, 0.01f, "zoom with the boss on top of the hero");

                // The boss gone: the normal view again, exactly (back on the pixel grid).
                CameraFollow.Boss = null;
                yield return Play.Wait(6f);
                Check.Near(follow.Zoom, 1f, 0.0001f, "zoom after the fight");
                Check.Near(cam.orthographicSize, size, 0.0001f, "view size after the fight");
            }
            finally
            {
                Object.Destroy(boss);
                CameraFollow.Boss = keep;
            }
        }

        [TwfCheck("Camera/The view follows the hero's height slowly, and all the way")]
        static IEnumerator HeightFollow()
        {
            yield return Play.Scene("TestRange", 2f);
            var hero = HeroController.Instance;
            var follow = Camera.main.GetComponent<CameraFollow>();
            // The hero's own height keeper would put the height back; the check drives it by hand.
            var ground = hero.GetComponent<GroundFollow>();
            bool was = ground != null && ground.enabled;
            float keep = follow.groundHeight;
            try
            {
                if (ground != null) ground.enabled = false;
                yield return Play.Wait(2.5f);
                float from = follow.Focus.y;
                Check.Near(from, follow.groundHeight, 0.02f, "height of the view at rest");
                // A step down of three units, below zero as well.
                follow.groundHeight = from - 3f;
                yield return Play.Wait(0.15f);
                float fallen = from - follow.Focus.y;
                Check.That(fallen > 0.02f, "The view did not start down with the hero");
                Check.That(fallen < 1.2f, $"The view dropped {fallen:0.##} of 3 units in 0.15 s: it snaps instead of following slowly");
                yield return Play.Wait(3.5f);
                Check.Near(follow.Focus.y, from - 3f, 0.03f, "height of the view after the step down");
            }
            finally
            {
                follow.groundHeight = keep;
                if (ground != null) ground.enabled = was;
            }
        }
    }
}
