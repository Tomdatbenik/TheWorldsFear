    using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    private PlayerMovement playermovementscript;

    public int DashDuration;
    private float Dashspeed = 20;

    private int duration;

    private bool dash = false;
    public Speed speed;

    private void Start()
    {
        duration = DashDuration;
        Dashspeed = speed.GetSpeed() * 5;
    }

    private void Update()
    {
        //PC input
        if (Input.GetKeyDown(KeyCode.LeftShift) && !dash)
        {
            Dash();
        }

        //if dash is activated
        if (dash)
        {
            //and the dash timer is still going
            if (duration != 0)
            {
                duration--;
                speed.SetSpeed(Dashspeed);
            }
            else
            {
                dash = false;
                duration = DashDuration;
                speed.ResetSpeed();
            }
        }
    }

    private void FixedUpdate()
    {

    }

    public void Dash()
    {
        //Mobile input
        if (!dash)
        {
            Debug.Log("dash");
            dash = true;
        }
    }
}
