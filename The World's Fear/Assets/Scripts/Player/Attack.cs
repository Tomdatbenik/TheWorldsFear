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
    int cooldown = 20;

    // Update is called once per frame
    void Update()
    {
        if (attack == false)
        {
            direction.x = Input.GetAxisRaw("Horizontal");
            direction.y = Input.GetAxisRaw("Vertical");
        }
        x = direction.x;
        y = direction.y;
        if (Input.GetKey(KeyCode.Mouse0))
        {
            attack = true;
        }
        animator.SetBool("Attack", attack);
        animator.SetFloat("Attack Horizontal", x);
        animator.SetFloat("Attack Vertical", y);
        Debug.Log(new Vector2(x,y));
    }

    private void FixedUpdate()
    {
        if(attack == true)
        {
            cooldown--;
            if (cooldown == 0)
            {
                attack = false;
                cooldown = 20;
            }
        }
    }
    
}
