﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Damage : MonoBehaviour
{

    public int Strength;
    private int InitStrength;
    Animator animator;

    public bool isBuffed = false;

    public void Start()
    {
        animator = gameObject.GetComponent<Animator>();
        if (gameObject.tag == "Player" || gameObject.tag == "Sword")
        {
            Strength = Random.Range(Strength - 2, Strength + 2);
            InitStrength = Strength;
        }
    }

    public int GetStrength()
    {
        return Strength;
    }

    public void IncreaseStrength(int sp)
    {
        if(!isBuffed)
        {
            isBuffed = true;
            this.Strength += sp;
        }
    }

    public void DecreaseDefence(int sp)
    {
        this.Strength -= sp;
    }

    public void RestoreBackToInit()
    {
        Strength = InitStrength;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.tag == "RiskSpot")
        {
            animator.SetBool("RiskSpot", true);
        }
    }
}
