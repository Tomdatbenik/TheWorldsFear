using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    Vector2 direction;
    float x;
    float y;
    public Animator animator;
    bool attack = false;

    // Update is called once per frame
    void Update()
    {
        //get the direction in which the player is moving
        direction.x = Input.GetAxisRaw("Horizontal");
        direction.y = Input.GetAxisRaw("Vertical");

        //detect if the player is attacking or not
        if (Input.GetKey(KeyCode.Mouse0))
        {
            attack = true;
        }
        if (Input.GetKeyUp(KeyCode.Mouse0))
        {
            attack = false;
        }
        
        //dectect if the player has last moved into one of the four animation directions
        if (direction.x != 0 && direction.y == 0)
        {
            x = direction.x;
            y = direction.y;
        }
        if(direction.y != 0 && direction.x == 0)
        {
            x = direction.x;
            y = direction.y;
        }
        animator.SetBool("Attack", attack);
        animator.SetFloat("Attack Horizontal", direction.x);
        animator.SetFloat("Attack Vertical", direction.y);
        //dectect if the player is moving at all
        if (direction.x == 0 && direction.y == 0)
        {
            animator.SetFloat("Attack Horizontal", x);
            animator.SetFloat("Attack Vertical", y);
        }
        
    }
}
