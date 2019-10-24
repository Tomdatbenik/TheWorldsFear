using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossFireAttack : MonoBehaviour
{
    //left top x = -19 y = 26
    //right down x = 19, y = 6
    private Random xpicker;
    private Random ypicker;
    int x = Random.Range(-19, 26);
    int y = Random.Range(6, 19);

    void PickCoordinate()
    {
        //int x = Random.Range(-19, 26);
        //int y = Random.Range(6, 19);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log(x);
            Debug.Log(y);
        }
    }

    void ShootFireball()
    {

    }
}
