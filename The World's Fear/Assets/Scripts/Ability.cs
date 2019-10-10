using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability : MonoBehaviour
{
    private int Cooldowntime;
    private int Counter;

    public Ability(int cooldowntime)
    {
        Cooldowntime = cooldowntime;
    }

    public bool Cooldown()
    {
        Counter++;
        if(Counter == Cooldowntime)
        {
            return true;
        }
        return false;
    }
}
