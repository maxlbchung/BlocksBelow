using UnityEngine;

public class PlayerDamageTaker : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;
    [SerializeField] private float baseKnockbackForce = 10f; // Base knockback force
    [SerializeField] private float fastestDeathTime = 0.5f; // Fastest death time in seconds

    private float immunityFrames = 0f; // Duration of immunity frames in seconds
    private void OnTriggerStay2D(Collider2D other)
    {
        Debug.Log("PlayerDamageTaker collided with: " + other.gameObject.name);




        if (other.CompareTag("Enemy"))
        {
            // Assuming the player has a health component

            if (playerController != null)
            {
                Debug.Log("Player collided with enemy: " + other.gameObject.name);

                if (other.TryGetComponent<BasicEnemy>(out BasicEnemy enemy))
                {
                    Debug.Log("Enemy damage: " + enemy.damage);
                    if (immunityFrames < 0)
                    {
                        playerController.DamagePlayer(enemy.damage, CalculateKnockbackDirection(gameObject.transform.position, other.gameObject.transform.position));
                        immunityFrames = CalculateImmunityFrames(enemy.damage, playerController.maxHealth, fastestDeathTime);
                    }
                    else
                    {
                        playerController.DamagePlayer(0, CalculateKnockbackDirection(gameObject.transform.position, other.gameObject.transform.position));

                    }
                }
                else if (other.TryGetComponent<EnemyBullet>(out EnemyBullet bullet))
                {

                    if (immunityFrames < 0)
                    {


                        playerController.DamagePlayer(bullet.damage, CalculateKnockbackDirection(gameObject.transform.position, other.gameObject.transform.position));
                        immunityFrames = CalculateImmunityFrames(bullet.damage, playerController.maxHealth, fastestDeathTime);
                    }
                    else
                    {
                        playerController.DamagePlayer(0, CalculateKnockbackDirection(gameObject.transform.position, other.gameObject.transform.position));
                    }
                }
            }
        }
    }



    private void Update()
    {
        if (immunityFrames >= 0)
        {
            immunityFrames -= Time.deltaTime; // Decrease immunity frames over time
        }
    }

    private Vector2 CalculateKnockbackDirection(Vector3 playerPosition, Vector3 enemyPosition)
    {
        Vector2 knockbackDirection = (playerPosition - enemyPosition).normalized;
        return knockbackDirection;
    }

    public float CalculateImmunityFrames(int damage, int maxHealth, float fastestDeathTime)
    {
        return fastestDeathTime * (damage / (float)maxHealth);
    }
}
