using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class VehicleDriver : MonoBehaviour
{
    [Header("Vehicle Settings")]
    public float moveForce = 50000f;
    public float brakeForce = 3000f;
    public float maxSpeed = 30f;

    [Header("Wheel Grounding")]
    public float groundRayLength = 8f;

    [Header("Seat")]
    public Transform seat;

    private Rigidbody rb;
    private bool isActive = false;

    private struct WheelEntry
    {
        public Transform transform;
        public int spinDirection;
    }
    private List<WheelEntry> wheels = new();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.angularDamping = 5f;
        rb.linearDamping = 1f;
        rb.centerOfMass = new Vector3(0f, -1f, 0f);
    }

    public void ActivateVehicle(GameObject player)
    {
        FPSController fps = player.GetComponent<FPSController>();
        CharacterController cc = player.GetComponent<CharacterController>();
        if (fps != null) fps.EnableControl(false);
        if (cc != null) cc.enabled = false;

        if (seat != null)
        {
            player.transform.SetParent(seat);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
        }

        FindWheels();
        isActive = true;
    }

    void FindWheels()
    {
        wheels.Clear();
        Collider frameCollider = GetComponent<Collider>();

        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (!child.CompareTag("Wheel")) continue;

            WheelSpinData spinData = child.GetComponent<WheelSpinData>();
            int dir = spinData != null ? spinData.spinDirection : 1;

            wheels.Add(new WheelEntry { transform = child, spinDirection = dir });

            Collider wc = child.GetComponent<Collider>();
            if (frameCollider != null && wc != null)
                Physics.IgnoreCollision(wc, frameCollider);

            Debug.Log($"[VehicleDriver] Wheel '{child.name}' | spinDir: {dir}");
        }

        Debug.Log($"[VehicleDriver] Total wheels: {wheels.Count} | isKinematic: {rb.isKinematic} | mass: {rb.mass}");
    }

    bool GetRollDirection(WheelEntry entry, out Vector3 rollDir, out Vector3 contactDir)
    {
        Transform wheel = entry.transform;
        rollDir = Vector3.zero;
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
                    contactDir = dir;
                    grounded = true;
                }
            }
        }

        if (!grounded) return false;

        Vector3 axle = wheel.forward;
        float angle = entry.spinDirection == 1 ? 90f : -90f;
        rollDir = Quaternion.AngleAxis(angle, axle) * contactDir;

        return true;
    }

    // Draws rays every frame — visible in both Scene AND Game view
    void Update()
    {
        foreach (WheelEntry entry in wheels)
        {
            if (entry.transform == null) continue;

            bool grounded = GetRollDirection(entry, out Vector3 rollDir, out Vector3 contactDir);

            // Roll direction — cyan (forward) or magenta (reverse)
            Color rollColor = entry.spinDirection == 1
                ? new Color(0f, 1f, 1f)  // cyan
                : new Color(1f, 0f, 1f); // magenta
            Debug.DrawRay(entry.transform.position, rollDir * 50f, rollColor);

            // Contact face toward ground — yellow
            Debug.DrawRay(entry.transform.position, contactDir * 50f, Color.yellow);

            // Axle both directions — white
            Debug.DrawRay(entry.transform.position,  entry.transform.forward * 50f, Color.white);
            Debug.DrawRay(entry.transform.position, -entry.transform.forward * 50f, Color.white);
        }
    }

    void FixedUpdate()
    {
        if (!isActive || wheels.Count == 0) return;

        float throttle = 0f;
        bool braking = false;
        if (Keyboard.current.wKey.isPressed) throttle =  1f;
        if (Keyboard.current.sKey.isPressed) throttle = -1f;
        if (Keyboard.current.spaceKey.isPressed) braking = true;

        float currentSpeed = rb.linearVelocity.magnitude;
        Vector3 netForce = Vector3.zero;
        int groundedWheels = 0;

        foreach (WheelEntry entry in wheels)
        {
            if (!GetRollDirection(entry, out Vector3 rollDir, out Vector3 contactDir)) continue;
            groundedWheels++;

            if (throttle != 0f && currentSpeed < maxSpeed)
            {
                Vector3 driveForce = rollDir * throttle * (moveForce / wheels.Count);
                netForce += driveForce;
                rb.AddForceAtPosition(driveForce, entry.transform.position, ForceMode.Force);
            }

            if (braking)
            {
                rb.AddForceAtPosition(
                    -rb.linearVelocity.normalized * (brakeForce / wheels.Count),
                    entry.transform.position,
                    ForceMode.Force
                );
            }
        }

        if (throttle != 0f)
            Debug.Log($"[VehicleDriver] Grounded: {groundedWheels}/{wheels.Count} | NET: {netForce} | speed: {currentSpeed:F2}");

        if (throttle == 0f && !braking)
        {
            Vector3 flatVel = rb.linearVelocity;
            flatVel.y = 0f;
            rb.AddForce(-flatVel * 2f, ForceMode.Force);
        }

        Vector3 sidewaysVel = Vector3.Dot(rb.linearVelocity, transform.right) * transform.right;
        rb.AddForce(-sidewaysVel * 10f, ForceMode.Force);
    }
}