using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    private PlayerMovement playermovementscript;

    private int DashDuration;
    public float Dashspeed = 20;

    private bool dash = false;

    private void Start()
    {
        playermovementscript = gameObject.GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        //PC input
        if (Input.GetKeyDown(KeyCode.RightShift) && !dash)
        {
            DashDuration = 5;
            Dash();
        }
    }

    private void FixedUpdate()
    {
        //if dash is activated
        if (dash)
        {
            Debug.Log("Dash");
            //and the dash timer is still going
            if(DashDuration != 0)
            {
                DashDuration--;
                playermovementscript.speed.SetSpeed(Dashspeed);
            }
            else
            {
                dash = false;
                playermovementscript.speed.ResetSpeed();
            }
        }
    }

    public void Dash()
    {
        //Mobile input
        if (!dash)
        {
            DashDuration = 5;
            dash = true;
        }
    }
}
