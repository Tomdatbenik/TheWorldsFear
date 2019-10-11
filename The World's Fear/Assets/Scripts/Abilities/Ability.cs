using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability
{
    private int Cooldowntime;
    private int Counter;
    public bool isCasted = false;
    public bool canCast = true;

    public Ability(int cooldowntime)
    {
        Cooldowntime = cooldowntime;
    }

    public bool isRecharged()
    {
        Counter++;
        if(Counter >= Cooldowntime && !canCast)
        {
            Debug.Log("recharged");
            isCasted = false;
            canCast = true;
            Counter = 0;
            return true;
        }
        return false;
    }

    public void Cast()
    {
        isCasted = true;
        canCast = false;
    }
}
