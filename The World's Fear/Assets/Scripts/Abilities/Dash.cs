using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Dash : MonoBehaviour
{
    private Ability ability = new Ability(200);
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
       
        if (Input.GetKey(KeyCode.LeftShift))
        {
            if (ability.canCast)
            {
                ability.Cast();
                PreformDash();
            }
        }
        if (ability.isCasted)
        {
            ability.isRecharged();
        }
    }

    public void PreformDash()
    {
        //dash here
    }
}
