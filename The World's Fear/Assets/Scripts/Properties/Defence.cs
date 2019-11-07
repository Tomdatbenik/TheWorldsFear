using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Defence : MonoBehaviour
{
    public int DefencePoints;
    private int InitPoints;

    public Defence()
    {
        DefencePoints = Random.Range(DefencePoints - 2, DefencePoints + 2);
        InitPoints = DefencePoints;
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
