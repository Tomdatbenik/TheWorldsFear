using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability
{

    private BasicAbilityInfo basicInfo;
    private Sprite icon;
    private List<AbilityBehavoir> behavoirs;
        
    private int cooldown; //miliseconds
    
    private GameObject caster; //Cster is the game object it origins from.
    private GameObject prefab; //Fireball arrow and those kinds of things.


    public BasicAbilityInfo BasicInfo{ get { return basicInfo; } set { basicInfo = value; } }
    public Sprite Icon { get { return icon; } set { icon = value; } }
    public List<AbilityBehavoir> Behavoirs { get { return behavoirs; } set { behavoirs = value; } }
    public int Cooldown { get { return cooldown; } set { cooldown = value; } }
    public GameObject Caster { get { return caster; } set { caster = value; } }
    public GameObject Prefab { get { return prefab; } set { prefab = value; } }


    public void PreformAbility()
    {
        foreach (AbilityBehavoir b in behavoirs)
        {
            if(prefab == null)
            {
                b.PreformBehavior(caster);
            }
            else
            {
                b.PreformBehavior(caster, prefab);
            }
        }
    }
}
