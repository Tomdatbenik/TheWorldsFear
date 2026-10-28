using UnityEngine;

public class RiskSpotGiveBuff : MonoBehaviour
{
    // Kept for the old scene reference; the buff now goes to the RPG hero.
    public GameObject sword;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.tag == "Player")
        {
            TWF.BossHelpers.RiskSpotBuff();
        }
    }
}
