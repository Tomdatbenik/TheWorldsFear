using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Heatlh : MonoBehaviour
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
        animator.SetBool("Damage", true);
        animator.SetBool("Damage", false);
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
                CanTakeDamage = true;
            }
        }
    }

    public void OnTriggerStay2D(Collider2D collision)
    {
        TakeDamage(collision);
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        TakeDamage(collision);
    }

    public void TakeDamage(Collider2D collision)
    {
        if (gameObject.tag != "Enemie" && gameObject.tag != "Damage")
        {
            if (collision.gameObject.tag == "Enemie" || collision.gameObject.tag == "Damage") // this string is your newly created tag
            {
                GameObject enemy = collision.gameObject;

                Damage damage = collision.gameObject.GetComponent(typeof(Damage)) as Damage;

                this.ApplyDamage(damage);

                //Rigidbody2D rb = gameObject.GetComponent(typeof(Rigidbody2D)) as Rigidbody2D;

                //Vector2 newPosition = gameObject.transform.position + (gameObject.transform.position - collision.transform.position);

                //Debug.DrawLine(collision.transform.position, newPosition, Color.white, 1000);



                //PlayerMovement movement = gameObject.GetComponent(typeof(PlayerMovement)) as PlayerMovement;

                //Debug.DrawLine(newPosition.normalized, newPosition, Color.red, 1000);
                //movement.Knockback(((Vector2)collision.transform.position + newPosition));
            }
        }
    }

}
