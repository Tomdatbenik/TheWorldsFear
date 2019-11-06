﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireballBehaviour : MonoBehaviour
{
    public GameObject Firebal;
    public int fireballspeed = 10;
    public GameObject Fireballshadow;
    private int stayduration = 500;
    public GameObject FireImpact;
    public Animator Fireimpactanim;
    

    private void Start()
    {
        FireImpact.SetActive(false);
    }

    private void FixedUpdate()
    {
        if (Firebal.transform.localPosition.y >= 0.4f)
        {
            Firebal.transform.position = Firebal.transform.position + new Vector3(0f, -1f * fireballspeed * Time.fixedDeltaTime);
        }
        else
        {
            stayduration--;
            Firebal.SetActive(false);
            FireImpact.SetActive(true);
        }
        if (stayduration == 0)
        {
            Destroy(gameObject);
        }
    }

    

}