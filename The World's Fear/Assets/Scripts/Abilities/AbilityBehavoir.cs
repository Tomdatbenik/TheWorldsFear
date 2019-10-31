using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class AbilityBehavoir : MonoBehaviour
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

    public AbilityBehavoir(BasicAbilityInfo basicAbilityInfo, BehaviorStartTime startTime)
    {
        this.basicAbilityInfo = basicAbilityInfo;
        this.startTime = startTime;
    }

    public virtual void PreformBehavior()
    {
        Debug.LogWarning("No behaivor added!");
    }

    public virtual void PreformBehavior(Vector2 startPosition)
    {
        Debug.LogWarning("No behaivor added!");
    }

    public virtual void PreformBehavior(Vector2 startPosition, GameObject go)
    {
        Debug.LogWarning("No behaivor added!");
    }

 
}
