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

    // Update is called once per frame
    void Update()
    {
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftHandAttack") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightHandAttack"))
        {
            lefthand.SetBool("Initiate", false);
            righthand.SetBool("Initiate", false);
        }
        if (lefthandcollider.IsTouching(playercollider) || righthandcollider.IsTouching(playercollider))
        {
            lefthand.SetBool("Initiate", true);
            righthand.SetBool("Initiate", true);
        }
    } 
}
