using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Leap : AbilityBehavoir
{
    private const string name = "Leap";
    private const string description = "Moves the caster fast towards a direction!";

    private static System.Timers.Timer timer;

    public int duration = 10;

    public Leap() : base(new BasicAbilityInfo(name, description, BehaviorStartTime.Start))
    {

    }

    private Speed speed;

    public override void PreformBehavior(GameObject caster)
    {
        speed = caster.GetComponent<Speed>();

        timer = new System.Timers.Timer();
        timer.Interval = duration;

        timer.Elapsed += timer1_Tick;
        timer.AutoReset = true;
        timer.Start();
    }

    private void timer1_Tick(object sender, EventArgs e)
    {
        if(duration >= 0)
        {
            duration--;
            speed.SetSpeed(20);
        }
        else
        { 
            timer.Stop();
            speed.ResetSpeed();
        }
    }
}
