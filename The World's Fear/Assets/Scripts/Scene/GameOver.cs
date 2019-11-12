using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOver : MonoBehaviour
{
    public GameObject player;
    private Health health;
    public SceneManager sm;
    // Start is called before the first frame update
    void Start()
    {
        health = player.GetComponent<Health>();
    }

    // Update is called once per frame
    void Update()
    {
        if(health.isDead())
        {
            SceneManager.LoadScene("GameOver");
        }
    }
}
