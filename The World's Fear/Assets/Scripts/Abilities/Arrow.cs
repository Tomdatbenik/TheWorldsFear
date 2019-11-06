﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Arrow : Ability 
{
    private const string name = "Arrow";
    private const string description = "Schoots an arrow!";

    public List<AbilityBehavoir> behavoirs;
    //private const Sprite sprite = Resources.Load("Arrow");

    public void Cast(GameObject caster, GameObject prefab, List<AbilityBehavoir> behavoirs)
    {
        base.Caster = caster;
        base.Prefab = prefab;

        prefab.transform.rotation = caster.transform.rotation;
        prefab.transform.position = caster.transform.position;

        this.Behavoirs = behavoirs;
        this.Prefab = GameObject.Instantiate<GameObject>(base.Prefab);
        base.PreformAbility();
    }
}