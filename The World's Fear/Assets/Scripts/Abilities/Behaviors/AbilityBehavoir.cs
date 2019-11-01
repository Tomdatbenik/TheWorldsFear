using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class AbilityBehavoir
{

    private BasicAbilityInfo basicAbilityInfo;
    private BehaviorStartTime startTime;

    public BehaviorStartTime StartTime { get { return startTime; } set { this.startTime = value; } }
    public BasicAbilityInfo BasicInfo { get { return basicAbilityInfo; } set { this.basicAbilityInfo = value; } }

    public AbilityBehavoir(BasicAbilityInfo basicAbilityInfo)
    {
        this.basicAbilityInfo = basicAbilityInfo;
        startTime = BehaviorStartTime.Start;
    }

    public virtual void PreformBehavior()
    {
        Debug.LogWarning("No behaivor added!");
    }

    public virtual void PreformBehavior(GameObject caster)
    {
        Debug.LogWarning("No behaivor added!");
    }

    public virtual void PreformBehavior(GameObject caster, GameObject prefab)
    {
        Debug.LogWarning("No behaivor added!");
    }

}
