using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Health : MonoBehaviour
{
    public int HealthPoints;
    public Defence defence;
    public Debuff debuff;
    private Animator animator;

    public int DamageCooldown;

    private bool CanTakeDamage = true;

    public void Start()
    {
        animator = gameObject.GetComponent<Animator>();
        if (gameObject.tag == "Player")
        {
            HealthPoints = Random.Range(HealthPoints - 10, HealthPoints + 10);
        }
    }

    public Health(int hp)
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
        if(gameObject.tag != "Enemie" && gameObject.tag != "untagged")
        {
            animator.SetBool("Damage", true);
        }

        if(CanTakeDamage && !isDead())
        {
      
            int dmg = damage.GetStrength() - defence.GetDefence();


            if(dmg > 0)
            {
                HealthPoints -= dmg;
            }

            if (isDead())
            {
                HealthPoints = 0;
            }

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
                cd++;
            }
            else
            {
                cd = 0;
                if (gameObject.tag != "Enemie" && gameObject.tag != "untagged")
                {
                    animator.SetBool("Damage", false);
                }
                CanTakeDamage = true;
            }
        }
    }

    public void OnTriggerStay2D(Collider2D collision)
    {
        TakeDamageAsPLayer(collision);
        TakeDamageAsEnemy(collision);
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        TakeDamageAsPLayer(collision);
        TakeDamageAsEnemy(collision);
    }

    public void TakeDamageAsPLayer(Collider2D collision)
    {
        if (gameObject.tag != "Enemie" && gameObject.tag != "untagged")
        {
            if (collision.gameObject.tag == "Enemie" || collision.gameObject.tag == "Damage") // this string is your newly created tag
            {
                GameObject enemy = collision.gameObject;

                Damage damage = enemy.GetComponent(typeof(Damage)) as Damage;

                this.ApplyDamage(damage);
            }
        }
    }

    public void TakeDamageAsEnemy(Collider2D collision)
    {
        if (gameObject.tag == "Enemie" && gameObject.tag != "untagged")
        {
            if (collision.gameObject.tag == "Sword") // this string is your newly created tag
            {
                GameObject sword = collision.gameObject;

                Damage damage = sword.GetComponent(typeof(Damage)) as Damage;

                this.ApplyDamage(damage);
            }
        }
    }

}
