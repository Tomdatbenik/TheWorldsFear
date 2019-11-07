using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RiskSpotAttack : MonoBehaviour
{
    public Animator animator;
    public BoxCollider2D boxCollider;
    public BoxCollider2D playercollider;

    // Update is called once per frame
    void Update()
    {
        if (boxCollider.IsTouching(playercollider))
        {
            animator.SetBool("Initiate", true);
        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("RiskSpotAttack"))
        {
            animator.SetBool("Initiate", false);
        }
    }
}
