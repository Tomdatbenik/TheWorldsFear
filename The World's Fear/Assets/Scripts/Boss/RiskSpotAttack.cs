using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RiskSpotAttack : MonoBehaviour
{
    public BoxCollider2D playercollider;
    public PolygonCollider2D Riskspotcollider;
    public Animator animator;
    public BoxCollider2D detectioncollider;
    public bool riskspot = false;


    private void OnTriggerEnter2D(Collider2D collision)
    {
        riskspot = true;
    }

    public bool RiskSpotDetection()
    {
        if (detectioncollider.IsTouching(playercollider))
        {
            ActivateRiskSpotAttack();
            return true;

        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("RiskSpotAttack"))
        {
            DeactivateRiskSpotAttack();
        }
        return false;
    }

    public void ActivateRiskSpotAttack()
    {
        animator.SetBool("Initiate", true);
    }

    public void DeactivateRiskSpotAttack()
    {
        animator.SetBool("Initiate", false);
    }

    public void CheckForAnimationRunningRiskSpot()
    {
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("RiskSpotAttack"))
        {
            DeactivateRiskSpotAttack();
        }
    }

   
}
