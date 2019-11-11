using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpiderMovement : MonoBehaviour
{
    // Start is called before the first frame update
    public Transform Target;
    public Rigidbody2D SpiderBody;
    public Speed speed;
    private Vector2 movement;
    public GameObject healthbar;

    public Health health;

    public void Update()
    {
        Vector3 Direction = Target.position - transform.position;
        float angle = Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;
        SpiderBody.rotation = angle;
        Direction.Normalize();
        movement = Direction;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if(!health.isDead())
        {
            moveCharacter(movement);
        }
        else
        {
            this.gameObject.SetActive(false);
        }

    }

    void moveCharacter(Vector2 direction)
    {
        SpiderBody.MovePosition((Vector2)SpiderBody.transform.position + (direction * speed.GetSpeed() * Time.deltaTime));
        healthbar.transform.position = SpiderBody.position + new Vector2(0, 1);
    }
}
