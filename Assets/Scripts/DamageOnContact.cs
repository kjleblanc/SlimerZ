using System.Collections.Generic;
using UnityEngine;

public class DamageOnContact : MonoBehaviour
{
    public float damage = 4f;
    public float perTargetCooldown = 0.6f;
    public LayerMask targetMask; // e.g., "Player"

    readonly Dictionary<Health, float> _cooldowns = new();

    void Reset()
    {
        targetMask = LayerMask.GetMask("Player");
    }

    void Update()
    {
        // decay cooldowns
        var keys = new List<Health>(_cooldowns.Keys);
        foreach (var k in keys)
            _cooldowns[k] = Mathf.Max(0f, _cooldowns[k] - Time.deltaTime);
    }

    void OnTriggerStay(Collider other)
    {
        if (((1 << other.gameObject.layer) & targetMask.value) == 0) return;

        var h = other.GetComponentInParent<Health>();
        if (!h) return;

        if (!_cooldowns.TryGetValue(h, out float cd) || cd <= 0f)
        {
            h.ApplyDamage(damage, gameObject);
            _cooldowns[h] = perTargetCooldown;
        }
    }
}
