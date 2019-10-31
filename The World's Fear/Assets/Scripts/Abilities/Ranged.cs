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

    public Ranged(float minDistance, float maxDistance) : base(new BasicAbilityInfo(name,description,BehaviorStartTime.Start))
    {
        this.minDistance = minDistance;
        this.maxDistance = maxDistance;
    }
    
    public override void PreformBehavior(Vector2 startPosition, GameObject go)
    {
        this.lifeDistance = maxDistance;

        StartCoroutine(checkDistance(startPosition, go));
    }

    private IEnumerator checkDistance(Vector2 startPosition,GameObject go)
    {
        Debug.Log("Test");
        float tempDistance = Vector2.Distance(startPosition, go.transform.position);

           tempDistance = Vector2.Distance(startPosition, go.transform.position);

        //GameObject.Destroy(go); //objecct pooling maybe?
        yield return null;  
    }

}
