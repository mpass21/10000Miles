using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class VehicleDriver : MonoBehaviour
{
    [Header("Vehicle Settings")]
    public float moveForce  = 50000f;
    public float brakeForce = 3000f;
    public float maxSpeed   = 30f;

    [Header("Steering")]
    public float maxSteerAngle  = 35f;
    public float steerSpeed     = 120f;
    public float steeringForce  = 40000f;

    [Header("Wheel Grounding")]
    public float groundRayLength   = 15f;
    public float groundBuffer      = 5f;  // extra ray length used only for grounded checks (steering/damping/downforce)

    [Header("Downforce")]
    public float wheelDownforce = 500f;

    [Header("Wheel Spin")]
    public float wheelSpinRate = 200f; // degrees per second

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

    // Tracks last known grounded state per wheel index to detect changes
    private Dictionary<int, bool> wheelGroundedState = new();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation    = RigidbodyInterpolation.Interpolate;
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

            // Visual steering always updates regardless of speed or grounding
            UpdateSteer(steerInput);
            UpdateSpin();

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
            // Use buffered ray length so small bounces don't break ground contact
            bool grounded = GetRollDirection(wheels[i], groundRayLength + groundBuffer, out _, out _);
            if (grounded) anyWheelGrounded = true;

            // Log whenever a wheel's grounded state changes
            if (!wheelGroundedState.TryGetValue(i, out bool wasGrounded) || wasGrounded != grounded)
            {
                string wheelName = wheels[i].transform != null ? wheels[i].transform.name : $"Wheel {i}";
                string state = grounded ? "GROUNDED" : "AIRBORNE";
                Debug.Log($"[VehicleDriver] {wheelName} (index {i}) -> {state} at t={Time.time:F2}s");
                wheelGroundedState[i] = grounded;
            }
        }

        foreach (WheelEntry entry in wheels)
        {
            if (entry.wheelType != WheelSpinData.WheelType.Drive) continue;
            if (!GetRollDirection(entry, out Vector3 rollDir, out _)) continue; // normal ray for drive

            if (throttle != 0f && currentSpeed < maxSpeed)
            {
                Vector3 driveForce = rollDir * throttle * (moveForce / wheels.Count);
                rb.AddForceAtPosition(driveForce, entry.transform.position, ForceMode.Force);
            }
        }

        foreach (WheelEntry entry in wheels)
        {
            // Buffered ray for downforce so it stays stable near ground
            if (!GetRollDirection(entry, groundRayLength + groundBuffer, out _, out _)) continue;
            rb.AddForceAtPosition(Vector3.down * wheelDownforce, entry.transform.position, ForceMode.Force);
        }

        // Physics steering force only applied when moving AND grounded
        bool isMoving = currentSpeed > 0.5f;

        foreach (WheelEntry entry in wheels)
        {
            if (entry.wheelType != WheelSpinData.WheelType.Turn) continue;

            float steerInput = 0f;
            if (Keyboard.current.aKey.isPressed) steerInput = -1f;
            if (Keyboard.current.dKey.isPressed) steerInput =  1f;

            if (steerInput != 0f && isMoving && anyWheelGrounded)
            {
                Vector3 steerForce = transform.right * steerInput * entry.spinDirection * (steeringForce * 0.1f);
                rb.AddForceAtPosition(steerForce, entry.transform.position, ForceMode.Force);
            }
        }

        if (anyWheelGrounded)
        {
            if (throttle == 0f)
            {
                Vector3 flatVel = rb.linearVelocity;
                flatVel.y = 0f;
                rb.AddForce(-flatVel * 2f, ForceMode.Force);
            }

            Vector3 sidewaysVel = Vector3.Dot(rb.linearVelocity, transform.right) * transform.right;
            rb.AddForce(-sidewaysVel * 20f, ForceMode.Force);
        }

        rb.angularDamping = anyWheelGrounded ? 5f : 0.05f;
    }
}