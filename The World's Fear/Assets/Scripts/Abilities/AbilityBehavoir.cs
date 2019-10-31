using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class AbilityBehavoir : MonoBehaviour
{

    private BasicAbilityInfo basicAbilityInfo;

    public AbilityBehavoir(BasicAbilityInfo basicAbilityInfo)
    {
        this.basicAbilityInfo = basicAbilityInfo;
    }

    public virtual void PreformBehavior()
    {
        Debug.LogWarning("No behaivor added!");
    }

    public BasicAbilityInfo BasicInfo { get { return basicAbilityInfo; } set { this.basicAbilityInfo = BasicInfo; } }
}
