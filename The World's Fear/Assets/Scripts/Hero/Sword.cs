using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sword : MonoBehaviour
{
    public GameObject wielder;
    public Animator animator;
    public Rigidbody2D rb;

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

        Vector2 AnimatorFloats = new Vector2(animator.GetFloat("Horizontal"), animator.GetFloat("Vertical"));

        if (AnimatorFloats.x > 0)
        {
            LastRotation.x = 1;
            AnimatorFloats.x = 1;
        }
        else if(AnimatorFloats.x < 0)
        {
            LastRotation.x = -1;
            AnimatorFloats.x = -1;
        }
        else if(AnimatorFloats.x == 0 && AnimatorFloats.y != 0)
        {
            LastRotation.x = 0;
        }

        if (AnimatorFloats.y > 0)
        {
            LastRotation.y = 1;
            AnimatorFloats.y = 1;
        }
        else if (AnimatorFloats.y < 0)
        {
            LastRotation.y = -1;
            AnimatorFloats.y = -1;
        }
        else if (AnimatorFloats.y == 0 && AnimatorFloats.x != 0)
        {
            LastRotation.y = 0;
        }

        Debug.Log("x " + LastRotation.x);
        Debug.Log("y " + LastRotation.y);

        if (AnimatorFloats == Vector2.zero)
        {
            //TODO Remember postion player is facing
            AnimatorFloats.x = LastRotation.x;
            AnimatorFloats.y = LastRotation.y;
        }

        this.transform.position = new Vector2(wielder.transform.position.x + AnimatorFloats.x, wielder.transform.position.y + AnimatorFloats.y);
    }
}
