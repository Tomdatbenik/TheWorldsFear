using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterMovement : MonoBehaviour
{
    // Start is called before the first frame update
    public Transform Target;
    public Rigidbody2D rb;
    public float movementspeed;

    private Vector2 movement;
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        this.transform.LookAt(Target);

        rb.transform.position += transform.forward * movementspeed * Time.fixedDeltaTime;
    }
}
