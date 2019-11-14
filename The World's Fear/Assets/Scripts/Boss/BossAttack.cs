using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossAttack : MonoBehaviour
{
    public FistAttack fa;
    public RiskSpotAttack rsa;
    public FistAttackClose fac;
    public BossFireAttack bfa;
    public Spawn_minion spawn_Minion;

    public bool Casting = false;
    private int timer = 0;

    private int TimeBetweenAttacks = 75;

    public Health health;
    private int MaxHp;

    private bool Ragemode = false;
    private bool SuperRagemode = false;

    private void Start()
    {
        MaxHp = health.gethealthpoints();
    }

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
                Casting = bfa.Shootfireball();
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
            if(timer == TimeBetweenAttacks)
            {
                Casting = false;
                timer = 0;
            }
        }

        if(health.isDead())
        {
            this.gameObject.SetActive(false);
        }

        if(!Ragemode)
        {
            if (health.gethealthpoints() < MaxHp / 4 * 3)
            {
                TimeBetweenAttacks = TimeBetweenAttacks / 2;
                Ragemode = true;
                timer = 0;
                spawn_Minion.SpawnSpider();
            }
        }

        if (!SuperRagemode)
        {
            if (health.gethealthpoints() < MaxHp / 2)
            {
                TimeBetweenAttacks = TimeBetweenAttacks / 4 * 3;
                SuperRagemode = true;
                timer = 0;
                spawn_Minion.SpawnSpider();
            }
        }

    }
}
