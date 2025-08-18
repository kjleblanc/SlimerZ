using UnityEngine;

public class GooLoot : MonoBehaviour
{
    public int value = 1;
    public float magnetRange = 6f;
    public float pickupRange = 0.6f;
    public float flySpeed = 8f;

    Transform player;
    GooWallet wallet;
    float bobT;

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p) { player = p.transform; wallet = p.GetComponent<GooWallet>(); }
    }

    void Update()
    {
        bobT += Time.deltaTime;
        transform.position += Vector3.up * Mathf.Sin(bobT * 6f) * 0.002f; // tiny bob

        if (!player) return;

        float d = Vector3.Distance(transform.position, player.position);
        if (d <= magnetRange)
        {
            Vector3 dir = (player.position + Vector3.up * 0.6f - transform.position).normalized;
            float spd = flySpeed * Mathf.Clamp01(1f - d / magnetRange) * 2f + flySpeed * 0.5f;
            transform.position += dir * spd * Time.deltaTime;
        }

        if (d <= pickupRange && wallet)
        {
            wallet.Add(value);
            Destroy(gameObject);
        }
    }
}
