using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Damage : MonoBehaviour
{

    public int Strength;
    private int InitStrength;


    public void Start()
    {
        if (gameObject.tag == "Player")
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
