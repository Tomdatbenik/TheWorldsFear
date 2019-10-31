using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability
{

    private BasicAbilityInfo basicInfo;
    private Sprite icon;
    private List<AbilityBehavoir> behavoirs;
    private bool requiresTarget;
    private bool canCastOnSelf;

    private int cooldown; //miliseconds
    private GameObject particals; //Swinging sword or leaving dust with dashes. can be assigned when creating ability


    public List<AbilityBehavoir> Behavoirs { get { return behavoirs; } set { this.behavoirs = Behavoirs; } }

    public Ability(BasicAbilityInfo basicInfo)
    {
        this.basicInfo = basicInfo;
        this.behavoirs = new List<AbilityBehavoir>();
        this.icon = null;
        cooldown = 0;
        requiresTarget = false;
        canCastOnSelf = false;
    }

    public Ability(BasicAbilityInfo basicInfo, Sprite icon, List<AbilityBehavoir> behavoirs,bool requireTarget, int cooldown)
    {
    this.basicInfo = basicInfo;
        this.behavoirs = behavoirs;
        this.icon = icon;
        this.cooldown = cooldown;
        this.requiresTarget = requireTarget;
        canCastOnSelf = false;
    }

    public BasicAbilityInfo BasicInfo { get { return basicInfo; } set { basicInfo = BasicInfo; } }

    public Sprite Icon{ get { return icon; } set { icon = Icon; } }

    public int Cooldown{ get { return cooldown; } set { cooldown = Cooldown; } }

    private int Cooldowntime;
    private int Counter;
    public bool isCasted = false;
    public bool canCast = true;

    public void SetCooldownTime(int cooldowntime)
    {
        Cooldowntime = cooldowntime;
    }

    public bool isRecharged()
    {
        Counter++;
        if(Counter >= Cooldowntime && !canCast)
        {
            isCasted = false;
            canCast = true;
            Counter = 0;
            return true;
        }
        return false;
    }

    public void Cast()
    {
        isCasted = true;
        canCast = false;
    }
}
