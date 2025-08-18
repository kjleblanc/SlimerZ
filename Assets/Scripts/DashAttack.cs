using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10)]
public class DashAttack : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Your SlimePlayer on the same object.")]
    public SlimePlayer player;                 // auto-assigned in Start if null
    [Tooltip("Optional: VFX controller to play whoosh, shake, hitstop, etc.")]
    public DashVFXController vfx;              // auto-assigned in Start if null
    [Tooltip("Layers considered 'enemies' (e.g., the Enemy layer).")]
    public LayerMask enemyMask;

    [Header("Attack")]
    [Tooltip("Damage dealt to each enemy hit by a single dash.")]
    public float dashDamage = 6f;
    [Tooltip("Radius of the sphere used to detect hits along the dash path.")]
    public float hitRadius = 0.8f;
    [Tooltip("Extra sweep length added beyond this frame's movement delta.")]
    public float sweepPadding = 0.2f;
    [Tooltip("Impulse applied to enemies on hit (visual knockback).")]
    public float knockback = 6f;

    [Header("Debug")]
    [Tooltip("Draw gizmos for the last sweep in the Scene view.")]
    public bool drawDebug = false;
    [Tooltip("Color for the gizmo sweep ray.")]
    public Color debugColor = new Color(0.2f, 1f, 0.6f, 0.8f);

    // --- internals ---
    readonly HashSet<Health> hitThisDash = new HashSet<Health>();
    Vector3 lastPos;
    bool wasDashing;

    void Reset()
    {
        // Try to guess a sensible default layer for enemies.
        enemyMask = LayerMask.GetMask("Enemy");
    }

    void Start()
    {
        if (!player) player = GetComponent<SlimePlayer>();
        if (!vfx)    vfx    = GetComponent<DashVFXController>();
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        bool isDashing = player && player.IsDashing;

        // Dash just started: clear per-dash memory & fire VFX hook.
        if (isDashing && !wasDashing)
        {
            hitThisDash.Clear();
            if (vfx) vfx.OnDashStart();
        }

        if (isDashing)
        {
            Vector3 curr = transform.position;
            Vector3 delta = curr - lastPos;
            float dist = delta.magnitude;

            if (dist > 0.0001f)
            {
                Vector3 dir = delta.normalized;
                float sweep = dist + Mathf.Max(0f, sweepPadding);

                // Sweep a sphere along the path this frame.
                var hits = Physics.SphereCastAll(
                    lastPos,
                    hitRadius,
                    dir,
                    sweep,
                    enemyMask,
                    QueryTriggerInteraction.Ignore
                );

                ProcessRayHits(hits, dir);
            }
            else
            {
                // If we didn't move this frame (e.g., very short dash or at start),
                // still allow hits right around us.
                var overlaps = Physics.OverlapSphere(
                    curr,
                    hitRadius,
                    enemyMask,
                    QueryTriggerInteraction.Ignore
                );

                ProcessOverlapHits(overlaps, transform.forward, curr);
            }
        }

        wasDashing = isDashing;
        lastPos = transform.position;
    }

    void ProcessRayHits(RaycastHit[] hits, Vector3 dir)
    {
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (!col) continue;

            var h = col.GetComponentInParent<Health>();
            if (!h || hitThisDash.Contains(h)) continue; // one hit per enemy per dash

            // Apply damage
            h.ApplyDamage(dashDamage, gameObject);
            hitThisDash.Add(h);

            // Optional knockback via enemy AI helper (if present)
            var ai = h.GetComponentInParent<EnemySlimeAI>();
            if (ai) ai.AddImpulse(dir * knockback);

            // VFX hook with the physics hit point
            if (vfx) vfx.OnDashHit(hits[i].point);
        }
    }

    void ProcessOverlapHits(Collider[] overlaps, Vector3 dir, Vector3 origin)
    {
        for (int i = 0; i < overlaps.Length; i++)
        {
            var col = overlaps[i];
            if (!col) continue;

            var h = col.GetComponentInParent<Health>();
            if (!h || hitThisDash.Contains(h)) continue;

            h.ApplyDamage(dashDamage, gameObject);
            hitThisDash.Add(h);

            var ai = h.GetComponentInParent<EnemySlimeAI>();
            if (ai) ai.AddImpulse(dir * knockback);

            // Approximate hit point via closest point on collider to our origin
            Vector3 hitPoint = col.ClosestPoint(origin);
            if (vfx) vfx.OnDashHit(hitPoint);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawDebug) return;

        Gizmos.color = debugColor;
        Gizmos.DrawWireSphere(lastPos, hitRadius);
        Gizmos.DrawWireSphere(transform.position, hitRadius);
        Gizmos.DrawLine(lastPos, transform.position);
    }
}
