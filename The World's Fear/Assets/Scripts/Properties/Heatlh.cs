using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Heatlh : MonoBehaviour
{
    public int HealthPoints;
    public Defence defence;
    public Debuff debuff;

    public Heatlh(int hp)
    {
        this.HealthPoints = hp;
    }

    public bool isDead()
    {
        if(HealthPoints <= 0)
        {
            return true;
        }

        return false;
    }

    public void Heal(int hp)
    {
        this.HealthPoints += hp;
    }

    public void ApplyDamage(Damage damage)
    {
        int dmg = damage.GetStrength() - defence.GetDefence();

        HealthPoints -= dmg;
    }

}
