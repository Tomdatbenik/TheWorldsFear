using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{

    public Speed speed;
    public Rigidbody2D rb;
    public Animator animator;
    private bool move = true;

    private Vector2 movement;

    public Joystick joystick;

    private bool knockback = false;

    public Health health;

    private void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (move)
        {
           
            if(!knockback)
            {
                movement.x = joystick.Direction.x;
                movement.y = joystick.Direction.y;
                //movement.x = Input.GetAxis("Horizontal");
                //movement.y = Input.GetAxis("Vertical");
            }
        }
  
        //if (Input.GetKeyDown(KeyCode.Mouse0))
        //{
        //    speed.DecreaseSpeed(speed.GetSpeed()/2);
        //}
        //if (Input.GetKeyUp(KeyCode.Mouse0))
        //{
        //    move = true;
        //    speed.ResetSpeed();
        //}

        animator.SetFloat("Horizontal", movement.x);
        animator.SetFloat("Vertical", movement.y);
        animator.SetFloat("Speed", movement.sqrMagnitude);
    }

    int counter = 0;

    private void FixedUpdate()
    {
        if(knockback)
        {
            if(counter < 2)
            {
                counter++;
                speed.SetSpeed(speed.GetSpeed() * 2);
                Debug.Log("Should move to: " + movement);
                rb.MovePosition(movement * speed.GetSpeed() * Time.fixedDeltaTime);
                Debug.Log("Moves to: " + rb.transform.position);

                Debug.DrawLine(movement, rb.transform.position, Color.green, 1);
                speed.ResetSpeed();
            }
            else
            {
                knockback = false;
                counter = 0;
            }
        }
        else
        {
            rb.MovePosition(rb.position + movement * speed.GetSpeed() * Time.fixedDeltaTime);
        }
          
    }

    public void Knockback(Vector2 position)
    {
        knockback = true;
        movement.x = position.x;
        movement.y = position.y;
    }

}
