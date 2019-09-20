using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{

    public Rigidbody rb;

    public float Movementspeed;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {

        rb.rotation = new Quaternion(0, 0, 0, 0);

        if (Input.GetKey("w"))
        {
            Move("foward");
        }

        if (Input.GetKey("a"))
        {
            Move("left");
        }

        if (Input.GetKey("s"))
        {
            Move("back");
        }

        if (Input.GetKey("d"))
        {
            Move("right");
        }
    }

    private void Move(string direction)
    {
       Vector3 d = new Vector3(0,0,0);

        switch (direction)
        {
            case "foward":
                d = transform.TransformDirection(Vector3.forward);
                break;
            case "left":
                d = transform.TransformDirection(Vector3.left);
                break;
            case "back":
                d = transform.TransformDirection(Vector3.back);
                break;
            case "right":
                d = transform.TransformDirection(Vector3.right);
                break;
        }

        float v = Input.GetAxis("Vertical") * Movementspeed * Time.deltaTime;
        transform.GetComponent<Rigidbody>().velocity = d * v;
    }
}
