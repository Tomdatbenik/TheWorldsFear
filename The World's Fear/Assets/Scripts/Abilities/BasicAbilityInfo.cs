using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BasicAbilityInfo
{

    private string name;
    private string description;
    private Sprite icon;

    public BehaviorStartTime startTime;

    public BasicAbilityInfo()
    {
        startTime = BehaviorStartTime.Start;
    }

    public BasicAbilityInfo(string name)
    {
        this.name = name;
        this.description = "Default";
        this.icon = null;
        this.startTime = BehaviorStartTime.Start;
    }

    public BasicAbilityInfo(string name, string description, BehaviorStartTime startTime)
    {
        this.name = name;
        this.description = description;
        this.icon = null;
        this.startTime = BehaviorStartTime.Start;
    }
}
