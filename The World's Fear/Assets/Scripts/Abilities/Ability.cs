using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability : MonoBehaviour
{

    private BasicAbilityInfo basicInfo;
    private Sprite icon;
    private List<AbilityBehavoir> behavoirs;
    private bool requiresTarget;
    private bool canCastOnSelf;

    private int cooldown; //miliseconds
    private GameObject particals; //Swinging sword or leaving dust with dashes. can be assigned when creating ability

    public GameObject Particals { get { return particals; } set { particals = value; } }

    public BasicAbilityInfo BasicInfo { get { return basicInfo; } set { basicInfo = value; } }

    public Sprite Icon { get { return icon; } set { icon = value; } }

    public int Cooldown { get { return cooldown; } set { cooldown = value; } }
    public List<AbilityBehavoir> Behavoirs { get { return behavoirs; } set { this.behavoirs = value; } }

    public Ability(BasicAbilityInfo basicInfo)
    {
        this.basicInfo = basicInfo;
        this.behavoirs = new List<AbilityBehavoir>();
        cooldown = 0;
    }
    public Ability(BasicAbilityInfo basicInfo, int cooldown)
    {
        this.basicInfo = basicInfo;
        this.behavoirs = new List<AbilityBehavoir>();
        this.cooldown = cooldown;
    }

    public Ability(BasicAbilityInfo basicInfo, int cooldown, GameObject partical)
    {
        this.basicInfo = basicInfo;
        this.behavoirs = new List<AbilityBehavoir>();
        this.cooldown = cooldown;
        this.particals = partical;
    }

    public Ability(BasicAbilityInfo basicInfo, List<AbilityBehavoir> behavoirs, int cooldown)
    {
        this.basicInfo = basicInfo;
        this.behavoirs = behavoirs;
        this.cooldown = cooldown;
    }

    public virtual void UseAbility(GameObject Caster, GameObject go)
    { 
        foreach(AbilityBehavoir b in behavoirs)
        {
            if(b.StartTime == BehaviorStartTime.Start)
            {
                b.PreformBehavior(Caster.transform.position, go);
            }
        }
    }


    //OLD CODE down below

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
