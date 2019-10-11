using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Damage : MonoBehaviour
{

    public int Strength;
    private int InitStrength;

    public Damage()
    {
        InitStrength = Strength;
    }

    public int GetStrength()
    {
        return Strength;
    }

    public void IncreaseStrength(int sp)
    {
        this.Strength += sp;
    }

    public void DecreaseDefence(int sp)
    {
        this.Strength -= sp;
    }

    public void RestoreBackToInit()
    {
        Strength = InitStrength;
    }

}
