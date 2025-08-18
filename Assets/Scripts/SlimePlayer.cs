using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class SlimePlayer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root of the visual mesh(s). We scale & rotate this without affecting collisions.")]
    public Transform visualRoot; 
    [Tooltip("Camera used to make movement camera-relative (WASD moves relative to view).")]
    public Camera followCamera; 
    [Tooltip("Surface wobble component on the Visual object (optional but nice).")]
    public MeshWobbleDeformer deformer;

    [Header("Input (New Input System)")]
    [Tooltip("Input Actions → Player → Move (Vector2). Drives X/Z movement.")]
    [SerializeField] private InputActionProperty moveAction;
    [Tooltip("Input Actions → Player → Jump (Button). Space / South button.")]
    [SerializeField] private InputActionProperty jumpAction;
    [Tooltip("Input Actions → Player → Dash (Button). Left Shift / East button.")]
    [SerializeField] private InputActionProperty dashAction;

    [Header("Movement")]
    [Tooltip("Target cruise speed on flat ground (meters/second).")]
    [Min(0f)] public float moveSpeed = 4f;
    [Tooltip("How quickly we reach target speed (m/s²). Higher = snappier starts.")]
    [Min(0f)] public float acceleration = 12f;
    [Tooltip("How quickly we slow down when input drops (m/s²). Higher = snappier stops.")]
    [Min(0f)] public float deceleration = 16f;
    [Tooltip("Rotation responsiveness for facing movement. Higher = snaps faster; lower = smoother/softer.")]
    [Min(0f)] public float turnSharpness = 10f;

    [Header("Gravity / Jump")]
    [Tooltip("Downward acceleration (negative, m/s²). More negative = heavier fall.")]
    public float gravity = -25f;
    [Tooltip("Small downward stick force while grounded to keep contact on small bumps (m/s).")]
    public float stickToGroundForce = -3f;
    [Tooltip("Desired jump apex height in METERS. We compute initial velocity from this.")]
    [Min(0f)] public float jumpHeight = 1.6f;
    [Tooltip("Time after leaving ground where jump still works (seconds).")]
    [Min(0f)] public float coyoteTime = 0.12f;
    [Tooltip("If jump pressed just BEFORE landing, buffer it for this time (seconds).")]
    [Min(0f)] public float jumpBufferTime = 0.12f;
    [Tooltip("Release jump early to cut upward velocity by this factor (0.5 = shorter hops).")]
    [Range(0.1f, 1f)] public float jumpCutMultiplier = 0.5f;

    [Header("Dash")]
    [Tooltip("Dash speed override (m/s). Applied for Dash Duration.")]
    [Min(0f)] public float dashSpeed = 11f;
    [Tooltip("How long the dash lasts (seconds).")]
    [Min(0f)] public float dashDuration = 0.20f;
    [Tooltip("Cooldown after a dash before another can start (seconds).")]
    [Min(0f)] public float dashCooldown = 0.60f;
    [Tooltip("Extra wobble energy injected at dash start (visual juice only).")]
    [Min(0f)] public float dashWobbleImpulse = 0.12f;

    [Header("Slope Stickiness")]
    [Tooltip("At maximum uphill, reduce speed by this fraction. 0.4 = 40% slower uphill.")]
    [Range(0f, 1f)] public float uphillSlowdown = 0.40f;
    [Tooltip("At maximum downhill, increase speed by this fraction. 0.15 = 15% faster downhill.")]
    [Range(0f, 1f)] public float downhillBoost = 0.15f;
    [Tooltip("Passive slide speed (m/s) when idle on steeper slopes (adds small downhill drift).")]
    [Min(0f)] public float downhillSlide = 1.2f;
    [Tooltip("Start sliding when slope angle exceeds this (degrees).")]
    [Range(0f, 89f)] public float slideMinSlope = 18f;

    [Header("Wobble (Global Jiggle)")]
    [Tooltip("Base wobble frequency in Hertz (oscillations per second) when standing still.")]
    [Min(0f)] public float baseWobbleFreq = 2.0f;
    [Tooltip("Extra wobble frequency added per 1 m/s of speed. Lower for calmer cruise.")]
    [Min(0f)] public float wobbleFreqPerSpeed = 1.5f;
    [Tooltip("Base wobble amplitude (scale) when standing still. Lower = less breathing.")]
    [Min(0f)] public float baseWobbleAmp = 0.10f;
    [Tooltip("Extra wobble amplitude added per 1 m/s of speed. Reduce to kill 'pulsing' while moving.")]
    [Min(0f)] public float wobbleAmpPerSpeed = 0.15f;
    [Tooltip("How much landing velocity turns into wobble energy. Higher = heavier impact feel.")]
    [Min(0f)] public float landingImpulseMultiplier = 0.02f;
    [Tooltip("How quickly wobble energy decays (1/s). Higher = settles faster.")]
    [Min(0f)] public float wobbleDamping = 6f;
    [Tooltip("Extra wobble energy injected on jump start (visual pop).")]
    [Min(0f)] public float jumpImpulse = 0.08f;

    [Header("Squash & Stretch (Visual Only)")]
    [Tooltip("Max downward squash amount (Y scale goes to 1 - squash). Typical 0.2–0.35.")]
    [Range(0f, 0.6f)] public float squashMax = 0.28f;
    [Tooltip("Max upward stretch amount (Y scale goes to 1 + stretch). Typical 0.1–0.25.")]
    [Range(0f, 0.6f)] public float stretchMax = 0.18f;
    [Tooltip("How quickly scale chases its target (higher = snappier, lower = smoother). Think of this as 'scale smoothing speed'.")]
    [Min(0f)] public float scaleLerp = 10f;

    [Header("Landing Puddle (Extra Impact Squish)")]
    [Tooltip("How long the puddle squish lasts after landing (seconds).")]
    [Min(0f)] public float landingPuddleDuration = 0.12f;
    [Tooltip("How strong the X/Z widen + Y flatten is at the start of the puddle effect.")]
    [Min(0f)] public float landingPuddleIntensity = 0.18f;

    [Header("Lean / Bank (Visual Only)")]
    [Tooltip("Max forward pitch when moving at full speed (degrees).")]
    [Range(0f, 60f)] public float maxForwardLeanDeg = 16f;
    [Tooltip("How quickly the forward lean responds to speed changes. Higher = snappier.")]
    [Min(0f)] public float forwardLeanResponse = 8f;
    [Tooltip("Max roll into turns (degrees). Set 0 to disable banking.")]
    [Range(0f, 45f)] public float maxBankDeg = 10f;
    [Tooltip("How quickly banking responds to turning. Higher = snappier.")]
    [Min(0f)] public float bankResponse = 7f;
    [Tooltip("Scale factor from signed turn angle per frame to bank degrees. Lower for subtler roll.")]
    [Min(0f)] public float bankFromTurnScale = 0.35f;

    [Header("Grounding / Tilt")]
    [Tooltip("Raycast length used to align visual 'up' to ground normal (meters).")]
    [Min(0.1f)] public float groundCheckDistance = 1.5f;
    [Tooltip("Layers considered 'ground' for tilt and slope logic.")]
    public LayerMask groundMask = ~0;

    // --- internals (runtime state) ---
    CharacterController controller;
    Vector3 horizVel;
    float verticalVel;
    bool wasGrounded;

    float wobblePhase;
    float wobbleValue;         // [-1, 1]
    float wobbleEnergy;        // decays over time
    Vector3 lastHorizVel;

    // Jump timing helpers
    float timeSinceGrounded = 999f;
    float timeSinceJumpPressed = 999f;

    // Dash helpers
    float dashTimer = 0f;
    float dashCooldownTimer = 0f;
    Vector3 dashDir = Vector3.zero;

    // Lean/bank state
    float _forwardLean, _bank, _forwardLeanVel, _bankVel;

    // Landing puddle
    float landingPuddleTimer = 0f;

    // Ground probe cache (for slope logic and tilt)
    bool hasGroundHit;
    RaycastHit groundHit;
    Vector3 groundNormal = Vector3.up;
    float groundSlopeDeg = 0f;
    Vector3 downhillDir = Vector3.zero;

    public float HorizontalSpeed => horizVel.magnitude;
    public bool IsDashing => dashTimer > 0f;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (!visualRoot && transform.childCount > 0) visualRoot = transform.GetChild(0);
        controller.enableOverlapRecovery = true;

        if (moveAction.action == null) Debug.LogWarning("SlimePlayer: Move Action not assigned.");
        if (jumpAction.action == null) Debug.LogWarning("SlimePlayer: Jump Action not assigned.");
        if (dashAction.action == null) Debug.LogWarning("SlimePlayer: Dash Action not assigned.");
    }

    void OnEnable()
    {
        var ma = moveAction.action;  if (ma != null) ma.Enable();
        var ja = jumpAction.action;  if (ja != null) ja.Enable();
        var da = dashAction.action;  if (da != null) da.Enable();
    }

    void OnDisable()
    {
        var ma = moveAction.action;  if (ma != null) ma.Disable();
        var ja = jumpAction.action;  if (ja != null) ja.Disable();
        var da = dashAction.action;  if (da != null) da.Disable();
    }

    void Update()
    {
        // ---------- INPUT ----------
        var ma = moveAction.action;
        var ja = jumpAction.action;
        var da = dashAction.action;

        Vector2 move2D = (ma != null) ? ma.ReadValue<Vector2>() : Vector2.zero;
        bool jumpPressed  = (ja != null) && ja.WasPressedThisFrame();
        bool jumpReleased = (ja != null) && ja.WasReleasedThisFrame();
        bool dashPressed  = (da != null) && da.WasPressedThisFrame();

        // Probe ground for slope + tilt
        ProbeGround();

        // Track grounded time for coyote (use previous grounded state)
        bool groundedPrev = controller.isGrounded;
        timeSinceGrounded = groundedPrev ? 0f : timeSinceGrounded + Time.deltaTime;
        timeSinceJumpPressed = jumpPressed ? 0f : timeSinceJumpPressed + Time.deltaTime;

        // Camera-relative input
        Vector3 camF = followCamera ? Vector3.ProjectOnPlane(followCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 camR = followCamera ? Vector3.ProjectOnPlane(followCamera.transform.right,  Vector3.up).normalized : Vector3.right;
        Vector3 inputDir = new Vector3(move2D.x, 0f, move2D.y);
        inputDir = Vector3.ClampMagnitude(inputDir, 1f);
        Vector3 moveDir = (camF * inputDir.z + camR * inputDir.x).normalized;

        // ---------- DASH ----------
        dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - Time.deltaTime);
        if (dashPressed && dashCooldownTimer <= 0f)
        {
            dashTimer = dashDuration;
            dashCooldownTimer = dashCooldown;
            dashDir = (moveDir.sqrMagnitude > 0.0001f) ? moveDir :
                      (visualRoot ? Vector3.ProjectOnPlane(visualRoot.forward, Vector3.up).normalized : camF);
            wobbleEnergy += dashWobbleImpulse;
        }
        bool isDashing = dashTimer > 0f;
        if (isDashing) dashTimer -= Time.deltaTime;

        // ---------- HORIZONTAL TARGET (slope-aware) ----------
        float speedFactor = 1f;
        if (hasGroundHit && moveDir.sqrMagnitude > 0.0001f)
        {
            // +1 downhill, -1 uphill relative to moveDir
            float slopeDot = Vector3.Dot(moveDir, downhillDir);
            if (slopeDot >= 0f)
                speedFactor *= 1f + downhillBoost * slopeDot;       // downhill boost
            else
                speedFactor *= 1f - uphillSlowdown * (-slopeDot);    // uphill slowdown
        }

        Vector3 targetHorizVel = moveDir * (moveSpeed * speedFactor);

        // Passive downhill slide when idle on steeper slopes
        if (hasGroundHit && groundSlopeDeg >= slideMinSlope && inputDir.sqrMagnitude < 0.04f && !isDashing)
        {
            targetHorizVel += downhillDir * downhillSlide;
        }

        // Dashing overrides target velocity
        if (isDashing)
        {
            targetHorizVel = dashDir * dashSpeed;
        }

        // ---------- ACCEL/DECEL ----------
        Vector3 velDelta = targetHorizVel - horizVel;
        float accel = (targetHorizVel.magnitude > horizVel.magnitude) ? acceleration : deceleration;
        if (isDashing) accel = Mathf.Max(accel, 100f); // snap harder into dash
        horizVel += Vector3.ClampMagnitude(velDelta, accel * Time.deltaTime);

        // Face movement direction smoothly
        if (visualRoot && horizVel.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(horizVel.normalized, Vector3.up);
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRot, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
        }

        // ---------- JUMP (coyote + buffer + variable height) ----------
        bool canUseCoyote   = timeSinceGrounded <= coyoteTime;
        bool bufferedJump   = timeSinceJumpPressed <= jumpBufferTime;
        bool shouldJumpNow  = canUseCoyote && bufferedJump;

        if (shouldJumpNow)
        {
            // v = sqrt(2 * g * h)  (g is negative)
            verticalVel = Mathf.Sqrt(2f * jumpHeight * -gravity);
            timeSinceJumpPressed = 999f;    // consume buffer
            timeSinceGrounded = 999f;       // leave ground
            wobbleEnergy += jumpImpulse;    // visual pop
        }

        if (jumpReleased && verticalVel > 0f)
        {
            verticalVel *= jumpCutMultiplier; // short hop on early release
        }

        // ---------- VERTICAL ----------
        if (groundedPrev && verticalVel < 0f && !shouldJumpNow)
        {
            verticalVel = stickToGroundForce; // stay glued on small bumps
        }
        verticalVel += gravity * Time.deltaTime;

        // ---------- MOVE ----------
        Vector3 delta = (horizVel + Vector3.up * verticalVel) * Time.deltaTime;
        CollisionFlags flags = controller.Move(delta);
        bool groundedNow = (flags & CollisionFlags.Below) != 0 || controller.isGrounded;

        // ---------- LANDING / IMPULSES ----------
        if (!wasGrounded && groundedNow)
        {
            float impact = Mathf.Max(0f, -verticalVel);
            wobbleEnergy += impact * landingImpulseMultiplier; // heavier land squish
            landingPuddleTimer = landingPuddleDuration;         // start puddle
        }
        wasGrounded = groundedNow;

        float accelImpulse = (horizVel - lastHorizVel).magnitude;
        wobbleEnergy += accelImpulse * 0.03f; // change-of-speed adds wobble
        lastHorizVel = horizVel;

        // ---------- OSCILLATOR + SQUASH/STRETCH (+ puddle overlay) ----------
        float freq = baseWobbleFreq + HorizontalSpeed * wobbleFreqPerSpeed; // Hz
        wobblePhase += freq * Time.deltaTime * Mathf.PI * 2f;
        wobbleValue = Mathf.Sin(wobblePhase);

        wobbleEnergy = Mathf.Max(0f, wobbleEnergy - wobbleDamping * Time.deltaTime);
        float wobbleAmp = baseWobbleAmp + HorizontalSpeed * wobbleAmpPerSpeed + wobbleEnergy;

        if (visualRoot)
        {
            // Volume-preserving-ish squash/stretch driven by wobble
            float stretch = Mathf.Clamp(wobbleValue * wobbleAmp, -squashMax, stretchMax);
            float y = 1f + stretch;
            float xz = 1f / Mathf.Sqrt(Mathf.Max(0.0001f, y));
            Vector3 targetScale = new Vector3(xz, y, xz);

            // Landing puddle overlay (wider XZ, flatter Y) that decays smoothly
            if (landingPuddleTimer > 0f)
            {
                landingPuddleTimer = Mathf.Max(0f, landingPuddleTimer - Time.deltaTime);
                float t = landingPuddleTimer / Mathf.Max(0.0001f, landingPuddleDuration); // 1 → 0
                float puddle = landingPuddleIntensity * (t * t * (3f - 2f * t)); // smoothstep
                targetScale = new Vector3(
                    targetScale.x * (1f + puddle),
                    targetScale.y * (1f - puddle * 0.8f),
                    targetScale.z * (1f + puddle)
                );
            }

            // Scale Lerp (what it does):
            // This uses exponential smoothing: 'scaleLerp' is the *speed* at which we chase targetScale.
            // Higher = snappier, lower = softer, more floaty transitions.
            visualRoot.localScale = Vector3.Lerp(
                visualRoot.localScale,
                targetScale,
                1f - Mathf.Exp(-scaleLerp * Time.deltaTime)
            );
        }

        if (deformer)
        {
            // Feed surface deformer with the current wobble/velocity signals
            deformer.ExternalWobble = wobbleValue * wobbleAmp;
            deformer.SpeedFactor = Mathf.Clamp01(HorizontalSpeed / Mathf.Max(0.01f, moveSpeed));
        }

        // ---------- TILT TO GROUND + APPLY LEANS ----------
        AlignToGroundNormal();
        ApplyLeans();
    }

    // Cast down to cache ground normal, slope angle and downhill direction
    void ProbeGround()
    {
        if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down,
            out groundHit, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            hasGroundHit = true;
            groundNormal = groundHit.normal;
            groundSlopeDeg = Vector3.Angle(Vector3.up, groundNormal);
            downhillDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
        }
        else
        {
            hasGroundHit = false;
            groundNormal = Vector3.up;
            groundSlopeDeg = 0f;
            downhillDir = Vector3.zero;
        }
    }

    // Align visual 'up' to the ground normal for a sticky, gooey look
    void AlignToGroundNormal()
    {
        if (!visualRoot) return;

        if (hasGroundHit)
        {
            Quaternion upAlign = Quaternion.FromToRotation(visualRoot.up, groundNormal);
            visualRoot.rotation = upAlign * visualRoot.rotation;
        }
        else if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down,
                 out var hit, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            Quaternion upAlign = Quaternion.FromToRotation(visualRoot.up, hit.normal);
            visualRoot.rotation = upAlign * visualRoot.rotation;
        }
    }

    // Forward-lean from speed + optional banking from turning
    void ApplyLeans()
    {
        if (!visualRoot) return;

        Quaternion baseRot = visualRoot.rotation;

        // Forward lean: map 0..speed to 0..maxForwardLeanDeg, smoothed
        float speed01 = Mathf.Clamp01(HorizontalSpeed / Mathf.Max(0.01f, moveSpeed));
        float targetForward = Mathf.Lerp(0f, maxForwardLeanDeg, speed01);
        _forwardLean = Mathf.SmoothDampAngle(_forwardLean, targetForward, ref _forwardLeanVel,
                                             1f / Mathf.Max(0.0001f, forwardLeanResponse));

        // Bank: signed roll from turn rate, smoothed
        float signedTurn = 0f;
        Vector3 up = baseRot * Vector3.up;
        if (lastHorizVel.sqrMagnitude > 0.0001f && horizVel.sqrMagnitude > 0.0001f)
        {
            signedTurn = Vector3.SignedAngle(lastHorizVel, horizVel, up);
        }
        float targetBank = Mathf.Clamp(signedTurn * bankFromTurnScale, -maxBankDeg, maxBankDeg);
        _bank = Mathf.SmoothDampAngle(_bank, targetBank, ref _bankVel,
                                      1f / Mathf.Max(0.0001f, bankResponse));

        // Compose pitch (forward) then roll (bank) on top of base rotation
        Vector3 right = baseRot * Vector3.right;
        Vector3 fwd   = baseRot * Vector3.forward;
        Quaternion forwardLeanRot = Quaternion.AngleAxis(-_forwardLean, right); // negative = lean forward
        Quaternion bankRot        = Quaternion.AngleAxis(-_bank, fwd);          // roll into turn
        visualRoot.rotation = bankRot * forwardLeanRot * baseRot;
    }
}
