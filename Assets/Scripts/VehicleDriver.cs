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
    public float maxSteerAngle  = 35f;   // degrees the turn wheel rotates
    public float steerSpeed     = 120f;  // degrees per second
    public float steeringForce  = 40000f;

    [Header("Wheel Grounding")]
    public float groundRayLength = 15f;

    [Header("Downforce")]
    public float wheelDownforce = 50f;  // downward force applied per grounded wheel

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
        public Quaternion baseLocalRotation; // local rotation at mount time (used for reset)
        public Vector3    parentWorldUp;     // parent's world-up at mount time — steering pivots
                                             // around this so the wheel turns relative to its
                                             // parent regardless of grandparent scale
    }

    private List<WheelEntry> wheels = new();
    private float currentSteerAngle = 0f;

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

        // Reset turn wheels to base rotation before dismounting
        ResetSteer();

        isActive       = false;
        mountedPlayer  = null;
    }

    void FindWheels()
    {
        wheels.Clear();
        Collider frameCollider = GetComponent<Collider>();

        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (!child.CompareTag("Wheel")) continue;

            WheelSpinData spinData = child.GetComponent<WheelSpinData>();
            int spinDir             = spinData != null ? spinData.spinDirection : 1;
            WheelSpinData.WheelType type = spinData != null
                ? spinData.wheelType
                : WheelSpinData.WheelType.Drive;

            wheels.Add(new WheelEntry
            {
                transform         = child,
                spinDirection     = spinDir,
                wheelType         = type,
                baseLocalRotation = child.localRotation,
                parentWorldUp     = child.parent != null
                                    ? child.parent.up   // steer around parent's up axis
                                    : Vector3.up
            });

            Collider wc = child.GetComponent<Collider>();
            if (frameCollider != null && wc != null)
                Physics.IgnoreCollision(wc, frameCollider);

            Debug.Log($"[VehicleDriver] Wheel '{child.name}' | spin={spinDir} | type={type}");
        }

        Debug.Log($"[VehicleDriver] Total wheels: {wheels.Count} | mass: {rb.mass}");
    }

    // -----------------------------------------------------------------------
    //  Steering helpers
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

            // Rotate the wheel's LOCAL rotation around the parent's world-up axis.
            // This keeps the turn relative to the parent (so a tilted axle still turns
            // correctly) while bypassing any grandparent scale that would cause stretching.
            Quaternion steerDelta = Quaternion.AngleAxis(currentSteerAngle, entry.parentWorldUp);
            entry.transform.rotation = steerDelta * (entry.transform.parent != null
                ? entry.transform.parent.rotation * entry.baseLocalRotation
                : entry.baseLocalRotation);
        }
    }

    void ResetSteer()
    {
        for (int i = 0; i < wheels.Count; i++)
        {
            WheelEntry entry = wheels[i];
            if (entry.wheelType != WheelSpinData.WheelType.Turn) continue;
            if (entry.transform == null) continue;
            entry.transform.localRotation = entry.baseLocalRotation;   // restore local snapshot
        }
        currentSteerAngle = 0f;
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
        int mask = ~(1 << wheel.gameObject.layer);

        foreach (Vector3 dir in localDirs)
        {
            if (Physics.Raycast(wheel.position, dir, out RaycastHit hit, groundRayLength, mask))
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

        Vector3 axle  = wheel.forward;
        float angle   = entry.spinDirection == 1 ? 90f : -90f;
        rollDir       = Quaternion.AngleAxis(angle, axle) * contactDir;

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
            bool grounded = GetRollDirection(entry, out Vector3 rollDir, out Vector3 contactDir);

            Color rollColor = entry.spinDirection == 1
                ? new Color(0f, 1f, 1f) : new Color(1f, 0f, 1f);

            // Turn wheels draw in orange so you can identify them
            if (entry.wheelType == WheelSpinData.WheelType.Turn)
                rollColor = Color.yellow;

            Debug.DrawRay(entry.transform.position, rollDir    * 50f, rollColor);
            Debug.DrawRay(entry.transform.position, contactDir * 50f, Color.gray);
            Debug.DrawRay(entry.transform.position,  entry.transform.forward * 50f, Color.white);
            Debug.DrawRay(entry.transform.position, -entry.transform.forward * 50f, Color.white);
        }

        if (isActive)
        {
            float steerInput = 0f;
            if (Keyboard.current.aKey.isPressed) steerInput = -1f;
            if (Keyboard.current.dKey.isPressed) steerInput =  1f;
            UpdateSteer(steerInput);

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

        float throttle = 0f;
        if (Keyboard.current.wKey.isPressed) throttle =  1f;
        if (Keyboard.current.sKey.isPressed) throttle = -1f;

        float currentSpeed = rb.linearVelocity.magnitude;

        Vector3 netForce   = Vector3.zero;
        int groundedDrive  = 0;
        int groundedTurn   = 0;

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
        // Applied to every wheel regardless of type so pitch is symmetrical
        // whether driving forward or backward.
        foreach (WheelEntry entry in wheels)
        {
            if (!GetRollDirection(entry, out _, out _)) continue;
            rb.AddForceAtPosition(Vector3.down * wheelDownforce, entry.transform.position, ForceMode.Force);
        }

        // ---- Turn wheels — lateral steering force only ------------------
        // Steering wheels should ONLY rotate the car, never propel it forward.
        // rollDir on a turned wheel points diagonally (forward + sideways), so
        // applying force along it would accelerate the car. Instead we:
        //   1. Project rollDir onto the vehicle's right axis → pure lateral direction
        //   2. Gate the force on how fast the car is rolling (via drive-wheel rollDir dot)
        //      so steering does nothing when stationary or airborne.
        foreach (WheelEntry entry in wheels)
        {
            if (entry.wheelType != WheelSpinData.WheelType.Turn) continue;
            if (!GetRollDirection(entry, out Vector3 rollDir, out _)) continue;
            groundedTurn++;

            // Use the vehicle's own right axis as the lateral direction —
            // this is purely sideways and has zero forward component.
            float lateralDot   = Vector3.Dot(rollDir, transform.right);
            Vector3 lateralDir = transform.right * lateralDot; // signed sideways push

            // Gate on forward speed so steering doesn't fire when stopped/airborne.
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

        // Reduce sideways slip on drive wheels only
        Vector3 sidewaysVel = Vector3.Dot(rb.linearVelocity, transform.right) * transform.right;
        rb.AddForce(-sidewaysVel * 5f, ForceMode.Force);

        if (throttle != 0f)
            Debug.Log($"[VehicleDriver] Drive:{groundedDrive} Turn:{groundedTurn} | net:{netForce} | spd:{currentSpeed:F2} | steer:{currentSteerAngle:F1}°");
    }
}