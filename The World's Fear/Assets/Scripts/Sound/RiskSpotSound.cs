using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RiskSpotSound : MonoBehaviour
{
    public AudioSource RiskSpotAudioSource;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.gameObject.tag == "Player")
        {
            RiskSpotAudioSource.Play();
        }       
    }
}
