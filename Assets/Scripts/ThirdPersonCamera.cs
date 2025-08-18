using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Usually the player root (with CharacterController).")]
    public Transform target;
    [Tooltip("Pivot offset above target (camera orbits around this point).")]
    public Vector3 pivotOffset = new Vector3(0f, 1.0f, 0f);

    [Header("Input (New Input System)")]
    [Tooltip("Input Actions → Player → Look (Vector2). Mouse Delta + Right Stick.")]
    [SerializeField] private InputActionProperty lookAction;
    [Tooltip("Input Actions → Player → LookEnable (Button). Hold to look (RMB).")]
    [SerializeField] private InputActionProperty lookEnableAction;
    [Tooltip("Optional. Input Actions → Player → Zoom (float or Vector2 scroll).")]
    [SerializeField] private InputActionProperty zoomAction;
    [Tooltip("If true, Look values are pixel deltas (mouse). If false, treat as deg/sec (stick).")]
    public bool lookIsDelta = true;

    [Header("Orbit")]
    [Tooltip("Degrees per mouse pixel (when lookIsDelta = true).")]
    public float mouseSensitivity = 0.12f;
    [Tooltip("Degrees per second from stick (when lookIsDelta = false).")]
    public float stickSensitivity = 180f;
    public bool invertY = false;
    [Tooltip("Clamp vertical angle (degrees).")]
    public float minPitch = -30f, maxPitch = 70f;
    [Tooltip("Start angles (degrees).")]
    public float yaw = 0f, pitch = 15f;

    [Header("Distance / Zoom")]
    [Tooltip("Default follow distance.")]
    public float distance = 6f;
    public float minDistance = 2f, maxDistance = 10f;
    [Tooltip("Zoom speed (units per scroll step or per second if axis).")]
    public float zoomSensitivity = 2f;

    [Header("Smoothing")]
    [Tooltip("Higher = snappier rotation; lower = smoother (expo smoothing).")]
    public float rotationDamp = 20f;
    [Tooltip("Higher = snappier position; lower = smoother (expo smoothing).")]
    public float positionDamp = 20f;

    [Header("Collision")]
    [Tooltip("Which layers can block the camera (exclude Player/Enemy).")]
    public LayerMask collisionMask = ~0;
    [Tooltip("Radius for sphere cast to keep cam out of walls.")]
    public float collisionRadius = 0.2f;
    [Tooltip("How far to keep the camera off the obstacle.")]
    public float collisionBuffer = 0.05f;

    [Header("Right-Mouse Look + Recenter")]
    [Tooltip("Hold this (RMB) to look. When released, auto-recenter behind target.")]
    public bool recenterOnRelease = true;
    [Tooltip("Also recenter pitch to this value when releasing RMB (if enabled).")]
    public bool recenterPitchToo = false;
    [Tooltip("Pitch to recenter to on release (degrees).")]
    public float recenterPitch = 15f;
    [Tooltip("How fast yaw recenters (deg/sec).")]
    public float recenterYawSpeed = 360f;
    [Tooltip("How fast pitch recenters (deg/sec).")]
    public float recenterPitchSpeed = 180f;

    [Header("Cursor")]
    [Tooltip("Lock/Hide cursor while holding RMB; restore on release.")]
    public bool lockCursorWhileLooking = true;

    float currentDistance;
    Quaternion rotSmoothed;
    bool wasHeldLastFrame;   // RMB held last frame?
    bool recenterActive;     // currently recentering?

    void Awake()
    {
        currentDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        rotSmoothed = Quaternion.Euler(pitch, yaw, 0f);
    }

    void OnEnable()
    {
        var a = lookAction.action;       if (a != null) a.Enable();
        var b = lookEnableAction.action; if (b != null) b.Enable();
        var c = zoomAction.action;       if (c != null) c.Enable();
    }

    void OnDisable()
    {
        var a = lookAction.action;       if (a != null) a.Disable();
        var b = lookEnableAction.action; if (b != null) b.Disable();
        var c = zoomAction.action;       if (c != null) c.Disable();
        if (lockCursorWhileLooking) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    void LateUpdate()
    {
        if (!target) return;

        // --- read inputs ---
        bool held = false;
        var hold = lookEnableAction.action;
        if (hold != null) held = hold.IsPressed();

        // handle cursor lock/visibility
        if (lockCursorWhileLooking)
        {
            if (held && !wasHeldLastFrame) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (!held && wasHeldLastFrame) { Cursor.lockState = CursorLockMode.None;   Cursor.visible = true;  }
        }

        // If RMB just released, start recenter
        if (!held && wasHeldLastFrame && recenterOnRelease) recenterActive = true;

        // While held, apply look deltas and cancel recenter
        if (held)
        {
            Vector2 look = Vector2.zero;
            var la = lookAction.action;
            if (la != null) look = la.ReadValue<Vector2>();

            float dx, dy;
            if (lookIsDelta)
            {
                dx = look.x * mouseSensitivity;
                dy = look.y * mouseSensitivity;
            }
            else
            {
                dx = look.x * stickSensitivity * Time.deltaTime;
                dy = look.y * stickSensitivity * Time.deltaTime;
            }

            yaw   += dx;
            pitch += (invertY ?  dy : -dy);
            pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);

            recenterActive = false; // user is actively looking
        }

        // Auto zoom (works even when not holding RMB)
        var za = zoomAction.action;
        if (za != null)
        {
            float z = 0f;
            var ctrl = za.activeControl;
            if (ctrl != null && ctrl.valueType == typeof(Vector2))
            {
                Vector2 v = za.ReadValue<Vector2>();
                z = v.y;
            }
            else
            {
                z = za.ReadValue<float>();
            }
            distance = Mathf.Clamp(distance - z * zoomSensitivity, minDistance, maxDistance);
        }

        // --- recenter logic ---
        if (recenterActive)
        {
            // Desired yaw = player's flat forward
            Vector3 fwd = target.forward;
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            float desiredYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, recenterYawSpeed * Time.deltaTime);

            if (recenterPitchToo)
                pitch = Mathf.MoveTowardsAngle(pitch, Mathf.Clamp(recenterPitch, minPitch, maxPitch), recenterPitchSpeed * Time.deltaTime);

            // stop when close enough
            bool yawDone   = Mathf.DeltaAngle(yaw, desiredYaw) * Mathf.DeltaAngle(yaw, desiredYaw) < 0.25f; // ~0.5°^2
            bool pitchDone = !recenterPitchToo || Mathf.Abs(Mathf.DeltaAngle(pitch, recenterPitch)) < 0.5f;
            if (yawDone && pitchDone) recenterActive = false;
        }

        // --- build desired transform, handle collision, smooth ---
        Quaternion desiredRot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + pivotOffset;

        float desiredDist = distance;
        Vector3 camDir = desiredRot * Vector3.back;
        float blockedDist = desiredDist;

        if (Physics.SphereCast(pivot, collisionRadius, camDir, out RaycastHit hit, desiredDist, collisionMask, QueryTriggerInteraction.Ignore))
            blockedDist = Mathf.Max(minDistance, hit.distance - collisionBuffer);

        rotSmoothed = Quaternion.Slerp(rotSmoothed, desiredRot, 1f - Mathf.Exp(-rotationDamp * Time.deltaTime));
        currentDistance = Mathf.Lerp(currentDistance, blockedDist, 1f - Mathf.Exp(-positionDamp * Time.deltaTime));

        transform.position = pivot + camDir * currentDistance;
        transform.rotation = rotSmoothed;

        wasHeldLastFrame = held;
    }
}
