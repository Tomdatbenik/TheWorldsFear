using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Healthbar : MonoBehaviour
{
    public Health health;

    private float maxhealth = 0;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(maxhealth == 0)
        {
            maxhealth = health.HealthPoints;
        }

        if(!health.isDead())
        {
            float healthLeft = 2f / 100.0f * ((float)(100f / float.Parse(maxhealth.ToString())) * health.HealthPoints);

            this.transform.localScale = new Vector3(healthLeft, .25f);
        }
        else
        {
            this.transform.localScale = new Vector3(0f, .25f);
        }
    }
}
