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

    private void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (move)
        {
            movement.x = Input.GetAxis("Horizontal");
            movement.y = Input.GetAxis("Vertical");
        }
        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            speed.DecreaseSpeed(speed.GetSpeed()/2);
        }
        if (Input.GetKeyUp(KeyCode.Mouse0))
        {
            move = true;
            speed.ResetSpeed();
        }

        animator.SetFloat("Horizontal", movement.x);
        animator.SetFloat("Vertical", movement.y);
        animator.SetFloat("Speed", movement.sqrMagnitude);
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + movement * speed.GetSpeed() * Time.fixedDeltaTime);
    }

}
