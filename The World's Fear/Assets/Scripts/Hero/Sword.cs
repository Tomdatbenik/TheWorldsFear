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
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 Direction = wielder.transform.position - transform.position;
        float angle = Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;
        rb.rotation = angle;

        Vector2 DirectionFloats = new Vector2(joystick.Direction.x, joystick.Direction.y);

        //if (DirectionFloats.x > 0)
        //{
        //    LastRotation.x = 1;
        //    DirectionFloats.x = 1;
        //}
        //else if(DirectionFloats.x < 0)
        //{
        //    LastRotation.x = -1;
        //    DirectionFloats.x = -1;
        //}
        //else if(DirectionFloats.x == 0 && DirectionFloats.y != 0)
        //{
        //    LastRotation.x = 0;
        //}

        //if (DirectionFloats.y > 0)
        //{
        //    LastRotation.y = 1;
        //    DirectionFloats.y = 1;
        //}
        //else if (DirectionFloats.y < 0)
        //{
        //    LastRotation.y = -1;
        //    DirectionFloats.y = -1;
        //}
        //else if (DirectionFloats.y == 0 && DirectionFloats.x != 0)
        //{
        //    LastRotation.y = 0;
        //}

        //Debug.Log("x " + LastRotation.x);
        //Debug.Log("y " + LastRotation.y);

        //if (DirectionFloats == Vector2.zero)
        //{
        //    //TODO Remember postion player is facing
        //    DirectionFloats.x = LastRotation.x;
        //    DirectionFloats.y = LastRotation.y;
        //}

        //Debug.Log(DirectionFloats.x);
        //Debug.Log(DirectionFloats.y);

        this.transform.position = new Vector2(wielder.transform.position.x + DirectionFloats.x, wielder.transform.position.y + DirectionFloats.y);
        animator.SetFloat("Attack Horizontal", DirectionFloats.x);
        animator.SetFloat("Attack Vertical", DirectionFloats.y);
    }
}
