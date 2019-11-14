using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PickUpSword : MonoBehaviour
{
    public Joystick joystick;

    public void OnTriggerEnter2D(Collider2D collision)
    {
        joystick.gameObject.SetActive(true);
        gameObject.SetActive(false);
    }
}

