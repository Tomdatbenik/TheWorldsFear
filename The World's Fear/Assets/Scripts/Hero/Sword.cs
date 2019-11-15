using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sword : MonoBehaviour
{
    public GameObject wielder;
    public Animator animator;
    public Rigidbody2D rb;
    public Joystick joystick;

    private Vector2 LastRotation;

    void Update()
    {
        Vector3 Direction = wielder.transform.position - transform.position;
        float angle = Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;
        rb.rotation = angle;

        Vector2 DirectionFloats = new Vector2(joystick.Direction.x, joystick.Direction.y);

        this.transform.position = new Vector2(wielder.transform.position.x + DirectionFloats.x, wielder.transform.position.y + DirectionFloats.y);
        animator.SetFloat("Attack Horizontal", DirectionFloats.x);
        animator.SetFloat("Attack Vertical", DirectionFloats.y);
    }
}
