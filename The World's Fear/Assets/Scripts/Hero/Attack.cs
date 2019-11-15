using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    public Animator animator;
    public GameObject sword;
    private Speed speed;

    public Joystick joystick;
    public bool CanAttack;

    private bool CanSlow = true;

    private void Start()
    {
        speed = gameObject.GetComponent(typeof(Speed)) as Speed;
    }

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
    }
}
