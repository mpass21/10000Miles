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
    public float groundRayLength = 15f;

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

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation    = RigidbodyInterpolation.Interpolate;
        rb.angularDamping   = 5f;
        rb.linearDamping    = 1f;
        rb.centerOfMass     = new Vector3(0f, -1f, 0f);
    }

    public void ActivateVehicle(GameObject player)
    {
        mountedPlayer = player;

        FPSController fps = player.GetComponent<FPSController>();
        CharacterController cc = player.GetComponent<CharacterController>();
        if (fps != null) fps.EnableControl(false);
        if (cc  != null) cc.enabled = false;

        if (seat != null)
        {
            player.transform.SetParent(seat);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
        }

        FindWheels();
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

            Debug.Log($"[VehicleDriver] Wheel '{child.name}' | spin={spinDir} | type={type}");
        }

        Debug.Log($"[VehicleDriver] Total wheels: {wheels.Count} | mass: {rb.mass}");
    }

    // -----------------------------------------------------------------------
    //  Steering
    // -----------------------------------------------------------------------

    void UpdateSteer(float steerInput)
    {
        float targetAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle, targetAngle, steerSpeed * Time.deltaTime);

        for (int i = 0; i < wheels.Count; i++)
        {
            WheelEntry entry = wheels[i];
            if (entry.wheelType != WheelSpinData.WheelType.Turn) continue;
            if (entry.transform == null) continue;

            Quaternion steerDelta = Quaternion.AngleAxis(currentSteerAngle, entry.parentWorldUp);
            entry.transform.rotation = steerDelta * entry.baseWorldRotation;
        }
    }

    void ResetSteer()
    {
        for (int i = 0; i < wheels.Count; i++)
        {
            WheelEntry entry = wheels[i];
            if (entry.wheelType != WheelSpinData.WheelType.Turn) continue;
            if (entry.transform == null) continue;
            entry.transform.localRotation = entry.baseLocalRotation;
        }
        currentSteerAngle = 0f;
    }

    // -----------------------------------------------------------------------
    //  Spin — simple local space rotation, no world space involved
    //  Spins when W or S is held, direction flips for reverse
    // -----------------------------------------------------------------------

    void UpdateSpin()
    {
        if (throttle == 0f) return;

        currentSpinAngle += wheelSpinRate * throttle * Time.deltaTime;
        currentSpinAngle %= 360f;

        for (int i = 0; i < wheels.Count; i++)
        {
            WheelEntry entry = wheels[i];
            if (entry.transform == null) continue;

            // Spin purely in local space around local Z — immune to any parent scale
            float spin = currentSpinAngle * entry.spinDirection;
            entry.transform.localRotation = entry.baseLocalRotation * Quaternion.AngleAxis(spin, Vector3.forward);
        }
    }

    // -----------------------------------------------------------------------
    //  Grounding
    // -----------------------------------------------------------------------

    bool GetRollDirection(WheelEntry entry, out Vector3 rollDir, out Vector3 contactDir)
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
            if (Physics.Raycast(wheel.position, dir, out RaycastHit hit, groundRayLength))
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

    // -----------------------------------------------------------------------
    //  Unity loop
    // -----------------------------------------------------------------------

    void Update()
    {
        foreach (WheelEntry entry in wheels)
        {
            if (entry.transform == null) continue;
            GetRollDirection(entry, out Vector3 rollDir, out Vector3 contactDir);

            Color rollColor = entry.spinDirection == 1
                ? new Color(0f, 1f, 1f) : new Color(1f, 0f, 1f);

            if (entry.wheelType == WheelSpinData.WheelType.Turn)
                rollColor = Color.yellow;

            Debug.DrawRay(entry.transform.position, rollDir     * 50f, rollColor);
            Debug.DrawRay(entry.transform.position, contactDir  * 50f, Color.gray);
            Debug.DrawRay(entry.transform.position,  entry.transform.forward * 50f, Color.white);
            Debug.DrawRay(entry.transform.position, -entry.transform.forward * 50f, Color.white);
        }

        if (isActive)
        {
            throttle = 0f;
            if (Keyboard.current.wKey.isPressed) throttle =  1f;
            if (Keyboard.current.sKey.isPressed) throttle = -1f;

            float steerInput = 0f;
            if (Keyboard.current.aKey.isPressed) steerInput = -1f;
            if (Keyboard.current.dKey.isPressed) steerInput =  1f;

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

        Vector3 netForce  = Vector3.zero;
        int groundedDrive = 0;
        int groundedTurn  = 0;

        // ---- Drive wheels ------------------------------------------------
        foreach (WheelEntry entry in wheels)
        {
            if (entry.wheelType != WheelSpinData.WheelType.Drive) continue;
            if (!GetRollDirection(entry, out Vector3 rollDir, out _)) continue;
            groundedDrive++;

            if (throttle != 0f && currentSpeed < maxSpeed)
            {
                Vector3 driveForce = rollDir * throttle * (moveForce / wheels.Count);
                netForce += driveForce;
                rb.AddForceAtPosition(driveForce, entry.transform.position, ForceMode.Force);
            }
        }

        // ---- Downforce — all grounded wheels -----------------------------
        foreach (WheelEntry entry in wheels)
        {
            if (!GetRollDirection(entry, out _, out _)) continue;
            rb.AddForceAtPosition(Vector3.down * wheelDownforce, entry.transform.position, ForceMode.Force);
        }

        // ---- Turn wheels — lateral steering force only ------------------
        foreach (WheelEntry entry in wheels)
        {
            if (entry.wheelType != WheelSpinData.WheelType.Turn) continue;
            if (!GetRollDirection(entry, out Vector3 rollDir, out _)) continue;
            groundedTurn++;

            float lateralDot   = Vector3.Dot(rollDir, transform.right);
            Vector3 lateralDir = transform.right * lateralDot;

            float forwardSpeed    = Vector3.Dot(rb.linearVelocity, transform.forward);
            float rollSpeedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 3f);

            if (Mathf.Abs(currentSteerAngle) > 0.5f && rollSpeedFactor > 0f)
            {
                float forceMag     = (steeringForce * 0.05f) / Mathf.Max(1, groundedTurn);
                Vector3 steerForce = lateralDir * forceMag * rollSpeedFactor;
                rb.AddForceAtPosition(steerForce, entry.transform.position, ForceMode.Force);
            }
        }

        // ---- Drag --------------------------------------------------------
        if (throttle == 0f)
        {
            Vector3 flatVel = rb.linearVelocity;
            flatVel.y = 0f;
            rb.AddForce(-flatVel * 2f, ForceMode.Force);
        }

        // Reduce sideways slip
        Vector3 sidewaysVel = Vector3.Dot(rb.linearVelocity, transform.right) * transform.right;
        rb.AddForce(-sidewaysVel * 5f, ForceMode.Force);

        if (throttle != 0f)
            Debug.Log($"[VehicleDriver] Drive:{groundedDrive} Turn:{groundedTurn} | net:{netForce} | spd:{currentSpeed:F2} | steer:{currentSteerAngle:F1}°");
    }
}