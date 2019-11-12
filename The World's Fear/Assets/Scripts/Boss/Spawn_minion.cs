using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spawn_minion : MonoBehaviour
{
    public GameObject Bossarmor;
    private Health health;

    // Start is called before the first frame update
    void Start()
    {
       health = Bossarmor.GetComponent<Health>(); 
    }

    // Update is called once per frame
    void Update()
    {
        int i = health.gethealthpoints();
        if(i <= 500)
        {
            Debug.Log("spawn minion");
        }
    }
}
