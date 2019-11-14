using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossDeath : MonoBehaviour
{

    bool PlayedDeathSound = false;
    public AudioSource AudioSource;
    public Health health;
    // Update is called once per frame
    public void Update()
    {
        if(health.isDead())
        {
            if (!PlayedDeathSound)
            {
                PlayedDeathSound = true;
                AudioSource.Play();
            }
        }
    }
}
