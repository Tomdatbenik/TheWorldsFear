using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sword : MonoBehaviour
{
    public GameObject wielder;
    public Animator animator;
    public Rigidbody2D rb;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 Direction = wielder.transform.position - transform.position;
        float angle = Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;
        rb.rotation = angle;
        this.transform.position = new Vector2(wielder.transform.position.x + animator.GetFloat("Horizontal"), wielder.transform.position.y + animator.GetFloat("Vertical"));
    }
}
