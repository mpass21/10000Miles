using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// .forward is the wheels side to side direction
// this is used to prevent wheel drift
// cars moving forward is for some reason .right
// car side to side is .forward for some reason

[RequireComponent(typeof(Rigidbody))]
public class VehicleDriver : MonoBehaviour
{
    [Header("Vehicle Settings")]
    public float moveForce  = 50000f;
    public float brakeForce = 3000f;
    public float maxSpeed   = 30f;

    [Header("Wheel Speed Limits")]
    public float maxWheelForwardSpeed  = 30f;
    public float maxWheelReverseSpeed  = 10f;
    public float speedLimitPushback    = 50f;

    [Header("Steering")]
    public float maxSteerAngle  = 35f;
    public float steerSpeed     = 120f;
    public float steeringForce  = 150f;

    [Header("Wheel Grounding")]
    public float groundRayLength   = 15f;
    public float groundBuffer      = 5f;

    [Header("Downforce")]
    public float wheelDownforce = 500f;

    [Header("Wheel Spin")]
    public float wheelSpinRate = 200f;

    [Header("Seat")]
    public Transform seat;

    private Rigidbody rb;
    private bool isActive = false;
    private GameObject mountedPlayer;

    private struct WheelEntry
    {
        public Transform transform;
        public int spinDirection;
        public WheelSpinData.WheelType wheelType;
        public Quaternion baseLocalRotation;
        public Quaternion baseWorldRotation;
        public Vector3 parentWorldUp;
        public Vector3 wheelWorldForward;
    }

    private List<WheelEntry> wheels = new();
    private float currentSteerAngle = 0f;
    private float currentSpinAngle  = 0f;
    private float throttle          = 0f;

    private Dictionary<int, bool> wheelGroundedState = new();

    private float pWheelForwardSpeed = 0f;
    private float speedTotal    = 0f;
    private float speedForward  = 0f;
    private float speedRight    = 0f;
    private float speedUp       = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void ActivateVehicle(GameObject player)
    {
        mountedPlayer = player;

        CharacterController cc = player.GetComponent<CharacterController>();
        FPSController fps = player.GetComponent<FPSController>();

        if (cc  != null) cc.enabled = false;
        if (fps != null) fps.EnableControl(false);

        if (seat != null)
        {
            player.transform.SetParent(seat);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
        }

        FindWheels();
        wheelGroundedState.Clear();
        currentSteerAngle = 0f;
        currentSpinAngle  = 0f;
        isActive = true;

        PlayerModeManager.SetMode(PlayerModeManager.Mode.Drive);
    }

    public void DeactivateVehicle()
    {
        if (mountedPlayer == null) return;

        FPSController fps = mountedPlayer.GetComponent<FPSController>();
        CharacterController cc = mountedPlayer.GetComponent<CharacterController>();

        mountedPlayer.transform.SetParent(null);
        mountedPlayer.transform.position = seat != null
            ? seat.position + Vector3.up * 2f
            : transform.position + Vector3.up * 2f;
        mountedPlayer.transform.rotation = Quaternion.identity;

        if (cc  != null) cc.enabled = true;
        if (fps != null) fps.EnableControl(true);

        ResetSteer();

        isActive      = false;
        mountedPlayer = null;

        PlayerModeManager.SetMode(PlayerModeManager.Mode.None);
    }

    void FindWheels()
    {
        wheels.Clear();
        Collider frameCollider = GetComponent<Collider>();

        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (!child.CompareTag("Wheel")) continue;

            WheelSpinData spinData = child.GetComponent<WheelSpinData>();
            int spinDir = spinData != null ? spinData.spinDirection : 1;
            WheelSpinData.WheelType type = spinData != null
                ? spinData.wheelType
                : WheelSpinData.WheelType.Drive;

            wheels.Add(new WheelEntry
            {
                transform         = child,
                spinDirection     = spinDir,
                wheelType         = type,
                baseLocalRotation = child.localRotation,
                baseWorldRotation = child.parent != null
                    ? child.parent.rotation * child.localRotation
                    : child.localRotation,
                parentWorldUp     = child.parent != null ? child.parent.up : Vector3.up,
                wheelWorldForward = child.forward
            });

            if (frameCollider != null)
            {
                foreach (Collider wc in child.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(wc, frameCollider);
            }
        }
    }

    void UpdateSteer(float steerInput)
    {
        float targetAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle, targetAngle, steerSpeed * Time.deltaTime);
    }

    void ResetSteer()
    {
        for (int i = 0; i < wheels.Count; i++)
        {
            WheelEntry entry = wheels[i];
            if (entry.transform == null) continue;
            entry.transform.localRotation = entry.baseLocalRotation;
        }
        currentSteerAngle = 0f;
    }

    void UpdateSpin()
    {
        currentSpinAngle += wheelSpinRate * -throttle * Time.deltaTime;
        currentSpinAngle %= 360f;

        for (int i = 0; i < wheels.Count; i++)
        {
            WheelEntry entry = wheels[i];
            if (entry.transform == null) continue;

            if (entry.wheelType == WheelSpinData.WheelType.Turn)
            {
                Quaternion steerDelta = Quaternion.AngleAxis(currentSteerAngle, Vector3.right);
                Quaternion spinDelta  = Quaternion.AngleAxis(currentSpinAngle * entry.spinDirection, Vector3.forward);
                entry.transform.localRotation = entry.baseLocalRotation * steerDelta * spinDelta;
            }
            else
            {
                if (throttle == 0f) continue;
                float spin = currentSpinAngle * entry.spinDirection;
                entry.transform.localRotation = entry.baseLocalRotation * Quaternion.AngleAxis(spin, Vector3.forward);
            }
        }
    }

    bool GetRollDirection(WheelEntry entry, out Vector3 rollDir, out Vector3 contactDir)
        => GetRollDirection(entry, groundRayLength, out rollDir, out contactDir);

    bool GetRollDirection(WheelEntry entry, float rayLength, out Vector3 rollDir, out Vector3 contactDir)
    {
        Transform wheel = entry.transform;
        rollDir    = Vector3.zero;
        contactDir = Vector3.zero;

        Vector3[] localDirs = new Vector3[]
        {
            -wheel.up, wheel.up,
            -wheel.right, wheel.right,
            -wheel.forward, wheel.forward,
        };

        float closestDist = float.MaxValue;
        bool grounded = false;

        foreach (Vector3 dir in localDirs)
        {
            if (Physics.Raycast(wheel.position, dir, out RaycastHit hit, rayLength))
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    contactDir  = dir;
                    grounded    = true;
                }
            }
        }

        if (!grounded) return false;

        Vector3 axle = wheel.forward;
        float angle  = entry.spinDirection == 1 ? 90f : -90f;
        rollDir      = Quaternion.AngleAxis(angle, axle) * contactDir;

        return true;
    }

    void Update()
    {
        if (isActive)
        {
            throttle = 0f;
            if (Keyboard.current.wKey.isPressed) throttle =  1f;
            if (Keyboard.current.sKey.isPressed) throttle = -1f;

            float steerInput = 0f;
            if (Keyboard.current.dKey.isPressed) steerInput = 1f;
            if (Keyboard.current.aKey.isPressed) steerInput = -1f;

            UpdateSteer(steerInput);
            UpdateSpin();

            Vector3 vel = rb.linearVelocity;
            speedTotal   = vel.magnitude;
            speedForward = Vector3.Dot(vel, transform.right);
            speedRight   = Vector3.Dot(vel, transform.forward);
            speedUp      = Vector3.Dot(vel, transform.up);

            if (Keyboard.current.qKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                DeactivateVehicle();
            }
        }
    }

    void FixedUpdate()
    {
        if (!isActive || wheels.Count == 0) return;

        float currentSpeed = rb.linearVelocity.magnitude;

        bool anyWheelGrounded = false;
        for (int i = 0; i < wheels.Count; i++)
        {
            bool grounded = GetRollDirection(wheels[i], groundRayLength + groundBuffer, out _, out _);
            if (grounded) anyWheelGrounded = true;

            if (!wheelGroundedState.TryGetValue(i, out bool wasGrounded) || wasGrounded != grounded)
            {
                wheelGroundedState[i] = grounded;
            }
        }

        foreach (WheelEntry entry in wheels)
        {
            if (entry.wheelType != WheelSpinData.WheelType.Drive) continue;
            if (!GetRollDirection(entry, out Vector3 rollDir, out _)) continue;

            if (throttle != 0f && currentSpeed < maxSpeed)
            {
                Vector3 driveForce = rollDir * throttle * (moveForce / wheels.Count);
                rb.AddForceAtPosition(driveForce, entry.transform.position, ForceMode.Force);
            }
        }

        foreach (WheelEntry entry in wheels)
        {
            if (!GetRollDirection(entry, groundRayLength + groundBuffer, out _, out _)) continue;
            rb.AddForceAtPosition(Vector3.down * wheelDownforce, entry.transform.position, ForceMode.Force);
        }

        bool isMoving = currentSpeed > 0.5f;

        foreach (WheelEntry entry in wheels)
        {
            if (entry.wheelType != WheelSpinData.WheelType.Turn)
            {
                Vector3 pointVel = rb.GetPointVelocity(entry.transform.position);
                float raw = Vector3.Dot(pointVel, transform.forward);

                bool wheelGrounded = GetRollDirection(entry, groundRayLength + groundBuffer, out _, out _);
                if (wheelGrounded)
                {
                    if (raw > maxWheelForwardSpeed)
                    {
                        float overshoot = raw - maxWheelForwardSpeed;
                        rb.AddForceAtPosition(-transform.forward * overshoot * speedLimitPushback, entry.transform.position, ForceMode.Force);
                    }
                    else if (raw < -maxWheelReverseSpeed)
                    {
                        float overshoot = Mathf.Abs(raw) - maxWheelReverseSpeed;
                        rb.AddForceAtPosition(transform.forward * overshoot * speedLimitPushback, entry.transform.position, ForceMode.Force);
                    }
                }

                pWheelForwardSpeed = raw;
            }
            else
            {
                float steerInput = 0f;
                if (Keyboard.current.dKey.isPressed) steerInput = -1f;
                if (Keyboard.current.aKey.isPressed) steerInput =  1f;
                if (speedForward < 0f) steerInput *= -1f;

                if (steerInput != 0f && isMoving && anyWheelGrounded)
                {
                    Vector3 steerForce = transform.forward * steerInput * steeringForce;
                    rb.AddForceAtPosition(steerForce, entry.transform.position, ForceMode.Force);
                    Debug.DrawRay(entry.transform.position, steerForce.normalized * 10f, Color.blue);
                }
            }
        }
    }

    void OnGUI()
    {
        if (!isActive) return;

        bool pHeld = Keyboard.current.pKey.isPressed;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize  = 16;
        style.alignment = TextAnchor.MiddleLeft;
        style.padding   = new RectOffset(10, 10, 8, 8);

        string direction = pWheelForwardSpeed >  0.01f ? "▶ FORWARD"
                         : pWheelForwardSpeed < -0.01f ? "◀ BACKWARD"
                         : "— STOPPED";

        string wheelLabel = $"Wheel → Forward\n{Mathf.Abs(pWheelForwardSpeed):F2} m/s  {direction}";

        GUI.color = pHeld
            ? new Color(1f, 0.4f, 0.4f, 0.95f)
            : new Color(1f, 1f, 1f, 0.8f);

        GUI.Box(new Rect(10, 10, 230, 60), wheelLabel, style);

        GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.85f);

        string fwdDir = speedForward >  0.1f ? "▶"
                      : speedForward < -0.1f ? "◀"
                      : "—";

        string rightDir = speedRight >  0.1f ? "▶"
                        : speedRight < -0.1f ? "◀"
                        : "—";

        string upDir = speedUp >  0.1f ? "▲"
                     : speedUp < -0.1f ? "▼"
                     : "—";

        string speedLabel =
            $"Speed: {speedTotal:F1} m/s ({speedTotal * 3.6f:F1} km/h)\n" +
            $"Fwd: {fwdDir} {Mathf.Abs(speedForward):F1}   " +
            $"Drift: {rightDir} {Mathf.Abs(speedRight):F1}   " +
            $"Up: {upDir} {Mathf.Abs(speedUp):F1}";

        GUI.Box(new Rect(10, 80, 380, 60), speedLabel, style);

        GUI.color = Color.white;
    }
}