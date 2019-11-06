using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sword : MonoBehaviour
{
    public GameObject wielder;
    public Animator animator;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.position = new Vector2(wielder.transform.position.x + animator.GetFloat("Horizontal"), wielder.transform.position.y + animator.GetFloat("Vertical"));
    }
}
