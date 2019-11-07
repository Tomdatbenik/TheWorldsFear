using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Heatlh : MonoBehaviour
{
    public int HealthPoints;
    public Defence defence;
    public Debuff debuff;

    public int DamageCooldown;

    private bool CanTakeDamage = true;

    public void Start()
    {
        HealthPoints = Random.Range(HealthPoints - 10, HealthPoints + 10);
    }

    public Heatlh(int hp)
    {
        hp = Random.Range(hp - 2, hp + 2);
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
        if(CanTakeDamage)
        {
            int dmg = damage.GetStrength() - defence.GetDefence();

            HealthPoints -= dmg;
            CanTakeDamage = false;
        }
    }

    int cd = 0;

    public void Update()
    {
        if(!CanTakeDamage)
        {
            if(cd < DamageCooldown)
            {
                CanTakeDamage = true;
            }
            else
            {
                cd++;
            }
        }
    }

}
