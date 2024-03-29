﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Defence : MonoBehaviour
{
    public int DefencePoints;
    private int InitPoints;

    public void Start()
    {
        if (gameObject.tag == "Player")
        {
            DefencePoints = Random.Range(DefencePoints - 5, DefencePoints + 5);
            InitPoints = DefencePoints;
        }
    }

    public Defence(int DP)
    {
        this.DefencePoints = DP;
        InitPoints = DP;
    }

    public int GetDefence()
    {
        return this.DefencePoints;
    }

    public void IncreaseDefence(int dp)
    {
        this.DefencePoints += dp;
    }

    public void DecreaseDefence(int dp)
    {
        this.DefencePoints -= dp;
    }

    public void RestoreBackToInit()
    {
        DefencePoints = InitPoints;
    }
}
