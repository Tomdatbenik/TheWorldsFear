using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    //Vector2 direction;
    //float x;
    //float y;
    public Animator animator;
    //bool attack = false;
    public GameObject sword;
    private Speed speed;

    //private int timer;
    //public int AttackDuration;

    public Joystick joystick;
    public bool CanAttack;

    private bool CanSlow = true;

    private void Start()
    {
        speed = gameObject.GetComponent(typeof(Speed)) as Speed;

        //sword.SetActive(false);
    }
    // Update is called once per frame
    void Update()
    {

        if(joystick.Direction != Vector2.zero)
        {
            if(CanSlow)
            {
                speed.SetSpeed(speed.GetSpeed() / 2);
                CanSlow = false;
            }
   
            animator.SetBool("Attack", true);
            sword.SetActive(true);
        }
        else
        {
            if(!CanSlow)
            {
                speed.ResetSpeed();
            }

            CanSlow = true;

            animator.SetBool("Attack", false);
            sword.SetActive(false);
        }


        ////get the direction in which the player is moving
        //direction.x = animator.GetFloat("Horizontal");
        //direction.y = animator.GetFloat("Vertical");

        ////detect if the player is attacking or not
        //if (Input.GetKeyDown(KeyCode.Mouse0))
        //{
        //    attack = true;
        //    timer = 0;
        //}

        //if (attack == true)
        //{
        //    Debug.Log("Attacking");
        //    //sword.SetActive(true);
        //    //timer++;
        //}

        //if(timer > AttackDuration)
        //{
        //    attack = false;
        //    //sword.SetActive(false);
        //}

        //SetAnimatorValues();


    }

    //public void CastAttack()
    //{
    //    attack = true;
    //    timer = 0;
    //}

    //private void SetAnimatorValues()
    //{
    //    //dectect if the player has last moved into one of the four animation directions
    //    if (direction.x != 0 && direction.y == 0)
    //    {
    //        x = direction.x;
    //        y = direction.y;
    //    }
    //    if (direction.y != 0 && direction.x == 0)
    //    {
    //        x = direction.x;
    //        y = direction.y;
    //    }
    //    //animator.SetBool("Attack", attack);
    //    animator.SetFloat("Attack Horizontal", direction.x);
    //    animator.SetFloat("Attack Vertical", direction.y);
    //    //detect if the player is moving at all
    //    if (direction.x == 0 && direction.y == 0)
    //    {
    //        animator.SetFloat("Attack Horizontal", x);
    //        animator.SetFloat("Attack Vertical", y);
    //    }
    //}
}
