using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class StatBlock : MonoBehaviour
{
    
    public TextMeshProUGUI shieldstat;
    public TextMeshProUGUI Healthstat;
    public TextMeshProUGUI Damagestat;
    public TextMeshProUGUI Speedstat;

    public Defence defence;
    public Health health;
    public Damage damage;
    public Speed speed;

    private double speedpoints;
    private void Start()
    {
        speedpoints = Mathf.Round(speed.SpeedPoints);
        shieldstat.text = defence.DefencePoints.ToString();
        Healthstat.text = health.HealthPoints.ToString();
        Damagestat.text = damage.Strength.ToString();
        Speedstat.text = speedpoints.ToString();
    }

    private void Update()
    {
        speedpoints = Mathf.Round(speed.SpeedPoints);
        shieldstat.text = defence.DefencePoints.ToString();
        Healthstat.text = health.HealthPoints.ToString();
        Damagestat.text = damage.Strength.ToString();
        Speedstat.text = speedpoints.ToString();
    }
}
