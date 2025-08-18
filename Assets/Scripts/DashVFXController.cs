using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(20)]
public class DashVFXController : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Player controller with IsDashing property.")]
    public SlimePlayer player;                 // auto-assigns in Awake if null
    [Tooltip("Main Camera used for FOV kick.")]
    public Camera cam;                         // auto-assigns in Awake if null
    [Tooltip("TrailRenderer on the visual; emits only while dashing.")]
    public TrailRenderer trail;
    [Tooltip("ScreenShake component on the camera.")]
    public ScreenShake screenShake;
    [Tooltip("AudioSource used to play dash SFX.")]
    public AudioSource audioSource;
    [Tooltip("Played at dash start.")]
    public AudioClip dashWhoosh;
    [Tooltip("Played on dash impact.")]
    public AudioClip hitThump;
    [Tooltip("Optional: small burst on the player at dash start.")]
    public ParticleSystem dashBurst;

    [Header("Impact VFX")]
    [Tooltip("Prefab to spawn on hit (can be a root with multiple child ParticleSystems).")]
    public GameObject hitSparkPrefab;          // <-- CHANGED from ParticleSystem to GameObject
    [Tooltip("Layers treated as ground for aligning the impact ring.")]
    public LayerMask groundMask = ~0;

    [Header("FOV Kick")]
    [Tooltip("How many degrees to add to FOV while dashing.")]
    public float fovKick = 6f;
    [Tooltip("How quickly FOV reaches target while dashing (1/s).")]
    public float fovLerpIn = 20f;
    [Tooltip("How quickly FOV relaxes back after dash (1/s).")]
    public float fovLerpOut = 12f;

    [Header("Hitstop & Shake")]
    [Tooltip("Global timescale during hitstop (0.08 = heavy).")]
    public float hitstopScale = 0.08f;
    [Tooltip("Duration of hitstop in real seconds.")]
    public float hitstopDuration = 0.06f;
    [Tooltip("Shake added at dash start.")]
    public float dashShake = 0.15f;
    [Tooltip("Shake added on impact.")]
    public float hitShake = 0.35f;
    [Tooltip("How quickly shake decays (1/s).")]
    public float shakeDecay = 3f;

    float baseFov;
    bool hitstopActive;

    void Awake()
    {
        if (!player) player = GetComponent<SlimePlayer>();
        if (!cam) cam = Camera.main;
        if (cam) baseFov = cam.fieldOfView;
        if (trail) trail.emitting = false;
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        bool dashing = player && player.IsDashing;

        // Trail on/off
        if (trail) trail.emitting = dashing;

        // FOV kick (use unscaled for smoothness during hitstop)
        if (cam)
        {
            float target = baseFov + (dashing ? fovKick : 0f);
            float speed = dashing ? fovLerpIn : fovLerpOut;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        }
    }

    public void OnDashStart()
    {
        if (dashBurst) dashBurst.Play();
        if (audioSource && dashWhoosh) audioSource.PlayOneShot(dashWhoosh, 0.9f);
        if (screenShake) screenShake.AddShake(dashShake, shakeDecay);
    }

    public void OnDashHit(Vector3 point)
    {
        // Spawn VFX (aligned to surface if we can raycast a normal)
        if (hitSparkPrefab)
        {
            Quaternion rot = Quaternion.identity;
            if (Physics.Raycast(point + Vector3.up * 0.2f, Vector3.down, out var hit, 1.0f, groundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 up = hit.normal;
                Vector3 fwd = transform.forward;
                Vector3 flatFwd = Vector3.ProjectOnPlane(fwd, up).normalized;
                if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Vector3.Cross(up, Vector3.right); // fallback
                rot = Quaternion.LookRotation(flatFwd, up);
                point = hit.point; // snap to surface
            }
            Instantiate(hitSparkPrefab, point, rot);
        }

        if (audioSource && hitThump) audioSource.PlayOneShot(hitThump, 1f);
        if (screenShake) screenShake.AddShake(hitShake, shakeDecay);

        if (!hitstopActive) StartCoroutine(CoHitstop());
    }

    IEnumerator CoHitstop()
    {
        hitstopActive = true;
        float prev = Time.timeScale;
        Time.timeScale = hitstopScale;
        yield return new WaitForSecondsRealtime(hitstopDuration);
        Time.timeScale = prev;
        hitstopActive = false;
    }
}
