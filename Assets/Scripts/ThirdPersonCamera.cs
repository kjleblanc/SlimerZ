using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;                    // player root (CharacterController object)
    public Vector3 pivotOffset = new Vector3(0f, 1.0f, 0f);

    [Header("Input (New Input System)")]
    [SerializeField] private InputActionProperty lookAction;        // Vector2 (Mouse Delta + Right Stick)
    [SerializeField] private InputActionProperty lookEnableAction;  // Button (RMB)  — optional; we also fallback to Mouse.rightButton
    [SerializeField] private InputActionProperty zoomAction;        // float or Vector2 (Mouse Wheel)

    [Header("Sensitivity")]
    public float mouseXSensitivity = 0.12f;
    public float mouseYSensitivity = 0.10f;
    public float stickXSensitivity = 180f;
    public float stickYSensitivity = 150f;
    public bool invertY = false;

    [Header("Orbit Limits")]
    public float minPitch = -30f;
    public float maxPitch = 70f;

    [Header("Distance / Zoom")]
    public float distance = 6f;
    public float minDistance = 2f;
    public float maxDistance = 10f;
    public float zoomSensitivity = 2f;

    [Header("Smoothing")]
    public float rotationDamp = 20f;            // camera rotation smoothing
    public float positionDamp = 20f;            // camera position smoothing
    public float followYawDamp = 35f;           // how quickly camera yaw follows player yaw (when not freelooking)

    [Header("Collision")]
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.2f;
    public float collisionBuffer = 0.05f;

    [Header("RMB Recenter")]
    public bool recenterOnRelease = true;
    public bool recenterPitchToo = false;
    public float recenterPitch = 15f;
    public float recenterYawSpeed = 540f;
    public float recenterPitchSpeed = 270f;

    [Header("Cursor")]
    public bool lockCursorAlways = true;
    public bool lockCursorDuringRMB = true;

    // --- internals ---
    float yaw;                  // camera yaw
    float pitch;                // camera pitch
    float currentDistance;
    Quaternion rotSmoothed;
    bool wasHeldLastFrame;
    bool recenterActive;
    float lastTargetYaw;        // used to freeze player yaw while RMB held

    void Awake()
    {
        currentDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        if (target) { yaw = lastTargetYaw = target.eulerAngles.y; }
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        rotSmoothed = Quaternion.Euler(pitch, yaw, 0f);

        if (lockCursorAlways) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
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
        if (lockCursorAlways || lockCursorDuringRMB) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    void LateUpdate()
    {
        if (!target) return;

        // --- Input ---
        Vector2 look = Vector2.zero;
        var la = lookAction.action; if (la != null) look = la.ReadValue<Vector2>();

        // Robust RMB detect: action OR raw mouse rightButton (fallback)
        bool held = false;
        var hold = lookEnableAction.action;
        if (hold != null && hold.enabled) held = hold.IsPressed();
        if (!held && Mouse.current != null) held = Mouse.current.rightButton.isPressed;

        // Cursor behavior
        if (lockCursorDuringRMB && !lockCursorAlways)
        {
            if (held && !wasHeldLastFrame) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (!held && wasHeldLastFrame) { Cursor.lockState = CursorLockMode.None;   Cursor.visible = true;  }
        }

        // Zoom (always allowed)
        var za = zoomAction.action;
        if (za != null)
        {
            float z = 0f;
            var ctrl = za.activeControl;
            if (ctrl != null && ctrl.valueType == typeof(Vector2)) z = za.ReadValue<Vector2>().y;
            else z = za.ReadValue<float>();
            distance = Mathf.Clamp(distance - z * zoomSensitivity, minDistance, maxDistance);
        }

        // Convert look to degrees
        float dt = Time.unscaledDeltaTime;
        float dx = look.x * (IsMouseDelta() ? mouseXSensitivity : stickXSensitivity * dt);
        float dy = look.y * (IsMouseDelta() ? mouseYSensitivity : stickYSensitivity * dt);
        if (!invertY) dy = -dy;

        // --- Modes ---
        if (held)
        {
            // FREE-LOOK: rotate camera only
            yaw   += dx;
            pitch += dy;
            pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);
            
            // Removed the problematic forced player rotation that was causing unwanted player movement
        }
        else
        {
            // LOCKED-BEHIND: mouse X rotates player; camera follows
            if (Mathf.Abs(dx) > 0.0001f)
            {
                target.Rotate(0f, dx, 0f, Space.World);
                lastTargetYaw = target.eulerAngles.y;
            }

            // Camera pitch from mouse Y
            pitch += dy;
            pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);

            // Camera yaw follows player yaw smoothly
            float t = 1f - Mathf.Exp(-followYawDamp * dt);
            yaw = Mathf.LerpAngle(yaw, lastTargetYaw, t);
        }

        // RMB released → recenter to player yaw (and optional pitch)
        if (!held && wasHeldLastFrame && recenterOnRelease) recenterActive = true;

        if (recenterActive)
        {
            yaw = Mathf.MoveTowardsAngle(yaw, lastTargetYaw, recenterYawSpeed * dt);
            if (recenterPitchToo) pitch = Mathf.MoveTowardsAngle(pitch, recenterPitch, recenterPitchSpeed * dt);

            if (Mathf.Abs(Mathf.DeltaAngle(yaw, lastTargetYaw)) < 0.5f &&
                (!recenterPitchToo || Mathf.Abs(Mathf.DeltaAngle(pitch, recenterPitch)) < 0.5f))
            {
                recenterActive = false;
            }
        }

        // --- Build camera transform + collision ---
        Quaternion desiredRot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + pivotOffset;
        Vector3 camDir = desiredRot * Vector3.back;

        float desiredDist = distance;
        float blockedDist = desiredDist;
        if (Physics.SphereCast(pivot, collisionRadius, camDir, out RaycastHit hit, desiredDist, collisionMask, QueryTriggerInteraction.Ignore))
            blockedDist = Mathf.Max(minDistance, hit.distance - collisionBuffer);

        rotSmoothed = Quaternion.Slerp(rotSmoothed, desiredRot, 1f - Mathf.Exp(-rotationDamp * dt));
        currentDistance = Mathf.Lerp(currentDistance, blockedDist, 1f - Mathf.Exp(-positionDamp * dt));

        transform.position = pivot + camDir * currentDistance;
        transform.rotation = rotSmoothed;

        wasHeldLastFrame = held;
    }

    bool IsMouseDelta() => Mouse.current != null;
}
