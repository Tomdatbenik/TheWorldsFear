using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Dash : Ability
{
    private const string name = "Arrow";
    private const string description = "Schoots an arrow!";

    public List<AbilityBehavoir> behavoirs;
    //private const Sprite sprite = Resources.Load("Arrow");

    public void Cast(GameObject caster)
    {
        base.Caster = caster;

        List<AbilityBehavoir> behavoirs = new List<AbilityBehavoir>();

        behavoirs.Add(new Leap());

        this.Behavoirs = behavoirs;

        base.PreformAbility();
    }
}
