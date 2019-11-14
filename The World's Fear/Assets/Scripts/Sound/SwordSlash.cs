using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SwordSlash : MonoBehaviour
{
    public AudioClip[] swordslashes;
    public AudioSource audiosource;

    int selectswordslash;
    bool play = false;
    int counter = 0;

    

    void FixedUpdate()
    {
        if(play == false)
        {
            play = true;
            selectswordslash = Random.Range(1, 3);
            audiosource.clip = swordslashes[selectswordslash];
            audiosource.Play();
        }
        counter++;
        if (counter == 20)
        {
            counter = 0;
            play = false;
        }
    }
}
