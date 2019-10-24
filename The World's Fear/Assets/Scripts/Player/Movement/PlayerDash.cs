using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    private Vector2 movement;
    public int dashspeed = 100;

    private float coolDowntime = 5f;
    private float nextCastTime;
    private float dashDuration = 0.5f;
    private float dashStopTime;

    private Rigidbody2D rb;
    // Start is called before the first frame update
    void Start()
    {
        rb = gameObject.GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        //when you're able to dash
        if (Time.time > nextCastTime)
        {
            movement.x = Input.GetAxis("Horizontal");
            movement.y = Input.GetAxis("Vertical");
            if (Input.GetKeyDown(KeyCode.LeftShift))
            {
                Debug.Log("dash");
                //set timers for the cooldown and the dash duration
                nextCastTime = Time.time + coolDowntime;
                dashStopTime = Time.time + dashDuration;
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.LeftShift))
            {
                Debug.Log("recharge");
            }
        }
    }

    private void FixedUpdate()
    {
        if(Time.time <= dashStopTime)
        {
            Debug.Log("test");
            rb.MovePosition(movement * dashspeed * Time.deltaTime);
        }
    }
}
