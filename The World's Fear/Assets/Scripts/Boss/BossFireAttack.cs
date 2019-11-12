using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossFireAttack : MonoBehaviour
{
    //PLAYFIELD:
    //left top x = -19 y = 26
    //right down x = 19, y = 6
    public GameObject Fireball;
    private Vector2 Position;

    public bool Shootfireball()
    {
        CreateFireballs(PickCoordinates());
        return true;
    }

    List<Vector2> PickCoordinates()
    {
        List<Vector2> Fireballs = new List<Vector2>();
        for (int i = 0; i < 3; i++)
        {
            Vector2 pos = new Vector2(Random.Range(-19, 19), Random.Range(6, 29));
            Fireballs.Add(pos);
        }
        return Fireballs;
    }

    void CreateFireballs(List<Vector2> fireballs)
    {
        for (int i = 0; i < 3; i++)
        {
            GameObject fireball = Fireball;
            fireball.transform.position = fireballs[i];
            Instantiate(fireball);
        }
    }
}
