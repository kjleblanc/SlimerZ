using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemySlimeAI : MonoBehaviour
{
    [Header("Refs")]
    public Transform target;        // assign Player
    public Health health;           // assign on this
    public GameObject lootPrefab;   // GooLoot prefab
    public int lootMin = 2, lootMax = 4;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float acceleration = 10f;
    public float deceleration = 12f;
    public float gravity = -25f;
    public float stickToGroundForce = -3f;

    [Header("Behavior")]
    public float detectRange = 10f;
    public float stopRange = 1.2f;
    public float wanderRadius = 6f;
    public float wanderCooldown = 2.5f;

    CharacterController controller;
    Vector3 horizVel;
    float verticalVel;
    Vector3 wanderTarget;
    float wanderTimer;
    Vector3 externalImpulse;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (!health) health = GetComponent<Health>();
        if (health) health.Died += OnDied;
    }

    void OnDestroy()
    {
        if (health) health.Died -= OnDied;
    }

    public void AddImpulse(Vector3 impulse)
    {
        externalImpulse += impulse;
    }

    void Update()
    {
        Vector3 toPlayer = target ? (target.position - transform.position) : Vector3.zero;
        float dist = toPlayer.magnitude;

        Vector3 desired = Vector3.zero;
        if (target && dist <= detectRange && dist > stopRange)
        {
            desired = toPlayer.normalized * moveSpeed;
        }
        else
        {
            // Wander
            wanderTimer -= Time.deltaTime;
            if (wanderTimer <= 0f || (transform.position - wanderTarget).sqrMagnitude < 1f)
            {
                wanderTimer = wanderCooldown;
                Vector2 rnd = Random.insideUnitCircle * wanderRadius;
                wanderTarget = new Vector3(transform.position.x + rnd.x, transform.position.y, transform.position.z + rnd.y);
            }
            Vector3 toWander = (wanderTarget - transform.position);
            toWander.y = 0;
            if (toWander.sqrMagnitude > 0.2f)
                desired = toWander.normalized * (moveSpeed * 0.6f);
        }

        // External impulse decays
        if (externalImpulse.sqrMagnitude > 0.001f)
        {
            desired += externalImpulse;
            externalImpulse = Vector3.MoveTowards(externalImpulse, Vector3.zero, 10f * Time.deltaTime);
        }

        // accel/decel
        Vector3 velDelta = desired - horizVel;
        float accel = (desired.magnitude > horizVel.magnitude) ? acceleration : deceleration;
        horizVel += Vector3.ClampMagnitude(velDelta, accel * Time.deltaTime);

        // face velocity a bit
        if (horizVel.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(new Vector3(horizVel.x, 0, horizVel.z), Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 0.2f);
        }

        // gravity + move
        bool grounded = controller.isGrounded;
        if (grounded && verticalVel < 0f) verticalVel = stickToGroundForce;
        verticalVel += gravity * Time.deltaTime;

        Vector3 delta = (horizVel + Vector3.up * verticalVel) * Time.deltaTime;
        controller.Move(delta);
    }

    void OnDied(GameObject killer)
    {
        // Spawn 2–4 goo orbs
        int count = Random.Range(lootMin, lootMax + 1);
        for (int i = 0; i < count; i++)
        {
            if (lootPrefab)
            {
                Vector3 p = transform.position + Random.insideUnitSphere * 0.3f;
                p.y = transform.position.y + 0.2f;
                Instantiate(lootPrefab, p, Quaternion.identity);
            }
        }
        Destroy(gameObject);
    }
}
