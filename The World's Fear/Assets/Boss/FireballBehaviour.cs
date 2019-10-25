using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireballBehaviour : MonoBehaviour
{
    public GameObject Firebal;
    public int fireballspeed = 10;
    public GameObject Fireballshadow;
    private bool hitground = false;
    private int stayduration = 500;

    private void FixedUpdate()
    {
        if (!hitground)
        {
            Firebal.transform.position = Firebal.transform.position + new Vector3(0f, -1f * fireballspeed * Time.fixedDeltaTime);
        }
        else
        {
            stayduration--;
        }
        if(stayduration == 0)
        {
            Destroy(gameObject);
        }
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.tag == "Fireball")
        {
            hitground = true;
        }
    }

}
