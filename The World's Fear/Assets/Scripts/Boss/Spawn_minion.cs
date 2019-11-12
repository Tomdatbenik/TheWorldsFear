using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spawn_minion : MonoBehaviour
{
    public GameObject Spider;
    public Transform target;

    public void SpawnSpider()
    {

        GameObject spider = Spider;

        Instantiate(spider);

        spider.transform.position = Vector2.zero;
        spider.SetActive(true);
    }
}
