using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    Vector2 direction;
    Vector2 storeddirection;
    float x;
    float y;
    public Animator animator;
    bool attack = false;
    int cooldown = 20;

    // Update is called once per frame
    void Update()
    {
        
        direction.x = Input.GetAxisRaw("Horizontal");
        direction.y = Input.GetAxisRaw("Vertical");
        

        if (Input.GetKey(KeyCode.Mouse0))
        {
            attack = true;
        }
        animator.SetBool("Attack", attack);
        animator.SetFloat("Attack Horizontal", direction.x);
        animator.SetFloat("Attack Vertical", direction.y);
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
