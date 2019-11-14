using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossFireLineAttack : MonoBehaviour
{
    public GameObject FireLine;
    public Transform Boss;
    public Rigidbody2D FirelineRigitBody;
    private Vector2 Position;

    public void ActivateFireLine()
    {
        GameObject fireline = FireLine;
        fireline = Instantiate(fireline);
        fireline.transform.position = Boss.position;
    }
}
