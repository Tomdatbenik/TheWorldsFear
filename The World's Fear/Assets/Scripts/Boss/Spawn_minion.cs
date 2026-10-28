using UnityEngine;

public class Spawn_minion : MonoBehaviour
{
    public GameObject Spider;
    public Transform target;

    // The boss now summons generated monsters instead of the old spider.
    public void SpawnSpider()
    {
        TWF.BossHelpers.SpawnMinions((Vector2)transform.position + Vector2.down * 8f, 2);
    }
}
