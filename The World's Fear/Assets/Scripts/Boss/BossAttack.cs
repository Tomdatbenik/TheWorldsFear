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
    public BossFireLineAttack fla;
    public Animator animator;

    public bool Casting = false;
    private int timer = 0;

    public int TimeBetweenAttacks;

    public Health health;
    private int MaxHp;

    private bool Ragemode = false;
    private bool SuperRagemode = false;
    private int BossStage = 0;
    private string LastAttack = "";

    private void Start()
    {
        MaxHp = health.gethealthpoints();
    }

    private void Update()
    {
        //the 2 if make sure that if the colliders overlap that fist attack will be prioritised
        if(!Casting)
        {
            Casting = fa.FistAttackDetection();
            if (!Casting)
            {
                Casting = fac.FistAttackCloseDetection();

                if(Casting)
                {
                    int choice = Random.Range(0, 10);
                    if(choice < 8)
                    {
                        fac.ActivateFistAttackClose();
                    }
                    else
                    {
                        rsa.ActivateRiskSpotAttack();
                    }
                    
                }
            }
            if (!Casting)
            {
                //CHoice between fire line attack
                Casting = rsa.RiskSpotDetection();

                if(Casting)
                {
                    int choice = Random.Range(0, 10);
                    if (BossStage == 0 || choice < 6 && LastAttack != "Risk")
                    {
                        LastAttack = "Risk";
                        rsa.ActivateRiskSpotAttack();
                    }
                    else if(LastAttack != "FireLine")
                    {
                        LastAttack = "FireLine";
                        fla.ActivateFireLine();
                    }
                    else
                    {
                        Casting = bfa.Shootfireball();
                    }
                }
            }
            if(!Casting)
            {
                if (BossStage == 0)
                {
                    int choice = Random.Range(0, 100);
                    if (choice > 90)
                    {
                        spawn_Minion.SpawnSpider();
                    }
                    else
                    {
                        Casting = bfa.Shootfireball();
                    }
                }
                if (BossStage == 1)
                {
                    int choice = Random.Range(0, 100);
                    if (choice > 80)
                    {
                        spawn_Minion.SpawnSpider();
                    }
                    else
                    {
                        Casting = bfa.Shootfireball();
                    }
                }
                if (BossStage == 2)
                {
                    int choice = Random.Range(0, 100);
                    if (choice > 70)
                    {
                        spawn_Minion.SpawnSpider();
                    }
                    else
                    {
                        Casting = bfa.Shootfireball();
                    }
                }
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

        if(!Ragemode && !Casting)
        {
            if (health.gethealthpoints() < MaxHp / 4 * 3)
            {
                TimeBetweenAttacks = TimeBetweenAttacks / 2;
                Ragemode = true;
                BossStage = 1;
                timer = 0;
                spawn_Minion.SpawnSpider();
            }
        }

        if (!SuperRagemode && !Casting)
        {
            if (health.gethealthpoints() < MaxHp / 2)
            {
                TimeBetweenAttacks = TimeBetweenAttacks / 4 * 3;
                SuperRagemode = true;
                BossStage = 2;
                timer = 0;
                spawn_Minion.SpawnSpider();
            }
        }

    }
}
