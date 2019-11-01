using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ranged : AbilityBehavoir
{

    private const string name = "Ranged";
    private const string description = "A ranged attack!";


    public float minDistance;
    public float maxDistance;

    private float lifeDistance;

    GameObject caster;
    GameObject prefab;


    public Ranged(float minDistance, float maxDistance) : base(new BasicAbilityInfo(name,description,BehaviorStartTime.Start))
    {
        this.minDistance = minDistance;
        this.maxDistance = maxDistance;
    }
    
    public override void PreformBehavior(GameObject caster, GameObject prefab)
    {
        
    }

}
