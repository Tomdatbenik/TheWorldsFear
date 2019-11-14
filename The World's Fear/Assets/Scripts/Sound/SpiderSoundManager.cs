using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpiderSoundManager : MonoBehaviour
{
    public AudioClip DeathSound;
    public AudioSource audiosource;
    public Health health;

    private bool DeathSoundPlayed = false;


    public void Update()
    {
        if(health.isDead())
        {
            if(!DeathSoundPlayed)
            {
                DeathSoundPlayed = true;
                audiosource.PlayOneShot(DeathSound);
            }
        }
    }
}
