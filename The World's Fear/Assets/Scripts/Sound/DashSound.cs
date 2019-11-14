using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DashSound : MonoBehaviour
{
    public PlayerDash playerDash;
    public AudioSource audioSource;

    private void Update()
    {
        if (playerDash.dash)
        {
            audioSource.Play();
        }
    }
}
