using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireSlimeMovement : MonoBehaviour
{
    // Start is called before the first frame update
    public Transform Target;
    public Rigidbody2D rb;
    public Speed speed;
    private Vector2 movement;

    public void Update()
    {
        Vector3 Direction = Target.position - transform.position;
        Direction.Normalize();
        movement = Direction;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        moveCharacter(movement);
    }

    void moveCharacter(Vector2 direction)
    {
        rb.MovePosition((Vector2)transform.position + (direction * speed.GetSpeed() * Time.deltaTime));
    }
}

