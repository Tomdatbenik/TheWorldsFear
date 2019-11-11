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

    private void Start()
    {
        playermovementscript = gameObject.GetComponent<PlayerMovement>();
        duration = DashDuration;
        Dashspeed = playermovementscript.speed.GetSpeed() * 5;
    }

    private void Update()
    {
        //PC input
        if (Input.GetKeyDown(KeyCode.RightShift) && !dash)
        {
            Dash();
        }
    }

    private void FixedUpdate()
    {
        //if dash is activated
        if (dash)
        {
            //and the dash timer is still going
            if(duration != 0)
            {
                duration--;
                playermovementscript.speed.SetSpeed(Dashspeed);
            }
            else
            {
                dash = false;
                duration = DashDuration;
                playermovementscript.speed.ResetSpeed();
            }
        }
    }

    public void Dash()
    {
        //Mobile input
        if (!dash)
        {
            dash = true;
        }
    }
}
