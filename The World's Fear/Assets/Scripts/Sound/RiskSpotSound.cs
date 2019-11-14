using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RiskSpotSound : MonoBehaviour
{
    public AudioSource audioSource;
    public RiskSpotAttack riskSpotAttack;

    private void Update()
    {
        if (riskSpotAttack.riskspot)
        {
            audioSource.Play();
            Debug.Log("yeet");
            riskSpotAttack.riskspot = false;
        }
    }
}
