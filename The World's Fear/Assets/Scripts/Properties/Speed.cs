using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Speed : MonoBehaviour
{
    public float SpeedPoints;
    private float InitSpeed;

    private void Start()
    {
        SpeedPoints = Random.Range(SpeedPoints - 2, SpeedPoints + 2);
        InitSpeed = SpeedPoints;
    }

    public float GetSpeed()
    {
        return SpeedPoints;
    }

    public void IncreaseSpeed(float sp)
    {
        this.SpeedPoints += sp;
    }

    public void SetSpeed(float sp)
    {
        this.SpeedPoints = sp;
    }

    public void DecreaseSpeed(float sp)
    {
        this.SpeedPoints -= sp;
    }

    public void ResetSpeed()
    {
        SpeedPoints = InitSpeed;
    }
}
