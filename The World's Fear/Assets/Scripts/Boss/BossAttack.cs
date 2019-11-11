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
    //FistAttackClose
    public Animator fistattackcloseanimator;
    public BoxCollider2D fistattackclosecollider;

    public bool Casting = false;
    public int timer = 0;
    public Health health;
   
    private void Update()
    {
        //the 2 if make sure that if the colliders overlap that fist attack will be prioritised
        if(Casting == false)
        {
            FistAttackDetection();
            if (Casting == false)
            {
                FistAttackCloseDetection();
            }
            if (Casting == false)
            {
                RiskSpotDetection();
            }
            if(Casting == false)
            {
                Shootfireball();
            }
        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("RiskSpotAttack"))
        {
            DeactivateRiskSpotAttack();
        }
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftHandAttack") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightHandAttack"))
        {
            DeactivateFistAttack();
        }
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
        if(health.isDead())
        {
            this.gameObject.SetActive(false);
        }
    }

    #region FistAttack
    public void FistAttackDetection()
    {
        if (lefthandcollider.IsTouching(playercollider) || righthandcollider.IsTouching(playercollider))
        {
            ActivateFistAttack();
            Casting = true;
        }
        if (lefthand.GetCurrentAnimatorStateInfo(0).IsName("LeftHandAttack") && righthand.GetCurrentAnimatorStateInfo(0).IsName("RightHandAttack"))
        {
            DeactivateFistAttack();
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
            Casting = true;
            
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
    #region Fireball
    public GameObject Fireball;
    private Vector2 Position;

    public void Shootfireball()
    {
        CreateFireballs(PickCoordinates());
        Casting = true;
    }

    List<Vector2> PickCoordinates()
    {
        List<Vector2> Fireballs = new List<Vector2>();
        for (int i = 0; i < 3; i++)
        {
            Vector2 pos = new Vector2(Random.Range(-19, 19), Random.Range(6, 29));
            Fireballs.Add(pos);
        }
        return Fireballs;
    }

    void CreateFireballs(List<Vector2> fireballs)
    {
        for (int i = 0; i < 3; i++)
        {
            GameObject fireball = Fireball;
            fireball.transform.position = fireballs[i];
            Instantiate(fireball);
        }
    }
    #endregion

    public void FistAttackCloseDetection()
    {
        if (fistattackclosecollider.IsTouching(playercollider))
        {
            ActivateFistAttackClose();
            Casting = true;
        }
        if (fistattackcloseanimator.GetCurrentAnimatorStateInfo(0).IsName("FistAttackClose"))
        {
            DeactivateFistAttackClose();
        }
    }

    public void ActivateFistAttackClose()
    {
        fistattackcloseanimator.SetBool("Initiate", true);
    }

    public void DeactivateFistAttackClose()
    {
        fistattackcloseanimator.SetBool("Initiate", false);
    }
}
