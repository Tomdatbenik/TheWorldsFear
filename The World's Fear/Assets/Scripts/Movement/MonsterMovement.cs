using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterMovement : MonoBehaviour
{
    // Start is called before the first frame update
    public Transform Target;
    public Rigidbody2D rb;
    public float movementspeed = 5f;
    private Vector2 movement;

    public void Update()
    {
        Vector3 Direction = Target.position - transform.position;
        float angle = Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;
        rb.rotation = angle;
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
        rb.MovePosition((Vector2)transform.position + (direction * movementspeed * Time.deltaTime));
    }
}
