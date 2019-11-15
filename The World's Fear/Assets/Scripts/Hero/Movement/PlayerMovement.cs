using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{

    public Speed speed;
    public Rigidbody2D rb;
    public Animator animator;

    private Vector2 movement;

    public Joystick joystick;

    public Health health;

    void Update()
    {
        movement.x = joystick.Direction.x;
        movement.y = joystick.Direction.y;

        animator.SetFloat("Horizontal", movement.x);
        animator.SetFloat("Vertical", movement.y);
        animator.SetFloat("Speed", movement.sqrMagnitude);
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + movement * speed.GetSpeed() * Time.fixedDeltaTime);
    }


}
