using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ranged : AbilityBehavoir
{

    private const string name = "Ranged";
    private const string description = "A ranged attack!";


    private float minDistance;
    private float maxDistance;

    private float lifeDistance;

    public Ranged(float minDistance, float maxDistance) : base(new BasicAbilityInfo(name,description,BehaviorStartTime.Start))
    {

    }
    
    public override void PreformBehavior(Vector2 startPosition)
    {
        this.lifeDistance = maxDistance;

        StartCoroutine(checkDistance(startPosition));

    }

    private IEnumerator checkDistance(Vector2 startPosition)
    {
        float tempDistance = Vector2.Distance(startPosition, this.transform.position);

        while (tempDistance < lifeDistance)
        {
            tempDistance = Vector2.Distance(startPosition, this.transform.position);           
        }

        this.gameObject.SetActive(false); //objecct pooling maybe?
        yield return null;
       
    }

}
