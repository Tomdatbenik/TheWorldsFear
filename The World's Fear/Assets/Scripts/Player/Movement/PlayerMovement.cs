using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{

    public float Movementspeed = 5f;
    private float ms;
    public Rigidbody2D rb;
    public Animator animator;
    private bool move = true;

    private Vector2 movement;

    private void Start()
    {
        ms = Movementspeed;
    }

    // Update is called once per frame
    void Update()
    {
        if (move)
        {
            Movementspeed = ms;
            movement.x = Input.GetAxis("Horizontal");
            movement.y = Input.GetAxis("Vertical");
        }
        if (Input.GetKeyDown(KeyCode.Mouse0))
        {

            if (movement.x == 0 && movement.y == 0)
            {
                move = false;
            }
            else
            {
                Movementspeed = ms/2;
            }
        }
        if (Input.GetKeyUp(KeyCode.Mouse0))
        {
            move = true;
        }

        animator.SetFloat("Horizontal", movement.x);
        animator.SetFloat("Vertical", movement.y);
        animator.SetFloat("Speed", movement.sqrMagnitude);
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + movement * Movementspeed * Time.fixedDeltaTime);
    }

}
