using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Arrow : Ability 
{
    private const string name = "Arrow";
    private const string description = "Schoots an arrow!";

    public GameObject Player;
    public GameObject ArrowPrefap;
    public List<AbilityBehavoir> behavoirs;
    //private const Sprite sprite = Resources.Load("Arrow");

    public Arrow() : base(new BasicAbilityInfo(name, description, BehaviorStartTime.Start),0)
    {
        base.Particals = ArrowPrefap;
    }

    public void Cast()
    {
        this.Behavoirs = behavoirs;
        GameObject.Instantiate<GameObject>(ArrowPrefap);
        this.UseAbility(Player, ArrowPrefap);
    }
}
