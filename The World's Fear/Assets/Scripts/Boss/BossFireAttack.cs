using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossFireAttack : MonoBehaviour
{
    //PLAYFIELD:
    //left top x = -19 y = 26
    //right down x = 19, y = 6
    public GameObject Fireball;
    
    
    private Vector2 Firebal1;
    private Vector2 Firebal2;
    private Vector2 Firebal3;

    Vector2 PickCoordinate()
    {
        Vector2 firebal = new Vector2(Random.Range(-19, 26), Random.Range(6, 19));
        return firebal;
    }
    
    void ShootFireball()
    {
        Fireball.transform.position = PickCoordinate();
        Instantiate(Fireball);
    }
}
