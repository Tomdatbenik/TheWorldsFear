using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossAttack : MonoBehaviour
{
    public FistAttack fa;
    public RiskSpotAttack rsa;
    public FistAttackClose fac;
    public BossFireAttack bfa;

    public bool Casting = false;
    public int timer = 0;
    public Health health;
   
    private void Update()
    {
        //the 2 if make sure that if the colliders overlap that fist attack will be prioritised
        if(Casting == false)
        {
            Casting = fa.FistAttackDetection();
            if (Casting == false)
            {
                Casting = fac.FistAttackCloseDetection();
            }
            if (Casting == false)
            {
                Casting = rsa.RiskSpotDetection();
            }
            if(Casting == false)
            {
                bfa.Shootfireball();
            }
        }
        
        fa.CheckForAnimationRunningFist();
        fac.CheckForAnimationRunningFistClose();
        rsa.CheckForAnimationRunningRiskSpot();
    }

    private void FixedUpdate()
    {
        if (Casting)
        {
            timer++;
            if(timer == 200)
            {
                Casting = false;
                timer = 0;
            }
        }
    }
}
