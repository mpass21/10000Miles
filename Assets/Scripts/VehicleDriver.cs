using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class VehicleDriver : MonoBehaviour
{
    [Header("Movement")]
    public float moveForce = 1500f;
    public float turnSpeed = 100f;

    [Header("Seat")]
    public Transform seat;

    private Rigidbody rb;
    private bool isActive = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void ActivateVehicle(GameObject player)
    {
        // Disable player controls (optional, based on your setup)
        FPSController fps = player.GetComponent<FPSController>();
        CharacterController cc = player.GetComponent<CharacterController>();

        if (fps != null) fps.EnableControl(false);
        if (cc != null) cc.enabled = false;

        // Snap player to seat
        if (seat != null)
        {
            player.transform.SetParent(seat);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
        }

        isActive = true;
    }

    void FixedUpdate()
    {
        if (!isActive) return;

        // Forward movement (W)
        if (Keyboard.current.wKey.isPressed)
        {
            rb.AddForce(transform.forward * moveForce * Time.fixedDeltaTime, ForceMode.Force);
        }

        // Turning (A / D)
        float turn = 0f;
        if (Keyboard.current.aKey.isPressed) turn -= 1f;
        if (Keyboard.current.dKey.isPressed) turn += 1f;

        rb.MoveRotation(
            rb.rotation * Quaternion.Euler(0f, turn * turnSpeed * Time.fixedDeltaTime, 0f)
        );
    }
}
