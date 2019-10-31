using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Arrow : Ability
{
    private const string name = "Arrow";
    private const string description = "Schoots an arrow!";
    //private const Sprite sprite = Resources.Load("Arrow");

    public Arrow() : base(new BasicAbilityInfo(name, description, BehaviorStartTime.Start))
    {
        this.Behavoirs.Add(new Ranged(10f,20f));
    }
}
