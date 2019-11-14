using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FistAttackClose : MonoBehaviour
{
    public Animator lefthand;
    public Animator righthand;
    public BoxCollider2D playercollider;
    public BoxCollider2D fistattackclosecollider;

    public bool FistAttackCloseDetection()
    {
        if (fistattackclosecollider.IsTouching(playercollider))
        {
            return true;
        }
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftFistAttackClose") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightFistAttackClose"))
        {
            DeactivateFistAttackClose();
        }
        return false;
    }

    public void ActivateFistAttackClose()
    {
        lefthand.SetBool("Initiate C", true);
        righthand.SetBool("Initiate C", true);
    }

    public void DeactivateFistAttackClose()
    {
        lefthand.SetBool("Initiate C", false);
        righthand.SetBool("Initiate C", false);
    }

    public void CheckForAnimationRunningFistClose()
    {
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftFistAttackClose") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightFistAttackClose"))
        {
            DeactivateFistAttackClose();
        }
    }
}
