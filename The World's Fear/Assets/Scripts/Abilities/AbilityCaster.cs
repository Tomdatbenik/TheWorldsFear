using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AbilityCaster : MonoBehaviour
{

    public GameObject Caster;
    public GameObject Prefab;

    void Start()
    {
        
    }

    public void CastArrow()
    {
        //Arrow arrow = Caster.AddComponent<Arrow>() as Arrow;

        //List<AbilityBehavoir> behavoirs = new List<AbilityBehavoir>();

        //behavoirs.Add(new Ranged(0f,10f));

        //arrow.Cast(Caster,Prefab, behavoirs);
    }

    public void CastDash()
    {
        Dash dash = new Dash();

        dash.Cast(Caster);
    }

}
