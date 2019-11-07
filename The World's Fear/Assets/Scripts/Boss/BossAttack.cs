using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossAttack : MonoBehaviour
{
    //Fist Attack
    public Animator lefthand;
    public Animator righthand;
    public BoxCollider2D lefthandcollider;
    public BoxCollider2D righthandcollider;
    public BoxCollider2D playercollider;
    //RiskSpotAttack
    public Animator animator;
    public BoxCollider2D boxCollider;
    
   
    private void Update()
    {
        FistAttackDetection();
        RiskSpotDetection();
    }

    #region FistAttack
    public void FistAttackDetection()
    {
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftHandAttack") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightHandAttack"))
        {
            DeactivateFistAttack();
        }
        if (lefthandcollider.IsTouching(playercollider) || righthandcollider.IsTouching(playercollider))
        {
            ActivateFistAttack();
        }
    }

    public void ActivateFistAttack()
    {
        lefthand.SetBool("Initiate", true);
        righthand.SetBool("Initiate", true);
    }

    public void DeactivateFistAttack()
    {
        lefthand.SetBool("Initiate", false);
        righthand.SetBool("Initiate", false);
    }
    #endregion
    #region RiskSpot
    public void RiskSpotDetection()
    {
        if (boxCollider.IsTouching(playercollider))
        {
            ActivateRiskSpotAttack();
        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("RiskSpotAttack"))
        {
            DeactivateRiskSpotAttack();
        }
    }

    public void ActivateRiskSpotAttack()
    {
        animator.SetBool("Initiate", true);
    }

    public void DeactivateRiskSpotAttack()
    {
        animator.SetBool("Initiate", false);
    }
    #endregion

}
