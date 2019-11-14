using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RiskSpotGiveBuff : MonoBehaviour
{
    public GameObject sword;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.tag == "Player")
        {
            Damage damage = sword.gameObject.GetComponent(typeof(Damage)) as Damage;
            
            damage.IncreaseStrength(damage.Strength * 2);
        }
    }
}
