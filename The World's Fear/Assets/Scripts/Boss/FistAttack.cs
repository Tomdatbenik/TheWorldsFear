using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FistAttack : MonoBehaviour
{
    public Animator lefthand;
    public Animator righthand;
    public BoxCollider2D lefthandcollider;
    public BoxCollider2D righthandcollider;
    public BoxCollider2D playercollider;

    public bool FistAttackDetection()
    {
        if (lefthandcollider.IsTouching(playercollider) || righthandcollider.IsTouching(playercollider))
        {
            ActivateFistAttack();
            return true;
        }
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftHandAttack") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightHandAttack"))
        {
            DeactivateFistAttack();
        }
        return false;
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

    public void CheckForAnimationRunningFist()
    {
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftHandAttack") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightHandAttack"))
        {
            DeactivateFistAttack();
        }
    }
}
